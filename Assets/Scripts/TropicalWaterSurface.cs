using System.Collections.Generic;
using UnityEngine;

public sealed class TropicalWaterSurface : MonoBehaviour
{
    // Smooth ocean grid reaching the horizon; rows get denser near the camera where waves are visible.
    public static GameObject Create(Material material, float y)
    {
        var go = new GameObject("Animated tropical ocean");
        go.transform.position = new Vector3(0, y, 0);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        const int columns = 140, rows = 220;
        const float width = 360f, startZ = -24f, length = 520f;
        var vertices = new List<Vector3>((columns + 1) * (rows + 1));
        var triangles = new List<int>(columns * rows * 6);
        for (int z = 0; z <= rows; z++) for (int x = 0; x <= columns; x++)
        {
            float u = x / (float)columns - .5f;
            float xPos = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u) * 2f, 1.8f) * width * .5f;
            float zPos = startZ + length * Mathf.Pow(z / (float)rows, 1.9f);
            vertices.Add(new Vector3(xPos, 0, zPos));
        }
        for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
        {
            int a = z * (columns + 1) + x, b = a + columns + 1;
            triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
            triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
        }
        var mesh = new Mesh { name = "Smooth tropical ocean", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(new Vector3(0, 0, startZ + length * .5f), new Vector3(width, 4, length));
        filter.sharedMesh = mesh;
        return go;
    }
}
