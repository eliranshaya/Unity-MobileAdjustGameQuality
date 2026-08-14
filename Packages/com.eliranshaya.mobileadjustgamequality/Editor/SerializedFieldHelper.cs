using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Every serialized write in this package goes through here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// URP configures almost everything through <c>private [SerializeField]</c> fields with
    /// no public setter, so <see cref="SerializedObject"/> is the only supported way to
    /// author a generated asset. There is no <c>asset.mainLightShadowmapResolution = 512</c>;
    /// that does not compile. The catch is that those field names are implementation detail
    /// and have changed between URP 14, 15, 16 and 17.
    /// </para>
    /// <para>
    /// A missing field must therefore never be fatal. Everything here returns <c>false</c>
    /// and records the miss instead of throwing, so a URP version this package has not seen
    /// degrades to "that one setting kept its default" rather than "Setup died halfway
    /// through and left broken assets behind".
    /// </para>
    /// </remarks>
    internal static class SerializedFieldHelper
    {
        static readonly List<string> s_Skipped = new List<string>();
        static readonly HashSet<string> s_Seen = new HashSet<string>();

        /// <summary>Fields that could not be written during the current run.</summary>
        internal static IReadOnlyList<string> Skipped => s_Skipped;

        /// <summary>Clears the record. Call once at the start of a Setup run.</summary>
        internal static void BeginRun()
        {
            s_Skipped.Clear();
            s_Seen.Clear();
        }

        /// <summary>Writes a value into a serialized property, if that property exists.</summary>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="path">The property path, for example <c>m_RenderScale</c>.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if the property existed and was written.</returns>
        internal static bool TrySet(SerializedObject so, string path, object value)
        {
            if (so == null || string.IsNullOrEmpty(path))
                return false;

            SerializedProperty property = so.FindProperty(path);
            if (property != null)
                return Write(property, Describe(so, path), value);

            Record($"{Describe(so, path)} (not found on this URP/Unity version)");
            return false;
        }

        /// <summary>
        /// Tries several property names in order and writes the first that exists. For
        /// fields renamed between versions, such as <c>textureQuality</c> becoming
        /// <c>globalTextureMipmapLimit</c> in Unity 2022.2.
        /// </summary>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="paths">Candidate paths, most likely first.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if one of the candidates existed and was written.</returns>
        internal static bool TrySetAny(SerializedObject so, string[] paths, object value)
        {
            if (so == null || paths == null)
                return false;

            foreach (string path in paths)
            {
                SerializedProperty property = so.FindProperty(path);
                if (property != null)
                    return Write(property, Describe(so, path), value);
            }

            Record($"[{string.Join(" | ", paths)}] (none found on this version)");
            return false;
        }

        /// <summary>Writes a property relative to an existing one, for array elements.</summary>
        /// <param name="parent">The element to write inside.</param>
        /// <param name="relativePath">Relative path, for example <c>lodBias</c>.</param>
        /// <param name="owner">Owner name used in the report.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if the property existed and was written.</returns>
        internal static bool TrySetRelative(SerializedProperty parent, string relativePath, string owner, object value)
        {
            if (parent == null || string.IsNullOrEmpty(relativePath))
                return false;

            SerializedProperty property = parent.FindPropertyRelative(relativePath);
            if (property != null)
                return Write(property, $"{owner}.{relativePath}", value);

            Record($"{owner}.{relativePath} (not found on this Unity version)");
            return false;
        }

        /// <summary>Relative counterpart of <see cref="TrySetAny"/>.</summary>
        /// <param name="parent">The element to write inside.</param>
        /// <param name="relativePaths">Candidate relative paths, most likely first.</param>
        /// <param name="owner">Owner name used in the report.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if one of the candidates existed and was written.</returns>
        internal static bool TrySetRelativeAny(SerializedProperty parent, string[] relativePaths, string owner, object value)
        {
            if (parent == null || relativePaths == null)
                return false;

            foreach (string relativePath in relativePaths)
            {
                SerializedProperty property = parent.FindPropertyRelative(relativePath);
                if (property != null)
                    return Write(property, $"{owner}.{relativePath}", value);
            }

            Record($"{owner}.[{string.Join(" | ", relativePaths)}] (none found)");
            return false;
        }

        /// <summary>
        /// Writes an enum property by matching a name against the property's own
        /// <see cref="SerializedProperty.enumNames"/>.
        /// </summary>
        /// <remarks>
        /// For enums whose numeric values are not stable across versions but whose names
        /// are. <c>RenderingMode</c> is the case that forces it: <c>ForwardPlus</c> and
        /// <c>DeferredPlus</c> were inserted into the middle of the enum after URP 14.
        /// </remarks>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="path">The enum property path.</param>
        /// <param name="candidateNames">Acceptable names, most preferred first.</param>
        /// <returns><c>true</c> if a matching name was found and written.</returns>
        internal static bool TrySetEnumByName(SerializedObject so, string path, params string[] candidateNames)
        {
            if (so == null || candidateNames == null)
                return false;

            SerializedProperty property = so.FindProperty(path);
            if (property == null)
            {
                Record($"{Describe(so, path)} (not found on this URP version)");
                return false;
            }

            if (property.propertyType != SerializedPropertyType.Enum)
            {
                Record($"{Describe(so, path)} (expected an enum, found {property.propertyType})");
                return false;
            }

            string[] names = property.enumNames;
            foreach (string candidate in candidateNames)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (!string.Equals(names[i], candidate, StringComparison.Ordinal))
                        continue;

                    property.enumValueIndex = i;
                    return true;
                }
            }

            Record($"{Describe(so, path)} (none of [{string.Join(", ", candidateNames)}] exist; " +
                   $"this version has [{string.Join(", ", names)}])");
            return false;
        }

        static bool Write(SerializedProperty property, string description, object value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return true;

                    // Enums are serialized by their underlying integer, which is what callers
                    // pass after casting the real URP enum. That matters: several URP enums
                    // have non-sequential values - MsaaQuality.Disabled is 1,
                    // LightRenderingMode.PerVertex is 2 - so intValue and enumValueIndex are
                    // not interchangeable.
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return true;

                    case SerializedPropertyType.String:
                        property.stringValue = value?.ToString() ?? string.Empty;
                        return true;

                    case SerializedPropertyType.ObjectReference:
                        property.objectReferenceValue = value as UnityEngine.Object;
                        return true;

                    default:
                        Record($"{description} (unsupported property type {property.propertyType})");
                        return false;
                }
            }
            catch (Exception e)
            {
                Record($"{description} ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }

        static string Describe(SerializedObject so, string path)
        {
            UnityEngine.Object target = so != null ? so.targetObject : null;
            return $"{(target != null ? target.GetType().Name : "<unknown>")}.{path}";
        }

        static void Record(string entry)
        {
            if (!s_Seen.Add(entry))
                return;

            s_Skipped.Add(entry);
            Debug.LogWarning($"[MobileQuality] Skipped {entry}");
        }
    }
}
