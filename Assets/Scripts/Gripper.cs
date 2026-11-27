using System;
using System.Collections.Generic;
using Obi;
using UnityEngine;
using System.Linq;

/// <summary>
/// Keyboard version of Clothilde's Gripper.cs for a regular XZ Obi cloth.
/// Uses the existing ObiMaterialGrid in Assets/Scripts/SimpleStretch.
/// No rigidbody, FixedJoint, attachment component, or SteamVR is required.
/// </summary>
public class Gripper : MonoBehaviour
{
    [Header("Cloth")]
    public ObiCloth cloth;
    private int nodesX;
    private int nodesZ;

    [Header("Grasp frame")]
    public Transform graspFrame;
    private Transform Frame { get { return graspFrame != null ? graspFrame : transform; } }
    public Vector3 initialBoxCenter = new Vector3(-0.35f, 0.75f, 0f);
    public Vector3 initialEulerAngles = new Vector3(0f, 90f, 0f);

    [Header("Keyboard control")]
    public bool enableKeyboardTranslation = true;
    public bool enableKeyboardRotation = true;
    public float translationSpeed = 0.1f;
    public float rotationSpeed = 50f;

    [Header("Grasp box visual")]
    public Transform graspBoxVisual;
    public Vector3 graspBoxSize = new Vector3(0.04f, 0.04f, 0.04f);

    [Header("Squeeze")]
    public bool enableSqueeze = true;
    [Range(0f, 1f)] public float squeezeAmount = 0.5f;
    [Range(0.001f, 1f)] public float squeezeAlphaStep = 0.1f;
    private float squeezeAlpha = 1f;

    [Header("Optional CAD gripper (visual only)")]
    public Transform gripperAssembly;
    public Transform leftJaw;
    public Transform rightJaw;
    public float gripperAssemblyScale = 0.04f;
    public float t = 0.06f;
    public Vector3 gripperAssemblyOffsetDirection = Vector3.up;
    public float openGap = 0f;
    public float closedGap = 0.96f;
    public float jawSpeed = 1.5f;
    private float currentGap;
    private float targetGap;

    [Header("Release")]
    public bool preserveReleaseVelocity = true;
    [Header("Diagnostics")]
    [SerializeField] private int graspedCount;
    public bool logSelectedNodes = true;
    private bool isGrasping;
    private ObiCloth subscribedCloth;
    private ObiSolver graspSolver;
    // ObiCloth provides access to the physical particles, 
    // while ObiMaterialGrid reconstructs their material-grid connectivity.
    private ObiMaterialGrid grid;
    private float stepDuration;
    private float stepElapsed;
    private readonly List<HeldNode> held = new List<HeldNode>();

    // Combines multiple dictionaries of Clothilde (localNodeRestOffsets, localNodeGoalOffsets
    // localNodeOffsets) into one class
    private sealed class HeldNode 
    {
        public int materialIndex, solverIndex;
        public float originalInverseMass;
        public bool massFrozen;
        public Vector3 restLocal, goalLocal;
        public Vector3 stepStart, stepTarget, lastVelocity;
    }

    private void Awake()
    {
        Frame.localPosition = initialBoxCenter;
        Frame.localRotation = Quaternion.Euler(initialEulerAngles);
        if (graspBoxVisual != null)
        {
            graspBoxVisual.localPosition = Vector3.zero;
            graspBoxVisual.localRotation = Quaternion.identity;
            graspBoxVisual.localScale = graspBoxSize;
        }
        if (gripperAssembly != null)
        {
            gripperAssembly.localPosition = gripperAssemblyOffsetDirection.normalized * t;
            gripperAssembly.localRotation = Quaternion.identity;
            gripperAssembly.localScale = Vector3.one * gripperAssemblyScale;
        }
        currentGap = targetGap = openGap;
        ApplyJawGap();
    }

    private void OnEnable()
    {
        subscribedCloth = cloth;
        if (subscribedCloth == null) return;
        subscribedCloth.OnPrepareStep += PrepareStep;
        subscribedCloth.OnSubstep += OnSubstep;
        subscribedCloth.OnBlueprintUnloaded += BlueprintUnloaded;
    }

    private void OnDisable()
    {
        ReleaseNodes();
        if (subscribedCloth != null)
        {
            subscribedCloth.OnPrepareStep -= PrepareStep;
            subscribedCloth.OnSubstep -= OnSubstep;
            subscribedCloth.OnBlueprintUnloaded -= BlueprintUnloaded;
        }
        subscribedCloth = null;
        grid = null;
    }

    private void BlueprintUnloaded(ObiActor actor, ObiActorBlueprint blueprint)
    {
        // Solver indices may already have been freed. Never write them here.
        ClearGrasp();
        grid = null;
    }

    private void Update()
    {
        if (enableKeyboardTranslation) TranslateBoxWithKeyboard();
        if (enableKeyboardRotation) RotateBoxWithKeyboard();
        if (Input.GetKeyDown(KeyCode.G) && !isGrasping)
        {
            targetGap = closedGap;
            GraspNodesInsideBox();
        }
        if (Input.GetKeyDown(KeyCode.R)) ReleaseNodes();
        if (isGrasping) squeezeAlpha = Mathf.Min(1f, squeezeAlpha + squeezeAlphaStep);
        currentGap = Mathf.MoveTowards(currentGap, targetGap, jawSpeed * Time.deltaTime);
        ApplyJawGap();
    }

    private void TranslateBoxWithKeyboard()
    {
        Vector3 motion = Vector3.zero;
        if (Input.GetKey(KeyCode.J)) motion.x -= 1f;
        if (Input.GetKey(KeyCode.L)) motion.x += 1f;
        if (Input.GetKey(KeyCode.I)) motion.z -= 1f;
        if (Input.GetKey(KeyCode.K)) motion.z += 1f;
        if (Input.GetKey(KeyCode.U)) motion.y -= 1f;
        if (Input.GetKey(KeyCode.O)) motion.y += 1f;
        if (motion.sqrMagnitude > 0f)
            Frame.position += motion.normalized * translationSpeed * Time.deltaTime;
    }

    private void RotateBoxWithKeyboard()
    {
        float a = rotationSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.N)) Frame.Rotate(Vector3.up, -a, Space.Self);
        if (Input.GetKey(KeyCode.M)) Frame.Rotate(Vector3.up, a, Space.Self);
        if (Input.GetKey(KeyCode.B)) Frame.Rotate(Vector3.right, -a, Space.Self);
        if (Input.GetKey(KeyCode.V)) Frame.Rotate(Vector3.right, a, Space.Self);
        if (Input.GetKey(KeyCode.Comma)) Frame.Rotate(Vector3.forward, -a, Space.Self);
        if (Input.GetKey(KeyCode.Period)) Frame.Rotate(Vector3.forward, a, Space.Self);
    }

    private bool IsInsideBox(Vector3 worldPoint)
    {
        Vector3 q = Frame.InverseTransformPoint(worldPoint);
        Vector3 h = graspBoxSize * 0.5f;
        return Mathf.Abs(q.x) <= h.x && Mathf.Abs(q.y) <= h.y && Mathf.Abs(q.z) <= h.z;
    }

    private int GridIndex(int x, int z) { return x * nodesZ + z; }

    private void GraspNodesInsideBox()
    {
        if (cloth == null || cloth != subscribedCloth || !cloth.isLoaded)
        {
            Debug.LogWarning("Gripper: assign a loaded ObiCloth before entering Play Mode.", this);
            return;
        }
        try
        {
            // Rebuild on each new grasp: topology is small, and no stale solver map is retained.
            var rest = cloth.clothBlueprint.positions;

            nodesX = rest.Select(p => Mathf.RoundToInt(p.x * 1e6f))
                        .Distinct().Count();

            nodesZ = rest.Select(p => Mathf.RoundToInt(p.z * 1e6f))
                        .Distinct().Count();

            grid = new ObiMaterialGrid(cloth, nodesX, nodesZ);
        }
        catch (Exception ex)
        {
            Debug.LogError("Gripper: " + ex.Message, this);
            return;
        }
        Vector3[] p = grid.Read();
        var selected = new HashSet<int>(); // Hashset to avoid repititions: unique elements of the set.
        int vertexHits = 0, edgeHits = 0, quadHits = 0;
        // Same three classes of candidates as Clothilde's TriangleMesh.
        for (int x = 0; x < nodesX; x++)
        for (int z = 0; z < nodesZ; z++)
        {
            int i = GridIndex(x, z);
            if (IsInsideBox(p[i])) { selected.Add(i); vertexHits++; }
            if (x + 1 < nodesX)
            {
                int j = GridIndex(x + 1, z);
                if (IsInsideBox((p[i] + p[j]) * 0.5f))
                { selected.Add(i); selected.Add(j); edgeHits++; }
            }
            if (z + 1 < nodesZ)
            {
                int j = GridIndex(x, z + 1);
                if (IsInsideBox((p[i] + p[j]) * 0.5f))
                { selected.Add(i); selected.Add(j); edgeHits++; }
            }
            if (x + 1 < nodesX && z + 1 < nodesZ)
            {
                int j = GridIndex(x + 1, z);
                int k = GridIndex(x + 1, z + 1);
                int l = GridIndex(x, z + 1);
                if (IsInsideBox((p[i] + p[j] + p[k] + p[l]) * 0.25f))
                { selected.Add(i); selected.Add(j); selected.Add(k); selected.Add(l); quadHits++; }
            }
        }
        graspSolver = cloth.solver;
        foreach (int i in selected)
        {
            int k = grid.Index(i);
            float w = graspSolver.invMasses[k];
            // Do not steal a particle already fixed by another controller/attachment.
            if (w <= 0f) continue;
            Vector3 rest = Frame.InverseTransformPoint(p[i]);
            Vector3 goal = rest;
            if (enableSqueeze) goal.x *= 1f - squeezeAmount;
            held.Add(new HeldNode {
                materialIndex = i, solverIndex = k, originalInverseMass = w,
                restLocal = rest, goalLocal = goal,
                lastVelocity = Vector3.zero
            });
        }
        graspedCount = held.Count;
        isGrasping = graspedCount > 0;
        squeezeAlpha = 0f;
        stepDuration = stepElapsed = 0f;
        if (logSelectedNodes)
        {
            var ids = new List<int>();
            foreach (HeldNode n in held) ids.Add(grid.map[n.materialIndex]);
            Debug.Log($"Gripper: {graspedCount} particles; candidate hits (node/edge/quad) " +
                      $"{vertexHits}/{edgeHits}/{quadHits}; actor indices: {string.Join(", ", ids)}", this);
        }
    }

    private bool HasValidGrasp()
    {
        return isGrasping && cloth != null && cloth.isLoaded && cloth.solver == graspSolver && graspSolver != null;
    }

    // Like DragHandle(): compute target positions
    private void PrepareStep(ObiActor actor, float dt)
    {
        if (!HasValidGrasp()) return;
        stepDuration = dt;
        stepElapsed = 0f;
        foreach (HeldNode n in held)
        {
            // Convert the desired position from the gripper frame to the world frame and 
            // finally to the solver frame.
            int k = n.solverIndex;
            n.stepStart = (Vector3)graspSolver.positions[k];
            Vector3 local = Vector3.Lerp(n.restLocal, n.goalLocal, squeezeAlpha);
            n.stepTarget = graspSolver.transform.InverseTransformPoint(Frame.TransformPoint(local));
            // Freeze at the beginning of the physics step, before constraint projection.
            graspSolver.invMasses[k] = 0f; // temporarily makes the particle kinematic
            n.massFrozen = true;
            graspSolver.velocities[k] = Vector4.zero;
        }
    }

    // Instead of moving the particle directly to its final target, 
    // we interpolate its motion across the physics substeps.
    private void OnSubstep(ObiActor actor, float h)
    {
        if (!HasValidGrasp() || stepDuration <= 0f || h <= 0f) return;
        stepElapsed = Mathf.Min(stepDuration, stepElapsed + h);
        float a = Mathf.Clamp01(stepElapsed / stepDuration);
        foreach (HeldNode n in held)
        {
            int k = n.solverIndex;
            Vector3 old = (Vector3)graspSolver.positions[k];
            Vector3 next = Vector3.Lerp(n.stepStart, n.stepTarget, a);
            n.lastVelocity = (next - old) / h;
            // Obi's predictor integrates even a zero-inverse-mass particle if velocity is nonzero.
            // Write prescribed positions directly and keep velocity zero while held.
            graspSolver.invMasses[k] = 0f;
            graspSolver.positions[k] = (Vector4)next;
            graspSolver.velocities[k] = Vector4.zero;
        }
    }

    private void ReleaseNodes()
    {
        if (HasValidGrasp())
        {
            foreach (HeldNode n in held)
            {
                // If no simulation step has happened, nothing was changed.
                if (!n.massFrozen) continue;
                graspSolver.invMasses[n.solverIndex] = n.originalInverseMass;
                graspSolver.velocities[n.solverIndex] = preserveReleaseVelocity ? (Vector4)n.lastVelocity : Vector4.zero;
            }
        }
        ClearGrasp();
    }

    private void ClearGrasp()
    {
        held.Clear();
        graspedCount = 0;
        isGrasping = false;
        graspSolver = null;
        squeezeAlpha = 1f;
        stepDuration = stepElapsed = 0f;
        targetGap = openGap;
    }

    private void ApplyJawGap()
    {
        if (leftJaw != null)
        {
            Vector3 p = leftJaw.localPosition;
            p.x = -0.5f * currentGap;
            leftJaw.localPosition = p;
        }
        if (rightJaw != null)
        {
            Vector3 p = rightJaw.localPosition;
            p.x = 0.5f * currentGap;
            rightJaw.localPosition = p;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (Frame == null) return;
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Frame.localToWorldMatrix;
        Gizmos.color = isGrasping ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, graspBoxSize);
        Gizmos.matrix = old;
    }
}
