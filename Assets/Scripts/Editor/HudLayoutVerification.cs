using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Offscreen verification of the same native authoring sessions, without opening/focusing windows.</summary>
public static class HudLayoutVerification
{
    public static string Capture()
    {
        const string folder = "Documentation/ProjectArchitecture/Validation/HudLayoutAuthoring";
        Directory.CreateDirectory(folder);
        foreach (HudLayoutSession.Kind mode in Enum.GetValues(typeof(HudLayoutSession.Kind)))
        {
            if (mode == HudLayoutSession.Kind.ExistingRect) continue;
            using (var session = new HudLayoutSession(mode, false))
                CaptureSession(session, folder + "/" + mode + ".png");
        }
        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/StageHUD.prefab");
        try
        {
            var hud = root.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
            using (var session = new HudLayoutSession(HudLayoutSession.Kind.ExistingRect, false, (RectTransform)hud.transform))
                CaptureSession(session, folder + "/AbilityHUD.png");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return Path.GetFullPath(folder);
    }
    static void CaptureSession(HudLayoutSession session, string path)
    {
        var previous = RenderTexture.active;
        var texture = new RenderTexture(1920, 1080, 24);
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            texture.Create(); session.Render(texture); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(image); }
    }
}
