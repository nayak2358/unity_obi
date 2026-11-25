using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Obi;
using UnityEngine;

/// <summary>
/// Scripted, non-VR Obi reference experiments for Clothilde calibration.
///
/// No ObiParticleAttachment components or manually created particle groups
/// are required. The script detects the u-min and u-max material edges from
/// the input mesh UVs, temporarily sets controlled particles to zero inverse
/// mass, writes their target positions directly into the Obi solver, and
/// restores their original masses at release.
///
/// Add this component to an empty GameObject, assign the ObiCloth, and remove
/// or disable any old particle attachments in the calibration scene.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ObiCalibrationBenchmark : MonoBehaviour
{
    public enum CalibrationMode
    {
        StretchPulse,
        ShearPulse,
        CantileverSag,
        CantileverRelease
    }

    [Header("References")]
    public ObiCloth cloth;

    [Header("Experiment")]
    public CalibrationMode mode = CalibrationMode.StretchPulse;
    public string runName = "obi_reference";
    public bool applyRecommendedIsolationSettings = true;
    public bool stopPlayModeWhenFinished = true;

    [Header("Reference solver settings")]
    public float frameTime = 1.0f / 60.0f;
    public Vector3 gravity = new Vector3(0.0f, -9.8f, 0.0f);

    [Tooltip(
        "Used only by CantileverRelease. Keep this fixed during " +
        "the entire calibration campaign."
    )]
    [Range(0.0f, 1.0f)]
    public float referenceSolverDamping = 0.0f;

    [Header("Stretch / shear pulse")]
    public float pulseRestSeconds = 1.0f;
    public float pulseMoveSeconds = 0.5f;
    public float pulseHoldSeconds = 0.5f;
    public float pulseFreeSeconds = 3.0f;
    public float stretchAmplitude = 0.035f;
    public float shearAmplitude = 0.070f;

    [Header("Cantilever sag")]
    public float sagDuration = 10.0f;

    [Header("Cantilever release")]
    public float cantileverSettleSeconds = 5.0f;
    public float cantileverLiftSeconds = 0.5f;
    public float cantileverHoldSeconds = 0.5f;
    public float cantileverRingDownSeconds = 5.0f;
    public float cantileverLiftAmplitude = 0.10f;

    [Header("Output")]
    public string outputFolderName = "Calibration";

    private readonly List<int> fixedEdge =
        new List<int>();

    private readonly List<int> movingEdge =
        new List<int>();

    private readonly HashSet<int> fixedEdgeSet =
        new HashSet<int>();

    private readonly HashSet<int> movingEdgeSet =
        new HashSet<int>();

    private Vector2[] materialUv;
    private Vector3[] initialWorldPositions;
    private Vector3[] capturedMovingEdgePositions;
    private float[] originalInverseMasses;

    private Vector3 stretchDirection;
    private Vector3 shearDirection;

    private StreamWriter writer;
    private float simulationTime;
    private float lastRecordedTime = -1.0f;

    private bool initialized;
    private bool movingEdgeReleased;
    private bool capturedDampingEdge;
    private bool finished;

    private string outputPath;

    private float PulseReleaseTime
    {
        get
        {
            return pulseRestSeconds +
                   pulseMoveSeconds +
                   pulseHoldSeconds;
        }
    }

    private float DampingLiftStart
    {
        get { return cantileverSettleSeconds; }
    }

    private float DampingReleaseTime
    {
        get
        {
            return cantileverSettleSeconds +
                   cantileverLiftSeconds +
                   cantileverHoldSeconds;
        }
    }

    private float TotalDuration
    {
        get
        {
            switch (mode)
            {
                case CalibrationMode.StretchPulse:
                case CalibrationMode.ShearPulse:
                    return PulseReleaseTime + pulseFreeSeconds;

                case CalibrationMode.CantileverSag:
                    return sagDuration;

                case CalibrationMode.CantileverRelease:
                    return DampingReleaseTime +
                           cantileverRingDownSeconds;

                default:
                    return 5.0f;
            }
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

        // At a 60 Hz target rate and a 60 Hz fixed step this records once
        // per simulated frame. The guard avoids duplicate rows if LateUpdate
        // is called without a new FixedUpdate.
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
                "ObiCalibrationBenchmark: cloth blueprint or " +
                "input mesh is missing."
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
                "ObiCalibrationBenchmark: the input mesh must " +
                "contain one UV coordinate per particle."
            );
            enabled = false;
            return;
        }

        if (cloth.solverIndices == null ||
            cloth.solverIndices.Length < particleCount)
        {
            Debug.LogError(
                "ObiCalibrationBenchmark: solverIndices do not " +
                "match the input mesh."
            );
            enabled = false;
            return;
        }

        ApplyReferenceSolverSettings();

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

        BuildBoundaryIndices();
        ComputeMaterialDirections();
        OpenCsv();

        ApplyControls(0.0f);
        WriteFrame(0.0f);
        lastRecordedTime = 0.0f;

        initialized = true;

        Debug.Log(
            "ObiCalibrationBenchmark initialized with " +
            particleCount + " particles."
        );
    }

    private void ApplyReferenceSolverSettings()
    {
        cloth.solver.simulateWhenInvisible = true;

        if (applyRecommendedIsolationSettings)
        {
            cloth.selfCollisions = false;
            cloth.aerodynamicsEnabled = false;
            cloth.distanceConstraintsEnabled = true;

            bool usesBending =
                mode == CalibrationMode.CantileverSag ||
                mode == CalibrationMode.CantileverRelease;

            cloth.bendConstraintsEnabled = usesBending;
        }

        Vector3 selectedGravity =
            (mode == CalibrationMode.StretchPulse ||
             mode == CalibrationMode.ShearPulse)
            ? Vector3.zero
            : gravity;

        float selectedDamping =
            (mode == CalibrationMode.CantileverRelease ||
            mode == CalibrationMode.CantileverSag)
            ? referenceSolverDamping
            : 0.0f;

        cloth.solver.gravity = selectedGravity;

        Oni.SolverParameters parameters =
            cloth.solver.parameters;

        parameters.gravity = selectedGravity;
        parameters.damping = selectedDamping;
        parameters.interpolation =
            Oni.SolverParameters.Interpolation.None;

        cloth.solver.parameters = parameters;

        // IMPORTANT:
        // actually send the modified values to the Obi backend.
        cloth.solver.PushSolverParameters();
    }

    private void BuildBoundaryIndices()
    {
        fixedEdge.Clear();
        movingEdge.Clear();
        fixedEdgeSet.Clear();
        movingEdgeSet.Clear();

        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;

        foreach (Vector2 uv in materialUv)
        {
            minU = Mathf.Min(minU, uv.x);
            maxU = Mathf.Max(maxU, uv.x);
        }

        const float tolerance = 1e-4f;

        for (int i = 0; i < materialUv.Length; i++)
        {
            if (Mathf.Abs(materialUv[i].x - minU) <
                tolerance)
            {
                fixedEdge.Add(i);
                fixedEdgeSet.Add(i);
            }

            if (Mathf.Abs(materialUv[i].x - maxU) <
                tolerance)
            {
                movingEdge.Add(i);
                movingEdgeSet.Add(i);
            }
        }

        fixedEdge.Sort(
            (a, b) =>
                materialUv[a].y.CompareTo(materialUv[b].y)
        );

        movingEdge.Sort(
            (a, b) =>
                materialUv[a].y.CompareTo(materialUv[b].y)
        );

        if (fixedEdge.Count < 2 ||
            movingEdge.Count < 2)
        {
            Debug.LogError(
                "ObiCalibrationBenchmark: could not detect " +
                "the two material edges from UV coordinates."
            );
            enabled = false;
        }
    }

    private void ComputeMaterialDirections()
    {
        Vector3 fixedMean =
            MeanPosition(initialWorldPositions, fixedEdge);

        Vector3 movingMean =
            MeanPosition(initialWorldPositions, movingEdge);

        stretchDirection =
            (movingMean - fixedMean).normalized;

        Vector3 edgeDirection =
            initialWorldPositions[
                movingEdge[movingEdge.Count - 1]
            ] -
            initialWorldPositions[movingEdge[0]];

        edgeDirection -=
            Vector3.Dot(
                edgeDirection,
                stretchDirection
            ) * stretchDirection;

        shearDirection = edgeDirection.normalized;
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
            "obi_stretch_compliance,obi_bend_compliance," +
            "obi_solver_damping,dt"
        );
    }

    private void ApplyControls(float time)
    {
        // The u-min material edge is the fixed boundary in all tests.
        for (int i = 0; i < fixedEdge.Count; i++)
        {
            int actorIndex = fixedEdge[i];

            ControlParticle(
                actorIndex,
                initialWorldPositions[actorIndex]
            );
        }

        switch (mode)
        {
            case CalibrationMode.StretchPulse:
                ApplyPulseControl(
                    time,
                    stretchDirection,
                    stretchAmplitude
                );
                break;

            case CalibrationMode.ShearPulse:
                ApplyPulseControl(
                    time,
                    shearDirection,
                    shearAmplitude
                );
                break;

            case CalibrationMode.CantileverSag:
                ReleaseMovingEdgeOnce();
                break;

            case CalibrationMode.CantileverRelease:
                ApplyCantileverReleaseControl(time);
                break;
        }
    }

    private void ApplyPulseControl(
        float time,
        Vector3 direction,
        float amplitude)
    {
        if (time <= PulseReleaseTime)
        {
            float offset =
                amplitude * PulseSmoothFraction(time);

            foreach (int actorIndex in movingEdge)
            {
                ControlParticle(
                    actorIndex,
                    initialWorldPositions[actorIndex] +
                    direction * offset
                );
            }
        }
        else
        {
            ReleaseMovingEdgeOnce();
        }
    }

    private float PulseSmoothFraction(float time)
    {
        if (time <= pulseRestSeconds)
            return 0.0f;

        float moveEnd =
            pulseRestSeconds + pulseMoveSeconds;

        if (time < moveEnd)
        {
            float t =
                (time - pulseRestSeconds) /
                Mathf.Max(pulseMoveSeconds, 1e-6f);

            return t * t * (3.0f - 2.0f * t);
        }

        return 1.0f;
    }

    private void ApplyCantileverReleaseControl(float time)
    {
        if (time < DampingLiftStart)
        {
            ReleaseMovingEdgeOnce();
            return;
        }

        if (!capturedDampingEdge)
        {
            capturedMovingEdgePositions =
                new Vector3[movingEdge.Count];

            for (int i = 0; i < movingEdge.Count; i++)
            {
                int actorIndex = movingEdge[i];
                int solverIndex =
                    cloth.solverIndices[actorIndex];

                capturedMovingEdgePositions[i] =
                    cloth.GetParticlePosition(solverIndex);
            }

            capturedDampingEdge = true;
            movingEdgeReleased = false;
        }

        if (time <= DampingReleaseTime)
        {
            float fraction =
                CantileverLiftFraction(time);

            Vector3 offset =
                Vector3.up *
                cantileverLiftAmplitude *
                fraction;

            for (int i = 0; i < movingEdge.Count; i++)
            {
                ControlParticle(
                    movingEdge[i],
                    capturedMovingEdgePositions[i] +
                    offset
                );
            }
        }
        else
        {
            ReleaseMovingEdgeOnce();
        }
    }

    private float CantileverLiftFraction(float time)
    {
        float liftEnd =
            cantileverSettleSeconds +
            cantileverLiftSeconds;

        if (time < liftEnd)
        {
            float t =
                (time - cantileverSettleSeconds) /
                Mathf.Max(cantileverLiftSeconds, 1e-6f);

            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }

        return 1.0f;
    }

    private void ControlParticle(
        int actorIndex,
        Vector3 targetWorldPosition)
    {
        int solverIndex =
            cloth.solverIndices[actorIndex];

        cloth.solver.invMasses[solverIndex] = 0.0f;

        Vector3 localPosition =
            cloth.solver.transform.InverseTransformPoint(
                targetWorldPosition
            );

        Vector4 position =
            cloth.solver.positions[solverIndex];

        position.x = localPosition.x;
        position.y = localPosition.y;
        position.z = localPosition.z;

        cloth.solver.positions[solverIndex] = position;
        cloth.solver.prevPositions[solverIndex] = position;
        cloth.solver.startPositions[solverIndex] = position;
        cloth.solver.renderablePositions[solverIndex] = position;
        cloth.solver.velocities[solverIndex] = Vector4.zero;
    }

    private void ReleaseMovingEdgeOnce()
    {
        if (movingEdgeReleased)
            return;

        foreach (int actorIndex in movingEdge)
        {
            int solverIndex =
                cloth.solverIndices[actorIndex];

            cloth.solver.invMasses[solverIndex] =
                originalInverseMasses[actorIndex];

            // The target has been held before release, so zero velocity
            // avoids injecting an artificial impulse.
            cloth.solver.velocities[solverIndex] =
                Vector4.zero;
        }

        movingEdgeReleased = true;
    }

    private bool IsControlled(int actorIndex, float time)
    {
        if (fixedEdgeSet.Contains(actorIndex))
            return true;

        if (!movingEdgeSet.Contains(actorIndex))
            return false;

        switch (mode)
        {
            case CalibrationMode.StretchPulse:
            case CalibrationMode.ShearPulse:
                return time <= PulseReleaseTime;

            case CalibrationMode.CantileverSag:
                return false;

            case CalibrationMode.CantileverRelease:
                return time >= DampingLiftStart &&
                       time <= DampingReleaseTime;

            default:
                return false;
        }
    }

    private string CurrentPhase(float time)
    {
        switch (mode)
        {
            case CalibrationMode.StretchPulse:
            case CalibrationMode.ShearPulse:
                if (time < pulseRestSeconds)
                    return "rest";

                if (time <
                    pulseRestSeconds + pulseMoveSeconds)
                {
                    return "move";
                }

                if (time <= PulseReleaseTime)
                    return "hold";

                return "free";

            case CalibrationMode.CantileverSag:
                return "sag";

            case CalibrationMode.CantileverRelease:
                if (time < cantileverSettleSeconds)
                    return "settle";

                if (time <
                    cantileverSettleSeconds +
                    cantileverLiftSeconds)
                {
                    return "lift";
                }

                if (time <= DampingReleaseTime)
                    return "hold";

                return "ringdown";

            default:
                return "unknown";
        }
    }

    private void WriteFrame(float time)
    {
        string phase = CurrentPhase(time);
        int particleCount = materialUv.Length;

        Oni.SolverParameters parameters =
            cloth.solver.parameters;

        for (int actorIndex = 0;
             actorIndex < particleCount;
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
                "{9},{10:R},{11:R},{12:R},{13:R}",
                mode,
                time,
                phase,
                actorIndex,
                uv.x,
                uv.y,
                p.x,
                p.y,
                p.z,
                IsControlled(actorIndex, time) ? 1 : 0,
                cloth.stretchCompliance,
                cloth.bendCompliance,
                parameters.damping,
                frameTime
            ));
        }
    }

    private Vector3 MeanPosition(
        Vector3[] positions,
        List<int> indices)
    {
        Vector3 sum = Vector3.zero;

        foreach (int index in indices)
            sum += positions[index];

        return sum / indices.Count;
    }

    private void RestoreAllMasses()
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

    private void FinishExperiment()
    {
        if (finished)
            return;

        finished = true;
        RestoreAllMasses();

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }

        Debug.Log(
            "Obi reference calibration CSV saved to:\n" +
            outputPath
        );

#if UNITY_EDITOR
        if (stopPlayModeWhenFinished)
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnDestroy()
    {
        RestoreAllMasses();

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }
}
