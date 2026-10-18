using NUnit.Framework;
using UnityEngine;
using Purgers.SettingsPreview;
using System.Linq;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[Category("PurgersRegression")]
public sealed class SettingsPreviewTests
{
    [Test] public void ConflictClearsPreviousGroupAndSavingAllowsBlank()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Capture = 11; m.Bind(KeyCode.Space);
        Assert.That(m.Draft.keys[4], Is.EqualTo(KeyCode.None));
        Assert.That(m.Draft.keys[11], Is.EqualTo(KeyCode.Space));
        m.RequestSave(); m.Yes(); Assert.That(m.Saved.keys[4], Is.EqualTo(KeyCode.None));
    }
    [Test] public void CaptureYDoesNotSaveAndEscapeConsumesCaptureBeforeVote()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Capture = 8; m.Bind(KeyCode.Y);
        Assert.That(m.Draft.keys[8], Is.EqualTo(KeyCode.Y)); Assert.That(m.Saved.keys[8], Is.EqualTo(KeyCode.E));
        m.Vote = true; m.Capture = 7; m.Escape(); Assert.That(m.Capture, Is.EqualTo(-1)); Assert.IsTrue(m.Vote);
        m.Escape(); Assert.IsFalse(m.Vote); Assert.That(m.Screen, Is.EqualTo(PreviewScreen.Settings));
    }
    [Test] public void ExplicitNoRetainsDraftButUnsavedNoDiscards()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Draft.total = 2; m.RequestSave(); m.No();
        Assert.AreEqual(2, m.Draft.total); m.Back(); m.Escape(); Assert.AreEqual(PreviewScreen.Settings, m.Screen);
        m.Back(); m.No(); Assert.AreEqual(1, m.Draft.total); Assert.AreEqual(PreviewScreen.Pause, m.Screen);
    }
    [Test] public void SaveFailureDoesNotApplyOrExit()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(true); m.Draft.total = 2; m.FailSave = true; m.Back(); m.Yes();
        Assert.AreEqual(1, m.Saved.total); Assert.AreEqual(2, m.Draft.total); Assert.AreEqual(PreviewPopup.Unsaved, m.Popup);
        m.FailSave = false; m.Yes(); Assert.AreEqual(PreviewScreen.Main, m.Screen); Assert.AreEqual(2, m.Saved.total);
    }
    [Test] public void DisplayTimeoutOnlyRevertsDisplayAndDoesNotPersist()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Draft.total = 2; m.Draft.resolution = 0; m.RequestSave(); m.Yes();
        Assert.AreEqual(PreviewPopup.Display, m.Popup); m.Tick(16); Assert.AreEqual(1, m.Draft.resolution);
        Assert.AreEqual(2, m.Draft.total); Assert.AreEqual(1, m.Saved.total);
    }
    [Test] public void DisplayKeepThenPersistenceFailureRetainsDraft()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Draft.fullscreen = false; m.RequestSave(); m.Yes();
        m.Persist = _ => { throw new System.IO.IOException("test"); }; m.Yes();
        Assert.IsTrue(m.Saved.fullscreen); Assert.IsFalse(m.Draft.fullscreen); Assert.AreEqual(PreviewPopup.Save, m.Popup);
    }
    [Test] public void ResetIsDraftAndClientLeaveDoesNotSaveWorld()
    {
        var m = new SettingsPreviewModel(); m.Saved.total = 2; m.OpenSettings(); m.Popup = PreviewPopup.Reset; m.Yes();
        Assert.AreEqual(1, m.Draft.total); Assert.AreEqual(2, m.Saved.total);
        m.Host = false; m.FailLeave = true; m.Popup = PreviewPopup.Leave; m.Yes(); Assert.AreEqual(PreviewScreen.Main, m.Screen);
    }
    [Test] public void ReservedEscapeCannotBecomeBinding()
    {
        var m = new SettingsPreviewModel(); m.OpenSettings(); m.Capture = 8; m.Bind(KeyCode.Escape);
        Assert.AreEqual(KeyCode.E, m.Draft.keys[8]);
    }
    [Test] public void NativeControlsUpdateDraftValidateNumbersAndFollowKeyHints()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var view = SettingsPreviewBuilder.CreateObjects(scene); view.Model.OpenSettings(); view.Build();
            var sliders = view.GetComponentsInChildren<SettingsPreviewSlider>();
            Assert.That(sliders.Length, Is.EqualTo(3)); sliders[0].value = 2.17f;
            Assert.That(view.Model.Draft.total, Is.EqualTo(2.17f).Within(.001f));
            var input = view.GetComponentsInChildren<TMP_InputField>().First();
            input.onEndEdit.Invoke("999"); Assert.That(view.Model.Draft.total, Is.EqualTo(5));
            Assert.That(input.text, Is.EqualTo("5.00"));
            sliders[0].value = 1;
            sliders[0].OnMove(new AxisEventData(null) { moveDir = MoveDirection.Right });
            Assert.That(view.Model.Draft.total, Is.EqualTo(1.01f).Within(.001f));
            view.SetTab(1); view.Build();
            view.GetComponentsInChildren<Button>().Single(b => b.name == "Bind 8").onClick.Invoke();
            Assert.That(view.Model.Capture, Is.EqualTo(8)); view.Model.Bind(KeyCode.J); view.Build();
            var hint = view.GetComponentsInChildren<TMP_Text>().Single(t => t.transform.parent.name == "Hint 互動／救援" && t.text == "J");
            Assert.IsNotNull(hint); Assert.AreEqual(KeyCode.E, view.Model.Saved.keys[8]);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    [Test] public void PopupBlocksLowerLayerAndHoverDoesNotRecreateButtons()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var view = SettingsPreviewBuilder.CreateObjects(scene); view.Model.OpenPause(); view.Build();
            var settings = view.GetComponentsInChildren<Button>().Single(b => b.name == "Button 設定");
            settings.GetComponent<EventTrigger>().triggers.First().callback.Invoke(new PointerEventData(null));
            Assert.That(view.Model.Selected, Is.EqualTo(1)); Assert.IsTrue(settings);
            settings.onClick.Invoke(); Assert.AreEqual(PreviewScreen.Settings, view.Model.Screen); view.Build();
            view.Model.Draft.total = 2; view.Model.RequestSave(); view.Model.Selected = 1; view.Build();
            var canvas = view.GetComponentInChildren<Canvas>();
            var modal = canvas.transform.GetChild(0).Find("Modal shade");
            Assert.IsNotNull(modal); Assert.IsTrue(modal.GetComponent<Image>().raycastTarget);
            Assert.That(modal.GetSiblingIndex(), Is.GreaterThan(canvas.transform.GetChild(0).Find("Settings panel").GetSiblingIndex()));
            var no = view.GetComponentsInChildren<Button>().Single(b => b.name == "Button 否");
            no.onClick.Invoke(); Assert.AreEqual(PreviewPopup.None, view.Model.Popup); Assert.IsTrue(view.Model.Dirty);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
