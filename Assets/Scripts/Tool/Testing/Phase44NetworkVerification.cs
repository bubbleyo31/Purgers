#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Fusion;
using Purgers.Map;
using Purgers.GameFlow.Stage;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit opt-in, isolated-save probe. Never bootstraps in ordinary gameplay.</summary>
public sealed class Phase44NetworkVerification : MonoBehaviour
{
    private NetworkRunner runner;
    private string role, output;
    private float started, bothAt = -1, nextLog, nextPin;
    private int phase;
    private bool completed, sawShared, sawIndependent;
    private int witnessChunk = -1, witnessCell = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--phase44-client");
        if (index < 0 || index + 2 >= args.Length) return;
        var menu = FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null) return;
        var prefab = (NetworkRunner)typeof(MultiClimb.Menu.MenuConnectionBehaviour)
            .GetField("networkRunnerPrefab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
        StartVerification("Client", args[index + 1], args[index + 2], prefab, 2);
    }

    public static void StartVerification(string role, string session, string output, NetworkRunner prefab, int level)
    {
        if (!Application.isPlaying || FindObjectOfType<Phase44NetworkVerification>() != null) return;
        var probe = new GameObject("Phase44Verification").AddComponent<Phase44NetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.Begin(role, session, output, prefab, level);
    }

    private async void Begin(string testRole, string session, string directory, NetworkRunner prefab, int level)
    {
        role = testRole; output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        started = Time.unscaledTime; Application.runInBackground = true;
        try
        {
            runner = Instantiate(prefab); runner.ProvideInput = true;
            var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();
            if (role == "Host")
            {
                var repository = new JsonGameSaveRepository(Path.Combine(output, "Saves"));
                var save = repository.CreateNew("Phase44 isolated verification");
                if (!save.Success) throw new Exception(save.Error);
                save.Value.RunProgression.StageLevel = level;
                context.InitializeHost(repository, save.Value);
            }
            else context.InitializeClientReadOnly();
            var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>(); manager.IsSceneTakeOverEnabled = false;
            var scene = new NetworkSceneInfo(); scene.AddSceneRef(SceneRef.FromIndex(2), LoadSceneMode.Additive);
            var settings = new Fusion.Photon.Realtime.FusionAppSettings();
            Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings); settings.FixedRegion = "asia";
            var result = await runner.StartGame(new StartGameArgs {
                CustomPhotonAppSettings = settings, GameMode = role == "Host" ? GameMode.Host : GameMode.Client,
                SessionName = session, Scene = role == "Host" ? scene : (NetworkSceneInfo?)null,
                SceneManager = manager, PlayerCount = 2 });
            Log("StartGame=" + result.Ok + ", reason=" + result.ShutdownReason);
            if (result.Ok)
            {
                var main = FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);
                if (main != null) main.gameObject.SetActive(false);
            }
            else completed = true;
        }
        catch (Exception exception) { Log("FAIL " + exception); completed = true; }
    }

    private void Update()
    {
        if (completed || runner == null || !runner.IsRunning) return;
        if (Time.unscaledTime - started > 240) { Log("FAIL timeout phase=" + phase); completed = true; return; }
        var map = FindObjectsOfType<NetworkMapState>().FirstOrDefault(m => m.Runner == runner);
        if (Time.unscaledTime >= nextLog)
        {
            nextLog = Time.unscaledTime + 3;
            Log("state ready=" + (map != null && map.IsReady) + ", snapshot=" + (map != null && map.HasExplorationSnapshot) + ", phase=" + phase);
        }
        if (map == null || !map.IsReady || !map.HasExplorationSnapshot) return;
        var stage = map.GetComponent<StageFlowController>();
        if (stage.Phase != StagePhase.Active) return;
        var view = FindObjectOfType<LocalMinimapController>();
        if (role == "Host") UpdateHost(map, stage, view);
        else UpdateClient(map, view);
    }

    private void UpdateHost(NetworkMapState map, StageFlowController stage, LocalMinimapController view)
    {
        var players = runner.ActivePlayers.ToArray();
        // Keep probe avatars on known walkable ground; never save these runtime positions.
        if (Time.unscaledTime > nextPin && phase < 3)
        {
            nextPin = Time.unscaledTime + 0.5f;
            foreach (var player in players)
                if (runner.TryGetPlayerObject(player, out var obj) && obj != null)
                {
                    int chunk = player == runner.LocalPlayer ? 0 : map.Chunks.Count - 1;
                    Pin(obj, FindGround(map.Chunks[chunk], player == runner.LocalPlayer ? -100 : 100));
                }
        }
        if (players.Length < 2) return;
        if (bothAt < 0) bothAt = Time.unscaledTime;
        float elapsed = Time.unscaledTime - bothAt;
        if (phase == 0 && elapsed > 5)
        {
            LogLayout(map);
            Capture("Compact");
            if (view == null || view.ExploredPixelCount == 0) Log("FAIL compact map has no explored pixels");
            else Log("PASS local map pixels=" + view.ExploredPixelCount + ", far markers=" + view.VisibleMarkerCount);
            view.SetExpanded(true); phase = 1;
        }
        else if (phase == 1 && elapsed > 7)
        {
            Capture("Expanded");
            Log("PASS expanded width=" + view.CurrentWorldWidth);
            map.SetSharedExploration(true); phase = 2;
        }
        else if (phase == 2 && elapsed > 12)
        {
            map.SetSharedExploration(false); phase = 3;
            if (stage.TryGetSelectedExtraction(out var point) && runner.TryGetPlayerObject(runner.LocalPlayer, out var obj))
                Pin(obj, point.transform.position + Vector3.up * 0.15f);
        }
        else if (phase == 3 && elapsed > 14)
        {
            view.SetExpanded(false); phase = 4;
        }
        else if (phase == 4 && elapsed > 16)
        {
            Capture("NearExtraction");
            Log((view.VisibleMarkerCount == 1 ? "PASS " : "FAIL ") + "near extraction marker count=" + view.VisibleMarkerCount);
            Log("Host sequence complete"); completed = true;
        }
    }

    private void UpdateClient(NetworkMapState map, LocalMinimapController view)
    {
        if (!sawIndependent)
        {
            if (view == null || view.ExploredPixelCount == 0) return;
            LogLayout(map);
            Log("PASS Client local map pixels=" + view.ExploredPixelCount + ", snapshot ready");
            Log((!map.SetSharedExploration(true) ? "PASS " : "FAIL ") + "Client cannot change Host sharing mode");
            Capture("Compact"); sawIndependent = true;
        }
        if (witnessCell < 0 && !map.SharedExploration)
        {
            foreach (var word in map.Exploration.Snapshot())
            {
                if (word.Player == runner.LocalPlayer.RawEncoded) continue;
                for (int bit = 0; bit < 32; bit++)
                {
                    int candidate = word.Index * 32 + bit;
                    if ((word.Bits & (1u << bit)) == 0 ||
                        map.Exploration.IsExplored(runner.LocalPlayer.RawEncoded, word.Chunk, candidate, false)) continue;
                    witnessChunk = word.Chunk; witnessCell = candidate; break;
                }
                if (witnessCell >= 0) break;
            }
            if (witnessCell >= 0) Log("Witness exclusively explored by Host: chunk=" + witnessChunk + ", cell=" + witnessCell);
        }
        if (witnessCell < 0) return;
        if (map.SharedExploration && !sawShared)
        {
            bool own = map.Exploration.IsExplored(runner.LocalPlayer.RawEncoded, witnessChunk, witnessCell, false);
            bool shared = map.Exploration.IsExplored(runner.LocalPlayer.RawEncoded, witnessChunk, witnessCell, true);
            Log((!own && shared ? "PASS " : "FAIL ") + "shared includes foreign cells without changing personal record");
            view.SetExpanded(true); Capture("Shared"); sawShared = true;
        }
        else if (!map.SharedExploration && sawShared)
        {
            bool own = map.Exploration.IsExplored(runner.LocalPlayer.RawEncoded, witnessChunk, witnessCell, false);
            Log((!own ? "PASS " : "FAIL ") + "switch back restores independent fog");
            Log((view.VisibleMarkerCount == 0 ? "PASS " : "FAIL ") + "remote extraction remains hidden");
            Capture("IndependentAgain"); completed = true;
        }
    }

    private void LogLayout(NetworkMapState map)
    {
        var blockers = FindObjectsOfType<MapConnectorBlockerPrototype>().First(b => b.gameObject.scene == map.gameObject.scene);
        bool matches = map.Chunks.Count == map.ChunkCount && blockers.SpawnedBlockerCount == map.ChunkCount * 2 + 2;
        for (int i = 0; i < map.Chunks.Count; i++)
            matches &= Vector3.Distance(map.Chunks[i].transform.position, map.Placements[i].Position) < 0.001f &&
                       Quaternion.Angle(map.Chunks[i].transform.rotation, map.Placements[i].Rotation) < 0.01f;
        Log((matches ? "PASS " : "FAIL ") + "layout chunks=" + map.Chunks.Count + ", blockers=" + blockers.SpawnedBlockerCount);
        for (int i = 0; i < map.Chunks.Count; i++)
            Log("chunk " + i + " position=" + map.Chunks[i].transform.position.ToString("F3") + " rotation=" + map.Chunks[i].transform.eulerAngles.ToString("F3") + " version=" + map.Chunks[i].Cartography.ContentVersion);
    }

    private static Vector3 FindGround(MapChunk chunk, float x)
    {
        var data = chunk.Cartography; int best = -1; float distance = float.MaxValue;
        for (int i = 0; i < data.CellCount; i++)
        {
            if (data.Kind(i) != 2) continue;
            var p = data.CellCenter(i); float d = (new Vector2(p.x - x, p.z)).sqrMagnitude;
            if (d < distance) { distance = d; best = i; }
        }
        return chunk.transform.TransformPoint(data.CellCenter(best)) + Vector3.up * 0.15f;
    }

    private static void Pin(NetworkObject obj, Vector3 position)
    {
        var kcc = obj.GetComponent<PlayerMovement>().KCC;
        kcc.FixedData.BasePosition = kcc.FixedData.DesiredPosition = kcc.FixedData.TargetPosition = position;
        kcc.SetPosition(position);
    }
    private void Capture(string label) => ScreenCapture.CaptureScreenshot(Path.Combine(output, role + "-" + label + ".png"));
    private void Log(string text)
    {
        File.AppendAllText(Path.Combine(output, role + ".log"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
        Debug.Log("[Phase44Verification] " + text);
    }
}
#endif
