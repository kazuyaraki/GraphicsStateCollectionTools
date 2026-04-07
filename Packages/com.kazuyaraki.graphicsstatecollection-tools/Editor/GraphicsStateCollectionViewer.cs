using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace GSCTools
{
    public sealed class GraphicsStateCollectionViewer : EditorWindow
    {
        [Serializable]
        private sealed class Root
        {
            public int m_DeviceRenderer;
            public int m_RuntimePlatform;
            public string m_QualityLevelName;
            public VariantEntry[] m_VariantInfoMap;
        }

        [Serializable]
        private sealed class VariantEntry
        {
            public VariantSecond second;
        }

        [Serializable]
        private sealed class VariantSecond
        {
            public string shaderName;
            public string shaderAssetGUID;
            public long shaderAssetLocalIdentifierInFile;
            public int subShaderIndex;
            public int passIndex;
            public string keywordNames;
        }

        private sealed class VariantView
        {
            public VariantSecond source;
            public string[] keywords; // pre-split, lower-cased for fast search
            public string[] displayKeywords;
            public HashSet<string> passKeywords;
            public Shader shader;
            public string passName;
        }

        private sealed class ShaderGroup
        {
            public string shaderName;
            public Shader shader;
            public List<VariantView> views = new List<VariantView>();
            public bool foldout;
        }

        [SerializeField]
        private string m_FilePath = string.Empty;

        private readonly List<ShaderGroup> m_Groups = new List<ShaderGroup>();
        private string m_ErrorMessage = string.Empty;
        private Vector2 m_Scroll;
        private string m_SearchQuery = string.Empty;
        private int m_DeviceRenderer = -1;
        private int m_RuntimePlatform = -1;
        private string m_QualityLevelName = string.Empty;
        private static GUIStyle s_KeywordRichStyle;

        [MenuItem("Window/Analysis/GSCTools/Graphics State Collection Viewer")]
        private static void OpenWindow()
        {
            GetWindow<GraphicsStateCollectionViewer>("GSC Viewer");
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(m_FilePath) && m_Groups.Count == 0)
            {
                LoadFile(m_FilePath);
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(m_ErrorMessage))
            {
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Error);
                return;
            }

            if (m_Groups.Count == 0)
            {
                EditorGUILayout.LabelField("No file loaded. Click \"Open File\" or drag a .graphicsstate asset here.");
                HandleDragDrop();
                return;
            }

            DrawFileSummary();
            DrawGroups();
            HandleDragDrop();
        }

        private void DrawFileSummary()
        {
            string graphicsApiName = GetGraphicsApiName(m_DeviceRenderer);
            string runtimePlatformName = GetRuntimePlatformName(m_RuntimePlatform);

            int totalVariants = 0;
            foreach (ShaderGroup group in m_Groups)
            {
                totalVariants += group.views.Count;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Device Renderer", $"{m_DeviceRenderer} ({graphicsApiName})");
            EditorGUILayout.LabelField("Runtime Platform", runtimePlatformName);
            EditorGUILayout.LabelField("Quality Level", string.IsNullOrEmpty(m_QualityLevelName) ? "(none)" : m_QualityLevelName);
            EditorGUILayout.LabelField("Total Variants", $"{totalVariants}");
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Open File", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                string selectedPath = EditorUtility.OpenFilePanel("Open .graphicsstate", "Assets", "graphicsstate");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    LoadFile(selectedPath);
                }
            }

            GUILayout.Label(
                string.IsNullOrEmpty(m_FilePath) ? "-" : Path.GetFileName(m_FilePath),
                EditorStyles.toolbarTextField,
                GUILayout.ExpandWidth(true));

            if (m_Groups.Count > 0 && GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                foreach (ShaderGroup group in m_Groups)
                {
                    group.foldout = true;
                }
            }

            if (m_Groups.Count > 0 && GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                foreach (ShaderGroup group in m_Groups)
                {
                    group.foldout = false;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.LabelField("Shader Filter (Shader Name / Shader Keywords / Pass Name)", EditorStyles.boldLabel);
            string newQuery = EditorGUILayout.TextField(m_SearchQuery, EditorStyles.toolbarSearchField);
            if (newQuery != m_SearchQuery)
            {
                m_SearchQuery = newQuery;
                Repaint();
            }
        }

        private void DrawGroups()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSearchBar();

            bool isFiltering = !string.IsNullOrWhiteSpace(m_SearchQuery);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            foreach (ShaderGroup group in m_Groups)
            {
                List<VariantView> visibleViews = isFiltering ? FilterViews(group.views, m_SearchQuery) : group.views;
                if (isFiltering && visibleViews.Count == 0)
                {
                    continue;
                }

                string header = isFiltering
                    ? $"Shader: {group.shaderName} ({visibleViews.Count} / {group.views.Count})"
                    : $"Shader: {group.shaderName} ({group.views.Count})";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                group.foldout = EditorGUILayout.Foldout(group.foldout, header, true);
                using (new EditorGUI.DisabledScope(group.shader == null))
                {
                    if (GUILayout.Button("Select Shader", ShaderEditorUtility.GetShaderLinkStyle(), GUILayout.Width(90f)))
                    {
                        Selection.activeObject = group.shader;
                        EditorGUIUtility.PingObject(group.shader);
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (group.foldout)
                {
                    foreach (VariantView view in visibleViews)
                    {
                        DrawVariantRow(view);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static List<VariantView> FilterViews(List<VariantView> views, string query)
        {
            string keyword = query.Trim();
            var result = new List<VariantView>();
            foreach (VariantView view in views)
            {
                if (IsVisible(view, keyword))
                {
                    result.Add(view);
                }
            }

            return result;
        }

        private static bool IsVisible(VariantView view, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(view.source.shaderName)
                && view.source.shaderName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(view.passName)
                && view.passName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            for (int i = 0; i < view.keywords.Length; i++)
            {
                if (view.keywords[i].IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DrawVariantRow(VariantView view)
        {
            string displayPassName = string.IsNullOrEmpty(view.passName)
                ? "(unknown pass)"
                : view.passName;
            string passLabel = $"{displayPassName} ({view.source.passIndex})";
            string keywordText = BuildKeywordDisplayText(view);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Pass={passLabel}");
            EditorGUILayout.LabelField($"Keywords: {keywordText}", GetKeywordRichStyle());
            EditorGUILayout.EndVertical();
        }

        private static GUIStyle GetKeywordRichStyle()
        {
            if (s_KeywordRichStyle != null)
            {
                return s_KeywordRichStyle;
            }

            s_KeywordRichStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = true
            };

            return s_KeywordRichStyle;
        }

        private static string BuildKeywordDisplayText(VariantView view)
        {
            if (view.displayKeywords == null || view.displayKeywords.Length == 0)
            {
                return "(none)";
            }

            bool canGrayOutMissingKeywords = view.shader != null;

            var sb = new StringBuilder();
            for (int i = 0; i < view.displayKeywords.Length; i++)
            {
                string token = view.displayKeywords[i];
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                bool inPass = view.passKeywords != null && view.passKeywords.Contains(token);
                if (!canGrayOutMissingKeywords || inPass)
                {
                    sb.Append(EscapeRichText(token));
                }
                else
                {
                    sb.Append("<color=#8C8C8C>");
                    sb.Append(EscapeRichText(token));
                    sb.Append("</color>");
                }
            }

            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void HandleDragDrop()
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (string path in DragAndDrop.paths)
                {
                    if (path.EndsWith(".graphicsstate", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFile(path);
                        break;
                    }
                }
            }

            currentEvent.Use();
        }

        private void LoadFile(string path)
        {
            m_ErrorMessage = string.Empty;
            m_Groups.Clear();
            m_FilePath = path;
            m_DeviceRenderer = -1;
            m_RuntimePlatform = -1;
            m_QualityLevelName = string.Empty;

            try
            {
                string json = File.ReadAllText(path);
                Root root = JsonUtility.FromJson<Root>(json);

                if (root?.m_VariantInfoMap == null)
                {
                    m_ErrorMessage = "m_VariantInfoMap not found or empty.";
                    return;
                }

                m_DeviceRenderer = root.m_DeviceRenderer;
                m_RuntimePlatform = root.m_RuntimePlatform;
                m_QualityLevelName = root.m_QualityLevelName;

                var groupsByShader = new Dictionary<string, ShaderGroup>(StringComparer.Ordinal);
                foreach (VariantEntry entry in root.m_VariantInfoMap)
                {
                    VariantSecond variant = entry.second;
                    if (variant == null)
                    {
                        continue;
                    }

                    if (!groupsByShader.TryGetValue(variant.shaderName, out ShaderGroup group))
                    {
                        group = new ShaderGroup { shaderName = variant.shaderName };
                        groupsByShader[variant.shaderName] = group;
                        m_Groups.Add(group);
                    }

                    string[] splitKeywords = string.IsNullOrEmpty(variant.keywordNames)
                        ? Array.Empty<string>()
                        : variant.keywordNames.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    string[] displayKeywords = (string[])splitKeywords.Clone();
                    for (int i = 0; i < splitKeywords.Length; i++)
                    {
                        splitKeywords[i] = splitKeywords[i].ToLowerInvariant();
                    }

                    Shader resolvedShader = ResolveShader(variant);
                    string passName = ResolvePassName(resolvedShader, variant.subShaderIndex, variant.passIndex);
                    HashSet<string> passKeywords = ResolvePassKeywordSet(resolvedShader, variant.subShaderIndex, variant.passIndex);

                    if (group.shader == null && resolvedShader != null)
                    {
                        group.shader = resolvedShader;
                    }

                    group.views.Add(new VariantView
                    {
                        source = variant,
                        keywords = splitKeywords,
                        displayKeywords = displayKeywords,
                        passKeywords = passKeywords,
                        shader = resolvedShader,
                        passName = passName
                    });
                }

                m_Groups.Sort((a, b) => string.Compare(a.shaderName, b.shaderName, StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                m_ErrorMessage = ex.Message;
            }

            Repaint();
        }

        private static Shader ResolveShader(VariantSecond sec)
        {
            if (!string.IsNullOrEmpty(sec.shaderAssetGUID))
            {
                string path = AssetDatabase.GUIDToAssetPath(sec.shaderAssetGUID);
                if (!string.IsNullOrEmpty(path))
                {
                    Shader assetShader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (assetShader != null)
                    {
                        return assetShader;
                    }
                }
            }

            if (!string.IsNullOrEmpty(sec.shaderName))
            {
                return Shader.Find(sec.shaderName);
            }

            return null;
        }

        private static string ResolvePassName(Shader shader, int subShaderIndex, int passIndex)
        {
            if (shader == null || passIndex < 0)
            {
                return string.Empty;
            }

            Type shaderUtilType = typeof(ShaderUtil);

            MethodInfo methodWithSubShader = shaderUtilType.GetMethod(
                "GetShaderPassName",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Shader), typeof(int), typeof(int) },
                null);

            if (methodWithSubShader != null)
            {
                object value = methodWithSubShader.Invoke(null, new object[] { shader, subShaderIndex, passIndex });
                return value as string ?? string.Empty;
            }

            return string.Empty;
        }

        private static HashSet<string> ResolvePassKeywordSet(Shader shader, int subShaderIndex, int passIndex)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (shader == null || subShaderIndex < 0 || passIndex < 0)
            {
                return result;
            }

            var passIdentifier = new PassIdentifier((uint)subShaderIndex, (uint)passIndex);
            Array shaderTypes = Enum.GetValues(typeof(ShaderType));
            for (int t = 0; t < shaderTypes.Length; t++)
            {
                ShaderType shaderType = (ShaderType)shaderTypes.GetValue(t);
                LocalKeyword[] passKeywords = ShaderUtil.GetPassKeywords(shader, passIdentifier, shaderType);
                if (passKeywords == null)
                {
                    continue;
                }

                for (int i = 0; i < passKeywords.Length; i++)
                {
                    string keyword = passKeywords[i].name;
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        continue;
                    }

                    result.Add(keyword.Trim());
                }
            }

            return result;
        }

        private static string GetGraphicsApiName(int rendererValue)
        {
            if (rendererValue < 0)
            {
                return "Unknown";
            }

            GraphicsDeviceType deviceType = (GraphicsDeviceType)rendererValue;
            string deviceTypeName = deviceType.ToString();
            return deviceTypeName == rendererValue.ToString() ? $"Unknown ({rendererValue})" : deviceTypeName;
        }

        private static string GetRuntimePlatformName(int platformValue)
        {
            if (platformValue < 0)
            {
                return "Unknown";
            }

            RuntimePlatform platform = (RuntimePlatform)platformValue;
            string platformName = platform.ToString();
            return platformName == platformValue.ToString() ? $"Unknown ({platformValue})" : $"{platformName} ({platformValue})";
        }

    }
}