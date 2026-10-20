using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Renders production HUD keycap layouts in an isolated, unfocused preview scene.</summary>
public static class HudKeyPromptPreview
{
    public static string RenderCatalog(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D output = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraObject = new GameObject("Keycap Catalog Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.orthographic = true;
            camera.orthographicSize = 250; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .11f, .08f);
            camera.cullingMask = 1 << 31;
            var canvasObject = new GameObject("Keycap Catalog", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            canvas.overrideSorting = true; canvas.sortingOrder = 10;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1000, 500);
            string[] captions = { "ENERGY", "ABILITY", "FOCUS", "RELOAD", "RESCUE", "READY", "CANCEL VOTE", "REWARDS",
                "PREVIOUS", "NEXT", "REWARD CHOICE", "MOUSE SIDE", "LONG NAME", "PAGE DOWN", "PRESSED", "UNBOUND" };
            KeyCode[] keys = { KeyCode.Q, KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.E, KeyCode.Tab, KeyCode.Escape, KeyCode.LeftAlt,
                KeyCode.Q, KeyCode.E, KeyCode.Mouse0, KeyCode.Mouse4, KeyCode.RightControl, KeyCode.PageDown, KeyCode.LeftShift, KeyCode.None };
            var style = HudKeyPromptStyle.Shared;
            for (int i = 0; i < keys.Length; i++)
            {
                var card = new GameObject(captions[i], typeof(RectTransform)).GetComponent<RectTransform>();
                card.SetParent(canvas.transform, false);
                card.sizeDelta = new Vector2(240, 100);
                card.anchoredPosition = new Vector2(-375 + i % 4 * 250, 180 - i / 4 * 120);
                var caption = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                caption.transform.SetParent(card, false); caption.font = style.font; caption.fontSharedMaterial = style.fontMaterial;
                caption.fontSize = 16; caption.color = style.readyColor; caption.text = captions[i]; caption.alignment = TextAlignmentOptions.Midline;
                caption.rectTransform.sizeDelta = new Vector2(240, 30); caption.rectTransform.anchoredPosition = new Vector2(0, 25);
                var image = new GameObject("Key", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(card, false); image.rectTransform.anchoredPosition = new Vector2(0, -16);
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                label.transform.SetParent(image.transform, false);
                var layout = new HudKeyPromptLayout(label, image, null);
                layout.Refresh(keys[i]); layout.SetState(i == 14, i == 15);
            }
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            target = new RenderTexture(2000, 1000, 24) { antiAliasing = 4 }; target.Create();
            camera.targetTexture = target; Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            camera.Render(); RenderTexture.active = target;
            output = new Texture2D(2000, 1000, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, 2000, 1000), 0, 0); output.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, output.EncodeToPNG());
            return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (output != null) Object.DestroyImmediate(output);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static string Render(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D output = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraObject = new GameObject("Keycap Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 150f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .11f, .08f);
            camera.cullingMask = 1 << 31;

            var canvasObject = new GameObject("Keycap Preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.layer = 31;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.sizeDelta = new Vector2(1000f, 300f);
            

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
            var hud = prefab.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
            var serialized = new SerializedObject(hud);
            var template = (GameObject)serialized.FindProperty("slotTemplate").objectReferenceValue;
            var energyFill = (Image)serialized.FindProperty("grappleFill").objectReferenceValue;
            var energyBackground = (Image)serialized.FindProperty("grappleKeyBackground").objectReferenceValue;
            var energyLabel = (TMP_Text)serialized.FindProperty("grappleKeyLabel").objectReferenceValue;
            string[] captions = { "ENERGY  ·  Q", "ABILITY  ·  Q", "FOCUS  ·  E", "MOUSE  ·  SIDE 2" };
            KeyCode[] keys = { KeyCode.Q, KeyCode.Q, KeyCode.E, KeyCode.Mouse4 };

            for (int i = 0; i < keys.Length; i++)
            {
                var card = new GameObject("Prompt " + i, typeof(RectTransform));
                card.transform.SetParent(canvas.transform, false);
                var cardRect = (RectTransform)card.transform;
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
                cardRect.pivot = new Vector2(.5f, .5f);
                cardRect.sizeDelta = new Vector2(240f, 260f);
                cardRect.anchoredPosition = new Vector2(-390f + i * 260f, 0f);

                var captionObject = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
                captionObject.transform.SetParent(card.transform, false);
                var caption = captionObject.GetComponent<TMP_Text>();
                caption.font = energyLabel.font;
                caption.fontSharedMaterial = energyLabel.fontSharedMaterial;
                caption.fontSize = 18f;
                caption.color = Color.white;
                caption.alignment = TextAlignmentOptions.Center;
                caption.text = captions[i];
                caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(.5f, 1f);
                caption.rectTransform.pivot = new Vector2(.5f, 1f);
                caption.rectTransform.sizeDelta = new Vector2(240f, 40f);
                caption.rectTransform.anchoredPosition = new Vector2(0f, -10f);

                Image above = null;
                Image keyBackground = null;
                TMP_Text keyLabel = null;
                if (i == 0)
                {
                    above = Object.Instantiate(energyFill, card.transform, false);
                    above.name = "Energy";
                    above.type = Image.Type.Simple;
                    above.fillAmount = 1f;
                    above.rectTransform.anchorMin = above.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    above.rectTransform.pivot = new Vector2(.5f, .5f);
                    above.rectTransform.sizeDelta = new Vector2(100f, 100f);
                    above.rectTransform.anchoredPosition = new Vector2(0f, 35f);
                    keyBackground = Object.Instantiate(energyBackground, card.transform, false);
                    keyLabel = keyBackground.GetComponentInChildren<TMP_Text>(true);
                }
                else
                {
                    var ability = Object.Instantiate(template, card.transform, false);
                    ability.name = "Ability";
                    ability.SetActive(true);
                    var abilityRect = (RectTransform)ability.transform;
                    abilityRect.anchorMin = abilityRect.anchorMax = new Vector2(.5f, .5f);
                    abilityRect.pivot = new Vector2(.5f, .5f);
                    abilityRect.sizeDelta = new Vector2(100f, 100f);
                    abilityRect.anchoredPosition = new Vector2(0f, 35f);
                    foreach (var image in ability.GetComponentsInChildren<Image>(true))
                    {
                        if (image.name == "Key") continue;
                        if (image.transform == ability.transform) image.color = new Color32(241, 237, 217, 255);
                        else image.enabled = false;
                    }
                    var icon = ability.transform.Find("Icon");
                    if (icon != null) icon.gameObject.SetActive(false);
                    var fallback = ability.transform.Find("Fallback");
                    if (fallback != null) fallback.gameObject.SetActive(false);
                    keyBackground = ability.transform.Find("Key").GetComponent<Image>();
                    keyLabel = keyBackground.GetComponentInChildren<TMP_Text>(true);
                    above = ability.GetComponent<Image>();
                }

                new HudKeyPromptLayout(keyLabel, keyBackground, above.rectTransform).Refresh(keys[i]);
                foreach (var transform in canvas.GetComponentsInChildren<Transform>(true))
                    transform.gameObject.layer = 31;
            }

            target = new RenderTexture(2000, 600, 24) { antiAliasing = 4 };
            target.Create();
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            output = new Texture2D(2000, 600, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0f, 0f, 2000f, 600f), 0, 0);
            output.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, output.EncodeToPNG());
            return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (output != null) Object.DestroyImmediate(output);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}


