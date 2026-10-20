
using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Purgers.Settings
{
    /// <summary>Production settings UI. Session, input, graphics and audio remain owned by their existing systems.</summary>
    [DefaultExecutionOrder(-31000)] public sealed partial class PlayerSettingsMenu : MonoBehaviour
    {
        public TMP_FontAsset font;
        [HideInInspector] public Sprite brush; // Retained for serialized compatibility; flat backgrounds are used.
        
        public PlayerSettingsSession Model { get; private set; }
        public int Tab { get; private set; }
        const string PreferenceKey = "Purgers.Settings.v1";
        static readonly Color Ink = new Color32(23, 29, 22, 255), Panel = new Color32(42, 49, 33, 255),
            Row = new Color32(52, 60, 42, 255), Cream = new Color32(242, 238, 219, 255),
            Muted = new Color32(173, 181, 152, 255), Gold = new Color32(210, 191, 91, 255),
            Red = new Color32(99, 44, 38, 255);
        RectTransform ui;
        TMP_Text footerStatus, notice, displayCountdown;
        Button saveButton;
        readonly System.Collections.Generic.List<Button> selectionButtons = new System.Collections.Generic.List<Button>();
        bool rebuild;
        int captureFrame;


        readonly KeyCode[] keyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        public void SetTab(int tab) { Tab = Mathf.Clamp(tab, 0, 4); rebuild = true; }
        public void RefreshView() { rebuild = true; }
        void Update()
        {
            if (Model == null) return;
            RefreshSession();
            if (Model.Busy || Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked) return;
            Model.Tick(Time.unscaledDeltaTime);
            if (displayCountdown && Model.Popup != SettingsPopup.Display) rebuild = true;
            if (displayCountdown) displayCountdown.text = "保留此顯示模式？  " + Mathf.CeilToInt(Model.DisplaySeconds) + " 秒";
            if (Input.GetKeyDown(KeyCode.Escape)) return;
            if (Model.Capture >= 0)
            {
                if (Time.frameCount <= captureFrame + 1) return;
                foreach (var key in keyCodes)
                    if (key != KeyCode.None && (int)key < (int)KeyCode.JoystickButton0 && Input.GetKeyDown(key))
                    { Model.Bind(key); rebuild = true; return; }
                return;
            }
            bool editing = EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
            if (editing) return;
            if (GetComponentsInChildren<TMP_Dropdown>().Any(d => d.IsExpanded)) return;
            if (Model.Popup != SettingsPopup.None)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
                { Model.Selected = 1 - Model.Selected; PaintSelection(); }
                if (Input.GetKeyDown(KeyCode.Return)) { if (Model.Selected == 0) Model.Yes(); else Model.No(); rebuild = true; }
                return;
            }
            if (Model.Screen == SettingsScreen.Pause)
            {
                int delta = Input.GetKeyDown(KeyCode.DownArrow) || Input.mouseScrollDelta.y < 0 ? 1 :
                    Input.GetKeyDown(KeyCode.UpArrow) || Input.mouseScrollDelta.y > 0 ? -1 : 0;
                if (delta != 0) { Model.Selected = Mathf.Clamp(Model.Selected + delta, 0, 2); PaintSelection(); }
                if (Input.GetKeyDown(KeyCode.Return)) ChoosePause(Model.Selected);
            }
            else if (Model.Screen == SettingsScreen.Settings && Input.GetKeyDown(KeyCode.Y))
            { Model.RequestSave(); Model.Selected = 1; rebuild = true; }
            RefreshLabels();
        }
        void LateUpdate() { SynchronizeOpenState(); if (rebuild) { rebuild = false; Build(); } }
        void ChoosePause(int index)
        {
            if (index == 0) Model.Screen = SettingsScreen.Game;
            if (index == 1) Model.OpenSettings();
            if (index == 2) { Model.Popup = SettingsPopup.Leave; Model.Selected = 1; }
            rebuild = true;
        }
        public void Build()
        {
            if (Model == null) return;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            footerStatus = notice = displayCountdown = null; saveButton = null;
            selectionButtons.Clear();
            // A nested Canvas must inherit the full screen, not a default 100 x 100 rectangle.
            var ownerRect = GetComponent<RectTransform>();
            if (!ownerRect) ownerRect = gameObject.AddComponent<RectTransform>();
            Stretch(ownerRect);
            var canvasGO = new GameObject("Player Settings Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false); Stretch((RectTransform)canvasGO.transform);
            var canvas = canvasGO.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 32000;
            canvas.sortingLayerID = SortingLayer.layers.OrderBy(l => l.value).Last().id;
            
            var scaler = canvasGO.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (Model.Screen == SettingsScreen.Pause || Model.Screen == SettingsScreen.Settings) Full(canvasGO.transform, "Menu shade 60 percent", new Color(0, 0, 0, .6f));
            ui = Rect(canvasGO.transform, "Centered safe area", 0, 0, 1600, 900);
            ui.anchorMin = ui.anchorMax = ui.pivot = Vector2.one * .5f; ui.anchoredPosition = Vector2.zero;
            BuildStatusHud();
            if (Model.Screen == SettingsScreen.Pause || Model.Screen == SettingsScreen.Settings)
            {
                if (Model.Screen == SettingsScreen.Pause) Pause(); else Settings();
            }

            if (Model.Popup != SettingsPopup.None) Popup();
            if (Model.Capture >= 0) CaptureOverlay();
            if (Model.Busy) foreach (var b in GetComponentsInChildren<Button>()) b.interactable = false;
            Canvas.ForceUpdateCanvases(); RefreshLabels();
        }
        void Pause()
        {
            var panel = Image(ui, "Pause panel", 535, 184, 530, 560, Panel).rectTransform;
            Text(panel, "遊戲選單", 42, 32, 446, 58, 38, Cream);
            Text(panel, "世界仍持續運作", 42, 94, 446, 32, 19, Muted);
            string[] labels = { "返回遊戲", "設定", Model.Host ? "離開並保存遊戲" : "離開遊戲" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i; var b = Button(panel, labels[i], 30, 156 + i * 84, 470, 72, () => ChoosePause(index), Model.Selected == i, 29);
                selectionButtons.Add(b);
                Hover(b, () => { Model.Selected = index; PaintSelection(); });
            }
            Text(panel, "房間號", 42, 438, 100, 30, 18, Muted);
            Text(panel, RoomCode, 42, 474, 260, 40, 27, Cream);
            Button(panel, "複製", 355, 468, 135, 48, () => { GUIUtility.systemCopyBuffer = RoomCode; Model.Notice = "房間號已複製"; rebuild = true; }, false, 20);
            notice = Text(ui, Model.Notice, 400, 760, 800, 38, 20, Gold, TextAlignmentOptions.Center);
            Text(ui, "滾輪 / ↑ ↓  選取     Enter  確認     ESC  返回", 400, 812, 800, 36, 19, Muted, TextAlignmentOptions.Center);
        }
        void Settings()
        {
            var panel = Image(ui, "Settings panel", 140, 76, 1320, 758, Panel).rectTransform;
            Text(panel, "設定", 36, 24, 300, 50, 36, Cream);
            Text(panel, Model.ReturnTo == SettingsScreen.Main ? "主選單 / 個人設定" : "遊戲內 / 個人設定", 880, 35, 400, 30, 19, Muted, TextAlignmentOptions.MidlineRight);
            Image(panel, "Header rule", 32, 88, 1256, 2, Gold);
            string[] tabs = { "操作", "自訂按鍵", "聲音", "畫面與顯示", "連線" };
            for (int i = 0; i < tabs.Length; i++)
            { int index = i; Button(panel, tabs[i], 24, 116 + i * 64, 218, 54, () => SetTab(index), Tab == i, 23); }
            Image(panel, "Column rule", 262, 114, 1, 496, new Color(1, 1, 1, .14f));
            Text(panel, "按鍵提示預覽", 36, 478, 210, 30, 17, Muted);
            Hint(panel, "鈎索", Model.Draft.keys[7], 32, 520, 210);
            Hint(panel, "互動／救援", Model.Draft.keys[8], 32, 580, 210);
            var body = Image(panel, "Content", 290, 112, 994, 526, Color.clear).rectTransform;
            switch (Tab) { case 0: Controls(body); break; case 1: Bindings(body); break; case 2: AudioPage(body); break; case 3: DisplayPage(body); break; case 4: ConnectionSettings(body); break; }
            notice = Text(panel, "", 292, 642, 992, 34, 18, Gold);
            Image(panel, "Footer rule", 32, 684, 1256, 1, new Color(1, 1, 1, .18f));
            footerStatus = Text(panel, "", 36, 704, 475, 34, 19, Muted);
            Button(panel, "重設初始值", 570, 702, 202, 42, () => { Model.Popup = SettingsPopup.Reset; Model.Selected = 1; rebuild = true; }, false, 20);
            Button(panel, "ESC  返回", 788, 702, 196, 42, () => { Model.Back(); Model.Selected = 1; rebuild = true; }, false, 20);
            saveButton = Button(panel, "Y  確認更動", 1000, 702, 286, 42, () => { Model.RequestSave(); Model.Selected = 1; rebuild = true; }, true, 21);
        }
        void Controls(RectTransform body)
        {
            Text(body, "滑鼠與操作模式", 0, 0, 900, 40, 28, Cream);
            Text(body, "倍率以目前手感為基準；按住放開解除，切換再按取消。", 0, 42, 994, 30, 18, Muted);
            Number(body, "總靈敏度", 82, .1f, 5, Model.Draft.total, .01f, "倍", v => Model.Draft.total = v);
            Number(body, "水平倍率", 132, .1f, 2, Model.Draft.horizontal, .01f, "倍", v => Model.Draft.horizontal = v);
            Number(body, "垂直倍率", 182, .1f, 2, Model.Draft.vertical, .01f, "倍", v => Model.Draft.vertical = v);
            Mode(body, "鈎索", 248, Model.Draft.grappleToggle, v => Model.Draft.grappleToggle = v);
            Mode(body, "瞄準", 298, Model.Draft.aimToggle, v => Model.Draft.aimToggle = v);
            Mode(body, "跑步", 348, Model.Draft.sprintToggle, v => Model.Draft.sprintToggle = v);
            Mode(body, "蹲下", 398, Model.Draft.crouchToggle, v => Model.Draft.crouchToggle = v);
            Mode(body, "隊伍資訊", 448, Model.Draft.teamInfoToggle, v => Model.Draft.teamInfoToggle = v);
        }
        void Bindings(RectTransform body)
        {
            Text(body, "自訂按鍵", 0, 0, 900, 40, 28, Cream);
            Text(body, "點選鍵帽後按新鍵。衝突會清空舊項目；Q / E 各自共用。", 0, 42, 994, 30, 18, Muted);
            for (int i = 0; i < Model.Draft.keys.Length; i++)
            {
                int index = i; float x = i < 8 ? 0 : 508, y = 88 + i % 8 * 52;
                bool empty = Model.Draft.keys[i] == KeyCode.None;
                var row = Image(body, "Binding " + i, x, y, 486, 46, empty ? Red : Row).rectTransform;
                Text(row, (empty ? "! " : "") + PlayerSettingsSession.Actions[i], 12, 4, 270, 38, 19, Cream);
                var button = Button(row, empty ? "未配置" : PlayerSettingsSession.KeyLabel(Model.Draft.keys[i]), 292, 4, 184, 38,
                    () => { Model.Capture = index; captureFrame = Time.frameCount; rebuild = true; }, false, 18);
                button.name = "Bind " + index;
            }
        }
        void AudioPage(RectTransform body)
        {
            Text(body, "聲音", 0, 0, 900, 40, 28, Cream);
            Text(body, "100% 保留原始比例；調整時即時預聽。", 0, 42, 994, 30, 18, Muted);
            Number(body, "總音量", 100, 0, 100, Model.Draft.master, 1, "%", v => Model.Draft.master = (int)v);
            Number(body, "音效", 172, 0, 100, Model.Draft.effects, 1, "%", v => Model.Draft.effects = (int)v);
            Number(body, "音樂", 244, 0, 100, Model.Draft.music, 1, "%", v => Model.Draft.music = (int)v);
            Button(body, "試聽音效", 0, 350, 234, 54, () => PlaySample(false), false, 22);
            Button(body, "試聽音樂音量", 254, 350, 270, 54, () => PlaySample(true), false, 22);
            Text(body, "離開並放棄後，回到最後保存的音量。", 0, 440, 994, 40, 20, Muted);
        }
        void DisplayPage(RectTransform body)
        {
            Text(body, "畫面與顯示", 0, 0, 900, 40, 28, Cream);
            Text(body, "顯示模式有 15 秒保留確認；逾時或取消將還原。", 0, 42, 994, 30, 18, Muted);
            Choice(body, "解析度", 88, ResolutionLabels, Model.Draft.resolution, v => Model.Draft.resolution = v);
            Choice(body, "顯示模式", 146, new[] { "視窗", "全螢幕" }, Model.Draft.fullscreen ? 1 : 0, v => Model.Draft.fullscreen = v == 1);
            Choice(body, "畫質", 204, QualitySettings.names, Model.Draft.quality, v => Model.Draft.quality = v);
            Choice(body, "垂直同步", 262, new[] { "關閉", "開啟" }, Model.Draft.vsync ? 1 : 0, v => Model.Draft.vsync = v == 1);
            Number(body, "幀率限制", 320, 0, FrameLimits.Length - 1, FrameIndex(Model.Draft.frameLimit), 1, "", v => Model.Draft.frameLimit = FrameLimits[(int)v],
                v => FrameLabels[Mathf.Clamp((int)v, 0, FrameLabels.Length - 1)]);
            Mode(body, "顯示 FPS", 394, Model.Draft.fps, v => Model.Draft.fps = v, "關閉", "開啟");
            Mode(body, "顯示延遲", 452, Model.Draft.ping, v => Model.Draft.ping = v, "關閉", "開啟");
        }
        void ConnectionSettings(RectTransform body)
        {
            Text(body, "連線", 0, 0, 900, 40, 28, Cream);
            bool locked = Model.ReturnTo != SettingsScreen.Main;
            Text(body, locked ? "已在房間中，區域與人數上限鎖定。" : "偏好只影響下一次連線，不改動當前房間。", 0, 42, 994, 32, 18, Muted);
            var region = Choice(body, "區域", 110, RegionLabels, Model.Draft.region, v => Model.Draft.region = v);
            region.template.GetComponent<ScrollRect>().scrollSensitivity = 80f; // Two 40-unit rows per standard wheel unit.
            region.interactable = !locked;
            var max = Number(body, "人數上限", 184, 1, MaxPlayers, Model.Draft.maxPlayers, 1, "人", v => Model.Draft.maxPlayers = (int)v);
            max.interactable = !locked;
            foreach (var input in body.GetComponentsInChildren<TMP_InputField>()) input.interactable = !locked;
            BuildAppVersion(body, locked);
        }
        void Mode(RectTransform parent, string label, float y, bool enabled, Action<bool> change, string off = "按住", string on = "切換")
        {
            Image(parent, label + " row", 0, y, 994, 50, Row);
            Text(parent, label, 16, y + 5, 400, 40, 21, Cream);
            Button(parent, off, 632, y + 5, 164, 40, () => { change(false); rebuild = true; }, !enabled, 20);
            Button(parent, on, 810, y + 5, 168, 40, () => { change(true); rebuild = true; }, enabled, 20);
        }
        Slider Number(RectTransform parent, string label, float y, float min, float max, float value, float step, string unit, Action<float> change, Func<float, string> formatter = null)
        {
            Image(parent, label + " row", 0, y, 994, 50, Row);
            Text(parent, label, 16, y + 5, 260, 40, 21, Cream);
            var sliderRect = Rect(parent, label + " slider", 292, y + 5, 440, 40);
            var slider = sliderRect.gameObject.AddComponent<PlayerSettingsSlider>(); slider.Step = step; slider.minValue = min; slider.maxValue = max;
            slider.wholeNumbers = step >= 1;
            Image(sliderRect, "Track", 0, 17, 440, 6, Ink);
            var fillArea = Rect(sliderRect, "Fill area", 0, 17, 440, 6);
            var fill = Image(fillArea, "Fill", 0, 0, 440, 6, Gold); slider.fillRect = fill.rectTransform;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = Rect(sliderRect, "Handle area", 8, 10, 424, 20);
            var handle = Image(handleArea, "Handle", 0, 0, 16, 0, Cream); handle.rectTransform.pivot = Vector2.one * .5f;
            handle.rectTransform.anchoredPosition = Vector2.zero;
            slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight; slider.SetValueWithoutNotify(value);
            var inputBg = Image(parent, label + " value", 758, y + 5, 164, 40, Ink);
            var input = inputBg.gameObject.AddComponent<TMP_InputField>();
            var text = Text(inputBg.rectTransform, "", 8, 0, 148, 40, 21, Cream, TextAlignmentOptions.Center);
            input.textViewport = text.rectTransform; input.textComponent = text; input.fontAsset = font;
            Func<float, string> format = formatter ?? (v => v.ToString(step < 1 ? "0.00" : "0", CultureInfo.InvariantCulture));
            input.SetTextWithoutNotify(format(value)); input.characterLimit = 10;
            input.onValueChanged.AddListener(s =>
            {
                float n;
                if (formatter == null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !float.IsNaN(n) && !float.IsInfinity(n))
                { change(Mathf.Clamp(Mathf.Round(n / step) * step, min, max)); RefreshLabels(); }
            });
            Text(parent, unit, 932, y + 5, 52, 40, 18, Muted);
            slider.onValueChanged.AddListener(v => { float n = Mathf.Clamp(Mathf.Round(v / step) * step, min, max); change(n); slider.SetValueWithoutNotify(n); input.SetTextWithoutNotify(format(n)); RefreshLabels(); });
            input.onEndEdit.AddListener(s =>
            {
                float n;
                if (formatter != null)
                {
                    string[] values = FrameLabels; int i = Array.IndexOf(values, s);
                    if (i < 0) { input.SetTextWithoutNotify(format(slider.value)); Model.Notice = "幀率請輸入 " + string.Join(" / ", FrameLabels); return; } n = i;
                }
                else if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || float.IsNaN(n) || float.IsInfinity(n))
                { input.SetTextWithoutNotify(format(slider.value)); Model.Notice = "請輸入有效數值"; return; }
                if (n < min || n > max) Model.Notice = "數值已限制在 " + min + "–" + max;
                n = Mathf.Clamp(Mathf.Round(n / step) * step, min, max); change(n); slider.SetValueWithoutNotify(n); input.SetTextWithoutNotify(format(n)); RefreshLabels();
            });
            var navigation = slider.navigation; navigation.mode = Navigation.Mode.Automatic; slider.navigation = navigation;
            return slider;
        }
        TMP_Dropdown Choice(RectTransform parent, string label, float y, string[] choices, int value, Action<int> change)
        {
            Image(parent, label + " row", 0, y, 994, 50, Row);
            Text(parent, label, 16, y + 5, 360, 40, 21, Cream);
            var bg = Image(parent, label + " dropdown", 540, y + 5, 438, 40, Ink);
            var dropdown = bg.gameObject.AddComponent<PlayerSettingsDropdown>(); dropdown.targetGraphic = bg;
            dropdown.captionText = Text(bg.rectTransform, "", 16, 0, 370, 40, 20, Cream);
            Text(bg.rectTransform, "v", 388, 0, 40, 40, 20, Gold, TextAlignmentOptions.Center);
            var template = Image(bg.rectTransform, "Template", 0, 42, 438, 200, Ink).rectTransform;
            template.pivot = new Vector2(.5f, 1); template.anchorMin = template.anchorMax = new Vector2(.5f, 0); template.anchoredPosition = new Vector2(0, -2);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Image(template, "Viewport", 0, 0, 438, 200, Color.clear).rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
            var content = Rect(viewport, "Content", 0, 0, 438, 40); scroll.content = content;
            var itemBg = Image(content, "Item", 0, 0, 438, 40, Row); var toggle = itemBg.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemBg;
            var mark = Image(itemBg.rectTransform, "Check", 8, 14, 10, 12, Gold); toggle.graphic = mark;
            var itemLabel = Text(itemBg.rectTransform, "", 30, 0, 400, 40, 20, Cream);
            dropdown.template = template; dropdown.itemText = itemLabel; dropdown.AddOptions(choices.ToList()); dropdown.SetValueWithoutNotify(value); dropdown.RefreshShownValue();
            template.gameObject.SetActive(false); dropdown.onValueChanged.AddListener(v => { change(v); RefreshLabels(); });
            return dropdown;
        }
        void Popup()
        {
            Full(ui, "Modal shade", new Color(0, 0, 0, .7f));
            var panel = Image(ui, "Confirmation", 440, 245, 720, 410, Panel).rectTransform;
            bool reset = Model.Popup == SettingsPopup.Reset, leave = Model.Popup == SettingsPopup.Leave, display = Model.Popup == SettingsPopup.Display;
            string title = reset ? "重設所有設定？" : leave ? (Model.Host ? "離開並保存遊戲？" : "離開遊戲？") : display ? "保留此顯示模式？" : "儲存此設定？";
            var heading = Text(panel, title, 36, 30, 648, 56, 32, Cream);
            if (display) displayCountdown = heading;
            string message = reset ? "將全部分頁恢復初始值，仍需確認更動才會保存。" : leave ? (Model.Host ? "房間將關閉，其他玩家將中斷連線。\n保存成功後才離開。" : "只離開自己的連線，不保存房主世界。\n返回主選單。") : display ? "15 秒內選擇保留，逾時還原顯示模式。\n確認前不會保存此顯示模式。" : Model.Popup == SettingsPopup.Unsaved ? "有未保存變更。\n是：保存並返回　否：放棄並返回　ESC：繼續編輯" : "保存後套用設定，並留在設定頁。";
            Text(panel, message, 36, 100, 648, 92, 21, Muted);
            if (!reset && !leave && !display && Model.Unbound.Length > 0)
            {
                Image(panel, "Unbound warning", 28, 200, 664, 88, Red);
                Text(panel, "未配置（仍可保存）：" + string.Join("、", Model.Unbound.Select(i => PlayerSettingsSession.Actions[i])), 42, 210, 636, 72, 18, Cream);
            }
            else Text(panel, Model.Notice, 36, 210, 648, 72, 19, Gold);
            string yes = display ? "保留" : leave ? "確認離開" : "是", no = display ? "還原" : leave || reset ? "取消" : "否";
            var y = Button(panel, yes, 36, 320, 312, 56, () => { Model.Yes(); rebuild = true; }, Model.Selected == 0, 25);
            var n = Button(panel, no, 372, 320, 312, 56, () => { Model.No(); rebuild = true; }, Model.Selected == 1, 25);
            selectionButtons.Clear(); selectionButtons.Add(y); selectionButtons.Add(n);
            Hover(y, () => { Model.Selected = 0; PaintSelection(); });
            Hover(n, () => { Model.Selected = 1; PaintSelection(); });
            if (Model.Notice.StartsWith("保存失敗") || Model.Notice.StartsWith("離開失敗"))
                Text(panel, Model.Notice, 36, 285, 648, 30, 16, Gold);
        }
        void CaptureOverlay()
        {
            Full(ui, "Capture shade", new Color(0, 0, 0, .75f));
            var box = Image(ui, "Capture prompt", 440, 310, 720, 240, Panel).rectTransform;
            Text(box, "請按下新按鍵", 36, 28, 648, 52, 32, Gold);
            Text(box, PlayerSettingsSession.Actions[Model.Capture], 36, 94, 648, 40, 26, Cream);
            Text(box, "支援鍵盤、滑鼠鍵；ESC 取消。Y 在此只會改鍵。", 36, 166, 648, 38, 20, Muted);
        }
        void RefreshLabels()
        {
            if (footerStatus) footerStatus.text = (Model.Dirty ? "● 未保存" : "已保存") + (Model.Unbound.Length > 0 ? "   /   " + Model.Unbound.Length + " 項未配置" : "");
            if (saveButton) saveButton.interactable = Model.Dirty;
            if (notice) notice.text = Model.Notice;
            RefreshStatusHud();
        }
        void Hint(RectTransform parent, string label, KeyCode key, float x, float y, float width)
        {
            var box = Image(parent, "Hint " + label, x, y, width, 48, key == KeyCode.None ? Red : Ink).rectTransform;
            var actionLabel = Text(box, label, 10, 4, width - 88, 40, 17, Muted);
            var cap = Image(box, "Keycap", width - 82, 8, 74, 32, Gold);
            var capLabel = Text(cap.rectTransform, "", 0, 0, 74, 32, 21, Ink, TextAlignmentOptions.Center);
            var layout = new HudKeyPromptLayout(capLabel, cap, null);
            layout.Refresh(key);
            cap.rectTransform.pivot = new Vector2(1, .5f);
            cap.rectTransform.anchoredPosition = new Vector2(width - 10, -24);
            actionLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(40, width - layout.Width - 28));
            actionLabel.enableAutoSizing = true; actionLabel.fontSizeMax = 17; actionLabel.fontSizeMin = 12;
        }
        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        Image Image(Transform parent, string name, float x, float y, float w, float h, Color color)
        { var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>(); image.color = color; return image; }
        void Full(Transform parent, string name, Color color)
        { var image = Image(parent, name, 0, 0, 1600, 900, color); image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one; image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero; }
        TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var text = Rect(parent, "Text " + value, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = align;
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false; return text;
        }
        Button Button(Transform parent, string label, float x, float y, float w, float h, Action action, bool selected = false, int size = 22)
        {
            var image = Image(parent, "Button " + label, x, y, w, h, selected ? Gold : Ink);

            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.28f, 1.28f, 1.15f); colors.pressedColor = new Color(.8f, .8f, .65f); colors.disabledColor = new Color(.5f, .5f, .5f); button.colors = colors;
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            Text(image.rectTransform, (selected ? "›  " : "") + label, 12, 0, w - 24, h, size, selected ? Ink : Cream, TextAlignmentOptions.Center);
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>())
                    EventSystem.current.SetSelectedGameObject(null);
                action();
            }); return button;
        }
        void Hover(Button button, Action action)
        {
            var trigger = button.gameObject.AddComponent<EventTrigger>(); var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => action()); trigger.triggers.Add(entry);
        }
        void PaintSelection()
        {
            for (int i = 0; i < selectionButtons.Count; i++)
            {
                var button = selectionButtons[i]; if (!button) continue;
                bool selected = i == Model.Selected;
                var image = button.GetComponent<Image>(); image.sprite = null; image.color = selected ? Gold : Ink;
                var label = button.GetComponentInChildren<TMP_Text>(); string value = label.text.StartsWith("›  ") ? label.text.Substring(3) : label.text;
                label.text = (selected ? "›  " : "") + value; label.color = selected ? Ink : Cream;
            }
        }
    }
}

