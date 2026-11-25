using System;
using Obi;
using UnityEngine;

/// <summary>
/// Maps blueprint physical particles into material-grid order using the rest XZ
/// coordinates. Render-mesh vertex order is not assumed to be particle order.
/// </summary>
public class ObiMaterialGrid
{
    public readonly int w;
    public readonly int h;
    public readonly Vector3[] rest;
    public readonly int[] map;

    private readonly ObiCloth cloth;

    public ObiMaterialGrid(ObiCloth c, int w, int h)
    {
        cloth = c;
        this.w = w;
        this.h = h;

        if (w < 2 || h < 2 || c.activeParticleCount != w * h)
            throw new Exception("Expected a regular rectangular physical particle grid.");

        var p = c.clothBlueprint.positions;
        rest = new Vector3[w * h];
        map = new int[w * h];
        bool[] seen = new bool[w * h];

        float x0 = float.PositiveInfinity;
        float x1 = float.NegativeInfinity;
        float z0 = x0;
        float z1 = x1;

        for (int i = 0; i < map.Length; i++)
        {
            x0 = Mathf.Min(x0, p[i].x);
            x1 = Mathf.Max(x1, p[i].x);
            z0 = Mathf.Min(z0, p[i].z);
            z1 = Mathf.Max(z1, p[i].z);
        }

        if (x1 - x0 < 1e-6 || z1 - z0 < 1e-6)
            throw new Exception("Blueprint must be a flat XZ grid.");

        for (int i = 0; i < map.Length; i++)
        {
            float u = (w - 1) * (p[i].x - x0) / (x1 - x0);
            float v = (h - 1) * (p[i].z - z0) / (z1 - z0);
            int a = Mathf.RoundToInt(u);
            int b = Mathf.RoundToInt(v);
            int k = a * h + b;

            if (Mathf.Abs(u - a) > 0.05f ||
                Mathf.Abs(v - b) > 0.05f || seen[k])
            {
                throw new Exception("Nonregular blueprint grid; cannot compare axial strains.");
            }

            seen[k] = true;
            map[k] = i;
            rest[k] = c.transform.TransformPoint(p[i]);
        }
    }

    public int Index(int i)
    {
        return cloth.solverIndices[map[i]];
    }

    public Vector3[] Read()
    {
        Vector3[] p = new Vector3[map.Length];
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = cloth.solver.transform.TransformPoint(
                (Vector3)cloth.solver.positions[Index(i)]
            );
        }

        return p;
    }
}
