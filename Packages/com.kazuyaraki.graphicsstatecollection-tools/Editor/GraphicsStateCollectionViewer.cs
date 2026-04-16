using System;
using System.Collections.Generic;
using System.Globalization;
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
            public VertexLayoutInfoEntry[] m_VertexLayoutInfoMap;
            public RenderPassInfoEntry[] m_RenderPassInfoMap;
            public RenderStateInfoEntry[] m_RenderStateMap;
        }

        [Serializable]
        private sealed class VariantEntry
        {
            public VariantSecond second;
        }

        [Serializable]
        private sealed class GraphicsStateInfo
        {
            public ulong vertexLayout;
            public ulong renderPass;
            public ulong renderState;
            public int subPassIndex;
            public int topology;
            public int forceCullMode;
            public float depthBias;
            public float slopeDepthBias;
            public bool userBackface;
            public bool appBackface;
            public bool wireframe;
            public bool invertProjectionMatrix;
        }

        [Serializable]
        private sealed class VertexChannelInfo
        {
            public int stream;
            public int offset;
            public int format;
            public int dimension;
        }

        [Serializable]
        private sealed class VertexLayoutInfo
        {
            public VertexChannelInfo[] vertexChannelsInfo;
        }

        [Serializable]
        private sealed class VertexLayoutInfoEntry
        {
            public ulong first;
            public VertexLayoutInfo second;
        }

        [Serializable]
        private sealed class RenderPassAttachment
        {
            public int format;
            public bool needsResolve;
        }

        [Serializable]
        private sealed class RenderPassInfo
        {
            public RenderPassAttachment[] attachments;
            public int attachmentCount;
            public RenderPassSubPass[] subPasses;
            public int subPassCount;
            public int depthAttachmentIndex;
        }

        [Serializable]
        private sealed class RenderPassSubPassAttachmentSet
        {
            public int[] attachments;
            public int activeAttachments;
        }

        [Serializable]
        private sealed class RenderPassSubPass
        {
            public RenderPassSubPassAttachmentSet inputs;
            public RenderPassSubPassAttachmentSet colorOutputs;
            public int flags;
        }

        [Serializable]
        private sealed class RenderPassInfoEntry
        {
            public ulong first;
            public RenderPassInfo second;
        }

        [Serializable]
        private sealed class BlendStateRT
        {
            public int writeMask;
            public int srcBlend;
            public int dstBlend;
            public int srcBlendAlpha;
            public int dstBlendAlpha;
            public int blendOp;
            public int blendOpAlpha;
        }

        [Serializable]
        private sealed class BlendState
        {
            public BlendStateRT[] rt;
            public int separateMRTBlend;
            public int alphaToMask;
        }

        [Serializable]
        private sealed class RasterState
        {
            public int cullMode;
            public float depthBias;
            public float slopeScaledDepthBias;
            public int depthClip;
            public int conservative;
        }

        [Serializable]
        private sealed class DepthState
        {
            public int depthWrite;
            public int depthFunc;
        }

        [Serializable]
        private sealed class StencilStateData
        {
            public int stencilEnable;
            public int readMask;
            public int writeMask;
            public int padding;
            public int stencilFuncFront;
            public int stencilPassOpFront;
            public int stencilFailOpFront;
            public int stencilZFailOpFront;
            public int stencilFuncBack;
            public int stencilPassOpBack;
            public int stencilFailOpBack;
            public int stencilZFailOpBack;
        }

        [Serializable]
        private sealed class RenderStateData
        {
            public BlendState blendState;
            public RasterState rasterState;
            public DepthState depthState;
            public StencilStateData stencilState;
            public int stencilRef;
            public int mask;
        }

        [Serializable]
        private sealed class RenderStateWrapper
        {
            public RenderStateData renderState;
        }

        [Serializable]
        private sealed class RenderStateInfoEntry
        {
            public ulong first;
            public RenderStateWrapper second;
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
            public GraphicsStateInfo[] graphicsStateInfoSet;
        }

        private sealed class VariantView
        {
            public VariantSecond source;
            public string[] keywords; // pre-split, lower-cased for fast search
            public string[] displayKeywords;
            public HashSet<string> passKeywords;
            public Shader shader;
            public string passName;
            public GraphicsStateInfo[] graphicsStateInfoSet;
            public VertexLayoutInfo[] decodedVertexLayouts;
            public RenderPassInfo[] decodedRenderPasses;
            public RenderStateWrapper[] decodedRenderStates;
            public bool graphicsStateInfoFoldout;
            public bool[] graphicsStateFoldout;
            public bool[] configurationFoldout;
            public bool[] vertexLayoutFoldout;
            public bool[] renderDetailsFoldout;
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
        private static GUIStyle s_RichMiniStyle;

        private static readonly string[] s_AttachmentIndexColors =
        {
            "#FF5A5A", // 0
            "#FF9F1A", // 1
            "#F4D35E", // 2
            "#6BCB77", // 3
            "#2EC4B6", // 4
            "#4D96FF", // 5
            "#8A84FF", // 6
            "#B983FF", // 7
            "#FF66C4"  // 8
        };
        
        private Dictionary<ulong, VertexLayoutInfo> m_VertexLayoutLookup = new Dictionary<ulong, VertexLayoutInfo>();
        private Dictionary<ulong, RenderPassInfo> m_RenderPassLookup = new Dictionary<ulong, RenderPassInfo>();
        private Dictionary<ulong, RenderStateWrapper> m_RenderStateLookup = new Dictionary<ulong, RenderStateWrapper>();

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
            
            if (view.graphicsStateInfoSet != null && view.graphicsStateInfoSet.Length > 0)
            {
                EditorGUILayout.Space(4f);
                view.graphicsStateInfoFoldout = EditorGUILayout.Foldout(
                    view.graphicsStateInfoFoldout,
                    $"Graphics State Info ({view.graphicsStateInfoSet.Length})",
                    true);

                if (view.graphicsStateInfoFoldout)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawGraphicsStateInfoSet(view);
                    }
                }
            }
            
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

        private static void DrawGraphicsStateInfoSet(VariantView view)
        {
            for (int i = 0; i < view.graphicsStateInfoSet.Length; i++)
            {
                GraphicsStateInfo info = view.graphicsStateInfoSet[i];
                VertexLayoutInfo vertexLayout = view.decodedVertexLayouts != null && i < view.decodedVertexLayouts.Length ? view.decodedVertexLayouts[i] : null;
                RenderPassInfo renderPass = view.decodedRenderPasses != null && i < view.decodedRenderPasses.Length ? view.decodedRenderPasses[i] : null;
                RenderStateWrapper renderState = view.decodedRenderStates != null && i < view.decodedRenderStates.Length ? view.decodedRenderStates[i] : null;
                
                string stateLabel = view.graphicsStateInfoSet.Length > 1
                    ? $"State [{i}]"
                    : "State Info";
                
                view.graphicsStateFoldout[i] = EditorGUILayout.Foldout(view.graphicsStateFoldout[i], stateLabel, true);
                if (view.graphicsStateFoldout[i])
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        if (view.configurationFoldout != null && i < view.configurationFoldout.Length)
                        {
                            view.configurationFoldout[i] = EditorGUILayout.Foldout(view.configurationFoldout[i], "Configuration", true);
                            if (view.configurationFoldout[i])
                            {
                                using (new EditorGUI.IndentLevelScope())
                                {
                                    DrawConfigurationInfo(info);
                                }
                            }
                        }

                        if (view.vertexLayoutFoldout != null && i < view.vertexLayoutFoldout.Length && vertexLayout != null)
                        {
                            EditorGUILayout.Space(2f);
                            view.vertexLayoutFoldout[i] = EditorGUILayout.Foldout(view.vertexLayoutFoldout[i], "Vertex Layout Details", true);
                            if (view.vertexLayoutFoldout[i])
                            {
                                using (new EditorGUI.IndentLevelScope())
                                {
                                    DrawVertexLayoutInfo(vertexLayout);
                                }
                            }
                        }

                        if (view.renderDetailsFoldout != null && i < view.renderDetailsFoldout.Length && (renderPass != null || renderState != null))
                        {
                            EditorGUILayout.Space(2f);
                            view.renderDetailsFoldout[i] = EditorGUILayout.Foldout(view.renderDetailsFoldout[i], "Render Details", true);
                            if (view.renderDetailsFoldout[i])
                            {
                                using (new EditorGUI.IndentLevelScope())
                                {
                                    if (renderPass != null)
                                    {
                                        DrawRenderPassInfo(renderPass, info.subPassIndex, renderState);
                                    }
                                    else
                                    {
                                        DrawRenderStateInfo(renderState, renderPass);
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (i < view.graphicsStateInfoSet.Length - 1)
                {
                    EditorGUILayout.Space(2f);
                }
            }
        }

        private static void DrawConfigurationInfo(GraphicsStateInfo info)
        {
            EditorGUILayout.LabelField("Topology", GetMeshTopologyName(info.topology));
            EditorGUILayout.LabelField("Force Cull Mode", GetForceCullModeName(info.forceCullMode));
            EditorGUILayout.LabelField("Offset(DepthBias)", GetShaderLabOffsetCommand(info.depthBias, info.slopeDepthBias));
            EditorGUILayout.LabelField("User Backface", info.userBackface.ToString());
            EditorGUILayout.LabelField("App Backface", info.appBackface.ToString());
            EditorGUILayout.LabelField("Wireframe", info.wireframe.ToString());
            EditorGUILayout.LabelField("Invert Projection Matrix", info.invertProjectionMatrix.ToString());
        }

        private static void DrawVertexLayoutInfo(VertexLayoutInfo vertexLayout)
        {
            if (vertexLayout.vertexChannelsInfo != null && vertexLayout.vertexChannelsInfo.Length > 0)
            {
                for (int i = 0; i < vertexLayout.vertexChannelsInfo.Length; i++)
                {
                    VertexChannelInfo channel = vertexLayout.vertexChannelsInfo[i];
                    if (channel.dimension == 0)
                        continue;

                    string channelName = $"{GetVertexAttributeName(i)}";
                    string formatSummary = GetVertexAttributeFormatSummary(channel.format, channel.dimension);
                    EditorGUILayout.LabelField(
                        $"{channelName}", $"{formatSummary}  offset: {channel.offset}  stream: {channel.stream}");
                }
            }
            else
            {
                EditorGUILayout.LabelField("(No channel info)");
            }
        }

        private static void DrawRenderPassInfo(RenderPassInfo renderPass, int subPassIndex, RenderStateWrapper renderStateWrapper)
        {
            BlendState blendState = renderStateWrapper?.renderState?.blendState;
            if (blendState != null)
            {
                EditorGUILayout.LabelField("AlphaToMask", GetShaderLabAlphaToMask(blendState.alphaToMask));
                EditorGUILayout.Space(2f);
            }

            if (renderPass.attachments != null && renderPass.attachments.Length > 0)
            {
                for (int i = 0; i < renderPass.attachments.Length; i++)
                {
                    RenderPassAttachment att = renderPass.attachments[i];
                    if (att.format == 0)
                        continue;

                    EditorGUILayout.LabelField(GetAttachmentDisplayLabel(i, renderPass.depthAttachmentIndex), GetRichMiniStyle());
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Format", GetGraphicsFormatName(att.format));
                        EditorGUILayout.LabelField("Needs Resolve", att.needsResolve.ToString());
                        DrawAttachmentBlendState(i, renderPass, renderStateWrapper);
                    }
                }
            }

            if (subPassIndex >= 0 && renderPass.subPasses != null && renderPass.subPasses.Length > 0)
            {
                int subPassDisplayCount = renderPass.subPassCount > 0
                    ? Math.Min(renderPass.subPassCount, renderPass.subPasses.Length)
                    : renderPass.subPasses.Length;

                if (subPassIndex < subPassDisplayCount)
                {
                    RenderPassSubPass subPass = renderPass.subPasses[subPassIndex];
                    if (subPass == null)
                    {
                        return;
                    }

                    bool hasInputs = subPass.inputs != null && subPass.inputs.activeAttachments > 0;
                    bool hasColorOutputs = subPass.colorOutputs != null && subPass.colorOutputs.activeAttachments > 0;

                    if (!hasInputs && !hasColorOutputs && subPass.flags == 0)
                    {
                        return;
                    }

                    EditorGUILayout.LabelField("SubPass", EditorStyles.label);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        if (hasInputs)
                        {
                            EditorGUILayout.LabelField("Inputs", FormatSubPassAttachmentSet(subPass.inputs, renderPass.depthAttachmentIndex), GetRichMiniStyle());
                        }

                        if (hasColorOutputs)
                        {
                            EditorGUILayout.LabelField("Color Outputs", FormatSubPassAttachmentSet(subPass.colorOutputs, renderPass.depthAttachmentIndex), GetRichMiniStyle());
                        }

                        if (subPass.flags != 0)
                        {
                            EditorGUILayout.LabelField("Flags", subPass.flags.ToString());
                        }
                    }
                }
            }

            if (renderStateWrapper != null)
            {
                EditorGUILayout.Space(2f);
                DrawRenderStateInfo(renderStateWrapper, renderPass);
            }
        }

        private static string FormatSubPassAttachmentSet(RenderPassSubPassAttachmentSet set, int depthAttachmentIndex)
        {
            if (set == null || set.attachments == null || set.activeAttachments <= 0)
            {
                return string.Empty;
            }

            int count = Math.Min(set.activeAttachments, set.attachments.Length);
            if (count <= 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(GetAttachmentIndexRichText(set.attachments[i], depthAttachmentIndex));
            }

            return sb.ToString();
        }

        private static GUIStyle GetRichMiniStyle()
        {
            if (s_RichMiniStyle != null)
            {
                return s_RichMiniStyle;
            }

            s_RichMiniStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true
            };

            return s_RichMiniStyle;
        }

        private static string GetAttachmentDisplayLabel(int attachmentIndex, int depthAttachmentIndex)
        {
            string coloredIndex = GetAttachmentIndexRichText(attachmentIndex, depthAttachmentIndex);
            return attachmentIndex == depthAttachmentIndex
                ? $"Attachment {coloredIndex} (Depth)"
                : $"Attachment {coloredIndex}";
        }

        private static string GetRtDisplayLabel(int attachmentIndex, int depthAttachmentIndex)
        {
            return $"RT[{GetAttachmentIndexRichText(attachmentIndex, depthAttachmentIndex)}]";
        }

        private static void DrawAttachmentBlendState(int attachmentIndex, RenderPassInfo renderPass, RenderStateWrapper renderStateWrapper)
        {
            BlendState blendState = renderStateWrapper?.renderState?.blendState;
            if (blendState?.rt == null || blendState.rt.Length == 0)
            {
                return;
            }

            if (!TryGetRtArrayIndexForAttachment(attachmentIndex, renderPass, out int rtArrayIndex))
            {
                return;
            }

            bool useRtIndexSyntax = blendState.separateMRTBlend != 0 || GetRenderPassColorAttachmentCount(renderPass) > 1;

            EditorGUILayout.LabelField("BlendState", EditorStyles.label);
            using (new EditorGUI.IndentLevelScope())
            {
                if (rtArrayIndex >= blendState.rt.Length)
                {
                    EditorGUILayout.LabelField("(No blend state entry)");
                    return;
                }

                BlendStateRT rt = blendState.rt[rtArrayIndex];
                EditorGUILayout.LabelField("Blend", GetShaderLabBlendCommand(attachmentIndex, rt, useRtIndexSyntax));
                EditorGUILayout.LabelField("BlendOp", GetShaderLabBlendOpCommand(attachmentIndex, rt, useRtIndexSyntax));
                EditorGUILayout.LabelField("ColorMask", GetShaderLabColorMaskCommand(attachmentIndex, rt.writeMask, useRtIndexSyntax));
            }
        }

        private static bool TryGetRtArrayIndexForAttachment(int attachmentIndex, RenderPassInfo renderPass, out int rtArrayIndex)
        {
            rtArrayIndex = -1;

            if (renderPass == null || attachmentIndex < 0)
            {
                return false;
            }

            int depthAttachmentIndex = renderPass.depthAttachmentIndex;
            if (depthAttachmentIndex >= 0 && attachmentIndex == depthAttachmentIndex)
            {
                return false;
            }

            int totalAttachments = renderPass.attachmentCount > 0
                ? renderPass.attachmentCount
                : (renderPass.attachments != null ? renderPass.attachments.Length : 0);

            if (attachmentIndex >= totalAttachments)
            {
                return false;
            }

            int colorRtCursor = 0;
            for (int i = 0; i <= attachmentIndex; i++)
            {
                if (depthAttachmentIndex >= 0 && i == depthAttachmentIndex)
                {
                    continue;
                }

                if (i == attachmentIndex)
                {
                    rtArrayIndex = colorRtCursor;
                    return true;
                }

                colorRtCursor++;
            }

            return false;
        }

        private static int GetRenderPassColorAttachmentCount(RenderPassInfo renderPass)
        {
            if (renderPass == null)
            {
                return 0;
            }

            int totalAttachments = renderPass.attachmentCount > 0
                ? renderPass.attachmentCount
                : (renderPass.attachments != null ? renderPass.attachments.Length : 0);

            if (totalAttachments <= 0)
            {
                return 0;
            }

            int depthAttachmentIndex = renderPass.depthAttachmentIndex;
            return depthAttachmentIndex >= 0 && depthAttachmentIndex < totalAttachments
                ? Math.Max(0, totalAttachments - 1)
                : totalAttachments;
        }

        private static string GetAttachmentIndexRichText(int attachmentIndex, int depthAttachmentIndex)
        {
            string color = GetAttachmentIndexColorHex(attachmentIndex);
            string suffix = attachmentIndex == depthAttachmentIndex ? "d" : string.Empty;
            return $"<b><color={color}>{attachmentIndex}{suffix}</color></b>";
        }

        private static string GetAttachmentIndexColorHex(int attachmentIndex)
        {
            if (attachmentIndex >= 0 && attachmentIndex < s_AttachmentIndexColors.Length)
            {
                return s_AttachmentIndexColors[attachmentIndex];
            }

            return "#CFCFCF";
        }

        private static void DrawRenderStateInfo(RenderStateWrapper renderStateWrapper, RenderPassInfo renderPass)
        {
            if (renderStateWrapper?.renderState == null)
                return;
            
            RenderStateData renderState = renderStateWrapper.renderState;

            using (new EditorGUI.IndentLevelScope())
            {
                if (renderState.blendState != null)
                {
                    if (renderPass == null)
                    {
                        EditorGUILayout.LabelField("BlendState:", EditorStyles.label);
                        using (new EditorGUI.IndentLevelScope())
                        {
                            if (renderState.blendState.rt != null)
                            {
                                int displayCount = renderState.blendState.rt.Length;
                                bool useRtIndexSyntax = renderState.blendState.separateMRTBlend != 0 || renderState.blendState.rt.Length > 1;

                                for (int rtArrayIndex = 0; rtArrayIndex < displayCount; rtArrayIndex++)
                                {
                                    string rtLabel = $"RT[{rtArrayIndex}]";
                                    EditorGUILayout.LabelField(rtLabel, EditorStyles.label);
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                    BlendStateRT rt = renderState.blendState.rt[rtArrayIndex];
                                        EditorGUILayout.LabelField("Blend", GetShaderLabBlendCommand(rtArrayIndex, rt, useRtIndexSyntax));
                                        EditorGUILayout.LabelField("BlendOp", GetShaderLabBlendOpCommand(rtArrayIndex, rt, useRtIndexSyntax));
                                        EditorGUILayout.LabelField("ColorMask", GetShaderLabColorMaskCommand(rtArrayIndex, rt.writeMask, useRtIndexSyntax));
                                    }
                                }
                            }
                        }
                    }
                }
            }
                
            if (renderState.rasterState != null)
            {
                EditorGUILayout.LabelField("RasterState:", EditorStyles.label);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Cull", GetShaderLabCullCommand(renderState.rasterState.cullMode));
                    EditorGUILayout.LabelField("Offset(DepthBias)", GetShaderLabOffsetCommand(renderState.rasterState.depthBias, renderState.rasterState.slopeScaledDepthBias));
                    EditorGUILayout.LabelField("ZClip", GetShaderLabZClipCommand(renderState.rasterState.depthClip));
                    EditorGUILayout.LabelField("Conservative", GetShaderLabConservativeCommand(renderState.rasterState.conservative));
                }
            }
            
            if (renderState.depthState != null)
            {
                EditorGUILayout.LabelField("DepthState:", EditorStyles.label);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("ZWrite", GetShaderLabZWriteCommand(renderState.depthState.depthWrite));
                    EditorGUILayout.LabelField("ZTest", GetShaderLabZTestCommand(renderState.depthState.depthFunc));
                }
            }

            if (renderState.stencilState != null && renderState.stencilState.stencilEnable != 0)
            {
                EditorGUILayout.LabelField("StencilState:", EditorStyles.label);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Ref", FormatStencil8BitValue(renderState.stencilRef));
                    EditorGUILayout.LabelField("Read Mask", FormatStencil8BitValue(renderState.stencilState.readMask));
                    EditorGUILayout.LabelField("Write Mask", FormatStencil8BitValue(renderState.stencilState.writeMask));
                    EditorGUILayout.LabelField("Mask", GetRenderStateMaskName(renderState.mask));

                    EditorGUILayout.LabelField("Front", EditorStyles.label);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Comp", GetCompareFunctionName(renderState.stencilState.stencilFuncFront));
                        EditorGUILayout.LabelField("Pass", GetStencilOpName(renderState.stencilState.stencilPassOpFront));
                        EditorGUILayout.LabelField("Fail", GetStencilOpName(renderState.stencilState.stencilFailOpFront));
                        EditorGUILayout.LabelField("ZFail", GetStencilOpName(renderState.stencilState.stencilZFailOpFront));
                    }

                    EditorGUILayout.LabelField("Back", EditorStyles.label);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField("Comp", GetCompareFunctionName(renderState.stencilState.stencilFuncBack));
                        EditorGUILayout.LabelField("Pass", GetStencilOpName(renderState.stencilState.stencilPassOpBack));
                        EditorGUILayout.LabelField("Fail", GetStencilOpName(renderState.stencilState.stencilFailOpBack));
                        EditorGUILayout.LabelField("ZFail", GetStencilOpName(renderState.stencilState.stencilZFailOpBack));
                    }
                }
            }
        }

        private static string GetMeshTopologyName(int topologyValue)
        {
            if (topologyValue < 0 || !Enum.IsDefined(typeof(MeshTopology), topologyValue))
            {
                return $"Unknown ({topologyValue})";
            }

            MeshTopology topology = (MeshTopology)topologyValue;
            return $"{topology}";
        }

        private static string GetVertexAttributeName(int channelIndex)
        {
            if (channelIndex < 0 || !Enum.IsDefined(typeof(VertexAttribute), channelIndex))
            {
                return $"Unknown ({channelIndex})";
            }

            VertexAttribute attribute = (VertexAttribute)channelIndex;
            return attribute.ToString();
        }

        private static string GetVertexAttributeFormatSummary(int formatValue, int dimension)
        {
            string formatName = GetVertexAttributeFormatName(formatValue);
            int bytesPerComponent = GetBytesPerVertexAttributeComponent(formatValue);

            if (dimension <= 0 || bytesPerComponent <= 0)
            {
                return $"{formatName} x {dimension} (? bytes)";
            }

            int totalBytes = bytesPerComponent * dimension;
            string byteLabel = totalBytes == 1 ? "1 byte" : $"{totalBytes} bytes";
            return $"{formatName} x {dimension} ({byteLabel})";
        }

        private static string GetVertexAttributeFormatName(int formatValue)
        {
            if (!Enum.IsDefined(typeof(VertexAttributeFormat), formatValue))
            {
                return $"Unknown ({formatValue})";
            }

            VertexAttributeFormat format = (VertexAttributeFormat)formatValue;
            return format.ToString();
        }

        private static int GetBytesPerVertexAttributeComponent(int formatValue)
        {
            if (!Enum.IsDefined(typeof(VertexAttributeFormat), formatValue))
            {
                return 0;
            }

            VertexAttributeFormat format = (VertexAttributeFormat)formatValue;
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8:
                    return 1;
                default:
                    return 0;
            }
        }

        private static string GetGraphicsFormatName(int formatValue)
        {
            Type graphicsFormatType = typeof(UnityEngine.Experimental.Rendering.GraphicsFormat);
            if (!Enum.IsDefined(graphicsFormatType, formatValue))
            {
                return $"Unknown ({formatValue})";
            }

            var format = (UnityEngine.Experimental.Rendering.GraphicsFormat)formatValue;
            return $"{format}";
        }

        private static string GetCullModeName(int cullModeValue)
        {
            if (!Enum.IsDefined(typeof(CullMode), cullModeValue))
            {
                return $"Unknown ({cullModeValue})";
            }

            CullMode cullMode = (CullMode)cullModeValue;
            return $"{cullMode}";
        }

        private static string GetForceCullModeName(int forceCullModeValue)
        {
            if (forceCullModeValue == -1)
            {
                return "Disabled";
            }

            return GetCullModeName(forceCullModeValue);
        }

        private static string GetBlendModeName(int blendModeValue)
        {
            if (!Enum.IsDefined(typeof(BlendMode), blendModeValue))
            {
                return $"Unknown ({blendModeValue})";
            }

            BlendMode blendMode = (BlendMode)blendModeValue;
            return $"{blendMode}";
        }

        private static string GetBlendOpName(int blendOpValue)
        {
            if (!Enum.IsDefined(typeof(BlendOp), blendOpValue))
            {
                return $"Unknown ({blendOpValue})";
            }

            BlendOp blendOp = (BlendOp)blendOpValue;
            return $"{blendOp}";
        }

        private static string GetShaderLabAlphaToMask(int alphaToMaskValue)
        {
            return alphaToMaskValue == 0 ? "Off" : "On";
        }

        private static string GetShaderLabBlendCommand(int rtIndex, BlendStateRT rt, bool useRtIndexSyntax)
        {
            string src = GetBlendModeName(rt.srcBlend);
            string dst = GetBlendModeName(rt.dstBlend);
            string srcAlpha = GetBlendModeName(rt.srcBlendAlpha);
            string dstAlpha = GetBlendModeName(rt.dstBlendAlpha);

            if (src == srcAlpha && dst == dstAlpha)
            {
                return $"{src} {dst}";
            }

            return $"{src} {dst}, {srcAlpha} {dstAlpha}";
        }

        private static string GetShaderLabBlendOpCommand(int rtIndex, BlendStateRT rt, bool useRtIndexSyntax)
        {
            string op = GetBlendOpName(rt.blendOp);
            string opAlpha = GetBlendOpName(rt.blendOpAlpha);

            if (op == opAlpha)
            {
                return $"{op}";
            }

            return $"{op}, {opAlpha}";
        }

        private static string GetShaderLabColorMaskCommand(int rtIndex, int writeMask, bool useRtIndexSyntax)
        {
            string channels = GetColorMaskSummary(writeMask);
            if (channels == "None")
            {
                channels = "0";
            }

            return channels;
        }

        private static string GetShaderLabCullCommand(int cullModeValue)
        {
            return GetCullModeName(cullModeValue);
        }

        private static string GetShaderLabOffsetCommand(float depthBias, float slopeScaledDepthBias)
        {
            return $"{FormatShaderLabFloat(slopeScaledDepthBias)}, {FormatShaderLabFloat(depthBias)}";
        }

        private static string FormatShaderLabFloat(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string GetShaderLabZClipCommand(int depthClipValue)
        {
            return depthClipValue == 0 ? "Off" : "On";
        }

        private static string GetShaderLabConservativeCommand(int conservativeValue)
        {
            return conservativeValue == 0 ? "Off" : "On";
        }

        private static string GetShaderLabZWriteCommand(int depthWriteValue)
        {
            return depthWriteValue == 0 ? "Off" : "On";
        }

        private static string GetShaderLabZTestCommand(int depthFuncValue)
        {
            return GetCompareFunctionName(depthFuncValue);
        }

        private static string GetCompareFunctionName(int compareFunctionValue)
        {
            if (!Enum.IsDefined(typeof(CompareFunction), compareFunctionValue))
            {
                return $"Unknown ({compareFunctionValue})";
            }

            CompareFunction compare = (CompareFunction)compareFunctionValue;
            return $"{compare}";
        }

        private static string GetStencilOpName(int stencilOpValue)
        {
            if (!Enum.IsDefined(typeof(StencilOp), stencilOpValue))
            {
                return $"Unknown ({stencilOpValue})";
            }

            StencilOp stencilOp = (StencilOp)stencilOpValue;
            return $"{stencilOp}";
        }

        private static string GetRenderStateMaskName(int maskValue)
        {
            if (maskValue == 0)
            {
                return "Nothing";
            }

            if (Enum.IsDefined(typeof(RenderStateMask), maskValue))
            {
                RenderStateMask mask = (RenderStateMask)maskValue;
                return $"{mask}";
            }

            return $"Unknown ({maskValue})";
        }

        private static string FormatStencil8BitValue(int value)
        {
            int byteValue = value & 0xFF;
            return $"{byteValue} (0x{byteValue:X2} / 0b{Convert.ToString(byteValue, 2).PadLeft(8, '0')})";
        }

        private static string GetColorMaskSummary(int writeMask)
        {
            bool r = (writeMask & (int)ColorWriteMask.Red) != 0;
            bool g = (writeMask & (int)ColorWriteMask.Green) != 0;
            bool b = (writeMask & (int)ColorWriteMask.Blue) != 0;
            bool a = (writeMask & (int)ColorWriteMask.Alpha) != 0;

            var enabled = new StringBuilder(4);
            if (r) enabled.Append('R');
            if (g) enabled.Append('G');
            if (b) enabled.Append('B');
            if (a) enabled.Append('A');

            return enabled.Length == 0 ? "None" : enabled.ToString();
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
            m_VertexLayoutLookup.Clear();
            m_RenderPassLookup.Clear();
            m_RenderStateLookup.Clear();

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
                
                // Build lookups for graphics state maps
                if (root.m_VertexLayoutInfoMap != null)
                {
                    foreach (VertexLayoutInfoEntry entry in root.m_VertexLayoutInfoMap)
                    {
                        m_VertexLayoutLookup[entry.first] = entry.second;
                    }
                }
                
                if (root.m_RenderPassInfoMap != null)
                {
                    foreach (RenderPassInfoEntry entry in root.m_RenderPassInfoMap)
                    {
                        m_RenderPassLookup[entry.first] = entry.second;
                    }
                }
                
                if (root.m_RenderStateMap != null)
                {
                    foreach (RenderStateInfoEntry entry in root.m_RenderStateMap)
                    {
                        m_RenderStateLookup[entry.first] = entry.second;
                    }
                }

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
                    
                    // Decode graphics state info
                    VertexLayoutInfo[] decodedVertexLayouts = null;
                    RenderPassInfo[] decodedRenderPasses = null;
                    RenderStateWrapper[] decodedRenderStates = null;
                    bool[] graphicsStateFoldout = null;
                    bool[] configurationFoldout = null;
                    bool[] vertexLayoutFoldout = null;
                    bool[] renderDetailsFoldout = null;
                    
                    if (variant.graphicsStateInfoSet != null && variant.graphicsStateInfoSet.Length > 0)
                    {
                        decodedVertexLayouts = new VertexLayoutInfo[variant.graphicsStateInfoSet.Length];
                        decodedRenderPasses = new RenderPassInfo[variant.graphicsStateInfoSet.Length];
                        decodedRenderStates = new RenderStateWrapper[variant.graphicsStateInfoSet.Length];
                        graphicsStateFoldout = new bool[variant.graphicsStateInfoSet.Length];
                        configurationFoldout = new bool[variant.graphicsStateInfoSet.Length];
                        vertexLayoutFoldout = new bool[variant.graphicsStateInfoSet.Length];
                        renderDetailsFoldout = new bool[variant.graphicsStateInfoSet.Length];
                        
                        for (int i = 0; i < variant.graphicsStateInfoSet.Length; i++)
                        {
                            GraphicsStateInfo stateInfo = variant.graphicsStateInfoSet[i];
                            m_VertexLayoutLookup.TryGetValue(stateInfo.vertexLayout, out decodedVertexLayouts[i]);
                            m_RenderPassLookup.TryGetValue(stateInfo.renderPass, out decodedRenderPasses[i]);
                            m_RenderStateLookup.TryGetValue(stateInfo.renderState, out decodedRenderStates[i]);
                            graphicsStateFoldout[i] = false;
                            configurationFoldout[i] = false;
                            vertexLayoutFoldout[i] = false;
                            renderDetailsFoldout[i] = false;
                        }
                    }

                    group.views.Add(new VariantView
                    {
                        source = variant,
                        keywords = splitKeywords,
                        displayKeywords = displayKeywords,
                        passKeywords = passKeywords,
                        shader = resolvedShader,
                        passName = passName,
                        graphicsStateInfoSet = variant.graphicsStateInfoSet,
                        decodedVertexLayouts = decodedVertexLayouts,
                        decodedRenderPasses = decodedRenderPasses,
                        decodedRenderStates = decodedRenderStates,
                        graphicsStateInfoFoldout = false,
                        graphicsStateFoldout = graphicsStateFoldout,
                        configurationFoldout = configurationFoldout,
                        vertexLayoutFoldout = vertexLayoutFoldout,
                        renderDetailsFoldout = renderDetailsFoldout
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