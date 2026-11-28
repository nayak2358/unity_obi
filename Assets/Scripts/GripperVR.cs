using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using Obi;
using System.Linq;

// Single-file Obi port of Clothilde's GripperVR.
// Add one instance to each SteamVR controller, pointing to the same ObiCloth.
// Supports intact, regular rectangular cloth grids in the blueprint's XZ plane.
[DefaultExecutionOrder(-100)]
public class GripperVR : MonoBehaviour
{
    [Header("Cloth")]
    public ObiCloth cloth;

    [Header("Grasp frame")]
    public Transform graspFrame;
    public Vector3 controllerLocalOffset = new Vector3(0f, 0f, 0.2f);
    public Vector3 controllerLocalEulerAngles = new Vector3(0f, 90f, 0f);
    public bool configureGraspFrameOnAwake = true;

    [Header("Grasp box visual")]
    public Transform graspBoxVisual;
    public Vector3 graspBoxSize = new Vector3(0.06f, 0.06f, 0.06f);

    [Header("Visual gripper assembly (optional)")]
    public Transform gripperAssembly;
    public float gripperAssemblyScale = 0.06f;
    public float t = 0.06f;
    public Vector3 gripperAssemblyOffsetDirection = new Vector3(-1.7f, 1.35f, -1.7f);
    public Transform leftJaw;
    public Transform rightJaw;
    public float openGap = 0f;
    public float closedGap = 0.04f * (30f - 6f);
    public float jawSpeed = 1.5f;

    [Header("Squeeze")]
    public bool enableSqueeze = true;
    [Range(0f, 1f)] public float squeezeAmount = 0.5f;
    [Range(0f, 1f)] public float squeezeAlphaStep = 0.1f;

    [Header("Particle control")]
    [Tooltip("Zero means no displacement limit. Match the control policy in both simulators for comparisons.")]
    [Min(0f)] public float maxDisplacementPerSubstep = 0f;

    [Header("VR input")]
    public SteamVR_Action_Boolean m_GrabAction;
    public SteamVR_Behaviour_Pose pose;
    public bool enableKeyboard = false;
    public KeyCode graspKey = KeyCode.G;
    public KeyCode releaseKey = KeyCode.R;

    private class GraspedNode
    {
        public int solverIndex;
        public float originalInverseMass;
        public Vector3 restLocal, goalLocal;
        public Vector3 targetWorld, startSolver, goalSolver, lastVelocity;
    }

    private readonly Dictionary<int, GraspedNode> graspedNodes = new Dictionary<int, GraspedNode>();
    private readonly List<int[]> candidates = new List<int[]>();
    private ObiActorBlueprint cachedBlueprint;
    private ObiCloth subscribedCloth;
    private bool gridValid;
    private float squeezeAlpha = 1f;
    private float stepDuration, stepProgress;
    private float currentGap, targetGap;
    private Transform Frame { get { return graspFrame != null ? graspFrame : transform; } }
    public bool IsGrasping { get { return graspedNodes.Count > 0; } }
    public int GraspedCount { get { return graspedNodes.Count; } }

    private void Awake()
    {
        if (pose == null) pose = GetComponent<SteamVR_Behaviour_Pose>();
        if (pose == null) pose = GetComponentInParent<SteamVR_Behaviour_Pose>();
        if (configureGraspFrameOnAwake && graspFrame != null && graspFrame != transform)
        {
            graspFrame.localPosition = controllerLocalOffset;
            graspFrame.localRotation = Quaternion.Euler(controllerLocalEulerAngles);
        }
        if (graspBoxVisual != null && graspBoxVisual != Frame)
        {
            graspBoxVisual.localPosition = Vector3.zero;
            graspBoxVisual.localRotation = Quaternion.identity;
            graspBoxVisual.localScale = graspBoxSize;
        }
        if (gripperAssembly != null && gripperAssembly != Frame)
        {
            gripperAssembly.localRotation = Quaternion.identity;
            gripperAssembly.localPosition = gripperAssemblyOffsetDirection * t;
            gripperAssembly.localScale = Vector3.one * gripperAssemblyScale;
        }
        currentGap = targetGap = openGap;
        ApplyJawGap(currentGap);
    }

    private void OnEnable()
    {
        subscribedCloth = cloth;
        if (subscribedCloth == null) return;
        subscribedCloth.OnBeginStep += BeginStep;
        subscribedCloth.OnSubstep += Substep;
        subscribedCloth.OnBlueprintLoaded += OnBlueprintChanged;
        subscribedCloth.OnBlueprintUnloaded += OnBlueprintChanged;
    }

    private void OnDisable()
    {
        ReleaseNodes();
        if (subscribedCloth != null)
        {
            subscribedCloth.OnBeginStep -= BeginStep;
            subscribedCloth.OnSubstep -= Substep;
            subscribedCloth.OnBlueprintLoaded -= OnBlueprintChanged;
            subscribedCloth.OnBlueprintUnloaded -= OnBlueprintChanged;
        }
        subscribedCloth = null;
        cachedBlueprint = null;
        gridValid = false;
    }

    private void OnBlueprintChanged(ObiActor actor, ObiActorBlueprint blueprint)
    {
        // The solver indices are being discarded/recreated. Never restore old
        // inverse masses into the new solver slots.
        graspedNodes.Clear();
        cachedBlueprint = null;
        gridValid = false;
        squeezeAlpha = 1f;
        targetGap = openGap;
    }

    private bool Ready()
    {
        return subscribedCloth != null && subscribedCloth == cloth &&
               cloth.isLoaded && cloth.solver != null;
    }

    private void Update()
    {
        bool vr = pose != null && m_GrabAction != null;
        if ((vr && m_GrabAction.GetStateDown(pose.inputSource)) ||
            (enableKeyboard && Input.GetKeyDown(graspKey)))
        {
            if (!IsGrasping)
            {
                targetGap = closedGap;
                GraspNodesInsideBox();
            }
        }
        if ((vr && m_GrabAction.GetStateUp(pose.inputSource)) ||
            (enableKeyboard && Input.GetKeyDown(releaseKey)))
            ReleaseNodes();

        if (IsGrasping && Ready()) UpdateGraspedNodePositions();
        else if (IsGrasping) ReleaseNodes();

        currentGap = Mathf.MoveTowards(currentGap, targetGap, jawSpeed * Time.deltaTime);
        ApplyJawGap(currentGap);
    }

    // All box coordinates are in metres; ignore hierarchy scale, like a rigid frame.
    private Vector3 WorldToGrasp(Vector3 world)
    {
        return Quaternion.Inverse(Frame.rotation) * (world - Frame.position);
    }
    private Vector3 GraspToWorld(Vector3 local)
    {
        return Frame.position + Frame.rotation * local;
    }
    private bool IsInsideBox(Vector3 world)
    {
        Vector3 p = WorldToGrasp(world), h = graspBoxSize * 0.5f;
        return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
    }

    private bool BuildCandidates()
    {
        var bp = cloth.sourceBlueprint;
        if (bp == null || bp.positions == null) return false;
        if (cachedBlueprint == bp) return gridValid;
        cachedBlueprint = bp;

        int nodesX = bp.positions.Select(p => Mathf.RoundToInt(p.x * 1e6f)).Distinct().Count();
        int nodesZ = bp.positions.Select(p => Mathf.RoundToInt(p.z * 1e6f)).Distinct().Count();


        gridValid = false;
        candidates.Clear();
        int n = nodesX * nodesZ;
        if (nodesX < 2 || nodesZ < 2 || bp.positions.Length != n || cloth.activeParticleCount != n)
        {
            Debug.LogError("GripperVR: expected a complete regular XZ grid; check Nodes X/Z.", this);
            return false;
        }
        float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity;
        float z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
        foreach (Vector3 p in bp.positions)
        {
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
            z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
        }
        float dx = (x1 - x0) / (nodesX - 1), dz = (z1 - z0) / (nodesZ - 1);
        if (dx <= 0f || dz <= 0f) return false;
        int[,] grid = new int[nodesX, nodesZ];
        for (int x = 0; x < nodesX; x++)
            for (int z = 0; z < nodesZ; z++) grid[x, z] = -1;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = bp.positions[i];
            int x = Mathf.RoundToInt((p.x - x0) / dx), z = Mathf.RoundToInt((p.z - z0) / dz);
            if (x < 0 || x >= nodesX || z < 0 || z >= nodesZ || grid[x, z] >= 0 ||
                Mathf.Abs(p.x - (x0 + x * dx)) > dx * 0.01f ||
                Mathf.Abs(p.z - (z0 + z * dz)) > dz * 0.01f)
            {
                Debug.LogError("GripperVR: blueprint is not a regular XZ grid.", this);
                return false;
            }
            grid[x, z] = i;
            candidates.Add(new[] { i });
        }
        // Material edges and quadrilateral centres, NOT triangulation diagonals.
        for (int x = 0; x < nodesX; x++)
            for (int z = 0; z < nodesZ; z++)
            {
                if (x + 1 < nodesX) candidates.Add(new[] { grid[x,z], grid[x+1,z] });
                if (z + 1 < nodesZ) candidates.Add(new[] { grid[x,z], grid[x,z+1] });
                if (x + 1 < nodesX && z + 1 < nodesZ)
                    candidates.Add(new[] { grid[x,z], grid[x+1,z], grid[x+1,z+1], grid[x,z+1] });
            }
        gridValid = true;
        Debug.Log($"{name}: Obi grasp grid: {n} nodes, {candidates.Count} candidates.", this);
        return true;
    }

    public void GraspNodesInsideBox()
    {
        if (IsGrasping || !Ready() || !BuildCandidates()) return;
        var solver = cloth.solver;
        Vector3[] world = new Vector3[cloth.activeParticleCount];
        for (int i = 0; i < world.Length; i++)
            world[i] = solver.transform.TransformPoint((Vector3)solver.positions[cloth.solverIndices[i]]);
        var selected = new HashSet<int>();
        foreach (int[] candidate in candidates)
        {
            Vector3 p = Vector3.zero;
            foreach (int i in candidate) p += world[i];
            if (IsInsideBox(p / candidate.Length))
                foreach (int i in candidate) selected.Add(i);
        }
        foreach (int i in selected)
        {
            int k = cloth.solverIndices[i];
            float w = solver.invMasses[k];
            // A second gripper cannot acquire an already controlled particle;
            // permanently fixed particles are also left alone.
            if (w <= 0f) continue;
            Vector3 rest = WorldToGrasp(world[i]);
            Vector3 goal = rest;
            if (enableSqueeze) goal.x *= 1f - squeezeAmount;
            graspedNodes.Add(i, new GraspedNode
            {
                solverIndex = k, originalInverseMass = w,
                restLocal = rest, goalLocal = goal, targetWorld = world[i],
                lastVelocity = Vector3.zero
            });
            solver.invMasses[k] = 0f;
            solver.velocities[k] = Vector4.zero;
        }
        squeezeAlpha = 0f;
        Debug.Log($"{name}: VR grasped nodes: {graspedNodes.Count}", this);
    }

    private void UpdateGraspedNodePositions()
    {
        squeezeAlpha = Mathf.Min(1f, squeezeAlpha + squeezeAlphaStep);
        foreach (GraspedNode node in graspedNodes.Values)
            node.targetWorld = GraspToWorld(Vector3.Lerp(node.restLocal, node.goalLocal, squeezeAlpha));
    }

    private void BeginStep(ObiActor actor, float dt)
    {
        stepDuration = dt;
        stepProgress = 0f;
        if (!Ready()) return;
        foreach (GraspedNode node in graspedNodes.Values)
        {
            node.startSolver = (Vector3)cloth.solver.positions[node.solverIndex];
            node.goalSolver = cloth.solver.transform.InverseTransformPoint(node.targetWorld);
        }
    }

    private void Substep(ObiActor actor, float dt)
    {
        if (!Ready() || dt <= 0f || stepDuration <= 0f) return;
        stepProgress = Mathf.Clamp01(stepProgress + dt / stepDuration);
        var solver = cloth.solver;
        foreach (GraspedNode node in graspedNodes.Values)
        {
            int k = node.solverIndex;
            Vector3 before = (Vector3)solver.positions[k];
            Vector3 next = Vector3.Lerp(node.startSolver, node.goalSolver, stepProgress);
            if (maxDisplacementPerSubstep > 0f)
                next = Vector3.MoveTowards(before, next, maxDisplacementPerSubstep);
            node.lastVelocity = (next - before) / dt;
            solver.invMasses[k] = 0f;
            solver.positions[k] = (Vector4)next;
            // Obi's Burst predictor integrates even zero-inverse-mass particles
            // using their velocity, so zero it after prescribing the position.
            solver.velocities[k] = Vector4.zero;
        }
    }

    public void ReleaseNodes()
    {
        var c = subscribedCloth;
        if (c != null && c.isLoaded && c.solver != null)
            foreach (var entry in graspedNodes)
            {
                GraspedNode node = entry.Value;
                if (entry.Key < c.activeParticleCount && c.solverIndices[entry.Key] == node.solverIndex)
                {
                    c.solver.invMasses[node.solverIndex] = node.originalInverseMass;
                    c.solver.velocities[node.solverIndex] = (Vector4)node.lastVelocity;
                }
            }
        graspedNodes.Clear();
        squeezeAlpha = 1f;
        targetGap = openGap;
    }

    private void ApplyJawGap(float gap)
    {
        if (leftJaw != null)
        {
            Vector3 p = leftJaw.localPosition;
            p.x = -gap * 0.5f;
            leftJaw.localPosition = p;
        }
        if (rightJaw != null)
        {
            Vector3 p = rightJaw.localPosition;
            p.x = gap * 0.5f;
            rightJaw.localPosition = p;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(Frame.position, Frame.rotation, Vector3.one);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, graspBoxSize);
        Gizmos.matrix = previous;
    }
}
