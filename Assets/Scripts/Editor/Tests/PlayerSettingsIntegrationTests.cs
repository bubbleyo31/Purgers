using NUnit.Framework;
using Purgers.Settings;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class PlayerSettingsIntegrationTests
{
    [Test] public void ExpandedRegionDropdownIsAboveMenuAndWritesDraft()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var parent = new GameObject("Parent canvas", typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent, scene);
            var root = new GameObject("Dropdown test", typeof(PlayerSettingsMenu)); root.transform.SetParent(parent.transform, false);
            var menu = root.GetComponent<PlayerSettingsMenu>();
            menu.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/PlayerSettings/PlayerSettingsFont.asset");
            var model = new PlayerSettingsSession(); model.OpenSettings(true);
            typeof(PlayerSettingsMenu).GetProperty("Model").SetValue(menu, model); menu.Build();
            var canvas = menu.GetComponentInChildren<Canvas>();
            var choice = typeof(PlayerSettingsMenu).GetMethod("Choice", BindingFlags.Instance | BindingFlags.NonPublic);
            var dropdown = (TMP_Dropdown)choice.Invoke(menu, new object[] { (RectTransform)canvas.transform, "區域", 100f, new[] { "Best", "jp", "asia" }, 0, new Action<int>(v => model.Draft.region = v) });
            typeof(TMP_Dropdown).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dropdown, null);
            dropdown.alphaFadeSpeed = 0; dropdown.Show(); Canvas.ForceUpdateCanvases();
            var list = dropdown.transform.Find("Dropdown List"); Assert.IsNotNull(list, "Native list must expand");
            var popupCanvas = list.GetComponent<Canvas>();
            Assert.AreEqual(canvas.sortingLayerID, popupCanvas.sortingLayerID);
            Assert.Greater(popupCanvas.sortingOrder, canvas.sortingOrder, "Expanded choices must render and receive input above the settings panel");
            var blocker = parent.GetComponentsInChildren<Canvas>().Single(c => c.name == "Blocker");
            Assert.Greater(blocker.sortingOrder, canvas.sortingOrder); Assert.Less(blocker.sortingOrder, popupCanvas.sortingOrder);
            Assert.Less(popupCanvas.sortingOrder, 32766, "Loading and scene fade remain above settings");
            var target = list.GetComponentsInChildren<Toggle>().Single(t => t.GetComponentsInChildren<TMP_Text>().Any(l => l.text == "jp"));
            Assert.IsNotNull(target);
            // Native click closes with delayed Destroy, which requires Play Mode; value dispatch is synchronous.
            dropdown.value = 1;
            Assert.AreEqual(1, model.Draft.region); Assert.AreEqual(0, model.Saved.region);
        }
        finally
        {
            // TMP uses delayed Destroy in OnDisable. The preview scene owns and destroys these objects instead.
            foreach (var dropdown in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Dropdown>(true)))
            {
                typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dropdown, null);
                typeof(TMP_Dropdown).GetField("m_Blocker", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dropdown, null);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    [Test] public void CaptureThenVoteConsumesEscapeWithoutOpeningMenu()
    {
        var m = new PlayerSettingsSession(); bool vote = true;
        m.CancelVote = () => { if (!vote) return false; vote = false; return true; };
        m.Capture = 8; m.Escape(); Assert.IsTrue(vote); Assert.AreEqual(-1, m.Capture);
        m.Escape(); Assert.IsFalse(vote); Assert.AreEqual(SettingsScreen.Game, m.Screen);
        m.Escape(); Assert.AreEqual(SettingsScreen.Pause, m.Screen);
    }
    [Test] public void UnsavedDiscardAndExplicitNoAreDifferentAndSaveFailureKeepsDraft()
    {
        var m = new PlayerSettingsSession(); m.OpenSettings(true); m.Draft.total = 2;
        m.RequestSave(); m.No(); Assert.AreEqual(2, m.Draft.total);
        m.Back(); m.Escape(); Assert.AreEqual(SettingsScreen.Settings, m.Screen);
        m.Persist = _ => throw new System.IO.IOException("test failure"); m.Back(); m.Yes();
        Assert.AreEqual(1, m.Saved.total); Assert.AreEqual(2, m.Draft.total); Assert.AreEqual(SettingsPopup.Unsaved, m.Popup);
        m.No(); Assert.AreEqual(1, m.Draft.total); Assert.AreEqual(SettingsScreen.Main, m.Screen);
    }
    [Test] public void DisplayRevertsWithoutWritingAnyPreferencesAndResetRemainsDraft()
    {
        var m = new PlayerSettingsSession(); int writes = 0, restores = 0;
        m.Persist = _ => writes++; m.RollbackDisplay = () => restores++;
        m.OpenSettings(); m.Draft.resolution++; m.Draft.total = 2; m.RequestSave(); m.Yes();
        Assert.AreEqual(SettingsPopup.Display, m.Popup); m.Tick(16);
        Assert.AreEqual(1, restores); Assert.AreEqual(0, writes); Assert.AreEqual(2, m.Draft.total);
        Assert.AreEqual(m.Saved.resolution, m.Draft.resolution);
        m.Popup = SettingsPopup.Reset; m.Yes(); Assert.AreEqual(1, m.Draft.total); Assert.AreEqual(0, writes);
    }
    [Test] public void HeldCombatMustReleaseBeforeRearmingAndAimCancelsButCrouchPersists()
    {
        var intent = new PlayerSettingsIntent(); uint fire = PlayerSettingsIntent.Bit(PlayerBinding.Fire);
        Assert.IsTrue(intent.Aim(true, true, true)); Assert.IsTrue(intent.Crouch(true, true, true));
        intent.BlockHeld(fire | PlayerSettingsIntent.Bit(PlayerBinding.Crouch)); Assert.AreEqual(0, intent.Rearm(fire));
        Assert.AreEqual(PlayerSettingsIntent.Bit(PlayerBinding.Crouch), intent.Rearm(fire | PlayerSettingsIntent.Bit(PlayerBinding.Crouch)), "Held crouch resumes after menu");
        Assert.IsFalse(intent.Aim(false, false, true)); Assert.IsTrue(intent.Crouch(false, false, true));
        intent.Rearm(0); Assert.AreEqual(fire, intent.Rearm(fire));
        Assert.IsFalse(intent.Crouch(true, true, true));
    }
    [Test] public void ProductionSceneHasSingleSettingsEntryAndSeparateAudioParameters()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity", OpenSceneMode.Additive);
        try
        {
            var menus = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerSettingsMenu>(true)).ToArray();
            Assert.AreEqual(1, menus.Length); var menu = menus[0];
            var button = menu.transform.root.GetComponentsInChildren<Button>(true).Single(b => b.name == "SettingsButton");
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount()); Assert.AreEqual(menu, button.onClick.GetPersistentTarget(0));
            Assert.AreEqual("OpenFromMain", button.onClick.GetPersistentMethodName(0)); Assert.IsNotNull(menu.font); Assert.IsNotNull(menu.brush);
            var main = (Fusion.Menu.FusionMenuUIMain)new SerializedObject(menu).FindProperty("mainScreen").objectReferenceValue;
            var wrapper = menu.transform.root.GetComponentInChildren<MultiClimb.Menu.MenuConnectionBehaviour>(true);
            var originalMain = main.Connection; var originalWrapped = wrapper.Connection;
            try
            {
                var connection = new MultiClimb.Menu.MenuConnection(null, null, new Purgers.Progression.JsonGameSaveRepository());
                wrapper.Connection = connection; main.Connection = wrapper;
                Assert.AreSame(connection, typeof(PlayerSettingsMenu).GetProperty("Connection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu), "Resolve production Fusion behaviour wrapper");
            }
            finally { main.Connection = originalMain; wrapper.Connection = originalWrapped; }
            var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/MainAudioMixer.mixer");
            foreach (var name in new[] {"PlayerMasterVolume", "PlayerEffectsVolume", "PlayerMusicVolume"}) Assert.IsTrue(mixer.GetFloat(name, out _), name);
            var gameplay = menu.transform.root.GetComponentInChildren<Fusion.Menu.FusionMenuUIGameplay>(true);
            Assert.IsTrue(new SerializedObject(gameplay).FindProperty("_useUnifiedGameplayMenu").boolValue);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
    [Test] public void ProductionControlsUseAlignedNativeWidgetsAndDynamicHints()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Settings test", typeof(PlayerSettingsMenu)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var parent = new GameObject("Production parent canvas", typeof(Canvas)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent, scene); root.transform.SetParent(parent.transform);
            var menu = root.GetComponent<PlayerSettingsMenu>(); menu.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/PlayerSettings/PlayerSettingsFont.asset");
            var model = new PlayerSettingsSession(); typeof(PlayerSettingsMenu).GetProperty("Model").SetValue(menu, model);
            model.OpenSettings(); menu.Build(); var sliders = menu.GetComponentsInChildren<PlayerSettingsSlider>(); Assert.AreEqual(3, sliders.Length);
            sliders[0].value = 2.17f; Assert.AreEqual(2.17f, model.Draft.total, .001f);
            menu.GetComponentsInChildren<TMP_InputField>().First().onEndEdit.Invoke("999"); Assert.AreEqual(5, model.Draft.total);
            menu.SetTab(1); menu.Build(); menu.GetComponentsInChildren<Button>().Single(b => b.name == "Bind 8").onClick.Invoke(); model.Bind(KeyCode.J); menu.Build();
            var hint = menu.GetComponentsInChildren<Image>().Single(i => i.name == "Hint 互動／救援");
            Assert.IsTrue(hint.GetComponentsInChildren<TMP_Text>().Any(t => t.transform.parent.name == "Keycap" && t.text == "J"));
            Assert.AreSame(HudKeyPromptStyle.Shared.background, hint.transform.Find("Keycap").GetComponent<Image>().sprite);
            var canvas = menu.GetComponentInChildren<Canvas>(); Assert.IsTrue(canvas.overrideSorting); Assert.AreEqual(32000, canvas.sortingOrder);
            Assert.AreEqual("Menu shade 60 percent", canvas.transform.GetChild(0).name);
            model.RequestSave(); model.Selected = 1; menu.Build();
            Assert.IsNotNull(menu.GetComponentsInChildren<Image>().Single(i => i.name == "Modal shade"));
            menu.GetComponentsInChildren<Button>().Single(b => b.name == "Button 否").onClick.Invoke(); Assert.IsTrue(model.Dirty);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
