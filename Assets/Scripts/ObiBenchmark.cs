using System;
using System.Globalization;
using System.IO;
using System.Text;
using Obi;
using UnityEngine;

/// <summary>
/// Deterministic non-VR benchmarks for Obi.
/// Put this file in Assets/Scripts/ObiBenchmark.cs.
///
/// EdgeShear:
/// - left attachment targets stay fixed,
/// - assigned right attachment targets move by 0.15 m,
/// - 1 s settle + 2 s smooth motion + 2 s hold.
///
/// TableSlide:
/// - no attachment targets are used,
/// - record free cloth motion for 6 s.
///
/// Results are written to:
/// Assets/Exports/NonVRExperiments/
/// </summary>
[DefaultExecutionOrder(-100)]
public class ObiBenchmark : MonoBehaviour
{
    public enum ExperimentMode
    {
        EdgeShear,
        TableSlide
    }

    [Header("Reference")]
    public ObiCloth cloth;

    [Tooltip("For EdgeShear assign BR, MR and TR only.")]
    public Transform[] movingTargets;

    [Header("Experiment")]
    public ExperimentMode mode = ExperimentMode.EdgeShear;
    public string runName = "obi_run";

    [Header("EdgeShear")]
    public float settleSeconds = 1.0f;
    public float moveSeconds = 2.0f;
    public float holdSeconds = 2.0f;
    public float pullDistance = 0.15f;
    public Vector3 pullDirection = Vector3.forward;

    [Header("TableSlide")]
    public float tableSlideDuration = 6.0f;

    private readonly StringBuilder csv = new StringBuilder();

    private Vector3[] previousPositions;
    private Vector3[] initialTargetPositions;

    private Vector3 metricReferenceCenter;
    private float metricReferenceAngle;
    private bool metricReferenceCaptured;

    private int leftSample;
    private int rightSample;
    private int bottomSample;
    private int topSample;

    private float simulationTime;
    private float lastLoggedTime = -1.0f;

    private bool initialized;
    private bool saved;

    private float TotalDuration
    {
        get
        {
            if (mode == ExperimentMode.TableSlide)
                return tableSlideDuration;

            return settleSeconds + moveSeconds + holdSeconds;
        }
    }

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Time.fixedDeltaTime = 1.0f / 60.0f;
    }

    private void FixedUpdate()
    {
        if (!initialized)
        {
            TryInitialize();
            return;
        }

        if (saved)
            return;

        float nextTime = simulationTime + Time.fixedDeltaTime;

        if (mode == ExperimentMode.EdgeShear)
            ApplyEdgeShear(nextTime);

        simulationTime = nextTime;
    }

    private void LateUpdate()
    {
        if (!initialized || saved)
            return;

        // Only log once per simulated step.
        if (Mathf.Approximately(lastLoggedTime, simulationTime))
            return;

        Vector3[] currentPositions = ReadParticlePositions();

        if (currentPositions == null)
            return;

        if (mode == ExperimentMode.EdgeShear &&
            !metricReferenceCaptured &&
            simulationTime >= settleSeconds)
        {
            CaptureMetricReference(currentPositions);

            Debug.Log(
                $"Obi EdgeShear reference captured at " +
                $"t={simulationTime:F3}s, " +
                $"angle={metricReferenceAngle:F2} deg"
            );
        }

        WriteSample(simulationTime, currentPositions);

        previousPositions = currentPositions;
        lastLoggedTime = simulationTime;

        if (simulationTime + 1e-6f >= TotalDuration)
            SaveCsv();
    }

    private void TryInitialize()
    {
        if (cloth == null || !cloth.isLoaded)
            return;

        if (cloth.clothBlueprint == null ||
            cloth.clothBlueprint.inputMesh == null)
        {
            Debug.LogError("ObiBenchmark: the cloth has no input mesh.");
            enabled = false;
            return;
        }

        Mesh inputMesh = cloth.clothBlueprint.inputMesh;
        Vector2[] uv = inputMesh.uv;
        int vertexCount = inputMesh.vertexCount;

        if (uv == null || uv.Length != vertexCount)
        {
            Debug.LogError(
                "ObiBenchmark: the input mesh needs one UV per vertex."
            );
            enabled = false;
            return;
        }

        // Same geometric observable used by the Clothilde benchmark:
        // two material directions sampled around the cloth centre.
        leftSample   = FindNearestUv(uv, new Vector2(0.25f, 0.50f));
        rightSample  = FindNearestUv(uv, new Vector2(0.75f, 0.50f));
        bottomSample = FindNearestUv(uv, new Vector2(0.50f, 0.25f));
        topSample    = FindNearestUv(uv, new Vector2(0.50f, 0.75f));

        previousPositions = ReadParticlePositions();

        if (mode == ExperimentMode.TableSlide)
            CaptureMetricReference(previousPositions);

        initialTargetPositions =
            new Vector3[movingTargets != null ? movingTargets.Length : 0];

        for (int i = 0; i < initialTargetPositions.Length; i++)
        {
            if (movingTargets[i] != null)
                initialTargetPositions[i] = movingTargets[i].position;
        }

        csv.AppendLine(
            "simulator,experiment,time_s,particle_count,target_offset_m," +
            "com_x,com_y,com_z,com_horizontal_displacement_m," +
            "center_angle_deg,center_angle_change_deg,rms_speed_mps"
        );

        WriteSample(0.0f, previousPositions);

        initialized = true;

        Debug.Log(
            "ObiBenchmark initialized with " +
            vertexCount + " particles."
        );
    }

    private void ApplyEdgeShear(float time)
    {
        float offset = CurrentTargetOffset(time);

        Vector3 direction = pullDirection.sqrMagnitude > 0.0f
            ? pullDirection.normalized
            : Vector3.forward;

        if (movingTargets == null)
            return;

        for (int i = 0; i < movingTargets.Length; i++)
        {
            if (movingTargets[i] != null)
            {
                movingTargets[i].position =
                    initialTargetPositions[i] +
                    direction * offset;
            }
        }
    }

    private float CurrentTargetOffset(float time)
    {
        if (mode != ExperimentMode.EdgeShear)
            return 0.0f;

        float t = Mathf.Clamp01(
            (time - settleSeconds) /
            Mathf.Max(moveSeconds, 1e-6f)
        );

        // Smoothstep.
        t = t * t * (3.0f - 2.0f * t);

        return pullDistance * t;
    }

    private void CaptureMetricReference(Vector3[] positions)
    {
        metricReferenceCenter = Average(positions);
        metricReferenceAngle = CenterAngle(positions);
        metricReferenceCaptured = true;
    }

    private void WriteSample(
        float time,
        Vector3[] currentPositions)
    {
        Vector3 centerOfMass = Average(currentPositions);
        float centerAngle = CenterAngle(currentPositions);
        float targetOffset = CurrentTargetOffset(time);

        float horizontalDisplacement = float.NaN;
        float centerAngleChange = float.NaN;

        if (metricReferenceCaptured)
        {
            horizontalDisplacement = HorizontalDistance(
                centerOfMass,
                metricReferenceCenter
            );

            centerAngleChange = Mathf.Abs(
                centerAngle - metricReferenceAngle
            );
        }

        float rmsSpeed = RmsSpeed(
            currentPositions,
            previousPositions,
            Mathf.Max(Time.fixedDeltaTime, 1e-6f)
        );

        csv.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Obi,{0},{1:F6},{2},{3:F6}," +
            "{4:F6},{5:F6},{6:F6},{7}," +
            "{8:F6},{9},{10:F6}",
            mode,
            time,
            currentPositions.Length,
            targetOffset,
            centerOfMass.x,
            centerOfMass.y,
            centerOfMass.z,
            CsvNumber(horizontalDisplacement),
            centerAngle,
            CsvNumber(centerAngleChange),
            rmsSpeed
        ));
    }

    private Vector3[] ReadParticlePositions()
    {
        int count = cloth.clothBlueprint.inputMesh.vertexCount;

        if (cloth.solverIndices == null ||
            cloth.solverIndices.Length < count)
        {
            Debug.LogError(
                "ObiBenchmark: solverIndices does not match the input mesh."
            );
            return null;
        }

        Vector3[] positions = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            int solverIndex = cloth.solverIndices[i];
            positions[i] = cloth.GetParticlePosition(solverIndex);
        }

        return positions;
    }

    private int FindNearestUv(Vector2[] uv, Vector2 target)
    {
        int nearestIndex = 0;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < uv.Length; i++)
        {
            float distance = (uv[i] - target).sqrMagnitude;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private Vector3 Average(Vector3[] positions)
    {
        Vector3 sum = Vector3.zero;

        foreach (Vector3 position in positions)
            sum += position;

        return sum / positions.Length;
    }

    private float CenterAngle(Vector3[] positions)
    {
        Vector3 horizontal =
            positions[rightSample] -
            positions[leftSample];

        Vector3 vertical =
            positions[topSample] -
            positions[bottomSample];

        if (horizontal.sqrMagnitude < 1e-12f ||
            vertical.sqrMagnitude < 1e-12f)
        {
            return float.NaN;
        }

        return Vector3.Angle(horizontal, vertical);
    }

    private float RmsSpeed(
        Vector3[] current,
        Vector3[] previous,
        float dt)
    {
        if (previous == null ||
            previous.Length != current.Length)
        {
            return 0.0f;
        }

        double squaredSpeedSum = 0.0;

        for (int i = 0; i < current.Length; i++)
        {
            Vector3 velocity =
                (current[i] - previous[i]) / dt;

            squaredSpeedSum += velocity.sqrMagnitude;
        }

        return Mathf.Sqrt(
            (float)(squaredSpeedSum / current.Length)
        );
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(
            new Vector2(a.x, a.z),
            new Vector2(b.x, b.z)
        );
    }

    private string CsvNumber(float value)
    {
        if (float.IsNaN(value))
            return "NaN";

        return value.ToString(
            "F6",
            CultureInfo.InvariantCulture
        );
    }

    private void SaveCsv()
    {
        if (saved)
            return;

        saved = true;

        string directory = Path.Combine(
            Application.dataPath,
            "Exports",
            "NonVRExperiments"
        );

        Directory.CreateDirectory(directory);

        string safeRunName = string.IsNullOrWhiteSpace(runName)
            ? "obi_" + mode
            : runName.Trim();

        string fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1:yyyyMMdd_HHmmss}.csv",
            safeRunName,
            DateTime.Now
        );

        string path = Path.Combine(directory, fileName);

        File.WriteAllText(path, csv.ToString());

        Debug.Log(
            "Obi benchmark saved to:\n" + path
        );
    }
}
