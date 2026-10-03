using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Purgers.GameFlow.Transition
{
    /// <summary>Local screen overlay. FadeIn reveals gameplay; FadeOut covers it.</summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFadeLayer : MonoBehaviour
    {
        private static readonly HashSet<ScreenFadeLayer> visibleLayers = new HashSet<ScreenFadeLayer>();
        public static bool IsCoveringScreen => visibleLayers.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => visibleLayers.Clear();

        [SerializeField, Min(0f), Tooltip("黑幕淡入或淡出的秒數，使用不受 Time.timeScale 影響的時間。")]
        private float fadeDuration = 1.5f;
        private Canvas canvas;
        private Image image;
        private float from;
        private float target;
        private float elapsed;
        public float Alpha { get; private set; }
        public bool IsFading { get; private set; }
        public float FadeDuration { get => fadeDuration; set => fadeDuration = Mathf.Max(0f, value); }

        private void Awake()
        {
            var root = new GameObject("BlackOverlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            // Select the highest existing sorting layer, without changing project layers.
            int highest = int.MinValue;
            foreach (SortingLayer layer in SortingLayer.layers)
                if (layer.value >= highest)
                {
                    highest = layer.value;
                    canvas.sortingLayerID = layer.id;
                }
            var panel = new GameObject("Black", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            image = panel.GetComponent<Image>();
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            SetImmediate(0f);
        }

        public void FadeIn() => FadeTo(0f);
        public void FadeOut() => FadeTo(1f);
        private void FadeTo(float alpha)
        {
            from = Alpha;
            target = alpha;
            elapsed = 0f;
            IsFading = fadeDuration > 0f && !Mathf.Approximately(from, target);
            if (!IsFading)
                SetImmediate(target);
        }

        public void SetImmediate(float alpha)
        {
            IsFading = false;
            Alpha = target = Mathf.Clamp01(alpha);
            Apply();
        }

        private void Update()
        {
            if (!IsFading)
                return;
            elapsed += Time.unscaledDeltaTime;
            Alpha = Mathf.Lerp(from, target, fadeDuration > 0f ? elapsed / fadeDuration : 1f);
            if (elapsed >= fadeDuration)
                IsFading = false;
            Apply();
        }

        private void Apply()
        {
            if (image == null)
                return;
            image.color = new Color(0f, 0f, 0f, Alpha);
            image.raycastTarget = Alpha > 0f;
            canvas.enabled = Alpha > 0f;
            if (Alpha > 0f) visibleLayers.Add(this);
            else visibleLayers.Remove(this);
        }

        private void OnDisable() => visibleLayers.Remove(this);
        private void OnEnable()
        {
            if (Alpha > 0f) visibleLayers.Add(this);
        }
        private void OnDestroy() => visibleLayers.Remove(this);
    }
}
