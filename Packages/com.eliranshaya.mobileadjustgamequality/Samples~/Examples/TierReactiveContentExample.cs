using UnityEngine;

namespace MobileAdjustGameQuality.Samples
{
    /// <summary>
    /// Shows how gameplay content reacts to a tier change: turn optional dressing off on
    /// Low, and let animators stop updating when they are off screen.
    /// </summary>
    /// <remarks>
    /// The URP asset covers rendering settings. It cannot know that a particle system is
    /// decorative or that a crowd of background characters does not need to animate off
    /// screen - that judgement lives in the game, which is what this pattern is for.
    /// </remarks>
    public class TierReactiveContentExample : MonoBehaviour
    {
        [Tooltip("Decorative effects. Disabled on Low.")]
        [SerializeField] GameObject[] optionalVfx = new GameObject[0];

        [Tooltip("Set dressing. Disabled on Low, enabled from Balanced up.")]
        [SerializeField] GameObject[] optionalProps = new GameObject[0];

        [Tooltip("Animators whose culling mode follows the tier.")]
        [SerializeField] Animator[] animators = new Animator[0];

        void OnEnable()
        {
            if (QualityTierManager.Instance == null)
                return;

            QualityTierManager.Instance.OnTierChanged += Apply;

            // Apply once on enable as well: the manager raises the event when the tier is
            // resolved at boot, which happens before most scene objects exist.
            Apply(QualityTierManager.Instance.CurrentTier);
        }

        void OnDisable()
        {
            if (QualityTierManager.Instance != null)
                QualityTierManager.Instance.OnTierChanged -= Apply;
        }

        void Apply(QualityTier tier)
        {
            SetActive(optionalVfx, tier >= QualityTier.Balanced);
            SetActive(optionalProps, tier >= QualityTier.Balanced);

            // AlwaysAnimate is only affordable when there is headroom to spare. On Low,
            // CullCompletely stops the animator entirely once it leaves the frustum.
            AnimatorCullingMode culling = tier switch
            {
                QualityTier.Low => AnimatorCullingMode.CullCompletely,
                QualityTier.Balanced => AnimatorCullingMode.CullUpdateTransforms,
                _ => AnimatorCullingMode.AlwaysAnimate
            };

            if (animators == null)
                return;

            foreach (Animator animator in animators)
            {
                if (animator != null)
                    animator.cullingMode = culling;
            }
        }

        static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null)
                return;

            foreach (GameObject item in objects)
            {
                if (item != null)
                    item.SetActive(active);
            }
        }
    }
}
