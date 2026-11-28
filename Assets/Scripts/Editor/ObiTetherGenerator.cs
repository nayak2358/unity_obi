
using UnityEngine;
using UnityEditor;
using Obi;

public static class ObiTetherGenerator
{
    [MenuItem("Tools/Obi/Generate Grasp Tethers")]
    public static void Generate()
    {
        var blueprint =
            Selection.activeObject as ObiClothBlueprint;

        if (blueprint == null || blueprint.positions == null)
        {
            Debug.LogError("Select a generated cloth blueprint.");
            return;
        }

        bool[] selected = new bool[blueprint.positions.Length];

        // string[] anchorNames =
        // {
        //     "TL", "TR", "ML", "MR", "BL", "BR"
        // };
        string[] anchorNames =
        {
            "TL"
        };

        int anchorCount = 0;

        foreach (var group in blueprint.groups)
        {
            if (group == null ||
                System.Array.IndexOf(anchorNames, group.name) < 0)
                continue;

            foreach (int index in group.particleIndices)
            {
                if (index < 0 || index >= selected.Length)
                {
                    Debug.LogError(
                        $"Invalid particle index: {index}");
                    return;
                }

                if (!selected[index])
                {
                    selected[index] = true;
                    anchorCount++;
                }
            }
        }

        if (anchorCount == 0)
        {
            Debug.LogError("No tether anchors found.");
            return;
        }

        Undo.RecordObject(blueprint, "Generate Tethers");

        blueprint.GenerateTethers(selected);

        EditorUtility.SetDirty(blueprint);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"Tethers generated using {anchorCount} anchors.");
    }
}
