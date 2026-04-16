using UnityEngine;
using UnityEngine.Experimental.Rendering;

public sealed class GSCSender : MonoBehaviour
{
    private GraphicsStateCollection m_GraphicsStateCollection;

    private void Awake()
    {
        m_GraphicsStateCollection = new GraphicsStateCollection();
        m_GraphicsStateCollection.BeginTrace();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!Debug.isDebugBuild || Application.isEditor)
        {
            return;
        }

        if (hasFocus)
        {
            return;
        }

        SendToEditor();
    }

    private void OnApplicationQuit()
    {
        if (!Debug.isDebugBuild || Application.isEditor)
        {
            return;
        }

        SendToEditor();
    }

    private void SendToEditor()
    {
        string fileName = $"gsc_{System.DateTime.Now:yyyyMMdd_HHmmss}";
        m_GraphicsStateCollection.SendToEditor(fileName);
    }
}
