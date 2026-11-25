using System;
using System.Collections.Generic;
using System.Diagnostics;
using Obi;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Vertical hanging-cloth benchmark for Obi 6.4.
/// Uses the existing ObiFixedUpdater and fixes the selected material nodes
/// directly by setting inverse mass and velocity to zero.
/// No attachment components, target objects, particle renderers, or gizmos
/// are created. The solver and blueprint assets are not modified.
/// Substeps are read from ObiFixedUpdater, never overwritten.
/// Automatically saves and freezes physics when the run finishes.
/// Requires the existing ObiMaterialGrid.cs and StrainCore.cs.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ObiStaticStretch : MonoBehaviour
{
    [Header("References")]
    public ObiCloth cloth;
    public ObiFixedUpdater updater;

    [Header("Experiment")]
    public StrainCore.SupportMode supportMode =
        StrainCore.SupportMode.FullTopEdge;
    public string runName = "trial1";
    public float dt = 1f / 60f;
    public float duration = 5f;

    [Header("Benchmark Mass")]
    public bool resetBenchmarkMasses = true;
    public float totalClothMass = 0.049f;

    [Header("Validation")]
    public float anchorTolerance = 1e-4f;

    private const int Width = 21;
    private const int Height = 21;
    private const float ClothSize = 0.7f;
    private const float GeometryTolerance = 1e-4f;

    private ObiSolver solver;
    private ObiMaterialGrid grid;
    private int[] anchors;
    private bool[] isAnchor;
    private float[] originalInverseMasses;
    private StrainCore.Log log;
    private readonly Stopwatch watch = new Stopwatch();

    private int sampleCount;
    private int totalSteps;
    private bool ready;
    private bool finished;
    private bool subscribed;
    private bool changedTimeStep;
    private float previousFixedDeltaTime;
    private int runSubsteps;
    private bool finalRenderPending;

    private void Awake()
    {
        // Find the same native updater used by the original EdgeShear scene.
        if (updater == null && cloth != null)
        {
            ObiSolver parentSolver = cloth.GetComponentInParent<ObiSolver>();
            if (parentSolver != null)
                updater = parentSolver.GetComponent<ObiFixedUpdater>();
        }

        // No physics steps until the rest state and fixed nodes are ready.
        if (updater != null)
            updater.enabled = false;
    }

    private void Start()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Initialize()
    {
        if (cloth == null || !cloth.isLoaded || cloth.solver == null)
            throw new InvalidOperationException(
                "Assign an enabled ObiCloth under an enabled ObiSolver.");

        solver = cloth.solver;

        if (updater == null || !updater.gameObject.activeInHierarchy)
            throw new InvalidOperationException(
                "Assign the original ObiFixedUpdater. Its GameObject must be active.");

        if (!solver.isActiveAndEnabled)
            throw new InvalidOperationException("Keep ObiSolver enabled.");

        if (updater.solvers == null || updater.solvers.Count != 1 ||
            updater.solvers[0] != solver)
            throw new InvalidOperationException(
                "ObiFixedUpdater must contain exactly this ObiSolver.");

        if (updater.substeps < 1 || !Finite(dt) || dt <= 0 ||
            !Finite(duration) || duration < dt ||
            !Finite(anchorTolerance) || anchorTolerance <= 0)
            throw new ArgumentException("Invalid experiment parameters.");

        CheckScene();

        // Blueprint particles remain in their original local XZ grid.
        // The grid helper transforms them into their world-space rest positions.
        grid = new ObiMaterialGrid(cloth, Width, Height);
        OrientMaterialGridTopFirst();

        // Obi 6.4 may retain a runtime state across actor reloads. Reset it
        // using the current actor-to-solver transform (including rotation).
        ResetRestState();

        anchors = StrainCore.GetAnchorIndices(grid.w, grid.h, supportMode);
        isAnchor = new bool[grid.map.Length];
        foreach (int materialIndex in anchors)
            isAnchor[materialIndex] = true;

        double massBeforePinning = InitializeMasses();
        string initialMassBalance = DescribeMassBalance();
        Debug.Log("[StaticStretch] Masses before clamping: " + initialMassBalance, this);

        // Standard Obi particle scripting API: zero inverse mass means fixed.
        // Do not reset masses or overwrite positions again during the run.
        foreach (int materialIndex in anchors)
        {
            int solverIndex = grid.Index(materialIndex);
            solver.invMasses[solverIndex] = 0f;
            solver.velocities[solverIndex] = Vector4.zero;
        }

        cloth.UpdateParticleProperties();
        solver.PushSolverParameters();

        float initialError = ValidateAnchors(grid.Read());
        string freeMassBalance = DescribeMassBalance();
        Debug.Log("[StaticStretch] Free masses after clamping: " + freeMassBalance, this);

        // Do not modify gravity or the chosen constraint evaluation method.
        // Report the actual world-space gravity for asymmetry diagnosis.
        Vector3 worldGravity = solver.gravitySpace == Space.World
            ? solver.gravity
            : solver.transform.TransformVector(solver.gravity);
        string gravityDescription = "worldGravity=" + worldGravity.ToString("F6");
        Debug.Log("[StaticStretch] " + gravityDescription, this);
        if (Mathf.Abs(worldGravity.x) > 1e-4f ||
            Mathf.Abs(worldGravity.z) > 1e-4f || worldGravity.y >= 0f)
        {
            Debug.LogWarning("[StaticStretch] Gravity is not vertically downward.", this);
        }

        previousFixedDeltaTime = Time.fixedDeltaTime;
        changedTimeStep = true;
        Time.fixedDeltaTime = dt;

        // Single source of truth: use the existing native updater setting.
        runSubsteps = updater.substeps;
        totalSteps = Mathf.RoundToInt(duration / dt);

        log = new StrainCore.Log(
            "Obi", runName, runSubsteps, cloth.stretchCompliance,
            solver.distanceConstraintParameters.iterations, dt,
            grid.rest, grid.w, grid.h,
            "Native ObiFixedUpdater; fixed inverse masses; no attachments; " +
            "timingScope=OnPrepareFrame through OnEndStep; " +
            "massBeforePinningKg=" + StrainCore.F(massBeforePinning) + "; " +
            "stretchingScale=" + cloth.stretchingScale + "; " +
            "maxCompression=" + cloth.maxCompression + "; " +
            "tethers=" + cloth.tetherConstraintsEnabled + "; " +
            "backend=" + solver.backendType + "; " +
            "distanceEvaluationOrder=" + solver.distanceConstraintParameters.evaluationOrder + "; " +
            "initialMassBalance=" + initialMassBalance + "; " +
            "freeMassBalance=" + freeMassBalance + "; " + gravityDescription,
            duration, supportMode
        );

        // Record exactly once per native physics step, not per render frame.
        solver.OnPrepareFrame += OnPrepareFrame;
        solver.OnEndStep += OnEndStep;
        subscribed = true;
        ready = true;

        Debug.Log(
            "[StaticStretch] READY: " + supportMode +
            "; substeps=" + runSubsteps +
            "; fixed=" + anchors.Length +
            "; free=" + (grid.map.Length - anchors.Length) +
            "; mass before pinning=" + StrainCore.F(massBeforePinning) +
            " kg; anchor error=" + StrainCore.F(initialError) + " m. " +
            "Material indices: " + string.Join(", ", anchors) +
            "; first anchor world position=" + grid.rest[anchors[0]] + ".",
            this);

        updater.enabled = true;
    }

    private void CheckScene()
    {
        // Old attachments can set masses and positions again in PrepareStep.
        foreach (ObiParticleAttachment attachment in
                 cloth.GetComponents<ObiParticleAttachment>())
        {
            if (attachment.isActiveAndEnabled)
                throw new InvalidOperationException(
                    "Disable every old ObiParticleAttachment on this cloth.");
        }

        foreach (ObiUpdater other in FindObjectsOfType<ObiUpdater>())
        {
            if (other != updater && other.isActiveAndEnabled &&
                other.solvers != null && other.solvers.Contains(solver))
                throw new InvalidOperationException(
                    "A second active updater controls this solver: " +
                    other.GetType().Name + " on " + other.gameObject.name);
        }

        if (!Mathf.Approximately(cloth.stretchCompliance, 0f))
            Debug.LogWarning("[StaticStretch] Stretch compliance is not zero.", this);

        if (cloth.tetherConstraintsEnabled)
            Debug.LogWarning("[StaticStretch] Tethers are enabled.", this);
    }

    private void OrientMaterialGridTopFirst()
    {
        // A +90 degree actor X rotation puts local z-min at the top;
        // a -90 degree rotation puts local z-max at the top. Normalize the
        // grid ordering so StrainCore always uses v=0 for the upper boundary.
        Vector3 first = grid.rest[0];
        Vector3 opposite = grid.rest[grid.h - 1];

        if (opposite.y > first.y)
        {
            for (int u = 0; u < grid.w; u++)
            {
                int offset = u * grid.h;
                Array.Reverse(grid.map, offset, grid.h);
                Array.Reverse(grid.rest, offset, grid.h);
            }
        }

        Vector3 horizontal = grid.rest[(grid.w - 1) * grid.h] - grid.rest[0];
        Vector3 vertical = grid.rest[grid.h - 1] - grid.rest[0];

        if (Mathf.Abs(horizontal.magnitude - ClothSize) > GeometryTolerance ||
            Mathf.Abs(horizontal.y) > GeometryTolerance ||
            Vector3.Distance(vertical, Vector3.down * ClothSize) > GeometryTolerance)
        {
            throw new InvalidOperationException(
                "Expected a 0.7 x 0.7 m vertical grid with local X horizontal. " +
                "Rotate the ObiCloth actor (not the solver) +90 or -90 degrees " +
                "around X and keep unit scale. Horizontal=" + horizontal +
                ", vertical=" + vertical);
        }

        float topY = grid.rest[0].y;
        float bottomY = grid.rest[grid.h - 1].y;
        if (bottomY <= 0.1f)
            throw new InvalidOperationException("Raise the cloth above the floor.");

        for (int u = 0; u < grid.w; u++)
        {
            for (int v = 0; v < grid.h; v++)
            {
                Vector3 expected = grid.rest[0] +
                    horizontal * (u / (float)(grid.w - 1)) +
                    vertical * (v / (float)(grid.h - 1));
                if (Vector3.Distance(grid.rest[u * grid.h + v], expected) >
                    GeometryTolerance)
                    throw new InvalidOperationException(
                        "The rest grid is not a regular, flat vertical square.");
            }
        }

        Debug.Log("[StaticStretch] Top y=" + StrainCore.F(topY) +
                  " m; bottom y=" + StrainCore.F(bottomY) + " m.", this);
    }

    private void ResetRestState()
    {
        cloth.ResetParticles();

        for (int i = 0; i < grid.map.Length; i++)
        {
            int k = grid.Index(i);
            Vector4 p = solver.positions[k];
            // Synchronize history buffers once, before the first physics step.
            solver.startPositions[k] = p;
            solver.prevPositions[k] = p;
            solver.renderablePositions[k] = p;
            solver.velocities[k] = Vector4.zero;
        }

        Vector3[] current = grid.Read();
        float error = 0f;
        for (int i = 0; i < current.Length; i++)
        {
            float distance = Vector3.Distance(current[i], grid.rest[i]);
            if (!Finite(distance))
                throw new InvalidOperationException("Nonfinite rest position.");
            error = Mathf.Max(error, distance);
        }

        if (error > GeometryTolerance)
            throw new InvalidOperationException(
                "Rest configuration mismatch after ResetParticles: " +
                StrainCore.F(error) + " m.");
    }

    private double InitializeMasses()
    {
        if (resetBenchmarkMasses &&
            (!Finite(totalClothMass) || totalClothMass <= 0))
            throw new ArgumentException("Total cloth mass must be positive.");

        // Save the original runtime masses; never edit the blueprint asset.
        originalInverseMasses = new float[grid.map.Length];
        var alreadyFixed = new List<int>();

        for (int i = 0; i < grid.map.Length; i++)
        {
            float inverseMass = solver.invMasses[grid.Index(i)];
            if (!Finite(inverseMass) || inverseMass < 0)
                throw new InvalidOperationException("Invalid particle mass at node " + i);
            originalInverseMasses[i] = inverseMass;
            if (inverseMass == 0)
                alreadyFixed.Add(i);
        }

        if (alreadyFixed.Count > 0 && !resetBenchmarkMasses)
            throw new InvalidOperationException(
                "Some original particles are fixed. Enable Reset Benchmark Masses " +
                "to ensure only the selected upper supports are fixed.");

        if (alreadyFixed.Count > 0)
            Debug.Log("[StaticStretch] Resetting pre-existing fixed nodes: " +
                      string.Join(", ", alreadyFixed), this);

        // Match the lumped mass of a uniform rectangular quadrilateral mesh.
        float cellMass = totalClothMass / ((grid.w - 1) * (grid.h - 1));
        double total = 0;

        for (int u = 0; u < grid.w; u++)
        {
            for (int v = 0; v < grid.h; v++)
            {
                int k = grid.Index(u * grid.h + v);
                if (resetBenchmarkMasses)
                {
                    float wu = (u == 0 || u == grid.w - 1) ? 0.5f : 1f;
                    float wv = (v == 0 || v == grid.h - 1) ? 0.5f : 1f;
                    solver.invMasses[k] = 1f / (cellMass * wu * wv);
                }
                total += 1.0 / solver.invMasses[k];
            }
        }

        return total;
    }

    /// <summary>
    /// Check left/right mass balance in the material grid. Fixed nodes have
    /// zero inverse mass and are omitted from the free-mass calculation.
    /// The middle column is reported separately, rather than assigned to one side.
    /// </summary>
    private string DescribeMassBalance()
    {
        double left = 0.0;
        double right = 0.0;
        double center = 0.0;
        double maxPairDifference = 0.0;

        for (int u = 0; u < grid.w; u++)
        {
            int mirrorU = grid.w - 1 - u;
            for (int v = 0; v < grid.h; v++)
            {
                float inverseMass = solver.invMasses[grid.Index(u * grid.h + v)];
                float mirrorInverseMass = solver.invMasses[grid.Index(mirrorU * grid.h + v)];
                double mass = inverseMass > 0f ? 1.0 / inverseMass : 0.0;
                double mirrorMass = mirrorInverseMass > 0f ? 1.0 / mirrorInverseMass : 0.0;

                if (u < mirrorU)
                    left += mass;
                else if (u > mirrorU)
                    right += mass;
                else
                    center += mass;

                maxPairDifference = Math.Max(maxPairDifference, Math.Abs(mass - mirrorMass));
            }
        }

        return "leftKg=" + StrainCore.F(left) +
            ", rightKg=" + StrainCore.F(right) +
            ", centerKg=" + StrainCore.F(center) +
            ", maxMirrorPairMassDifferenceKg=" + StrainCore.F(maxPairDifference);
    }

    // A zero residual indicates a left/right mirror-symmetric configuration.
    // This is only a diagnostic, not an additional constraint on the cloth.
    private string DescribeShapeSymmetry(Vector3[] positions)
    {
        Vector3 leftTop = grid.rest[0];
        Vector3 rightTop = grid.rest[(grid.w - 1) * grid.h];
        Vector3 planeOrigin = (leftTop + rightTop) * 0.5f;
        Vector3 normal = (rightTop - leftTop).normalized;
        double sumSquares = 0.0;
        float maxError = 0f;

        for (int u = 0; u < grid.w; u++)
        {
            int mirrorU = grid.w - 1 - u;
            for (int v = 0; v < grid.h; v++)
            {
                Vector3 point = positions[u * grid.h + v];
                Vector3 mirrorPoint = positions[mirrorU * grid.h + v];
                Vector3 reflection = point - 2f * Vector3.Dot(point - planeOrigin, normal) * normal;
                float error = Vector3.Distance(reflection, mirrorPoint);
                sumSquares += (double)error * error;
                maxError = Mathf.Max(maxError, error);
            }
        }

        return "mirrorRmsMm=" + StrainCore.F(1000.0 * Math.Sqrt(sumSquares / positions.Length)) +
            ", mirrorMaxMm=" + StrainCore.F(1000.0 * maxError);
    }

    private float ValidateAnchors(Vector3[] positions)
    {
        float maxError = 0f;
        for (int i = 0; i < grid.map.Length; i++)
        {
            float inverseMass = solver.invMasses[grid.Index(i)];
            if (!Finite(inverseMass) || inverseMass < 0 ||
                (inverseMass == 0f) != isAnchor[i])
            {
                throw new InvalidOperationException(
                    "Unexpected inverse mass at material node " + i +
                    " (solver index " + grid.Index(i) + "): " + inverseMass +
                    ". Expected fixed=" + isAnchor[i] + ".");
            }

            if (!isAnchor[i])
                continue;

            float error = Vector3.Distance(positions[i], grid.rest[i]);
            if (!Finite(error) || error > anchorTolerance)
            {
                throw new InvalidOperationException(
                    "Fixed node " + i + " moved " + StrainCore.F(error) +
                    " m; expected " + grid.rest[i] + ", actual " + positions[i] +
                    ". Check whether the actor or solver transform is moving.");
            }
            maxError = Mathf.Max(maxError, error);
        }

        return maxError;
    }

    private void OnPrepareFrame(ObiSolver currentSolver)
    {
        if (ready && !finished)
            watch.Restart();
    }

    private void OnEndStep(ObiSolver currentSolver)
    {
        if (!ready || finished)
            return;

        watch.Stop();
        double milliseconds = watch.Elapsed.TotalMilliseconds;

        try
        {
            if (updater.substeps != runSubsteps ||
                Mathf.Abs(Time.fixedDeltaTime - dt) > 1e-7f)
            {
                throw new InvalidOperationException(
                    "Do not change substeps or timestep during a run. " +
                    "Stop Play mode, change the Inspector setting, then restart.");
            }

            Vector3[] positions = grid.Read();
            float error = ValidateAnchors(positions);
            log.Sample(positions, milliseconds);
            sampleCount++;

            if (sampleCount == 1)
                Debug.Log("[StaticStretch] First physics step valid; " +
                          "anchor error=" + StrainCore.F(error) + " m.", this);

            if (sampleCount >= totalSteps)
            {
                finished = true;
                ready = false;
                log.Dispose();
                log = null;

                // Exactly like Clothilde: finish the run and freeze dynamics,
                // while leaving the scene in Play mode for screenshots.
                updater.enabled = false;
                Unsubscribe();
                finalRenderPending = true;

                Debug.Log("[StaticStretch] Final shape symmetry: " +
                          DescribeShapeSymmetry(positions), this);
                Debug.Log("[StaticStretch] COMPLETE: " + sampleCount +
                          " physics steps. CSV saved; simulation frozen. " +
                          "Stop Play mode before changing parameters.", this);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void LateUpdate()
    {
        // The native updater also handles rendering interpolation. It has
        // now been disabled, so publish the last completed physics state
        // once, outside the solver callback, without another physics step.
        if (!finalRenderPending)
            return;

        finalRenderPending = false;
        if (solver != null && solver.isActiveAndEnabled)
            solver.Interpolate(dt, dt);
    }

    private void Fail(Exception exception)
    {
        ready = false;
        finished = true;
        watch.Stop();
        if (updater != null)
            updater.enabled = false;
        Unsubscribe();
        log?.Dispose();
        log = null;
        Debug.LogError("[StaticStretch] Benchmark stopped: " + exception, this);
    }

    private void Unsubscribe()
    {
        if (!subscribed || solver == null)
            return;

        solver.OnPrepareFrame -= OnPrepareFrame;
        solver.OnEndStep -= OnEndStep;
        subscribed = false;
    }

    private void OnDisable()
    {
        ready = false;
        Unsubscribe();
        log?.Dispose();
        log = null;

        if (Application.isPlaying && updater != null)
            updater.enabled = false;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        log?.Dispose();

        if (cloth != null && cloth.isLoaded && grid != null &&
            originalInverseMasses != null)
        {
            for (int i = 0; i < originalInverseMasses.Length; i++)
                cloth.solver.invMasses[grid.Index(i)] = originalInverseMasses[i];
            cloth.UpdateParticleProperties();
        }

        if (changedTimeStep)
            Time.fixedDeltaTime = previousFixedDeltaTime;
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
