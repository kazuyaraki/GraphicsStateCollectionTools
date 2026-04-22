using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Rendering;
#else
using UnityEngine.Experimental.Rendering;
#endif

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
