using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>手動啟動的隔離測試。只改暫時 Runner／Clone，不保存 Scene／Prefab，不聚焦任何視窗。</summary>
public static class TankIndicatorRuntimeVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static NetworkRunner host, client, late;
    static NetworkButtons buttons;
    static string session;
    static bool multiplayer;
    static readonly List<string> results = new List<string>();
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly Vector3 Start = new Vector3(9000, 100, 9000);
    public static string Status { get; private set; } = "Not started";
    public static string Stage { get; private set; } = "";

    [MenuItem("Tools/Purgers/Verification/Tank Indicator/Single")]
    static void Single() => Begin(false);
    [MenuItem("Tools/Purgers/Verification/Tank Indicator/Host Client")]
    static void Network() => Begin(true);

    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); results.Add("PASS " + message); }
    static async Task Until(Func<bool> condition, string stage)
    {
        Stage = stage; float end = Time.realtimeSinceStartup + 25f;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(stage); await Task.Delay(25); }
        results.Add("PASS " + stage);
    }
    static TankMeleeCombo Melee(Player player) => player.ProfessionRuntimeManager.CurrentRuntimeObject != null
        ? player.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<TankMeleeCombo>() : null;
    static void Configure(TankMeleeCombo melee)
    {
        foreach (string name in new[] { "lightAttack1", "lightAttack2", "heavyAttack" })
        {
            var definition = (TankMeleeAttackDefinition)typeof(TankMeleeCombo).GetField(name, Private).GetValue(melee);
            definition.UseAnimationFrames = true;
            definition.FrameTiming = new AnimationFrameTiming { ClipDuration = 1f, FramesPerSecond = 30f, PlaybackSpeed = 1f };
            definition.PreparationFrame = 6f; definition.DamageFrame = 15f; definition.RecoveryFrame = 21f;
        }
    }
    static BoxCollider Geometry(NetworkObject enemy)
    {
        foreach (var collider in enemy.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (var hitbox in enemy.GetComponentsInChildren<Hitbox>()) hitbox.enabled = false;
        var go = new GameObject("Indicator controlled hit region"); go.layer = 9; go.transform.SetParent(enemy.transform, false);
        go.transform.position = enemy.transform.position + Vector3.up * 1.6f;
        Vector3 scale = enemy.transform.lossyScale; go.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
        return go.AddComponent<BoxCollider>();
    }
    static void Floor(NetworkRunner runner)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); runner.MoveToRunnerScene(floor); objects.Add(floor);
        floor.transform.position = Start + Vector3.down * .5f; floor.transform.localScale = new Vector3(60, 1, 60); floor.layer = 0;
    }

    public static async void Begin(bool network)
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning) || Status == "Running")
            throw new InvalidOperationException("需要沒有其他 Runner 的空閒 Play Mode；不得中斷現有使用者遊戲。");
        Status = "Running"; multiplayer = network; buttons = default; results.Clear(); objects.Clear();
        session = "tank-indicator-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true); Floor(host);
            if (network) { client = await Connect(false); Floor(client); }
            NetworkRunner inputRunner = network ? client : host;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj = host.Spawn(playerPrefab, Start, Quaternion.identity, inputRunner.LocalPlayer);
            host.SetPlayerObject(inputRunner.LocalPlayer, obj);
            var player = obj.GetComponent<Player>();
            await Until(() => player.ProfessionRuntimeManager.CurrentRuntimeObject != null, "default runtime");
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Tank, out var error), "equip Tank: " + error);
            await Until(() => Melee(player) != null && Melee(player).OwnerPlayer != null, "Tank owner binding");
            var authorityMelee = Melee(player); Configure(authorityMelee);
            Player local = player;
            if (network)
            {
                await Until(() => client.TryGetPlayerObject(client.LocalPlayer, out var copy) && copy != null && Melee(copy.GetComponent<Player>()) != null, "input owner replica");
                local = client.GetPlayerObject(client.LocalPlayer).GetComponent<Player>(); Configure(Melee(local));
                Check(!local.Object.HasStateAuthority && local.Object.HasInputAuthority, "Client input with Host gameplay authority");
            }
            var localMelee = Melee(local);
            await Until(() => localMelee.GetComponent<LocalTankPreparedDashIndicator>() != null, "automatic local presentation binding");
            var indicator = localMelee.GetComponent<LocalTankPreparedDashIndicator>();
            var cameraObject = new GameObject("Tank indicator verification camera", typeof(Camera)); objects.Add(cameraObject);
            inputRunner.MoveToRunnerScene(cameraObject);
            var camera = cameraObject.GetComponent<Camera>(); camera.cullingMask = 0;
            camera.transform.position = Start + Vector3.up * 1.6f; camera.transform.rotation = Quaternion.identity;
            indicator.SetWorldCamera(camera);
            if (network) Check(authorityMelee.GetComponent<LocalTankPreparedDashIndicator>() == null, "Host has no Client-owned target frame");
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
            var enemy = host.Spawn(prefab, Start + Vector3.forward * 8f, Quaternion.identity, onBeforeSpawned: (r, o) =>
            {
                foreach (var behaviour in o.GetComponents<MonoBehaviour>())
                    if (behaviour is EnemyIdlePatrolBrain || behaviour is EnemyAwarenessBrain || behaviour is EnemyPerceptionController ||
                        behaviour is EnemyCombatDecisionController || behaviour is EnemyChaseBrain || behaviour is EnemyPatrolNavigator) behaviour.enabled = false;
            });
            BoxCollider selected = Geometry(enemy);
            if (network)
            { await Until(() => client.FindObject(enemy.Id) != null, "enemy replica"); selected = Geometry(client.FindObject(enemy.Id)); }
            await Task.Delay(500);
            int sequence = authorityMelee.SwingSequence; float hp = enemy.GetComponent<TestDamageReceiver>().CurrentHealth;
            buttons.Set(InputButton.Fire, true);
            await Until(() => localMelee.IsPreparing && indicator.BracketVisible, "frozen target frame");
            Check(indicator.SelectedHitCollider == selected, "frame identifies actual selected child collider");
            Check(authorityMelee.SwingSequence == sequence && enemy.GetComponent<TestDamageReceiver>().CurrentHealth == hp, "preview does not attack or damage");
            Check(indicator.GetComponentsInChildren<TMPro.TMP_Text>(true).Length == 0 && indicator.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length == 0, "frame contains no text");
            Check(indicator.GetComponentsInChildren<TankTargetBracketGraphic>(true).Length == 1, "exactly one target frame");
            await Until(() => indicator.GroundRingAlpha > .95f, "ground ring fades in");
            var mesh = (Mesh)typeof(LocalTankPreparedDashIndicator).GetField("groundMesh", Private).GetValue(indicator);
            var material = (Material)typeof(LocalTankPreparedDashIndicator).GetField("groundMaterial", Private).GetValue(indicator);
            Check(mesh != null && mesh.vertexCount > 0 && material != null && material.shader.isSupported, "ground mesh and supported URP shader are active");
            await Capture(indicator, camera, network ? "host-client" : "single");
            buttons.Set(InputButton.Jump, true);
            await Until(() => !local.Movement.IsGrounded, "actual jump input makes KCC airborne");
            buttons.Set(InputButton.Jump, false);
            await Until(() => !local.Movement.IsGrounded && indicator.GroundRingAlpha < .01f, "airborne fades ground ring completely");
            await Until(() => local.Movement.IsGrounded && indicator.GroundRingAlpha > .95f && indicator.BracketVisible, "landing restores ring and frame");
            var query = new TankPreparedDashPreviewQuery();
            Vector3 origin = local.Movement.KCC.Data.TargetPosition + Vector3.up * 1.6f;
            Check(query.TrySelect(local, origin, Vector3.forward, 12, 10, 1 << 9, ~((1 << 5) | (1 << 7) | (1 << 8))), "local query finds direct target");
            var extra = new GameObject("large second hit region"); extra.layer = 9; extra.transform.SetParent(selected.transform, false);
            extra.transform.localPosition = Vector3.up * 4; extra.AddComponent<BoxCollider>().size = new Vector3(4, 4, 2);
            Check(query.TryGetFrameGeometry(1 << 9, 4, out var region, out _), "large target geometry is available");
            Check(region.size == selected.size, "large enemy frames selected box instead of aggregate body");
            UnityEngine.Object.Destroy(extra);
            // Out-of-range local preview must not change the existing direct enemy-recognition rule.
            Check(!query.TrySelect(local, origin, Vector3.forward, 3, 10, 1 << 9, ~((1 << 5) | (1 << 7) | (1 << 8))), "range restriction hides frame");
            RaycastHit[] rays = null;
            Check(PlayerEnemyAimQuery.TryGetLocal(local, origin, Vector3.forward, 100, 1 << 9, ~((1 << 5) | (1 << 7) | (1 << 8)), ref rays), "red crosshair still identifies distant enemy");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(wall); inputRunner.MoveToRunnerScene(wall);
            wall.transform.position = origin + Vector3.forward * 3; wall.transform.localScale = new Vector3(4, 4, .25f);
            await Until(() => !indicator.BracketVisible, "wall hides target frame");
            UnityEngine.Object.Destroy(wall);
            await Until(() => indicator.BracketVisible, "frame returns after obstruction removed");
            buttons.Set(InputButton.Fire, false);
            await Until(() => !indicator.BracketVisible && !localMelee.IsPreparing, "release removes target frame");
            await Until(() => indicator.GroundRingAlpha < .01f, "release fades ring out");
            if (network)
            {
                late = await Connect(false);
                await Until(() => late.FindObject(obj.Id) != null && Melee(late.FindObject(obj.Id).GetComponent<Player>()) != null, "late join receives Tank");
                await Task.Delay(250);
                var observer = Melee(late.FindObject(obj.Id).GetComponent<Player>());
                Check(observer.GetComponent<LocalTankPreparedDashIndicator>() == null, "late observer does not receive another player's frame");
            }
            Check(player.ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Attack, out _), "switch weapon through existing authority owner");
            await Until(() => localMelee == null && indicator == null, "weapon change removes local presentation");
            Status = "Passed";
        }
        catch (Exception exception) { Status = "Failed"; results.Add("FAIL " + Stage + ": " + exception); Debug.LogError("[TankIndicator] " + exception); }
        finally
        {
            buttons = default;
            if (late != null) { await late.Shutdown(); late = null; }
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.Destroy(obj);
            objects.Clear(); Directory.CreateDirectory("Temp/TankIndicator");
            File.WriteAllLines("Temp/TankIndicator/" + (network ? "host-client" : "single") + ".txt", results);
            Debug.Log("[TankIndicator] " + Status + " checks=" + results.Count);
        }
    }

    static async Task Capture(LocalTankPreparedDashIndicator indicator, Camera camera, string name)
    {
        var canvas = indicator.GetComponentInChildren<Canvas>();
        var texture = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        var mask = camera.cullingMask;
        try
        {
            camera.targetTexture = texture; camera.cullingMask = ~((1 << 7) | (1 << 8));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .15f, .18f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
            await Task.Delay(350); Canvas.ForceUpdateCanvases();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/TankIndicator");
            File.WriteAllBytes("Temp/TankIndicator/" + name + ".png", image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }
        finally
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            camera.targetTexture = null; camera.cullingMask = mask; RenderTexture.active = previous;
            texture.Release(); UnityEngine.Object.Destroy(texture);
        }
    }

    static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject("Tank indicator " + (server ? "Host" : "Client")).AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(new NetInput { Buttons = (multiplayer ? !server && client == r : server) ? buttons : default }));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var settings = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings); settings.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = multiplayer ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            Config = config, SceneManager = manager, SessionName = session, PlayerCount = 3, IsVisible = false, CustomPhotonAppSettings = settings });
        if (!result.Ok) { await runner.Shutdown(); throw new InvalidOperationException(result.ShutdownReason.ToString()); }
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "Runner first tick"); return runner;
    }
}
