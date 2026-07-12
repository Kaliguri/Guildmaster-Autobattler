using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Отличия конфиг-SO от его код-дефолтов (свежий <c>CreateInstance</c> того же типа). Показывает
    /// «что вообще накручено» — field-initializers как страховка, теперь и визуально. Тестируемо.
    /// </summary>
    public static class ConfigDiff
    {
        public readonly struct Row
        {
            public readonly string Path, Value, Default;
            public Row(string path, string value, string def) { Path = path; Value = value; Default = def; }
        }

        public static List<Row> Diff(Object asset)
        {
            var result = new List<Row>();
            if (asset == null) return result;

            var def = ScriptableObject.CreateInstance(asset.GetType());
            try
            {
                using var soA = new SerializedObject(asset);
                using var soB = new SerializedObject(def);
                var a = soA.GetIterator();
                while (a.NextVisible(true))
                {
                    if (a.propertyPath == "m_Script") continue;
                    var b = soB.FindProperty(a.propertyPath);
                    if (b == null) continue;
                    if (!SerializedProperty.DataEquals(a, b))
                        result.Add(new Row(a.propertyPath, ValStr(a), ValStr(b)));
                }
            }
            finally { Object.DestroyImmediate(def); }

            return result;
        }

        private static string ValStr(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.###");
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue.ToString();
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumDisplayNames != null && p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString();
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? p.objectReferenceValue.name : "null";
                case SerializedPropertyType.ArraySize: return p.intValue.ToString();
                default: return p.propertyType.ToString();
            }
        }
    }
}
