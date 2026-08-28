using System;
using System.Reflection;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The one place that absorbs a difference between the URP versions this package
    /// supports. Nothing here throws: a version that does not fit is reported and skipped.
    /// </summary>
    internal static class UrpCompat
    {
        /// <summary>
        /// The serialized field behind <c>mainLightShadowmapResolution</c>, identical in
        /// URP 14 and URP 17.
        /// </summary>
        const string k_ShadowmapResolutionField = "m_MainLightShadowmapResolution";

        static FieldInfo s_ShadowmapResolution;
        static bool s_LookedUp;
        static bool s_Reported;

        /// <summary>
        /// Sets the main light shadowmap resolution on a URP asset at runtime.
        /// </summary>
        /// <param name="asset">The active URP asset.</param>
        /// <param name="resolution">Resolution in pixels, for example 512.</param>
        /// <returns><c>true</c> when it was applied.</returns>
        /// <remarks>
        /// <para>
        /// URP 17 made this property's setter public. URP 14 - what Unity 2022.3 ships, and
        /// the oldest version this package supports - declares it <c>internal set</c>, so
        /// assigning the property does not compile there at all. The property setter is
        /// nothing but an assignment to the serialized field on both versions, so writing
        /// that field does exactly the same thing on every version, and compiles on all of
        /// them.
        /// </para>
        /// <para>
        /// The field rather than the setter, deliberately. The field is
        /// <c>[SerializeField]</c> on the asset Graphics Settings points at, so managed
        /// stripping has to keep it or the asset could not be deserialized in a build. An
        /// internal setter that nothing calls has no such protection.
        /// </para>
        /// </remarks>
        internal static bool TrySetMainLightShadowmapResolution(
            UniversalRenderPipelineAsset asset, int resolution)
        {
            if (asset == null)
                return false;

            if (!s_LookedUp)
            {
                s_LookedUp = true;

                FieldInfo field = typeof(UniversalRenderPipelineAsset).GetField(
                    k_ShadowmapResolutionField, BindingFlags.Instance | BindingFlags.NonPublic);

                // Enum, because the field holds URP's ShadowResolution rather than an int.
                // Checking instead of casting keeps a renamed or retyped field from throwing.
                if (field != null && field.FieldType.IsEnum)
                    s_ShadowmapResolution = field;
            }

            if (s_ShadowmapResolution == null)
            {
                if (!s_Reported)
                {
                    s_Reported = true;
                    QualityDevLog.Log(
                        $"URP has no '{k_ShadowmapResolutionField}' field on this version - the " +
                        "ladder will leave the shadowmap resolution alone. Everything else works.");
                }

                return false;
            }

            s_ShadowmapResolution.SetValue(
                asset, Enum.ToObject(s_ShadowmapResolution.FieldType, resolution));
            return true;
        }
    }
}
