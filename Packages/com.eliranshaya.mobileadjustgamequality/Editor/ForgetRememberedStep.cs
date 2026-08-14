using UnityEditor;
using UnityEngine;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Clears the quality rung remembered for this machine.
    /// </summary>
    /// <remarks>
    /// Useful after changing the ladder or the tuning, and after any session where the
    /// editor's own stalls pushed the ladder somewhere it does not belong - a remembered rung
    /// is sticky by design, so without this the next session starts from the bad answer.
    /// </remarks>
    internal static class ForgetRememberedStep
    {
        [MenuItem("Tools/Mobile Quality/Forget Remembered Step", false, 210)]
        static void Forget()
        {
            QualityStepMemory.Clear();
            Debug.Log("[MobileQuality] Forgot the remembered quality step. The next session starts from " +
                      "the profile's Start Step and calibrates from there.");
        }
    }
}
