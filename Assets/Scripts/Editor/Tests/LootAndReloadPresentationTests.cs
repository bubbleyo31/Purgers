using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class LootAndReloadPresentationTests
{
    [TestCase("Attack/Attack_FPS")]
    [TestCase("Support/Support_FPS")]
    public void ReloadMultiplierOnlyDrivesReloadState(string path)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/_Project_Assets/Models/Player/Profession/" + path + ".controller");
        Assert.That(controller, Is.Not.Null);
        var parameter = System.Array.Find(controller.parameters, p => p.name == "ReloadPlaybackSpeed");
        Assert.That(parameter, Is.Not.Null, "Reload needs its own speed parameter, not Animator.speed.");
        Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
        Assert.That(parameter.defaultFloat, Is.EqualTo(1f));
        int reloads = 0;
        foreach (var layer in controller.layers)
        foreach (var child in layer.stateMachine.states)
        {
            var state = child.state;
            if (state.name == "Reload")
            {
                reloads++;
                Assert.That(state.speed, Is.EqualTo(1.3f), "Preserve the existing calibrated speed.");
                Assert.That(state.speedParameterActive, Is.True);
                Assert.That(state.speedParameter, Is.EqualTo("ReloadPlaybackSpeed"));
            }
            else Assert.That(state.speedParameter, Is.Not.EqualTo("ReloadPlaybackSpeed"));
        }
        Assert.That(reloads, Is.EqualTo(1));
    }

    [TestCase(1.8f, 1.8f, 1f)]
    [TestCase(1.8f, 1.44f, 1.25f)]
    [TestCase(2f, 1f, 2f)]
    [TestCase(2f, 0f, 1f)]
    public void ReloadSpeedUsesTheCurrentCycleDuration(float baseline, float snapshot, float expected)
    {
        var method = typeof(FirstPersonViewModelActionAnimator).GetMethod("CalculateReloadPlaybackSpeed",
            BindingFlags.Static | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);
        Assert.That((float)method.Invoke(null, new object[] { baseline, snapshot }), Is.EqualTo(expected).Within(.0001f));
    }
    [Test]
    public void UnspawnedLootCannotReadNetworkStateOrShowOnMap()
    {
        var root = new GameObject("Unspawned loot");
        try
        {
            var loot = root.AddComponent<PlayerAbilityRuntime>();
            var method = typeof(LocalMinimapController).GetMethod("TryReadLootPosition", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            object[] args = { loot, null, Vector3.one };
            Assert.That((bool)method.Invoke(null, args), Is.False);
            Assert.That((Vector3)args[2], Is.EqualTo(Vector3.zero));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void PooledLootMarkerIsGoldenSquareAndResetsForOtherContacts()
    {
        var root = new GameObject("Map presentation test");
        var lootRoot = new GameObject("Loot");
        try
        {
            var map = root.AddComponent<LocalMinimapController>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var markerRoot = new GameObject("Markers", typeof(RectTransform)); markerRoot.transform.SetParent(root.transform);
            typeof(LocalMinimapController).GetField("markerRoot", flags).SetValue(map, markerRoot.transform);
            foreach (var field in new[] { "playerDot", "playerHeading" })
            {
                var child = new GameObject(field, typeof(RectTransform), typeof(UnityEngine.UI.Image)); child.transform.SetParent(root.transform);
                typeof(LocalMinimapController).GetField(field, flags).SetValue(map, child.GetComponent<UnityEngine.UI.Image>());
            }
            var loot = lootRoot.AddComponent<PlayerAbilityRuntime>();
            typeof(LocalMinimapController).GetMethod("UseLootMarker", flags).Invoke(map, new object[] { 0, loot });
            var image = markerRoot.GetComponentInChildren<UnityEngine.UI.Image>();
            Assert.That((Color32)image.color, Is.EqualTo(new Color32(255, 195, 45, 255)));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(9, 9)));
            Assert.That(image.rectTransform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(image.raycastTarget, Is.False);
            typeof(LocalMinimapController).GetMethod("UseMarker", flags).Invoke(map, new object[] { 0, loot.transform, null });
            Assert.That((Color32)image.color, Is.EqualTo(new Color32(80, 255, 140, 255)));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(7, 7)));
            Assert.That(markerRoot.transform.childCount, Is.EqualTo(1), "Reuse the existing pool.");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(lootRoot); }
    }
    [TestCase("Attack/Attack_FPS")]
    [TestCase("Support/Support_FPS")]
    public void ReloadClipActuallyAdvancesAtTheMultipliedRate(string path)
    {
        var root = new GameObject("Reload animation evaluation");
        var graph = UnityEngine.Playables.PlayableGraph.Create("Reload speed verification");
        try
        {
            var animator = root.AddComponent<Animator>();
            animator.fireEvents = false; // Isolated pose timing; no production audio receiver on this test object.
            var asset = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/_Project_Assets/Models/Player/Profession/" + path + ".controller");
            var playable = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, asset);
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Animation", animator);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
            graph.Play(); graph.Evaluate(.01f);
            playable.SetBool("Reloading", true); playable.SetFloat("ReloadPlaybackSpeed", 1.25f);
            graph.Evaluate(.3f); graph.Evaluate(.3f);
            var before = playable.GetCurrentAnimatorStateInfo(0);
            Assert.That(before.IsName(asset.layers[0].name + ".Reload"), Is.True);
            graph.Evaluate(.1f);
            var after = playable.GetCurrentAnimatorStateInfo(0);
            AnimationClip clip = null;
            foreach (var state in asset.layers[0].stateMachine.states)
                if (state.state.name == "Reload") clip = state.state.motion as AnimationClip;
            Assert.That(clip, Is.Not.Null);
            Assert.That(after.normalizedTime - before.normalizedTime,
                Is.EqualTo(.1f * 1.3f * 1.25f / clip.length).Within(.002f));
            Assert.That(animator.speed, Is.EqualTo(1f), "Global Animator speed stays unchanged.");
        }
        finally { graph.Destroy(); Object.DestroyImmediate(root); }
    }
}
