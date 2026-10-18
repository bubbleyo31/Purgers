using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>以正式槽位模板與正式呈現函式離屏渲染；不開視窗、不取得焦點、不儲存場景。</summary>
public static class AbilitySlotStatePreview
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static string Render(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D output = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraRoot = new GameObject("Skill preview camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 150; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .095f, .065f);
            camera.cullingMask = 1 << 31;
            var root = new GameObject("Skill preview", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)root.transform).sizeDelta = new Vector2(1000, 300);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
            var saved = prefab.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
            var serialized = new SerializedObject(saved);
            var template = (GameObject)serialized.FindProperty("slotTemplate").objectReferenceValue;
            var hud = root.AddComponent<LocalPlayerBattleAbilityHUD>(); hud.enabled = false;
            typeof(LocalPlayerBattleAbilityHUD).GetField("slotTemplate", Private).SetValue(hud, template);
            typeof(LocalPlayerBattleAbilityHUD).GetField("slotsRoot", Private).SetValue(hud, root.transform);
            var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Blink.asset");
            var states = new[] {
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Cooling, CooldownRemaining = 9, CooldownDuration = 12 },
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Cooling, CooldownRemaining = 3, CooldownDuration = 12 },
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Ready, CanPress = true },
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Recast, CanPress = true, PhaseRemaining = 3, PhaseDuration = 5 },
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Active, CanPress = true, CanEnd = true, PhaseRemaining = 3, PhaseDuration = 5 },
                new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Unavailable }
            };
            for (int i = 0; i < states.Length; i++)
            {
                object view = typeof(LocalPlayerBattleAbilityHUD).GetMethod("GetView", Private).Invoke(hud, new object[] { i });
                var slot = (GameObject)view.GetType().GetField("Root").GetValue(view);
                slot.SetActive(true);
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.anchoredPosition = new Vector2(-400 + i * 160, 25); rect.sizeDelta = new Vector2(80, 80);
                slot.transform.Find("Icon").GetComponent<Image>().sprite = definition.HudIcon;
                slot.transform.Find("Fallback").gameObject.SetActive(false);
                slot.transform.Find("Key/Label").GetComponent<TMP_Text>().text = "E";
                object identity = new object();
                var present = typeof(LocalPlayerBattleAbilityHUD).GetMethod("PresentSlotState", Private);
                if (i == 2) present.Invoke(hud, new object[] { view, states[0], identity });
                present.Invoke(hud, new object[] { view, states[i], identity });
                var caption = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
                caption.transform.SetParent(root.transform, false);
                var text = caption.GetComponent<TextMeshProUGUI>(); text.font = slot.transform.Find("CooldownText").GetComponent<TMP_Text>().font;
                text.text = new[] { "冷卻 75%", "冷卻 25%", "完成亮光", "二次施放", "可提前結束", "條件不足" }[i];
                text.fontSize = 14; text.alignment = TextAlignmentOptions.Center; text.color = Color.white;
                text.rectTransform.sizeDelta = new Vector2(160, 30); text.rectTransform.anchoredPosition = new Vector2(-400 + i * 160, -110);
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            target = new RenderTexture(2000, 600, 24); target.antiAliasing = 4; target.Create(); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            output = new Texture2D(2000, 600, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, 2000, 600), 0, 0); output.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, output.EncodeToPNG());
            return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
