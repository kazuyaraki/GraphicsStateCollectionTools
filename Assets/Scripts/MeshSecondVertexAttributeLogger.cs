using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class MeshSecondVertexAttributeLogger : MonoBehaviour
{
    [SerializeField]
    private List<Mesh> m_Meshes = new List<Mesh>();

    private readonly List<VertexAttributeDescriptor> m_Attributes = new List<VertexAttributeDescriptor>();

    private void Start()
    {
        for (int i = 0; i < m_Meshes.Count; i++)
        {
            Mesh mesh = m_Meshes[i];
            if (mesh == null)
            {
                Debug.LogWarning($"Mesh list index {i} is null.", this);
                continue;
            }

            m_Attributes.Clear();
            mesh.GetVertexAttributes(m_Attributes);

            if (m_Attributes.Count < 2)
            {
                Debug.LogWarning($"Mesh '{mesh.name}' has fewer than 2 vertex attributes.", this);
                continue;
            }

            VertexAttributeDescriptor attr = m_Attributes[1];
            Debug.Log(
                $"Mesh '{mesh.name}' second vertex attribute: Attribute={attr.attribute}, Format={attr.format}, Dimension={attr.dimension}, Stream={attr.stream}",
                this);
        }
    }
}
