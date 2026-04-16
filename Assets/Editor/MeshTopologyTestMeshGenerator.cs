using System;
using UnityEditor;
using UnityEngine;

public static class MeshTopologyTestMeshGenerator
{
    private const string OutputRootFolder = "Assets/TestMeshes/MeshTopology";

    [MenuItem("Tools/Generate MeshTopology Test Meshes")]
    private static void Generate()
    {
        EnsureFolder(OutputRootFolder);

        MeshTopology[] topologies = (MeshTopology[])Enum.GetValues(typeof(MeshTopology));
        int createdCount = 0;

        for (int i = 0; i < topologies.Length; i++)
        {
            MeshTopology topology = topologies[i];
            if (!TryCreateMesh(topology, out Mesh mesh))
            {
                Debug.LogWarning($"Skipping unsupported MeshTopology value: {(int)topology} ({topology})");
                continue;
            }

            string assetPath = $"{OutputRootFolder}/MeshTopology_{topology}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Generated {createdCount} MeshTopology test meshes in '{OutputRootFolder}'.");
    }

    private static bool TryCreateMesh(MeshTopology topology, out Mesh mesh)
    {
        mesh = new Mesh
        {
            name = $"MeshTopology_{topology}"
        };

        Vector3[] vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
        };
        mesh.vertices = vertices;

        int[] indices;
        switch (topology)
        {
            case MeshTopology.Triangles:
                indices = new[] { 0, 1, 2, 2, 1, 3 };
                break;
            case MeshTopology.Quads:
                indices = new[] { 0, 1, 3, 2 };
                break;
            case MeshTopology.Lines:
                indices = new[] { 0, 1, 2, 3 };
                break;
            case MeshTopology.LineStrip:
                indices = new[] { 0, 1, 3, 2 };
                break;
            case MeshTopology.Points:
                indices = new[] { 0, 1, 2, 3 };
                break;
            default:
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = null;
                return false;
        }

        mesh.SetIndices(indices, topology, 0, calculateBounds: true);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return true;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] split = folderPath.Split('/');
        string currentPath = split[0];
        for (int i = 1; i < split.Length; i++)
        {
            string nextPath = currentPath + "/" + split[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, split[i]);
            }

            currentPath = nextPath;
        }
    }
}
