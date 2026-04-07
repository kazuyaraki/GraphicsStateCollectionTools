using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using InternalProfilerDriver = UnityEditorInternal.ProfilerDriver;

namespace GSCTools
{
    public sealed class ProfilerShaderCreateGpuProgramExtractor : EditorWindow
    {
        private const string TargetMarker = "Shader.CreateGPUProgram";
        private const int MaxThreadScan = 512;

        private enum SampleGroupingMode
        {
            Shader,
            Frame,
        }

        private static readonly MethodInfo GetSampleMetadataCountMethod =
            typeof(UnityEditor.Profiling.RawFrameDataView).GetMethod("GetSampleMetadataCount", new[] { typeof(int) });

        private static readonly MethodInfo GetSampleMetadataAsStringMethod =
            typeof(UnityEditor.Profiling.RawFrameDataView).GetMethod("GetSampleMetadataAsString", new[] { typeof(int), typeof(int) });

        private static readonly MethodInfo GetSampleMetadataAsLongMethod =
            typeof(UnityEditor.Profiling.RawFrameDataView).GetMethod("GetSampleMetadataAsLong", new[] { typeof(int), typeof(int) });

        [Serializable]
        private struct SampleRow
        {
            public int frame;
            public int thread;
            public int sampleIndex;
            public ulong durationNs;
            public string shaderName;
            public string passName;
            public string shaderStage;
            public int passSortKey;
            public int stageSortKey;
            public string shaderKeywords;
            public string metadata;
        }

        [Serializable]
        private struct SummaryRow
        {
            public int frame;
            public int count;
            public ulong totalDurationNs;
        }

        private sealed class DisplayGroup
        {
            public string key;
            public string header;
            public string shaderName;
            public List<SampleRow> rows;
        }

        [SerializeField]
        private string m_ProfilePath = string.Empty;

        [SerializeField]
        private string m_ShaderFilter = string.Empty;

        [SerializeField]
        private Vector2 m_Scroll;

        [SerializeField]
        private Vector2 m_SummaryScroll;

        [SerializeField]
        private SampleGroupingMode m_GroupingMode = SampleGroupingMode.Shader;

        [SerializeField]
        private List<SampleRow> m_Samples = new List<SampleRow>();

        [SerializeField]
        private List<SummaryRow> m_Summaries = new List<SummaryRow>();

        private readonly Dictionary<string, bool> m_GroupFoldouts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UnityEngine.Object> m_ShaderAssetCache = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);

        [SerializeField]
        private int m_FirstFrame;

        [SerializeField]
        private int m_LastFrame;

        [MenuItem("Window/Analysis/GSCTools/Shader.CreateGPUProgram Extractor")]
        private static void OpenWindow()
        {
            GetWindow<ProfilerShaderCreateGpuProgramExtractor>("GPU Program Extractor");
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (string.IsNullOrEmpty(m_ProfilePath))
            {
                EditorGUILayout.LabelField("No file loaded. Click \"Open File\" and select a Profiler .data file.");
                HandleDragDrop();
                return;
            }

            DrawSummary();
            DrawSamples();
            HandleDragDrop();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Open File", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                string selectedPath = EditorUtility.OpenFilePanel("Select Profiler .data file", "ProfilerCaptures", "data");
                TryLoadProfilePath(selectedPath);
            }

            GUILayout.Label(
                string.IsNullOrEmpty(m_ProfilePath) ? "-" : m_ProfilePath,
                EditorStyles.toolbarTextField,
                GUILayout.ExpandWidth(true));

            if (m_Samples.Count > 0 && GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                SetAllFoldouts(true);
            }

            if (m_Samples.Count > 0 && GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                SetAllFoldouts(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SetAllFoldouts(bool expanded)
        {
            List<DisplayGroup> groups = BuildDisplayGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                DisplayGroup group = groups[i];
                m_GroupFoldouts[group.key] = expanded;

                if (m_GroupingMode != SampleGroupingMode.Frame)
                {
                    continue;
                }

                var shaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = 0; r < group.rows.Count; r++)
                {
                    string shaderName = string.IsNullOrEmpty(group.rows[r].shaderName) ? "(unknown)" : group.rows[r].shaderName;
                    shaders.Add(shaderName);
                }

                foreach (string shaderName in shaders)
                {
                    string nestedKey = group.key + "|shader:" + shaderName;
                    m_GroupFoldouts[nestedKey] = expanded;
                }
            }
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
                    if (path.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
                    {
                        TryLoadProfilePath(path);
                        break;
                    }
                }
            }

            currentEvent.Use();
        }

        private void TryLoadProfilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            bool changed = !string.Equals(m_ProfilePath, path, StringComparison.Ordinal);
            m_ProfilePath = path;
            if (changed)
            {
                ResetAnalysisResults();
            }

            AnalyzeProfile();
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.LabelField("Shader Filter (Shader Name / Shader Keywords / Pass Name)", EditorStyles.boldLabel);
            m_ShaderFilter = EditorGUILayout.TextField(m_ShaderFilter, EditorStyles.toolbarSearchField);
            m_GroupingMode = (SampleGroupingMode)EditorGUILayout.EnumPopup("Display Grouping", m_GroupingMode);
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Frame Summary", EditorStyles.boldLabel);

            if (m_Summaries.Count == 0)
            {
                EditorGUILayout.LabelField("No summary available.");
                EditorGUILayout.EndVertical();
                return;
            }

            float rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float contentHeight = (m_Summaries.Count * rowHeight) + 2f;
            const float maxSummaryHeight = 100f;

            if (contentHeight <= maxSummaryHeight)
            {
                foreach (SummaryRow row in m_Summaries)
                {
                    EditorGUILayout.LabelField($"Frame {ToDisplayFrameNumber(row.frame)}: Count={row.count}");
                }
            }
            else
            {
                m_SummaryScroll = EditorGUILayout.BeginScrollView(m_SummaryScroll, GUILayout.Height(maxSummaryHeight));
                foreach (SummaryRow row in m_Summaries)
                {
                    EditorGUILayout.LabelField($"Frame {ToDisplayFrameNumber(row.frame)}: Count={row.count}");
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSamples()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSearchBar();
            EditorGUILayout.Space(4f);

            if (m_Samples.Count == 0)
            {
                EditorGUILayout.LabelField("Samples", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("No samples available.");
                EditorGUILayout.EndVertical();
                return;
            }

            int filtered = 0;
            foreach (SampleRow row in m_Samples)
            {
                if (IsVisible(row))
                {
                    filtered++;
                }
            }

            EditorGUILayout.LabelField($"Samples ({filtered}/{m_Samples.Count})", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            List<DisplayGroup> grouped = BuildDisplayGroups();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (DisplayGroup group in grouped)
            {
                bool expanded;
                if (!m_GroupFoldouts.TryGetValue(group.key, out expanded))
                {
                    expanded = false;
                    m_GroupFoldouts[group.key] = false;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                expanded = EditorGUILayout.Foldout(expanded, group.header, true);
                if (m_GroupingMode == SampleGroupingMode.Shader)
                {
                    bool canSelectShader = !string.IsNullOrEmpty(group.shaderName)
                        && !string.Equals(group.shaderName, "(unknown)", StringComparison.OrdinalIgnoreCase)
                        && ResolveShaderAsset(group.shaderName) != null;
                    using (new EditorGUI.DisabledScope(!canSelectShader))
                    {
                        if (GUILayout.Button("Select Shader", ShaderEditorUtility.GetShaderLinkStyle(), GUILayout.Width(90f)))
                        {
                            SelectShaderInProject(group.shaderName);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                m_GroupFoldouts[group.key] = expanded;

                if (expanded)
                {
                    if (m_GroupingMode == SampleGroupingMode.Frame)
                    {
                        DrawFrameGroupAsShaderSections(group.key, group.rows);
                    }
                    else
                    {
                        foreach (SampleRow row in group.rows)
                        {
                            DrawSampleRowCard(row);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private List<DisplayGroup> BuildDisplayGroups()
        {
            return m_GroupingMode == SampleGroupingMode.Frame
                ? BuildFrameDisplayGroups()
                : BuildShaderDisplayGroups();
        }

        private void DrawFrameGroupAsShaderSections(string parentGroupKey, List<SampleRow> rows)
        {
            var byShader = new SortedDictionary<string, List<SampleRow>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                SampleRow row = rows[i];
                string shader = string.IsNullOrEmpty(row.shaderName) ? "(unknown)" : row.shaderName;
                if (!byShader.TryGetValue(shader, out List<SampleRow> list))
                {
                    list = new List<SampleRow>();
                    byShader.Add(shader, list);
                }

                list.Add(row);
            }

            foreach (KeyValuePair<string, List<SampleRow>> pair in byShader)
            {
                pair.Value.Sort(CompareSamplesInShaderGroup);

                string nestedKey = parentGroupKey + "|shader:" + pair.Key;
                bool nestedExpanded;
                if (!m_GroupFoldouts.TryGetValue(nestedKey, out nestedExpanded))
                {
                    nestedExpanded = false;
                    m_GroupFoldouts[nestedKey] = false;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                nestedExpanded = EditorGUILayout.Foldout(nestedExpanded, $"Shader: {pair.Key} ({pair.Value.Count})", true);
                bool canSelectShader = !string.Equals(pair.Key, "(unknown)", StringComparison.OrdinalIgnoreCase)
                    && ResolveShaderAsset(pair.Key) != null;
                using (new EditorGUI.DisabledScope(!canSelectShader))
                {
                    if (GUILayout.Button("Select Shader", ShaderEditorUtility.GetShaderLinkStyle(), GUILayout.Width(90f)))
                    {
                        SelectShaderInProject(pair.Key);
                    }
                }
                EditorGUILayout.EndHorizontal();
                m_GroupFoldouts[nestedKey] = nestedExpanded;

                if (nestedExpanded)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        DrawSampleRowCard(pair.Value[i]);
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawSampleRowCard(SampleRow row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Pass={row.passName}, Stage={row.shaderStage}, Frame={ToDisplayFrameNumber(row.frame)}");
            EditorGUILayout.LabelField($"Keywords: {row.shaderKeywords}", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private List<DisplayGroup> BuildShaderDisplayGroups()
        {
            var grouped = new SortedDictionary<string, List<SampleRow>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < m_Samples.Count; i++)
            {
                SampleRow row = m_Samples[i];
                if (!IsVisible(row))
                {
                    continue;
                }

                string shader = string.IsNullOrEmpty(row.shaderName) ? "(unknown)" : row.shaderName;
                if (!grouped.TryGetValue(shader, out List<SampleRow> list))
                {
                    list = new List<SampleRow>();
                    grouped.Add(shader, list);
                }

                list.Add(row);
            }

            var result = new List<DisplayGroup>(grouped.Count);
            foreach (KeyValuePair<string, List<SampleRow>> pair in grouped)
            {
                pair.Value.Sort(CompareSamplesInShaderGroup);
                result.Add(new DisplayGroup
                {
                    key = "shader:" + pair.Key,
                    header = $"Shader: {pair.Key} ({pair.Value.Count})",
                    shaderName = pair.Key,
                    rows = pair.Value,
                });
            }

            return result;
        }

        private List<DisplayGroup> BuildFrameDisplayGroups()
        {
            var grouped = new SortedDictionary<int, List<SampleRow>>();
            for (int i = 0; i < m_Samples.Count; i++)
            {
                SampleRow row = m_Samples[i];
                if (!IsVisible(row))
                {
                    continue;
                }

                if (!grouped.TryGetValue(row.frame, out List<SampleRow> list))
                {
                    list = new List<SampleRow>();
                    grouped.Add(row.frame, list);
                }

                list.Add(row);
            }

            var result = new List<DisplayGroup>(grouped.Count);
            foreach (KeyValuePair<int, List<SampleRow>> pair in grouped)
            {
                pair.Value.Sort(CompareSamplesInFrameGroup);
                result.Add(new DisplayGroup
                {
                    key = "frame:" + pair.Key,
                    header = $"Frame: {ToDisplayFrameNumber(pair.Key)} ({pair.Value.Count})",
                    shaderName = null,
                    rows = pair.Value,
                });
            }

            return result;
        }

        private static int CompareSamplesInShaderGroup(SampleRow a, SampleRow b)
        {
            int c = a.passSortKey.CompareTo(b.passSortKey);
            if (c != 0)
            {
                return c;
            }

            c = string.Compare(a.passName, b.passName, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            c = a.stageSortKey.CompareTo(b.stageSortKey);
            if (c != 0)
            {
                return c;
            }

            c = string.Compare(a.shaderStage, b.shaderStage, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            c = a.frame.CompareTo(b.frame);
            if (c != 0)
            {
                return c;
            }

            c = a.thread.CompareTo(b.thread);
            if (c != 0)
            {
                return c;
            }

            return a.sampleIndex.CompareTo(b.sampleIndex);
        }

        private static int CompareSamplesInFrameGroup(SampleRow a, SampleRow b)
        {
            int c = string.Compare(a.shaderName, b.shaderName, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            c = a.passSortKey.CompareTo(b.passSortKey);
            if (c != 0)
            {
                return c;
            }

            c = string.Compare(a.passName, b.passName, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            c = a.stageSortKey.CompareTo(b.stageSortKey);
            if (c != 0)
            {
                return c;
            }

            c = string.Compare(a.shaderStage, b.shaderStage, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            c = a.thread.CompareTo(b.thread);
            if (c != 0)
            {
                return c;
            }

            return a.sampleIndex.CompareTo(b.sampleIndex);
        }

        private bool IsVisible(SampleRow row)
        {
            if (string.IsNullOrWhiteSpace(m_ShaderFilter))
            {
                return true;
            }

            string keyword = m_ShaderFilter.Trim();
            return (!string.IsNullOrEmpty(row.shaderName) && row.shaderName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(row.passName) && row.passName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(row.shaderKeywords) && row.shaderKeywords.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void AnalyzeProfile()
        {
            ResetAnalysisResults();

            try
            {
                InternalProfilerDriver.LoadProfile(m_ProfilePath, false);
            }
            catch (Exception e)
            {
                string message = "Failed to load profile: " + e.Message + "\n" + m_ProfilePath;
                Debug.LogError(message);
                EditorUtility.DisplayDialog("Shader.CreateGPUProgram Extractor", message, "OK");
                return;
            }

            int firstFrame = InternalProfilerDriver.firstFrameIndex;
            int lastFrame = InternalProfilerDriver.lastFrameIndex;

            if (firstFrame < 0 || lastFrame < firstFrame)
            {
                EditorUtility.DisplayDialog("Shader.CreateGPUProgram Extractor", "No valid frames in selected profile.", "OK");
                return;
            }

            m_FirstFrame = firstFrame;
            m_LastFrame = lastFrame;

            for (int frame = firstFrame; frame <= lastFrame; frame++)
            {
                int frameCount = 0;
                ulong frameDuration = 0;

                for (int thread = 0; thread < MaxThreadScan; thread++)
                {
                    using var frameData = InternalProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!frameData.valid)
                    {
                        continue;
                    }

                    int markerId = frameData.GetMarkerId(TargetMarker);
                    if (markerId < 0)
                    {
                        continue;
                    }

                    for (int sample = 0; sample < frameData.sampleCount; sample++)
                    {
                        if (frameData.GetSampleMarkerId(sample) != markerId)
                        {
                            continue;
                        }

                        string metadata;
                        string passName;
                        string shaderStage;
                        string shaderKeywords;
                        string shaderName = ExtractShaderInfo(frameData, sample, out passName, out shaderStage, out shaderKeywords, out metadata);

                        ulong durationNs = frameData.GetSampleTimeNs(sample);
                        m_Samples.Add(new SampleRow
                        {
                            frame = frame,
                            thread = thread,
                            sampleIndex = sample,
                            durationNs = durationNs,
                            shaderName = string.IsNullOrEmpty(shaderName) ? "(unknown)" : shaderName,
                            passName = string.IsNullOrEmpty(passName) ? "(unknown pass)" : passName,
                            shaderStage = string.IsNullOrEmpty(shaderStage) ? "(unknown stage)" : shaderStage,
                            passSortKey = BuildPassSortKey(passName),
                            stageSortKey = BuildStageSortKey(shaderStage),
                            shaderKeywords = string.IsNullOrEmpty(shaderKeywords) ? "(none)" : shaderKeywords,
                            metadata = string.IsNullOrEmpty(metadata) ? "(none)" : metadata
                        });

                        frameCount++;
                        frameDuration += durationNs;
                    }
                }

                if (frameCount > 0)
                {
                    m_Summaries.Add(new SummaryRow
                    {
                        frame = frame,
                        count = frameCount,
                        totalDurationNs = frameDuration
                    });
                }
            }
        }

        private void ResetAnalysisResults()
        {
            m_Samples.Clear();
            m_Summaries.Clear();
            m_GroupFoldouts.Clear();
            m_FirstFrame = 0;
            m_LastFrame = 0;
            m_Scroll = Vector2.zero;
            m_SummaryScroll = Vector2.zero;
        }

        private static string ExtractShaderInfo(
            UnityEditor.Profiling.RawFrameDataView frameData,
            int sampleIndex,
            out string passName,
            out string shaderStage,
            out string shaderKeywords,
            out string metadataRaw)
        {
            var pieces = new List<string>();
            var metadataStrings = new List<string>();
            string shaderName = string.Empty;
            passName = string.Empty;
            shaderStage = string.Empty;
            shaderKeywords = string.Empty;

            int metadataCount = TryGetMetadataCount(frameData, sampleIndex);
            if (metadataCount <= 0)
            {
                metadataCount = 4;
            }

            for (int i = 0; i < metadataCount; i++)
            {
                string asString = TryGetMetadataAsString(frameData, sampleIndex, i);
                if (!string.IsNullOrEmpty(asString))
                {
                    pieces.Add(asString);
                    metadataStrings.Add(asString);
                    if (string.IsNullOrEmpty(shaderName) && LooksLikeShaderName(asString))
                    {
                        shaderName = asString;
                    }

                    if (string.IsNullOrEmpty(passName))
                    {
                        passName = TryExtractPassName(asString);
                    }

                    if (string.IsNullOrEmpty(shaderStage))
                    {
                        shaderStage = TryExtractStageName(asString);
                    }
                }

                string asLong = TryGetMetadataAsLong(frameData, sampleIndex, i);
                if (!string.IsNullOrEmpty(asLong))
                {
                    pieces.Add("m" + i + "=" + asLong);
                }
            }

            if (string.IsNullOrEmpty(passName))
            {
                passName = InferPassNameFromMetadataStrings(metadataStrings, shaderName);
            }

            if (string.IsNullOrEmpty(shaderStage))
            {
                shaderStage = InferStageFromMetadataStrings(metadataStrings);
            }

            shaderKeywords = BuildShaderKeywordList(metadataStrings);

            metadataRaw = string.Join(" | ", pieces);
            if (!string.IsNullOrEmpty(shaderName))
            {
                return shaderName;
            }

            if (!string.IsNullOrEmpty(metadataRaw))
            {
                return metadataRaw;
            }

            return string.Empty;
        }

        private static string BuildShaderKeywordList(List<string> metadataStrings)
        {
            var keywords = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < metadataStrings.Count; i++)
            {
                string token = metadataStrings[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                string[] parts = token.Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    string candidate = parts[p].Trim().Trim('"', '\'', '(', ')', '[', ']', '{', '}');
                    if (!LooksLikeKeywordToken(candidate))
                    {
                        continue;
                    }

                    if (seen.Add(candidate))
                    {
                        keywords.Add(candidate);
                    }
                }
            }

            return keywords.Count == 0 ? string.Empty : string.Join(", ", keywords);
        }

        private static int BuildPassSortKey(string passName)
        {
            if (string.IsNullOrWhiteSpace(passName))
            {
                return int.MaxValue;
            }

            int value = 0;
            bool hasDigit = false;
            for (int i = 0; i < passName.Length; i++)
            {
                char ch = passName[i];
                if (ch >= '0' && ch <= '9')
                {
                    hasDigit = true;
                    value = value * 10 + (ch - '0');
                    if (value > 1000000)
                    {
                        break;
                    }
                }
                else if (hasDigit)
                {
                    break;
                }
            }

            return hasDigit ? value : int.MaxValue - 1;
        }

        private static int BuildStageSortKey(string stageName)
        {
            return ShaderEditorUtility.BuildStageSortKey(stageName);
        }

        private static string TryExtractPassName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Handle angle-bracket tokens like <Unnamed Pass 0>
            // The inline regex would incorrectly extract "0>" without this guard.
            if (value.Length > 2 && value[0] == '<' && value[value.Length - 1] == '>')
            {
                string inner = value.Substring(1, value.Length - 2).Trim();
                if (inner.IndexOf("pass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return inner;
                }
                return string.Empty;
            }

            Match labeled = Regex.Match(
                value,
                "\\bpass(?:\\s*name|name)?\\s*[:=]\\s*[\"']?(?<name>[^\"'|,;\\]\\r\\n]+)",
                RegexOptions.CultureInvariant);
            if (labeled.Success)
            {
                return CleanExtractedPassName(labeled.Groups["name"].Value);
            }

            // Handles formats like: "... Pass ShadowCaster Stage Vertex ..."
            Match inline = Regex.Match(
                value,
                "\\bpass\\s+(?<name>[^|,;\\r\\n]+?)(?=\\s+stage\\b|\\s+shader\\b|\\||,|;|$)",
                RegexOptions.CultureInvariant);
            if (inline.Success)
            {
                return CleanExtractedPassName(inline.Groups["name"].Value);
            }

            string pass = ExtractLabeledValue(value, "pass");
            if (!string.IsNullOrEmpty(pass))
            {
                return CleanExtractedPassName(pass);
            }

            string lower = value.ToLowerInvariant();
            if (lower.Contains("pass"))
            {
                return CleanExtractedPassName(value);
            }

            return string.Empty;
        }

        private static string CleanExtractedPassName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string cleaned = raw.Trim().Trim('"', '\'', '[', ']', '(', ')');

            int stagePos = cleaned.IndexOf(" stage", StringComparison.OrdinalIgnoreCase);
            if (stagePos > 0)
            {
                cleaned = cleaned.Substring(0, stagePos).Trim();
            }

            return cleaned;
        }

        private static string InferPassNameFromMetadataStrings(List<string> metadataStrings, string shaderName)
        {
            for (int i = 0; i < metadataStrings.Count; i++)
            {
                string token = metadataStrings[i]?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(shaderName) && string.Equals(token, shaderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LooksLikeShaderName(token))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(NormalizeStageName(token)))
                {
                    continue;
                }

                if (LooksLikeKeywordToken(token))
                {
                    continue;
                }

                return CleanExtractedPassName(token);
            }

            return string.Empty;
        }

        private static string InferStageFromMetadataStrings(List<string> metadataStrings)
        {
            for (int i = 0; i < metadataStrings.Count; i++)
            {
                string normalized = NormalizeStageName(metadataStrings[i]);
                if (!string.IsNullOrEmpty(normalized))
                {
                    return normalized;
                }
            }

            return string.Empty;
        }

        private static bool LooksLikeKeywordToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token.StartsWith("m", StringComparison.OrdinalIgnoreCase) && token.Length > 1 && char.IsDigit(token[1]))
            {
                return false;
            }

            if (token.StartsWith("_", StringComparison.Ordinal))
            {
                return true;
            }

            return Regex.IsMatch(token, "^[A-Z0-9_]+$") && token.Contains("_");
        }

        private static string TryExtractStageName(string value)
        {
            string labeled = ExtractLabeledValue(value, "stage");
            if (!string.IsNullOrEmpty(labeled))
            {
                return NormalizeStageName(labeled);
            }

            return NormalizeStageName(value);
        }

        private static string NormalizeStageName(string value)
        {
            return ShaderEditorUtility.NormalizeStageName(value);
        }

        private static string ExtractLabeledValue(string source, string label)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            int start = source.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            int from = start + label.Length;
            while (from < source.Length && source[from] == ' ')
            {
                from++;
            }

            if (from >= source.Length)
            {
                return string.Empty;
            }

            if (source[from] == ':' || source[from] == '=')
            {
                from++;
            }

            while (from < source.Length && source[from] == ' ')
            {
                from++;
            }

            int end = from;
            while (end < source.Length)
            {
                char ch = source[end];
                if (ch == ',' || ch == ';' || ch == '|')
                {
                    break;
                }
                end++;
            }

            if (end <= from)
            {
                return string.Empty;
            }

            return source.Substring(from, end - from).Trim().Trim('"');
        }

        private static int TryGetMetadataCount(UnityEditor.Profiling.RawFrameDataView frameData, int sampleIndex)
        {
            if (GetSampleMetadataCountMethod == null)
            {
                return -1;
            }

            try
            {
                object value = GetSampleMetadataCountMethod.Invoke(frameData, new object[] { sampleIndex });
                return Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static string TryGetMetadataAsString(UnityEditor.Profiling.RawFrameDataView frameData, int sampleIndex, int metadataIndex)
        {
            if (GetSampleMetadataAsStringMethod == null)
            {
                return string.Empty;
            }

            try
            {
                object value = GetSampleMetadataAsStringMethod.Invoke(frameData, new object[] { sampleIndex, metadataIndex });
                return value as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryGetMetadataAsLong(UnityEditor.Profiling.RawFrameDataView frameData, int sampleIndex, int metadataIndex)
        {
            if (GetSampleMetadataAsLongMethod == null)
            {
                return string.Empty;
            }

            try
            {
                object value = GetSampleMetadataAsLongMethod.Invoke(frameData, new object[] { sampleIndex, metadataIndex });
                if (value == null)
                {
                    return string.Empty;
                }

                ulong n = Convert.ToUInt64(value);
                return n.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksLikeShaderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string v = value.Trim();
            if (v.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return v.IndexOf('/') >= 0;
        }

        private static int ToDisplayFrameNumber(int frameIndex)
        {
            return frameIndex + 1;
        }

        private void SelectShaderInProject(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return;
            }

            UnityEngine.Object shaderAsset = ResolveShaderAsset(shaderName);
            if (shaderAsset == null)
            {
                Debug.LogWarning($"[Shader.CreateGPUProgram Extractor] Shader not found in project: {shaderName}");
                return;
            }

            Selection.activeObject = shaderAsset;
            EditorGUIUtility.PingObject(shaderAsset);
        }

        private UnityEngine.Object ResolveShaderAsset(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return null;
            }

            if (m_ShaderAssetCache.TryGetValue(shaderName, out UnityEngine.Object cached))
            {
                return cached;
            }

            string[] guids = AssetDatabase.FindAssets("t:Shader");
            UnityEngine.Object firstPartialCandidate = null;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    continue;
                }

                if (string.Equals(shader.name, shaderName, StringComparison.OrdinalIgnoreCase))
                {
                    m_ShaderAssetCache[shaderName] = shader;
                    return shader;
                }

                if (firstPartialCandidate == null
                    && shader.name.IndexOf(shaderName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    firstPartialCandidate = shader;
                }
            }

            m_ShaderAssetCache[shaderName] = firstPartialCandidate;
            return firstPartialCandidate;
        }
    }
}
