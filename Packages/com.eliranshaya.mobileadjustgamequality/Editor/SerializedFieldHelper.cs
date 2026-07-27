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
    /// URP configures almost everything through <c>private [SerializeField]</c> fields
    /// with no public setter, so <see cref="SerializedObject"/> is the only supported way
    /// to author a generated asset. The catch is that those field names are internal
    /// implementation detail and have changed between URP 14, 15, 16 and 17.
    /// </para>
    /// <para>
    /// A missing field must therefore never be fatal. Everything here returns
    /// <c>false</c> and records the miss instead of throwing, so a URP version this
    /// package has not seen degrades to "that one setting kept its default" rather than
    /// "Setup died halfway through and left six broken assets behind".
    /// </para>
    /// </remarks>
    internal static class SerializedFieldHelper
    {
        static readonly List<string> s_Skipped = new List<string>();
        static readonly HashSet<string> s_WarnedOnce = new HashSet<string>();

        /// <summary>
        /// Fields that could not be written during the current run, formatted for the
        /// Setup window's report.
        /// </summary>
        internal static IReadOnlyList<string> Skipped => s_Skipped;

        /// <summary>Clears the skipped-field record. Call once at the start of a Setup run.</summary>
        internal static void BeginRun()
        {
            s_Skipped.Clear();
            s_WarnedOnce.Clear();
        }

        /// <summary>
        /// Writes a value into a serialized property, if that property exists.
        /// </summary>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="path">The serialized property path, for example <c>m_RenderScale</c>.</param>
        /// <param name="value">The value to assign. Converted to match the property type.</param>
        /// <returns><c>true</c> if the property existed and was written.</returns>
        internal static bool TrySet(SerializedObject so, string path, object value)
        {
            if (so == null || string.IsNullOrEmpty(path))
                return false;

            SerializedProperty property = so.FindProperty(path);
            if (property == null)
            {
                RecordMiss(so, path);
                return false;
            }

            return TrySet(property, DescribeTarget(so, path), value);
        }

        /// <summary>
        /// Writes a value into an already-resolved serialized property.
        /// </summary>
        /// <param name="property">The property to write.</param>
        /// <param name="description">Human-readable identity used in the skipped report.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if the value was written.</returns>
        internal static bool TrySet(SerializedProperty property, string description, object value)
        {
            if (property == null)
                return false;

            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return true;

                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return true;

                    case SerializedPropertyType.Enum:
                        // Unity serializes enums by their underlying integer, which is
                        // what callers pass after casting the real URP enum type. That
                        // matters: several URP enums have non-sequential values
                        // (MsaaQuality.Disabled is 1, LightRenderingMode.PerVertex is 2)
                        // so intValue and enumValueIndex are not interchangeable.
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.String:
                        property.stringValue = value == null ? string.Empty : value.ToString();
                        return true;

                    case SerializedPropertyType.ObjectReference:
                        property.objectReferenceValue = value as UnityEngine.Object;
                        return true;

                    case SerializedPropertyType.LayerMask:
                        property.intValue = Convert.ToInt32(value);
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

        /// <summary>
        /// Tries a list of candidate property names in order and writes the first one that
        /// exists. Use for fields that were renamed between URP or Unity versions, for
        /// example <c>textureQuality</c> which became <c>globalTextureMipmapLimit</c>.
        /// </summary>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="paths">Candidate property paths, most likely first.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if one of the candidates existed and was written.</returns>
        internal static bool TrySetAny(SerializedObject so, string[] paths, object value)
        {
            if (so == null || paths == null || paths.Length == 0)
                return false;

            foreach (string path in paths)
            {
                SerializedProperty property = so.FindProperty(path);
                if (property == null)
                    continue;

                return TrySet(property, DescribeTarget(so, path), value);
            }

            RecordMiss(so, string.Join(" | ", paths));
            return false;
        }

        /// <summary>
        /// Writes a property relative to an existing one. Used for array elements, where
        /// there is no absolute path to hand to <see cref="TrySet(SerializedObject,string,object)"/> -
        /// a Quality Level is an element of <c>m_QualitySettings</c>, not a top-level object.
        /// </summary>
        /// <param name="parent">The element to write inside.</param>
        /// <param name="relativePath">Relative property path, for example <c>lodBias</c>.</param>
        /// <param name="ownerDescription">Owner name used in the skipped report.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if the property existed and was written.</returns>
        internal static bool TrySetRelative(SerializedProperty parent, string relativePath, string ownerDescription, object value)
        {
            if (parent == null || string.IsNullOrEmpty(relativePath))
                return false;

            SerializedProperty property = parent.FindPropertyRelative(relativePath);
            if (property == null)
            {
                Record($"{ownerDescription}.{relativePath} (property not found on this Unity version)");
                return false;
            }

            return TrySet(property, $"{ownerDescription}.{relativePath}", value);
        }

        /// <summary>
        /// Relative counterpart of <see cref="TrySetAny"/>, for fields that were renamed
        /// between Unity versions - <c>textureQuality</c> became
        /// <c>globalTextureMipmapLimit</c> in Unity 2022.2.
        /// </summary>
        /// <param name="parent">The element to write inside.</param>
        /// <param name="relativePaths">Candidate relative paths, most likely first.</param>
        /// <param name="ownerDescription">Owner name used in the skipped report.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns><c>true</c> if one of the candidates existed and was written.</returns>
        internal static bool TrySetRelativeAny(SerializedProperty parent, string[] relativePaths, string ownerDescription, object value)
        {
            if (parent == null || relativePaths == null || relativePaths.Length == 0)
                return false;

            foreach (string relativePath in relativePaths)
            {
                SerializedProperty property = parent.FindPropertyRelative(relativePath);
                if (property == null)
                    continue;

                return TrySet(property, $"{ownerDescription}.{relativePath}", value);
            }

            Record($"{ownerDescription}.[{string.Join(" | ", relativePaths)}] (none found on this Unity version)");
            return false;
        }

        /// <summary>
        /// Writes an enum property by matching one of the supplied names against the
        /// property's own <see cref="SerializedProperty.enumNames"/>.
        /// </summary>
        /// <remarks>
        /// Use this when the enum's numeric values are not stable across versions but its
        /// names are - <c>RenderingMode</c> is the case that forces it, because
        /// <c>ForwardPlus</c> and <c>DeferredPlus</c> were inserted into the middle of the
        /// enum after URP 14.
        /// </remarks>
        /// <param name="so">The serialized object to write into.</param>
        /// <param name="path">The enum property path.</param>
        /// <param name="candidateNames">Acceptable enum names, most preferred first.</param>
        /// <returns><c>true</c> if a matching name was found and written.</returns>
        internal static bool TrySetEnumByName(SerializedObject so, string path, params string[] candidateNames)
        {
            if (so == null || string.IsNullOrEmpty(path) || candidateNames == null)
                return false;

            SerializedProperty property = so.FindProperty(path);
            if (property == null)
            {
                RecordMiss(so, path);
                return false;
            }

            if (property.propertyType != SerializedPropertyType.Enum)
            {
                Record($"{DescribeTarget(so, path)} (expected an enum, found {property.propertyType})");
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

            Record($"{DescribeTarget(so, path)} (none of [{string.Join(", ", candidateNames)}] " +
                   $"exist on this version, which has [{string.Join(", ", names)}])");
            return false;
        }

        static void RecordMiss(SerializedObject so, string path)
        {
            Record($"{DescribeTarget(so, path)} (property not found on this URP/Unity version)");
        }

        static string DescribeTarget(SerializedObject so, string path)
        {
            UnityEngine.Object target = so != null ? so.targetObject : null;
            string typeName = target != null ? target.GetType().Name : "<unknown>";
            return $"{typeName}.{path}";
        }

        static void Record(string entry)
        {
            if (!s_WarnedOnce.Add(entry))
                return;

            // Once per distinct field per run: three tiers write the same field list, and
            // a version mismatch would otherwise produce the same warning three times.
            s_Skipped.Add(entry);
            Debug.LogWarning($"[MobileQuality] Skipped {entry}");
        }
    }
}
