#if UNITY_EDITOR
using System.Collections.Generic;
using Obi;
using UnityEditor;
using UnityEngine;

public static class ObiEdgeShearGroupCreator
{
    private static readonly string[] GroupNames =
    {
        "BL", "ML", "TL", "BR", "MR", "TR"
    };

    [MenuItem("Tools/Obi/Create EdgeShear Groups")]
    public static void CreateGroups()
    {
        ObiClothBlueprint blueprint =
            Selection.activeObject as ObiClothBlueprint;

        if (blueprint == null)
        {
            Debug.LogError(
                "Select the generated ObiClothBlueprint asset " +
                "(for example RecordSquare_07m.asset) in the Project window."
            );
            return;
        }

        if (blueprint.positions == null || blueprint.activeParticleCount == 0)
        {
            Debug.LogError(
                "The selected blueprint has no generated particle data. " +
                "Generate it first."
            );
            return;
        }

        // Remove only groups created by this tool, so running it again is safe.
        for (int i = blueprint.groups.Count - 1; i >= 0; --i)
        {
            ObiParticleGroup group = blueprint.groups[i];

            if (group != null &&
                System.Array.IndexOf(GroupNames, group.name) >= 0)
            {
                blueprint.RemoveParticleGroupAt(i, false);
            }
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < blueprint.activeParticleCount; ++i)
        {
            Vector3 p = blueprint.positions[i];

            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z);
            maxZ = Mathf.Max(maxZ, p.z);
        }

        float midX = 0.5f * (minX + maxX);
        float midZ = 0.5f * (minZ + maxZ);

        Dictionary<string, Vector3> targets =
            new Dictionary<string, Vector3>
            {
                { "BL", new Vector3(minX, 0, minZ) },
                { "ML", new Vector3(minX, 0, midZ) },
                { "TL", new Vector3(minX, 0, maxZ) },
                { "BR", new Vector3(maxX, 0, minZ) },
                { "MR", new Vector3(maxX, 0, midZ) },
                { "TR", new Vector3(maxX, 0, maxZ) }
            };

        foreach (string name in GroupNames)
        {
            int particleIndex =
                FindNearestParticleXZ(blueprint, targets[name]);

            ObiParticleGroup group =
                blueprint.AppendNewParticleGroup(name, false);

            group.particleIndices.Clear();
            group.particleIndices.Add(particleIndex);

            EditorUtility.SetDirty(group);

            Debug.Log(
                $"{name}: particle {particleIndex}, " +
                $"position = {blueprint.positions[particleIndex]}"
            );
        }

        EditorUtility.SetDirty(blueprint);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Created EdgeShear groups: " +
            "BL, ML, TL, BR, MR, TR. " +
            "Each group contains exactly one particle."
        );
    }

    private static int FindNearestParticleXZ(
        ObiClothBlueprint blueprint,
        Vector3 target)
    {
        int nearest = -1;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < blueprint.activeParticleCount; ++i)
        {
            Vector3 p = blueprint.positions[i];

            float dx = p.x - target.x;
            float dz = p.z - target.z;
            float d2 = dx * dx + dz * dz;

            if (d2 < bestDistance)
            {
                bestDistance = d2;
                nearest = i;
            }
        }

        return nearest;
    }
}
#endif
