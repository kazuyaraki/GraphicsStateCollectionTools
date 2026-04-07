using System;
using UnityEditor;
using UnityEngine;

namespace GSCTools
{
    internal static class ShaderEditorUtility
    {
        private static GUIStyle s_ShaderLinkStyle;

        public static string NormalizeStageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string lower = value.ToLowerInvariant();
            if (lower.Contains("vertex")) return "Vertex";
            if (lower.Contains("hull")) return "Hull";
            if (lower.Contains("domain")) return "Domain";
            if (lower.Contains("geometry")) return "Geometry";
            if (lower.Contains("fragment") || lower.Contains("pixel")) return "Fragment";
            if (lower.Contains("compute")) return "Compute";
            if (lower.Contains("all")) return "All";
            if (lower.Contains("raytracing") || lower.Contains("ray tracing")) return "RayTracing";
            return string.Empty;
        }

        public static int BuildStageSortKey(string stageName)
        {
            if (string.IsNullOrEmpty(stageName))
            {
                return 999;
            }

            string normalized = NormalizeStageName(stageName);
            switch (normalized)
            {
                case "Vertex": return 0;
                case "Hull": return 1;
                case "Domain": return 2;
                case "Geometry": return 3;
                case "Fragment": return 4;
                case "Compute": return 5;
                case "All": return 6;
                case "RayTracing": return 7;
                default: return 998;
            }
        }

        public static GUIStyle GetShaderLinkStyle()
        {
            if (s_ShaderLinkStyle != null)
            {
                return s_ShaderLinkStyle;
            }

            s_ShaderLinkStyle = new GUIStyle(EditorStyles.label);
            Color linkColor = EditorGUIUtility.isProSkin
                ? new Color(0.50f, 0.75f, 1.00f)
                : new Color(0.10f, 0.33f, 0.80f);
            s_ShaderLinkStyle.normal.textColor = linkColor;
            s_ShaderLinkStyle.hover.textColor = linkColor;
            s_ShaderLinkStyle.active.textColor = linkColor;
            s_ShaderLinkStyle.focused.textColor = linkColor;

            return s_ShaderLinkStyle;
        }
    }
}
