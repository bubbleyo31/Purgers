using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

[Category("PurgersRegression")]
public sealed class ReloadCameraRuntimeTests
{
    private NetworkRunner runner;
    private GameObject view;
    private NetworkObject runtimeOwner;
    private PlayerProfessionType runtimeProfession;
    private void BeforeRuntimeSpawn(NetworkRunner source, NetworkObject spawned) =>
        spawned.GetComponent<PlayerProfessionRuntime>().InitializeBeforeSpawn(runtimeOwner, runtimeProfession);
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Field(object o, string n, object v) => o.GetType().GetField(n, Private).SetValue(o, v);
    private static void Call(object o, string n) => o.GetType().GetMethod(n, Private).Invoke(o, null);

    [UnityTest]
    public IEnumerator SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt()
    {
        yield return new EnterPlayMode();
        Assert.That(Application.isPlaying, Is.True, "Runtime fixture must enter Play Mode");
        runner = new GameObject("Reload Camera Isolated Runner").AddComponent<NetworkRunner>();

        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var e in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(e.Item2);
        var sceneManager = runner.gameObject.AddComponent<GrappleVisualVerificationSceneManager>();
        sceneManager.IsSceneTakeOverEnabled = false;
        var start = runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config, SceneManager = sceneManager });
        float deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(start.IsCompleted && start.GetAwaiter().GetResult().Ok, Is.True);
        int tick = runner.Tick.Raw;
        while ((runner.SceneManager.IsBusy || runner.Tick.Raw == tick) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(runner.Tick.Raw, Is.Not.EqualTo(tick));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        Assert.That(prefab, Is.Not.Null, "Player prefab"); Assert.That(runner, Is.Not.Null, "Runner survived startup"); Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null, "Player NetworkObject");
        var playerId = runner.LocalPlayer;
        Assert.That(runner.IsRunning, Is.True, "Runner running before spawn");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000, 100, 1000), Quaternion.identity, playerId);
        Assert.That(obj != null, Is.True, "Spawn returned null");
        runner.SetPlayerObject(runner.LocalPlayer, obj);
        var input = runner.gameObject.AddComponent<InputManager>();
        var owner = obj.GetComponent<Player>();
        Assert.That(owner != null, Is.True, "Player component missing");
        owner.enabled = false; // 不提交自然玩法輸入；以下只操作本輪暫時實例。
        var sink = FirstPersonHitCameraShake.Singleton;
        Assert.That(sink != null && sink.EffectsRoot != null, Is.True, "需現有 _Menu 鏡頭效果引用。");
        var baseCamera = sink.EffectsRoot.localRotation;
        var look = owner.Movement.KCC.Data.LookRotation;
        var sourceNames = new[] { "Attack", "Support" };
        var viewGuids = new[] { "3d421ceae2128d74ea561e5e37f164d4", "1d2a979e7a5c2144c81927185aecc5bd" };
        var clipGuids = new[] { "2c7c8e328072ad944a1928157bac364a", "ffc8164cfdd358b4aa4793e1a78b7ef9" };
        for (int i = 0; i < 2; i++)
        {
            var profession = i == 0 ? PlayerProfessionType.Attack : PlayerProfessionType.Support;
            var runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProfessionRuntime/" + sourceNames[i] + "ProfessionRuntime.prefab");
            // 已知的獨立 E 技能遷移診斷；本測試不建立／修改 AttackFocusAbility。
            if (i == 0) LogAssert.Expect(UnityEngine.LogType.Error, "[AttackProfessionRuntimeDriver] 找不到 AttackFocusAbility。");
            runtimeOwner = obj; runtimeProfession = profession;
            var runtime = runner.Spawn(runtimePrefab.GetComponent<NetworkObject>(), owner.transform.position, Quaternion.identity,
                runner.LocalPlayer, BeforeRuntimeSpawn);
            var rifle = runtime.GetComponent<AttackRifle>();
            var smg = runtime.GetComponent<SupportSMG>();
            object weapon = i == 0 ? (object)rifle : smg;
            if (rifle != null) rifle.BindOwnerPlayer(owner); else smg.BindOwnerPlayer(owner);
            view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(viewGuids[i])));
            view.SetActive(false);
            var driver = view.GetComponent<FirstPersonViewModelActionAnimator>();
            driver.BindGameplaySources(profession, rifle, smg, null);
            var model = driver.AnimationTransform;
            var bone = model.Find("第一人稱手/骨骼.r/骨骼.001.r/骨骼.002.r/骨骼.003.r/Root");
            var effect = view.AddComponent<FirstPersonReloadCameraMotion>();
            Field(effect, "actionAnimator", driver); Field(effect, "animatedModelRoot", model); Field(effect, "weaponRoot", bone);
            Field(effect, "blendSeconds", 0f); Field(effect, "turnResponseSeconds", 0f);
            effect.enabled = false; // 控制取樣順序，不讓自然 Update 與測試取樣競爭。
            view.SetActive(true);
            driver.BindGameplaySources(profession, rifle, smg, null);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(clipGuids[i]));
            clip.SampleAnimation(model.gameObject, 0);
            Call(effect, "Update"); Call(effect, "LateUpdate");
            weapon.GetType().GetProperty("MagazineAmmo").SetValue(weapon, 1);
            Assert.That((bool)weapon.GetType().GetMethod("TryStartReload").Invoke(weapon, null), Is.True);
            int ammo = (int)weapon.GetType().GetProperty("MagazineAmmo").GetValue(weapon);
            Assert.That(driver.TryGetLocalReloadCameraState(out var readOwner, out var reloading) && readOwner == owner && reloading, Is.True);
            Call(effect, "Update");
            clip.SampleAnimation(model.gameObject, clip.length * .3f);
            Field(input, "frameLookNumber", Time.frameCount); Field(input, "frameLookDelta", Vector2.zero);
            Call(effect, "LateUpdate");
            float normal = Quaternion.Angle(baseCamera, sink.EffectsRoot.localRotation);
            Assert.That(normal, Is.GreaterThan(.1f), sourceNames[i] + " 應有換彈旋轉");
            Field(input, "frameLookDelta", new Vector2(0, 1000f * Time.unscaledDeltaTime / owner.Movement.LookSensitivity));
            Call(effect, "LateUpdate");
            Assert.That(Quaternion.Angle(baseCamera, sink.EffectsRoot.localRotation), Is.LessThan(normal * .3f));
            Assert.That(owner.Movement.KCC.Data.LookRotation, Is.EqualTo(look), "不能改玩法 Look");
            Assert.That((int)weapon.GetType().GetProperty("MagazineAmmo").GetValue(weapon), Is.EqualTo(ammo));
            driver.UnbindGameplaySources();
            Call(effect, "Update");
            Assert.That(Quaternion.Angle(baseCamera, sink.EffectsRoot.localRotation), Is.LessThan(.04f));
            Object.DestroyImmediate(view); view = null;
            runner.Despawn(runtime);
        }
        runner.Despawn(obj);
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (view != null) Object.DestroyImmediate(view);
        if (runner != null)
        {
            var task = runner.Shutdown(); float end = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            if (runner != null)
            {
                if (Application.isPlaying) Object.Destroy(runner.gameObject);
                else Object.DestroyImmediate(runner.gameObject);
            }
            runner = null;
        }
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
