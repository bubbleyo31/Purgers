using System.Linq;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemySuicideModelTests
{
    [TestCase(false, .3333333f, 1f, 0f)]
    [TestCase(false, 0f, 1f, 8f / 13f)]
    [TestCase(true, .6f, 1f, 8f / 13f)]
    [TestCase(true, .5f, 1f, 10.4f / 13f)]
    [TestCase(true, .3f, 1f, 1f)]
    [TestCase(true, 0f, 1f, 1f)]
    [TestCase(false, 0f, 2f, 8f / 13f)]
    [TestCase(true, .5f, 2f, 12.8f / 13f)]
    public void PresentationUsesAuthoritativePhaseAndClampsTail(bool airborne, float remaining, float speed, float expected)
    {
        var method = typeof(EnemySuicideRules).GetMethod("PounceNormalizedTime");
        Assert.That(method, Is.Not.Null, "缺少與權威時序共用的動畫取樣");
        var timing = new AnimationFrameTiming {ClipDuration = 13f / 24f, FramesPerSecond = 24f, PlaybackSpeed = speed};
        var actual = (float)method.Invoke(null, new object[] {timing, 8f, .6f, airborne, remaining});
        Assert.That(actual, Is.EqualTo(expected).Within(.0001f));
    }

    [Test] public void ControllerActuallyEvaluatesCrouchAndHeldTailWithoutRootMotion()
    {
        var preview = new PreviewRenderUtility();
        var graph = UnityEngine.Playables.PlayableGraph.Create("Suicide animation test");
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
            var model = preview.InstantiatePrefabInScene(prefab.GetComponentInChildren<Animator>().gameObject);
            var animator = model.GetComponent<Animator>();
            var controller = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Animator", animator);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, controller);
            graph.Play();
            graph.Evaluate(.01f);
            var body = model.transform.Find("物体/body");
            controller.SetBool("IsUsingCombatAction", true);
            controller.SetInteger("CombatOptionId", (int)EnemyCombatOptionId.SuicidePounce);
            controller.SetFloat("PounceNormalizedTime", 4f / 13f);
            graph.Evaluate(.01f);
            Assert.That(controller.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Pounce"), Is.True);
            Assert.That(body.localPosition.z, Is.LessThan(-.001f), "原始蹲低姿勢確實播放");
            controller.SetFloat("PounceNormalizedTime", 1f);
            graph.Evaluate(.01f);
            var tail = body.localPosition;
            var rotations = model.GetComponentsInChildren<Transform>().Select(t => t.localRotation).ToArray();
            graph.Evaluate(.4f);
            Assert.That(body.localPosition, Is.EqualTo(tail));
            var after = model.GetComponentsInChildren<Transform>();
            for (int i = 0; i < after.Length; i++) Assert.That(Quaternion.Angle(rotations[i], after[i].localRotation), Is.LessThan(.01f));
            Assert.That(animator.applyRootMotion, Is.False);
            controller.SetBool("IsUsingCombatAction", false);
            controller.SetFloat("MoveSpeed", 0f);
            graph.Evaluate(.2f); graph.Evaluate(.2f);
            Assert.That(controller.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), Is.True, "中斷後回待機");
            controller.SetFloat("MoveSpeed", 2); controller.SetFloat("PatrolPlaybackSpeed", 1);
            graph.Evaluate(.2f); graph.Evaluate(.2f);
            Assert.That(controller.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"), Is.True);
            controller.SetBool("IsAlerted", true); controller.SetFloat("ChasePlaybackSpeed", 1);
            graph.Evaluate(.2f); graph.Evaluate(.2f);
            Assert.That(controller.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Run"), Is.True);
        }
        finally {graph.Destroy(); preview.Cleanup();}
    }

    [Test] public void PresentationDoesNotReadNetworkStateBeforeSpawn()
    {
        var go = new GameObject("Unspawned suicide");
        try {Assert.That(go.AddComponent<EnemySuicideAttack>().TryGetPounceAnimationTime(out _), Is.False);}
        finally {Object.DestroyImmediate(go);}
    }
    public const string AnimationPath = "Assets/_Project_Assets/Animations/Enemy/Suicide/Pounce_InPlace.anim";
    [Test] public void PrefabHasOneAnimatorAndOnePresentationOwner()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
        var animators = root.GetComponentsInChildren<Animator>(true);
        Assert.That(animators.Length, Is.EqualTo(1));
        Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length, Is.Zero, "灰盒眼睛不可殘留顯示");
        Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(22));
        Assert.That(animators[0].applyRootMotion, Is.False);
        Assert.That(animators[0].runtimeAnimatorController, Is.Not.Null);
        Assert.That(root.GetComponents<EnemyPresentationAnimatorDriver>().Length, Is.EqualTo(1));
        Assert.That(root.GetComponentsInChildren<EnemyCombatAnimatorDriver>(true), Is.Empty);
    }

    [Test] public void PounceCopyHasNoForwardDriftAndHoldsBodyHeightAfterTakeoff()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationPath);
        Assert.That(clip, Is.Not.Null);
        Assert.That(clip.isLooping, Is.False);
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var forward = AnimationUtility.GetEditorCurve(clip, bindings.Single(b => b.path == "物体/body" && b.propertyName == "m_LocalPosition.y"));
        var vertical = AnimationUtility.GetEditorCurve(clip, bindings.Single(b => b.path == "物体/body" && b.propertyName == "m_LocalPosition.z"));
        for (int frame = 0; frame <= 13; frame++)
        {
            Assert.That(forward.Evaluate(frame / 24f), Is.EqualTo(forward.Evaluate(0)).Within(.000001f));
            if (frame >= 8) Assert.That(vertical.Evaluate(frame / 24f), Is.EqualTo(vertical.Evaluate(8f / 24f)).Within(.000001f));
        }
        Assert.That(vertical.Evaluate(4f / 24f), Is.LessThan(vertical.Evaluate(0)), "保留預備蹲低");
    }

    [Test] public void PrefabUsesFrameEightAndAlignedVolumes()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemySuicideSetup.PrefabPath);
        var attack = new SerializedObject(root.GetComponent<EnemySuicideAttack>());
        Assert.That(attack.FindProperty("useAnimationFrameTiming").boolValue, Is.True);
        Assert.That(attack.FindProperty("takeoffFrame").floatValue, Is.EqualTo(8));
        Assert.That(attack.FindProperty("flightSeconds").floatValue, Is.EqualTo(.6f).Within(.001f));
        var capsule = root.GetComponent<CapsuleCollider>();
        Assert.That(capsule.height, Is.EqualTo(2.1f).Within(.001f));
        Assert.That(capsule.center.y, Is.EqualTo(1.05f).Within(.001f));
        foreach (var motor in new Component[] {root.GetComponent<EnemyGroundPatrolNavigator>(), root.GetComponent<EnemyGroundChaseMotor>()})
        {
            var so = new SerializedObject(motor);
            Assert.That(so.FindProperty("bodyHeight").floatValue, Is.EqualTo(capsule.height));
            Assert.That(so.FindProperty("bodyRadius").floatValue, Is.EqualTo(capsule.radius));
        }
        var hitbox = root.GetComponent<HitboxRoot>().Hitboxes.Single();
        Assert.That(hitbox.transform.lossyScale, Is.EqualTo(Vector3.one));
        Assert.That(hitbox.BoxExtents.y, Is.EqualTo(1.05f).Within(.001f));
        Assert.That(attack.FindProperty("explosionCenterOffset").vector3Value, Is.EqualTo(capsule.center));
        Assert.That(root.GetComponent<EnemyHealthBarTarget>().HeadPosition.y, Is.GreaterThan(2.1f));
    }
}
