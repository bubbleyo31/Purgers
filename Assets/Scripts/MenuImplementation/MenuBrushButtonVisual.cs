using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MultiClimb.Menu
{
    /// <summary>Local decoration for an existing Button. Never invokes or replaces its click events.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    public sealed class MenuBrushButtonVisual : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        [Header("既有按鈕與獨立視覺")]
        [Tooltip("沿用原本的 Button；本元件只讀取可互動狀態，不接管 On Click。")]
        [SerializeField] private Button button;
        [Tooltip("只縮放這個視覺子物件；按鈕本身的點擊範圍不縮放。")]
        [SerializeField] private RectTransform visualRoot;
        [Tooltip("黃色筆觸 Image，需設為 Filled / Horizontal / Left，Raycast Target 關閉。")]
        [SerializeField] private Image highlight;
        [Tooltip("原按鈕文字，保留原本內容與功能。")]
        [SerializeField] private TMP_Text label;
        [Tooltip("可選的右側箭頭；與文字同步變色。")]
        [SerializeField] private Graphic arrow;
        [Tooltip("視覺子物件的 CanvasGroup；用於不可互動時降低亮度。")]
        [SerializeField] private CanvasGroup visualGroup;

        [Header("文字位置與配色")]
        [Tooltip("文字未選取時的位置，使用父 RectTransform 的座標。")]
        [SerializeField] private Vector2 labelRestPosition;
        [Tooltip("未選取文字顏色；主要按鈕使用深色，其餘使用米白。")]
        [SerializeField] private Color normalText = new Color(.945f, .929f, .851f, 1);
        [Tooltip("黄色筆觸展開後的文字顏色，需維持清楚對比。")]
        [SerializeField] private Color highlightedText = new Color(.075f, .098f, .055f, 1);
        [Header("互動回饋（不受 Time Scale 影響）")]
        [Tooltip("黄色橫條展開／收回秒數。範圍 0.01～1；越小反應越快。")]
        [Range(.01f, 1f), SerializeField] private float revealDuration = .18f;
        [Tooltip("選取時文字向右位移，參考畫面像素。範圍 0～24。")]
        [Range(0, 24), SerializeField] private float hoverOffset = 8;
        [Tooltip("按下時僅視覺縮放。範圍 0.9～1；1 表示不縮放。")]
        [Range(.9f, 1f), SerializeField] private float pressedScale = .975f;
        [Tooltip("按壓與放開的縮放過渡秒數。範圍 0.01～0.3。")]
        [Range(.01f, .3f), SerializeField] private float pressDuration = .07f;
        [Tooltip("按鈕或上層 CanvasGroup 不可互動時的透明度。範圍 0～1。")]
        [Range(0, 1), SerializeField] private float disabledAlpha = .35f;

        private bool hovered, selected, pressed;
        private float reveal, scale = 1, submitRemaining;
        private Vector3 restScale = Vector3.one;

        private void Awake() { if (!button) button = GetComponent<Button>(); if (visualRoot) restScale = visualRoot.localScale; }
        private void OnEnable() { if (!button) button = GetComponent<Button>(); ResetVisual(); }
        private void OnDisable() => ResetVisual();
        private void OnApplicationFocus(bool focused) { if (!focused) ResetVisual(); }
        private void Update() => AdvanceVisual(Time.unscaledDeltaTime);

        private bool CanInteract => button && button.isActiveAndEnabled && button.IsInteractable();

        private void AdvanceVisual(float delta)
        {
            bool allowed = CanInteract;
            if (!allowed) { hovered = selected = pressed = false; submitRemaining = 0; }
            float target = allowed && (hovered || selected) ? 1 : 0;
            reveal = Mathf.MoveTowards(reveal, target, Mathf.Max(0, delta) / Mathf.Max(.01f, revealDuration));
            bool down = allowed && (pressed || submitRemaining > 0);
            scale = Mathf.MoveTowards(scale, down ? pressedScale : 1,
                Mathf.Max(0, delta) * (1 - pressedScale) / Mathf.Max(.01f, pressDuration));
            submitRemaining = Mathf.Max(0, submitRemaining - Mathf.Max(0, delta));
            Apply(allowed);
        }

        private void ResetVisual()
        {
            hovered = selected = pressed = false;
            reveal = submitRemaining = 0; scale = 1;
            Apply(CanInteract);
        }

        private void Apply(bool allowed)
        {
            if (highlight) highlight.fillAmount = reveal;
            if (visualRoot) visualRoot.localScale = restScale * scale;
            if (visualGroup) visualGroup.alpha = allowed ? 1 : disabledAlpha;
            Color textColor = Color.Lerp(normalText, highlightedText, reveal);
            if (label)
            {
                label.rectTransform.anchoredPosition = labelRestPosition + Vector2.right * (hoverOffset * reveal);
                label.color = textColor;
            }
            if (arrow) arrow.color = textColor;
        }

        public void OnPointerEnter(PointerEventData eventData) { if (CanInteract) hovered = true; }
        public void OnPointerExit(PointerEventData eventData) { hovered = pressed = false; }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left && CanInteract) pressed = true;
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) pressed = false;
        }
        public void OnSelect(BaseEventData eventData) { selected = CanInteract && !(eventData is PointerEventData); }
        public void OnDeselect(BaseEventData eventData) { selected = pressed = false; }
        public void OnSubmit(BaseEventData eventData) { if (CanInteract) submitRemaining = pressDuration; }
    }
}
