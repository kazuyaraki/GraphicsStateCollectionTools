using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class VertexAttributeFormatTestMeshGenerator
{
    private const string OutputRootFolder = "Assets/TestMeshes/VertexAttributeFormat";

    [MenuItem("Tools/Generate VertexAttributeFormat Test Meshes")]
    private static void Generate()
    {
        EnsureFolder(OutputRootFolder);

        VertexAttributeFormat[] formats = (VertexAttributeFormat[])Enum.GetValues(typeof(VertexAttributeFormat));
        int createdCount = 0;

        for (int i = 0; i < formats.Length; i++)
        {
            VertexAttributeFormat format = formats[i];
            if (!TryCreateMesh(format, out Mesh mesh, out int dimension))
            {
                Debug.LogWarning($"Skipping unsupported VertexAttributeFormat value: {(int)format} ({format})");
                continue;
            }

            string assetPath = $"{OutputRootFolder}/VertexAttributeFormat_{format}_D{dimension}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Generated {createdCount} VertexAttributeFormat test meshes in '{OutputRootFolder}'.");
    }

    private static bool TryCreateMesh(VertexAttributeFormat format, out Mesh mesh, out int dimension)
    {
        mesh = new Mesh
        {
            name = $"VertexAttributeFormat_{format}"
        };

        dimension = FindSupportedDimension(format);
        if (dimension == 0)
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            mesh = null;
            return false;
        }

        VertexAttributeDescriptor[] attributes = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, format, dimension, stream: 1),
        };

        mesh.SetVertexBufferParams(3, attributes);

        Vector3[] positions =
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.0f, 0.5f, 0f),
        };
        mesh.SetVertexBufferData(positions, 0, 0, positions.Length, 0, MeshUpdateFlags.DontRecalculateBounds);

        int texcoordStride = mesh.GetVertexBufferStride(1);
        byte[] texcoordData = new byte[texcoordStride * positions.Length];
        mesh.SetVertexBufferData(texcoordData, 0, 0, texcoordData.Length, 1, MeshUpdateFlags.DontRecalculateBounds);

        mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, calculateBounds: false);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
        return true;
    }

    private static int FindSupportedDimension(VertexAttributeFormat format)
    {
        for (int dimension = 4; dimension >= 1; dimension--)
        {
            if (SystemInfo.SupportsVertexAttributeFormat(format, dimension))
            {
                return dimension;
            }
        }

        return 0;
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
