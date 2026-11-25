using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Obi;
using UnityEngine;

/// <summary>
/// Deterministic one-corner self-collision reference benchmarks for Obi.
///
/// Only ONE cloth particle is kinematically controlled:
/// the material corner nearest (u,v)=(1,1).
/// No cloth edge is fixed. The u=0 edge is used only as a geometric
/// reference to construct the initial material frame.
///
/// The prescribed corner trajectory is the same as in
/// ClothildeCornerSelfCollisionBenchmark.
///
/// CornerFoldCompression:
///   self-friction is set to zero; use as the Obi reference for fitting slf.
///
/// CornerFoldSlide:
///   uses referenceSelfFriction; use as the Obi reference for fitting mu_s
///   after slf has been frozen in Clothilde.
///
/// Disable all old ObiParticleAttachment components in the calibration scene.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ObiCornerSelfCollisionBenchmark : MonoBehaviour
{
    public enum BenchmarkMode
    {
        CornerFoldCompression,
        CornerFoldSlide
    }

    [Header("Reference")]
    public ObiCloth cloth;

    [Header("Experiment")]
    public BenchmarkMode mode = BenchmarkMode.CornerFoldCompression;
    public string runName = "obi_corner_self_collision_reference";
    public bool stopPlayModeWhenFinished = true;

    [Header("Reference solver settings")]
    public float frameTime = 1.0f / 60.0f;

    [Range(0.0f, 1.0f)]
    public float referenceSolverDamping = 0.9f;

    [Tooltip(
        "Fixed Obi particle self-friction used in CornerFoldSlide. " +
        "CornerFoldCompression automatically uses zero."
    )]
    public float referenceSelfFriction = 0.5f;

    [Tooltip(
        "Keep these fixed for the reference. Increase them only before " +
        "creating all self-collision reference files."
    )]
    [Range(1, 32)]
    public int particleCollisionIterations = 4;

    [Range(1, 32)]
    public int particleFrictionIterations = 4;

    [Header("Timing")]
    public float restSeconds = 0.50f;
    public float foldSeconds = 2.50f;
    public float compressionSeconds = 0.75f;
    public float compressionHoldSeconds = 1.50f;
    public float preSlideHoldSeconds = 0.75f;
    public float slideSeconds = 2.00f;
    public float slideHoldSeconds = 1.00f;

    [Header("Fold target in material coordinates")]
    [Range(0.0f, 1.0f)]
    public float targetU = 0.25f;

    [Range(0.0f, 1.0f)]
    public float targetV = 0.25f;

    public float approachGap = 0.045f;
    public float compressedGap = 0.025f;

    [Header("Tangential slide")]
    public float slideDistance = 0.120f;

    [Header("Output")]
    public string outputFolderName = "CornerSelfCollisionCalibration";

    private readonly List<int> fixedEdge = new List<int>();
    private readonly HashSet<int> controlledNodes = new HashSet<int>();

    private Vector2[] materialUv;
    private Vector3[] initialWorldPositions;
    private float[] originalInverseMasses;

    private int movingCornerIndex;
    private int targetNodeIndex;

    private Vector3 materialUDirection;
    private Vector3 materialVDirection;
    private Vector3 materialNormal;

    private Vector3 movingCornerInitial;
    private Vector3 targetPointInitial;

    private ObiCollisionMaterial originalCollisionMaterial;
    private ObiCollisionMaterial runtimeCollisionMaterial;

    private StreamWriter writer;
    private float simulationTime;
    private float lastRecordedTime = -1.0f;
    private bool initialized;
    private bool finished;
    private string outputPath;

    private float FoldStart => restSeconds;
    private float FoldEnd => FoldStart + foldSeconds;
    private float CompressionEnd => FoldEnd + compressionSeconds;

    private float SlideStart =>
        CompressionEnd + preSlideHoldSeconds;

    private float SlideEnd =>
        SlideStart + slideSeconds;

    private float TotalDuration
    {
        get
        {
            if (mode == BenchmarkMode.CornerFoldCompression)
                return CompressionEnd + compressionHoldSeconds;

            return SlideEnd + slideHoldSeconds;
        }
    }

    private void Awake()
    {
        Time.fixedDeltaTime = frameTime;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    private void FixedUpdate()
    {
        if (finished)
            return;

        if (!initialized)
        {
            TryInitialize();
            return;
        }

        float nextTime =
            simulationTime + Time.fixedDeltaTime;

        ApplyControls(nextTime);
        simulationTime = nextTime;
    }

    private void LateUpdate()
    {
        if (!initialized || finished)
            return;

        if (Mathf.Approximately(
            lastRecordedTime,
            simulationTime))
        {
            return;
        }

        WriteFrame(simulationTime);
        lastRecordedTime = simulationTime;

        if (simulationTime + 1e-6f >= TotalDuration)
            FinishExperiment();
    }

    private void TryInitialize()
    {
        if (cloth == null ||
            cloth.solver == null ||
            !cloth.isLoaded)
        {
            return;
        }

        if (cloth.clothBlueprint == null ||
            cloth.clothBlueprint.inputMesh == null)
        {
            Debug.LogError(
                "ObiCornerSelfCollisionBenchmark: blueprint/input mesh missing."
            );
            enabled = false;
            return;
        }

        int particleCount =
            cloth.clothBlueprint.inputMesh.vertexCount;

        materialUv =
            cloth.clothBlueprint.inputMesh.uv;

        if (materialUv == null ||
            materialUv.Length != particleCount)
        {
            Debug.LogError(
                "The input mesh must contain one UV coordinate per particle."
            );
            enabled = false;
            return;
        }

        if (cloth.solverIndices == null ||
            cloth.solverIndices.Length < particleCount)
        {
            Debug.LogError(
                "solverIndices do not match the input mesh."
            );
            enabled = false;
            return;
        }

        initialWorldPositions =
            new Vector3[particleCount];

        originalInverseMasses =
            new float[particleCount];

        for (int actorIndex = 0;
             actorIndex < particleCount;
             actorIndex++)
        {
            int solverIndex =
                cloth.solverIndices[actorIndex];

            initialWorldPositions[actorIndex] =
                cloth.GetParticlePosition(solverIndex);

            originalInverseMasses[actorIndex] =
                cloth.solver.invMasses[solverIndex];
        }

        BuildIndices();
        ComputeMaterialFrame();

        movingCornerInitial =
            initialWorldPositions[movingCornerIndex];

        targetPointInitial =
            initialWorldPositions[targetNodeIndex];

        ApplyReferenceSettings();
        OpenCsv();

        ApplyControls(0.0f);
        WriteFrame(0.0f);
        lastRecordedTime = 0.0f;

        initialized = true;

        Debug.Log(
            "Obi corner self-collision benchmark initialized with " +
            particleCount + " particles."
        );
    }

    private void BuildIndices()
    {
        fixedEdge.Clear();
        controlledNodes.Clear();

        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;

        foreach (Vector2 uv in materialUv)
        {
            minU = Mathf.Min(minU, uv.x);
            maxU = Mathf.Max(maxU, uv.x);
            minV = Mathf.Min(minV, uv.y);
            maxV = Mathf.Max(maxV, uv.y);
        }

        const float tolerance = 1e-4f;
        float bestCornerDistance = float.PositiveInfinity;
        float bestTargetDistance = float.PositiveInfinity;

        for (int i = 0; i < materialUv.Length; i++)
        {
            Vector2 uv = materialUv[i];

            if (Mathf.Abs(uv.x - minU) < tolerance)
            {
                // Keep this edge only as a geometric reference for
                // constructing the material frame.
                // IMPORTANT: these particles are NOT constrained.
                fixedEdge.Add(i);
            }

            float cornerDistance =
                (uv - new Vector2(maxU, maxV)).sqrMagnitude;

            if (cornerDistance < bestCornerDistance)
            {
                bestCornerDistance = cornerDistance;
                movingCornerIndex = i;
            }

            float targetDistance =
                (uv - new Vector2(targetU, targetV)).sqrMagnitude;

            if (targetDistance < bestTargetDistance)
            {
                bestTargetDistance = targetDistance;
                targetNodeIndex = i;
            }
        }

        controlledNodes.Add(movingCornerIndex);

        fixedEdge.Sort(
            (a, b) =>
                materialUv[a].y.CompareTo(materialUv[b].y)
        );

        if (fixedEdge.Count < 2 ||
            movingCornerIndex < 0 ||
            targetNodeIndex < 0)
        {
            Debug.LogError(
                "Could not detect the fixed edge, moving corner, or target."
            );
            enabled = false;
        }
    }

    private void ComputeMaterialFrame()
    {
        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;

        foreach (Vector2 uv in materialUv)
        {
            minU = Mathf.Min(minU, uv.x);
            maxU = Mathf.Max(maxU, uv.x);
        }

        Vector3 u0Mean = Vector3.zero;
        Vector3 u1Mean = Vector3.zero;
        int n0 = 0;
        int n1 = 0;

        int lowVOnFixed = fixedEdge[0];
        int highVOnFixed = fixedEdge[fixedEdge.Count - 1];

        for (int i = 0; i < materialUv.Length; i++)
        {
            if (Mathf.Abs(materialUv[i].x - minU) < 1e-4f)
            {
                u0Mean += initialWorldPositions[i];
                n0++;
            }

            if (Mathf.Abs(materialUv[i].x - maxU) < 1e-4f)
            {
                u1Mean += initialWorldPositions[i];
                n1++;
            }
        }

        u0Mean /= Mathf.Max(n0, 1);
        u1Mean /= Mathf.Max(n1, 1);

        materialUDirection =
            (u1Mean - u0Mean).normalized;

        Vector3 vRaw =
            initialWorldPositions[highVOnFixed] -
            initialWorldPositions[lowVOnFixed];

        vRaw -=
            Vector3.Dot(vRaw, materialUDirection) *
            materialUDirection;

        materialVDirection = vRaw.normalized;

        materialNormal = Vector3.Cross(
            materialUDirection,
            materialVDirection
        ).normalized;

        if (Vector3.Dot(materialNormal, Vector3.up) < 0.0f)
            materialNormal = -materialNormal;
    }

    private void ApplyReferenceSettings()
    {
        cloth.solver.simulateWhenInvisible = true;

        cloth.selfCollisions = true;
        cloth.surfaceCollisions = false;
        cloth.aerodynamicsEnabled = false;
        cloth.tetherConstraintsEnabled = false;
        cloth.volumeConstraintsEnabled = false;

        // Keep the already selected reference stretch/bend settings.
        cloth.distanceConstraintsEnabled = true;
        cloth.bendConstraintsEnabled = true;

        cloth.solver.gravity = Vector3.zero;

        Oni.SolverParameters parameters =
            cloth.solver.parameters;

        parameters.gravity = Vector3.zero;
        parameters.damping = referenceSolverDamping;
        parameters.interpolation =
            Oni.SolverParameters.Interpolation.None;

        cloth.solver.parameters = parameters;

        Oni.ConstraintParameters particleCollision =
            cloth.solver.particleCollisionConstraintParameters;

        particleCollision.enabled = true;
        particleCollision.iterations = particleCollisionIterations;

        cloth.solver.particleCollisionConstraintParameters =
            particleCollision;

        Oni.ConstraintParameters particleFriction =
            cloth.solver.particleFrictionConstraintParameters;

        particleFriction.enabled = true;
        particleFriction.iterations = particleFrictionIterations;

        cloth.solver.particleFrictionConstraintParameters =
            particleFriction;

        cloth.solver.PushSolverParameters();

        originalCollisionMaterial =
            cloth.collisionMaterial;

        runtimeCollisionMaterial =
            ScriptableObject.CreateInstance<ObiCollisionMaterial>();

        float friction =
            mode == BenchmarkMode.CornerFoldCompression
            ? 0.0f
            : referenceSelfFriction;

        runtimeCollisionMaterial.dynamicFriction = friction;
        runtimeCollisionMaterial.staticFriction = friction;
        runtimeCollisionMaterial.stickiness = 0.0f;
        runtimeCollisionMaterial.stickDistance = 0.0f;
        runtimeCollisionMaterial.frictionCombine =
            Oni.MaterialCombineMode.Minimum;
        runtimeCollisionMaterial.stickinessCombine =
            Oni.MaterialCombineMode.Minimum;
        runtimeCollisionMaterial.rollingContacts = false;
        runtimeCollisionMaterial.UpdateMaterial();

        cloth.collisionMaterial =
            runtimeCollisionMaterial;

        Debug.Log(
            "Obi corner self-contact reference: mode=" + mode +
            ", damping=" + referenceSolverDamping +
            ", self friction=" + friction +
            ", particle collision iterations=" +
            particleCollisionIterations +
            ", particle friction iterations=" +
            particleFrictionIterations
        );
    }

    private void ApplyControls(float time)
    {
        // ONLY ONE PARTICLE IS KINEMATICALLY CONTROLLED:
        // the material corner nearest (u,v) = (1,1).
        //
        // The u=0 edge is NOT fixed. It is stored in fixedEdge only
        // to construct the initial material coordinate frame.
        ControlParticle(
            movingCornerIndex,
            MovingCornerTarget(time)
        );
    }

    private Vector3 MovingCornerTarget(float time)
    {
        if (time <= FoldStart)
            return movingCornerInitial;

        if (time < FoldEnd)
        {
            float fraction = SmoothStep01(
                (time - FoldStart) /
                Mathf.Max(foldSeconds, 1e-6f)
            );

            return SemicircularFoldTarget(fraction);
        }

        return
            targetPointInitial +
            materialNormal * CurrentGap(time) +
            materialVDirection * CurrentSlideOffset(time);
    }

    private Vector3 SemicircularFoldTarget(float fraction)
    {
        Vector3 center =
            0.5f * (movingCornerInitial + targetPointInitial);

        Vector3 radial =
            0.5f * (movingCornerInitial - targetPointInitial);

        float radius = radial.magnitude;
        float theta = Mathf.PI * fraction;
        float gapFraction =
            0.5f * (1.0f - Mathf.Cos(theta));

        return
            center +
            Mathf.Cos(theta) * radial +
            Mathf.Sin(theta) * radius * materialNormal +
            gapFraction * approachGap * materialNormal;
    }

    private float CurrentGap(float time)
    {
        if (time <= FoldEnd)
            return approachGap;

        if (time >= CompressionEnd)
            return compressedGap;

        float fraction = SmoothStep01(
            (time - FoldEnd) /
            Mathf.Max(compressionSeconds, 1e-6f)
        );

        return Mathf.Lerp(
            approachGap,
            compressedGap,
            fraction
        );
    }

    private float CurrentSlideOffset(float time)
    {
        if (mode != BenchmarkMode.CornerFoldSlide ||
            time <= SlideStart)
        {
            return 0.0f;
        }

        if (time >= SlideEnd)
            return slideDistance;

        float fraction = SmoothStep01(
            (time - SlideStart) /
            Mathf.Max(slideSeconds, 1e-6f)
        );

        return slideDistance * fraction;
    }

    private float SmoothStep01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3.0f - 2.0f * t);
    }

    private string CurrentPhase(float time)
    {
        if (time < FoldStart)
            return "rest";

        if (time < FoldEnd)
            return "fold";

        if (time < CompressionEnd)
            return "compress";

        if (mode == BenchmarkMode.CornerFoldCompression)
            return "hold";

        if (time < SlideStart)
            return "prehold";

        if (time < SlideEnd)
            return "slide";

        return "hold";
    }

    private void ControlParticle(
        int actorIndex,
        Vector3 targetWorldPosition)
    {
        int solverIndex =
            cloth.solverIndices[actorIndex];

        cloth.solver.invMasses[solverIndex] = 0.0f;

        Vector3 local =
            cloth.solver.transform.InverseTransformPoint(
                targetWorldPosition
            );

        Vector4 position =
            cloth.solver.positions[solverIndex];

        position.x = local.x;
        position.y = local.y;
        position.z = local.z;

        cloth.solver.positions[solverIndex] = position;
        cloth.solver.prevPositions[solverIndex] = position;
        cloth.solver.startPositions[solverIndex] = position;
        cloth.solver.renderablePositions[solverIndex] = position;
        cloth.solver.velocities[solverIndex] = Vector4.zero;
    }

    private void OpenCsv()
    {
        string directory = Path.Combine(
            Application.dataPath,
            "Exports",
            outputFolderName
        );

        Directory.CreateDirectory(directory);

        string fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}_{2:yyyyMMdd_HHmmss}.csv",
            runName,
            mode,
            DateTime.Now
        );

        outputPath = Path.Combine(directory, fileName);
        writer = new StreamWriter(outputPath, false);

        writer.WriteLine(
            "simulator,experiment,parameter_name,parameter_value," +
            "time_s,phase,node_index,u,v,x,y,z,controlled," +
            "obi_self_friction,obi_solver_damping,dt," +
            "target_u,target_v,approach_gap,compressed_gap,slide_distance"
        );
    }

    private void WriteFrame(float time)
    {
        string phase = CurrentPhase(time);

        Oni.SolverParameters parameters =
            cloth.solver.parameters;

        float activeFriction =
            mode == BenchmarkMode.CornerFoldCompression
            ? 0.0f
            : referenceSelfFriction;

        for (int actorIndex = 0;
             actorIndex < materialUv.Length;
             actorIndex++)
        {
            int solverIndex =
                cloth.solverIndices[actorIndex];

            Vector3 p =
                cloth.GetParticlePosition(solverIndex);

            Vector2 uv = materialUv[actorIndex];

            writer.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Obi,{0},reference,NaN,{1:F6},{2}," +
                "{3},{4:F6},{5:F6},{6:F9},{7:F9},{8:F9}," +
                "{9},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R}," +
                "{16:R},{17:R}",
                mode,
                time,
                phase,
                actorIndex,
                uv.x,
                uv.y,
                p.x,
                p.y,
                p.z,
                controlledNodes.Contains(actorIndex) ? 1 : 0,
                activeFriction,
                parameters.damping,
                frameTime,
                targetU,
                targetV,
                approachGap,
                compressedGap,
                slideDistance
            ));
        }
    }

    private void RestoreParticleMasses()
    {
        if (originalInverseMasses == null ||
            cloth == null ||
            cloth.solver == null ||
            cloth.solverIndices == null)
        {
            return;
        }

        for (int actorIndex = 0;
             actorIndex < originalInverseMasses.Length;
             actorIndex++)
        {
            int solverIndex =
                cloth.solverIndices[actorIndex];

            cloth.solver.invMasses[solverIndex] =
                originalInverseMasses[actorIndex];
        }
    }

    private void RestoreCollisionMaterial()
    {
        if (cloth != null)
            cloth.collisionMaterial = originalCollisionMaterial;

        if (runtimeCollisionMaterial != null)
        {
            Destroy(runtimeCollisionMaterial);
            runtimeCollisionMaterial = null;
        }
    }

    private void FinishExperiment()
    {
        if (finished)
            return;

        finished = true;

        RestoreParticleMasses();
        RestoreCollisionMaterial();

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }

        Debug.Log(
            "Obi corner self-collision CSV saved to:\n" +
            outputPath
        );

#if UNITY_EDITOR
        if (stopPlayModeWhenFinished)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnDestroy()
    {
        RestoreParticleMasses();
        RestoreCollisionMaterial();

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }
}
