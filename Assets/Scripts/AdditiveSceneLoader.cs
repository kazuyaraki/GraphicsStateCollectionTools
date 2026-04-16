using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AdditiveSceneLoader : MonoBehaviour
{
    [SerializeField]
    private List<string> m_SceneNames = new List<string>();

    [SerializeField]
    private bool m_LoadOnStart = true;

    [SerializeField]
    private float m_DelaySeconds = 0f;

    private void Start()
    {
        if (!m_LoadOnStart)
        {
            return;
        }

        StartCoroutine(LoadConfiguredScenesAfterDelay());
    }

    private System.Collections.IEnumerator LoadConfiguredScenesAfterDelay()
    {
        if (m_DelaySeconds > 0f)
        {
            yield return new WaitForSeconds(m_DelaySeconds);
        }

        LoadConfiguredScenes();
    }

    public void LoadConfiguredScenes()
    {
        for (int i = 0; i < m_SceneNames.Count; i++)
        {
            string sceneName = m_SceneNames[i];
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning($"Scene name at index {i} is empty.", this);
                continue;
            }

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
            {
                Debug.Log($"Scene '{sceneName}' is already loaded.", this);
                continue;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation == null)
            {
                Debug.LogWarning($"Failed to load scene '{sceneName}'. Check Build Settings.", this);
            }
        }
    }
}
