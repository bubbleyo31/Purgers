using Fusion;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.Stage;
using Purgers.GameFlow.Transition;
using Purgers.Map;
using UnityEngine;
using UnityEngine.UI;

/// <summary>North-up player-centered view. Never renders world cameras or discovers from the view bounds.</summary>
[DisallowMultipleComponent]
public sealed class LocalMinimapController : MonoBehaviour
{
    [SerializeField] private NetworkMapState map;
    [SerializeField] private Purgers.GameFlow.SafeHouse.SafeHouseFlowController safeHouse;
    [SerializeField] private MapCartographyData safeHouseCartography;
    [SerializeField] private RectTransform compactContent;
    [SerializeField] private GameObject placeholder;
    [SerializeField, Min(10), Tooltip("一般小地圖顯示的世界寬度（公尺），不是探索半徑。")]
    private float compactWorldWidth = 140f;
    [SerializeField, Min(10), Tooltip("展開後顯示的世界寬度；必須比一般模式大，仍以玩家為中心。")]
    private float expandedWorldWidth = 360f;
    [SerializeField, Tooltip("展開／收合只影響地圖視圖，不鎖定玩家、不移動滑鼠游標。")]
    private KeyCode expandKey = KeyCode.M;
    [SerializeField, Min(1), Tooltip("撤離與其他敏感標記的個人發現半徑；共享探索與展開不會增加此距離。")]
    private float markerDiscoveryRadius = 30f;
    // Retain the old serialized field until an explicit scene migration; it has no rendering cost.
#pragma warning disable CS0414
    [SerializeField, HideInInspector] private int textureResolution = 256;
#pragma warning restore CS0414

    private RectTransform viewport, terrainRoot, overlayRoot, markerRoot;
    private Image playerDot, playerHeading;
    private bool isExpanded, lastShared;
    private int localPlayerId;
    private StageFlowController stage;
    private ExplorationStore subscribedStore;
    private readonly System.Collections.Generic.List<Tile> tiles = new();
    private readonly System.Collections.Generic.List<Marker> markers = new();
    private EnemyActor[] enemies = System.Array.Empty<EnemyActor>();
    private float nextRefresh, nextEnemyScan;
    private static readonly Unity.Profiling.ProfilerMarker ViewMarker = new("Purgers.Minimap.View");
    private static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("Purgers.Minimap.BuildCache");

    private sealed class Tile
    {
        public MapChunk Chunk;
        public MinimapTileCache Cache;
        public RectTransform Ground, Fog;
        public Matrix4x4 LocalToWorld;
        public Vector3 WorldCenter;
        public float Angle;
    }

    private sealed class Marker
    {
        public Image Image;
        public Transform Target;
        public EnemyActor Enemy;
        public bool IsEnemy;
    }

    public bool IsExpanded => isExpanded;
    public float CurrentWorldWidth => isExpanded ? Mathf.Max(compactWorldWidth + 1, expandedWorldWidth) : compactWorldWidth;
    public int VisibleMarkerCount { get; private set; }
    public int VisibleEnemyCount { get; private set; }
    // Compatibility diagnostics now count cached grid cells, not viewport pixels.
    public int ExploredPixelCount { get; private set; }
    public int TerrainPixelCount { get; private set; }
    public bool IsTerrainReady { get; private set; }
    public int TerrainCellsBuiltLastFrame { get; private set; }
    public double LastViewMilliseconds { get; private set; }

    private void Awake()
    {
        if (compactContent == null || (map == null &&
            (safeHouse == null || safeHouseCartography == null || !safeHouseCartography.IsValid)))
        { enabled = false; return; }
        stage = map != null ? map.GetComponent<StageFlowController>() : null;
        viewport = CreateRect("MinimapViewport", compactContent);
        var background = viewport.gameObject.AddComponent<Image>();
        background.color = new Color32(12, 12, 17, 255);
        background.raycastTarget = false;
        viewport.gameObject.AddComponent<RectMask2D>();
        terrainRoot = CreateRect("Terrain", viewport);
        overlayRoot = CreateRect("Exploration", viewport);
        markerRoot = CreateRect("Markers", viewport);
        playerDot = CreateMarker("Player", new Color32(255, 238, 160, 255), 7);
        playerHeading = CreateMarker("Heading", playerDot.color, 3);
        playerHeading.rectTransform.sizeDelta = new Vector2(3, 12);
        ApplyViewport();
        viewport.gameObject.SetActive(false);
    }

    private static RectTransform CreateRect(string name, RectTransform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private Image CreateMarker(string name, Color color, float size)
    {
        var rect = CreateRect(name, markerRoot);
        rect.sizeDelta = Vector2.one * size;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false;
        return image;
    }

    public void SetExpanded(bool value)
    {
        isExpanded = value;
        if (viewport != null) ApplyViewport();
    }

    private void ApplyViewport()
    {
        var parent = isExpanded ? (RectTransform)compactContent.GetComponentInParent<Canvas>().transform : compactContent;
        viewport.SetParent(parent, false);
        viewport.anchoredPosition = Vector2.zero;
        viewport.anchorMin = isExpanded ? Vector2.one * 0.5f : Vector2.zero;
        viewport.anchorMax = isExpanded ? Vector2.one * 0.5f : Vector2.one;
        viewport.sizeDelta = isExpanded ? Vector2.one * 600 : Vector2.zero;
    }

    private void Update()
    {
        double start = Time.realtimeSinceStartupAsDouble;
        using (ViewMarker.Auto()) UpdateView();
        LastViewMilliseconds = (Time.realtimeSinceStartupAsDouble - start) * 1000;
    }

    private void UpdateView()
    {
        TerrainCellsBuiltLastFrame = 0;
        if (viewport == null)
        {
            enabled = false;
            return;
        }

        NetworkObject player = null;
        bool isSafeHouse = map == null && safeHouse != null;
        var runner = isSafeHouse ? (safeHouse.IsNetworkReady ? safeHouse.Runner : null)
            : (map != null && map.IsNetworkReady ? map.Runner : null);
        bool valid = runner != null && (isSafeHouse || (map.IsReady && map.HasExplorationSnapshot)) &&
            LocalSceneTransition.TryGetReadyLocalPlayer(runner, out _) && !LocalPlayerControl.AllInputBlocked &&
            runner.TryGetPlayerObject(runner.LocalPlayer, out player) && player != null;
        if (valid)
        {
            var health = player.GetComponent<PlayerHealth>();
            valid = health != null && health.IsAlive;
        }
        if (!valid && isExpanded) SetExpanded(false);
        else if (valid && Input.GetKeyDown(expandKey)) SetExpanded(!isExpanded);
        viewport.gameObject.SetActive(valid);
        if (placeholder != null) placeholder.SetActive(!valid);
        if (!valid) return;

        if (isSafeHouse) EnsureSafeHouseTile();
        else EnsureTiles();
        if (!isSafeHouse && (localPlayerId != map.Runner.LocalPlayer.RawEncoded || lastShared != (bool)map.SharedExploration))
            RebuildExploration();
        using (BuildMarker.Auto())
        {
            // Total work budget across all chunks, independent of output size.
            int budget = 8192;
            IsTerrainReady = true;
            foreach (var tile in tiles)
            {
                if (!tile.Cache.Ready && budget > 0)
                {
                    int built = tile.Cache.Build(budget, tile.LocalToWorld, isSafeHouse ? null : map.IsBlocked);
                    budget -= built; TerrainCellsBuiltLastFrame += built;
                    if (tile.Cache.Ready) tile.Ground.gameObject.SetActive(true);
                }
                IsTerrainReady &= tile.Cache.Ready;
            }
        }

        Vector3 center = player.transform.position;
        float scale = Mathf.Min(viewport.rect.width, viewport.rect.height) / CurrentWorldWidth;
        TerrainPixelCount = ExploredPixelCount = 0;
        foreach (var tile in tiles)
        {
            Vector3 delta = tile.WorldCenter - center;
            Vector2 position = new(delta.x * scale, delta.z * scale);
            Vector2 size = new(tile.Cache.Data.Width * tile.Cache.Data.CellSize * scale,
                               tile.Cache.Data.Height * tile.Cache.Data.CellSize * scale);
            tile.Ground.anchoredPosition = tile.Fog.anchoredPosition = position;
            tile.Ground.sizeDelta = tile.Fog.sizeDelta = size;
            if (tile.Cache.Ready) TerrainPixelCount += tile.Cache.Data.CellCount;
            ExploredPixelCount += tile.Cache.ExploredCells;
        }
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.1f;
            foreach (var tile in tiles) tile.Cache.FlushExploration();
            if (!isSafeHouse) RefreshMarkers(center);
        }
        VisibleMarkerCount = VisibleEnemyCount = 0;
        foreach (var marker in markers)
        {
            bool show = marker.Target != null && (!marker.IsEnemy ||
                (marker.Enemy != null && marker.Enemy.IsFusionSpawned && marker.Enemy.Object != null &&
                 marker.Enemy.Object.IsValid && marker.Enemy.IsAlive));
            if (show)
            {
                Vector3 delta = marker.Target.position - center;
                Vector2 position = new(delta.x * scale, delta.z * scale);
                show = Mathf.Abs(position.x) < viewport.rect.width * 0.5f &&
                       Mathf.Abs(position.y) < viewport.rect.height * 0.5f;
                marker.Image.rectTransform.anchoredPosition = position;
            }
            marker.Image.gameObject.SetActive(show);
            if (show) { if (marker.IsEnemy) VisibleEnemyCount++; else VisibleMarkerCount++; }
        }
        Vector3 forward = player.transform.forward;
        playerHeading.rectTransform.anchoredPosition = new Vector2(forward.x, forward.z).normalized * 7;
        playerHeading.rectTransform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
    }

    private void EnsureTiles()
    {
        if (subscribedStore == map.Exploration && tiles.Count == map.Chunks.Count) return;
        ClearTiles();
        foreach (var chunk in map.Chunks)
        {
            var data = chunk.Cartography;
            var cache = new MinimapTileCache(data);
            var localCenter = new Vector3(data.Minimum.x + data.Width * data.CellSize * 0.5f, 0,
                                          data.Minimum.y + data.Height * data.CellSize * 0.5f);
            var tile = new Tile {
                Chunk = chunk, Cache = cache, LocalToWorld = chunk.transform.localToWorldMatrix,
                WorldCenter = chunk.transform.TransformPoint(localCenter), Angle = -chunk.transform.eulerAngles.y };
            tile.Ground = CreateTile("ChunkTerrain", terrainRoot, cache.Terrain, tile.Angle);
            tile.Fog = CreateTile("ChunkExploration", overlayRoot, cache.Exploration, tile.Angle);
            tile.Ground.gameObject.SetActive(false);
            tiles.Add(tile);
        }
        subscribedStore = map.Exploration;
        subscribedStore.Changed += OnExplorationChanged;
        RebuildExploration();
    }

    private void EnsureSafeHouseTile()
    {
        if (tiles.Count != 0) return;
        var data = safeHouseCartography;
        var cache = new MinimapTileCache(data);
        // SafeHouse cartography is baked in scene world coordinates. Fusion's
        // scene merge preserves these coordinates; no AI or network map is built.
        var tile = new Tile {
            Cache = cache, LocalToWorld = Matrix4x4.identity,
            WorldCenter = new Vector3(data.Minimum.x + data.Width * data.CellSize * 0.5f, 0,
                data.Minimum.y + data.Height * data.CellSize * 0.5f) };
        tile.Ground = CreateTile("SafeHouseTerrain", terrainRoot, cache.Terrain, 0);
        tile.Fog = CreateTile("SafeHouseExploration", overlayRoot, cache.Exploration, 0);
        tile.Ground.gameObject.SetActive(false);
        tiles.Add(tile);
    }

    private static RectTransform CreateTile(string name, RectTransform parent, Texture texture, float angle)
    {
        var rect = CreateRect(name, parent);
        rect.localRotation = Quaternion.Euler(0, 0, angle);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false;
        return rect;
    }

    private void OnExplorationChanged(ExplorationStore.Word word)
    {
        if ((lastShared || word.Player == localPlayerId) && word.Chunk >= 0 && word.Chunk < tiles.Count)
            tiles[word.Chunk].Cache.RevealWord(word.Index, word.Bits);
    }

    private void RebuildExploration()
    {
        localPlayerId = map.Runner.LocalPlayer.RawEncoded;
        lastShared = map.SharedExploration;
        foreach (var tile in tiles) tile.Cache.ClearExploration();
        foreach (var word in map.Exploration.Snapshot()) OnExplorationChanged(word);
    }

    private bool CanShow(Vector3 center, Vector3 target)
    {
        float distance = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(target.x, target.z));
        if (distance > markerDiscoveryRadius ||
            !MinimapRules.CanExploreHeight(center.y, target.y, map.VerticalTolerance)) return false;
        bool known = map.TryGetCell(target, out int chunk, out int cell) &&
                     map.Exploration.IsExplored(localPlayerId, chunk, cell, lastShared);
        return known && map.HasLineOfSight(center, target);
    }

    private void RefreshMarkers(Vector3 center)
    {
        foreach (var marker in markers) { marker.Target = null; marker.Enemy = null; }
        int used = 0;
        if (stage != null && stage.IsExtractionAvailable && stage.TryGetSelectedExtraction(out var point) &&
            CanShow(center, point.transform.position)) UseMarker(used++, point.transform, null);
        if (Time.unscaledTime >= nextEnemyScan)
        {
            nextEnemyScan = Time.unscaledTime + 0.5f;
            enemies = FindObjectsOfType<EnemyActor>();
        }
        foreach (var enemy in enemies)
            if (enemy != null && enemy.isActiveAndEnabled && enemy.IsFusionSpawned && enemy.Object != null &&
                enemy.Object.IsValid && enemy.Runner == map.Runner && enemy.IsAlive && CanShow(center, enemy.transform.position))
                UseMarker(used++, enemy.transform, enemy);
    }

    private void UseMarker(int index, Transform target, EnemyActor enemy)
    {
        if (index == markers.Count)
        {
            markers.Add(new Marker { Image = CreateMarker("Contact", Color.white, 7) });
            // Do this only on pool growth; reordering each frame forces unnecessary Canvas rebuilds.
            playerDot.transform.SetAsLastSibling();
            playerHeading.transform.SetAsLastSibling();
        }
        var marker = markers[index];
        marker.Target = target; marker.Enemy = enemy; marker.IsEnemy = enemy != null;
        marker.Image.color = marker.IsEnemy ? new Color32(255, 85, 83, 255) : new Color32(80, 255, 140, 255);
        marker.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, marker.IsEnemy ? 45 : 0);
    }

    private void ClearTiles()
    {
        if (subscribedStore != null) subscribedStore.Changed -= OnExplorationChanged;
        foreach (var tile in tiles)
        {
            tile.Cache.Dispose();
            if (tile.Ground != null) Destroy(tile.Ground.gameObject);
            if (tile.Fog != null) Destroy(tile.Fog.gameObject);
        }
        tiles.Clear(); subscribedStore = null;
    }

    private void OnDestroy()
    {
        ClearTiles();
        if (viewport != null) Destroy(viewport.gameObject);
    }

    private void OnDisable()
    {
        if (viewport != null) viewport.gameObject.SetActive(false);
    }
}
