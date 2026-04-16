using UnityEngine;

public sealed class TargetFrameRateSetter : MonoBehaviour
{
    [SerializeField]
    private int m_TargetFrameRate = 60;

    private void Awake()
    {
        Application.targetFrameRate = m_TargetFrameRate;
        Debug.Log($"Application.targetFrameRate set to {m_TargetFrameRate}.", this);
    }
}
