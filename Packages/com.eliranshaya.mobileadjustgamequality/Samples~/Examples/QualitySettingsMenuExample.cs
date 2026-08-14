using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MobileAdjustGameQuality.Samples
{
    /// <summary>
    /// Wires a UI dropdown to the three tiers. Drop it on the same object as the
    /// dropdown, or assign the dropdown in the inspector.
    /// </summary>
    /// <remarks>
    /// The one thing worth copying from this file is <c>isPlayerChoice: true</c>. It marks
    /// the selection as deliberate, so device detection will not quietly override it the
    /// next time the game starts - a player who picked Low on a flagship phone to save
    /// battery meant it.
    /// </remarks>
    [RequireComponent(typeof(Dropdown))]
    public class QualitySettingsMenuExample : MonoBehaviour
    {
        [SerializeField] Dropdown dropdown;

        void Awake()
        {
            if (dropdown == null)
                dropdown = GetComponent<Dropdown>();

            // Order must match QualityTier: index 0 Low, 1 Balanced, 2 High.
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "Low", "Balanced", "High" });
        }

        void OnEnable()
        {
            if (MobileQualityManager.Instance != null)
                dropdown.SetValueWithoutNotify((int)MobileQualityManager.Instance.CurrentTier);

            dropdown.onValueChanged.AddListener(OnDropdownChanged);
        }

        void OnDisable()
        {
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }

        void OnDropdownChanged(int index)
        {
            if (MobileQualityManager.Instance == null)
                return;

            MobileQualityManager.Instance.SetTier(index, isPlayerChoice: true);
        }
    }
}
