using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class MenuBrushAssetsTests
{
    [Test]
    public void SavedFadeControllerHasUsableShowAndHideStates()
    {
        const string folder = "Assets/_Project_Assets/UI/MenuBrushV1/";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "MenuBrushFadeV1.controller");
        Assert.That(controller, Is.Not.Null);
        var machine = controller.layers[0].stateMachine;
        foreach (string name in new[] { "Show", "Hide" })
        {
            var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name);
            Assert.That(state, Is.Not.Null, name + " state must survive asset reload.");
            var clip = state.motion as AnimationClip;
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.GreaterThan(0));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.EqualTo(1));
            Assert.That(bindings[0].path, Is.Empty);
            Assert.That(bindings[0].type, Is.EqualTo(typeof(CanvasGroup)));
            Assert.That(bindings[0].propertyName, Is.EqualTo("m_Alpha"));
        }
        Assert.That(machine.defaultState.name, Is.EqualTo("Show"));
    }
}
