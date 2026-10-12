using UnityEngine;
using UnityEngine.Rendering;

/// <summary>本機直立弧形波牆，只讀權威快照。固定幾何以 Transform 平移，不自行計時或決定傷害。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class PlayerShockwaveVisual : MonoBehaviour
{
    [Header("純外觀品質")]
    [SerializeField, Range(8, 96), Tooltip("水平弧線分段數；8～96，預設 48。只影響平滑度與頂點成本，不改角度或傷害。")]
    private int arcSegments = 48;
    [SerializeField, Range(8, 48), Tooltip("垂直分段數；8～48，預設 24。保留舊序列化欄位，只影響網格密度；波牆高度由技能 Wave Height 給定。")]
    private int elevationSegments = 24;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private Vector3[] vertices, normals;
    private Vector2[] uv;
    private Color[] colors;
    private int[] triangles;
    private int arcs, elevations;
    private Vector4 lastGeometry;
    private bool hasGeometry;
    private static readonly int WaveDistanceId = Shader.PropertyToID("_WaveDistance");
    private static readonly int WaveOpacityId = Shader.PropertyToID("_WaveOpacity");
    public Mesh GeneratedMesh => mesh;

    /// <summary>front 為整片位移，radius 為固定弧半徑；完整座標於出手時固定，不查地板也不跟隨玩家。</summary>
    public void Present(Vector3 origin, Vector3 forward, float front, float radius, float halfAngle,
        float thickness, float travelDistance = 20f, float height = 3f)
        => Present(origin, ShockwavePropagationRules.RotationFromDirection(forward), front, radius,
            halfAngle, thickness, travelDistance, height);

    public void Present(Vector3 origin, Quaternion rotation, float front, float radius, float halfAngle,
        float thickness, float travelDistance = 20f, float height = 3f)
    {
        EnsureMesh();
        radius = Mathf.Max(0.1f, radius);
        thickness = ShockwavePropagationRules.GetThickness(radius, thickness);
        halfAngle = Mathf.Clamp(halfAngle, 0f, 180f);
        height = Mathf.Max(0.1f, height);
        Vector3 facing = rotation * Vector3.forward;
        transform.SetPositionAndRotation(origin + facing *
            (Mathf.Clamp(front, 0f, Mathf.Max(0.1f, travelDistance)) +
             ShockwavePropagationRules.GetCenterOffset(radius, halfAngle, thickness)), rotation);
        transform.localScale = Vector3.one;
        meshRenderer.enabled = front >= 0f && front <= Mathf.Max(0.1f, travelDistance) && halfAngle > 0f;
        if (!meshRenderer.enabled) return;

        var geometry = new Vector4(radius, halfAngle, thickness, height);
        if (!hasGeometry || geometry != lastGeometry)
        {
            UpdateGeometry(radius, halfAngle, thickness, height);
            lastGeometry = geometry;
            hasGeometry = true;
        }
        properties.SetFloat(WaveDistanceId, front);
        properties.SetFloat(WaveOpacityId, 1f);
        meshRenderer.SetPropertyBlock(properties);
    }

    private void UpdateGeometry(float radius, float halfAngle, float thickness, float height)
    {
        int surfaceSize = (arcs + 1) * (elevations + 1);
        for (int shell = 0; shell < 2; shell++)
            for (int a = 0; a <= arcs; a++)
                for (int e = 0; e <= elevations; e++)
                {
                    float u = (float)a / arcs, v = (float)e / elevations;
                    int i = shell * surfaceSize + a * (elevations + 1) + e;
                    float yaw = Mathf.Lerp(-halfAngle, halfAngle, u);
                    SetVertex(i, ShockwavePropagationRules.SurfacePoint(shell == 0 ? radius : radius - thickness, yaw, v * height),
                        ShockwavePropagationRules.SurfacePoint(1f, yaw, 0f) * (shell == 0 ? 1f : -1f),
                        new Vector2(u, v), shell == 0 ? 1f : 0.45f);
                }
        // 左右切面保持徑向厚度；頂底封面讓空中與側面視角都能看到完整波牆。
        for (int side = 0; side < 2; side++)
            for (int e = 0; e <= elevations; e++)
                for (int edge = 0; edge < 2; edge++)
                {
                    int i = surfaceSize * 2 + side * (elevations + 1) * 2 + e * 2 + edge;
                    float v = (float)e / elevations, yaw = side == 0 ? -halfAngle : halfAngle;
                    SetVertex(i, ShockwavePropagationRules.SurfacePoint(edge == 0 ? radius - thickness : radius, yaw, v * height),
                        Quaternion.Euler(0f, yaw, 0f) * (side == 0 ? Vector3.left : Vector3.right),
                        new Vector2(side, v), edge == 0 ? 0.45f : 1f);
                }
        int capStart = surfaceSize * 2 + (elevations + 1) * 4;
        for (int cap = 0; cap < 2; cap++)
            for (int a = 0; a <= arcs; a++)
                for (int edge = 0; edge < 2; edge++)
                {
                    float u = (float)a / arcs;
                    int i = capStart + cap * (arcs + 1) * 2 + a * 2 + edge;
                    SetVertex(i, ShockwavePropagationRules.SurfacePoint(edge == 0 ? radius - thickness : radius,
                        Mathf.Lerp(-halfAngle, halfAngle, u), cap * height), cap == 0 ? Vector3.down : Vector3.up,
                        new Vector2(u, cap), edge == 0 ? 0.45f : 1f);
                }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    private void SetVertex(int i, Vector3 point, Vector3 normal, Vector2 texcoord, float alpha)
    {
        vertices[i] = point; normals[i] = normal; uv[i] = texcoord;
        colors[i] = new Color(1f, 1f, 1f, alpha);
    }

    private void EnsureMesh()
    {
        if (mesh != null) return;
        arcs = Mathf.Clamp(arcSegments, 8, 96);
        elevations = Mathf.Clamp(elevationSegments, 8, 48);
        int surfaceSize = (arcs + 1) * (elevations + 1);
        int capStart = surfaceSize * 2 + (elevations + 1) * 4;
        vertices = new Vector3[capStart + (arcs + 1) * 4];
        normals = new Vector3[vertices.Length]; uv = new Vector2[vertices.Length]; colors = new Color[vertices.Length];
        triangles = new int[(arcs * elevations * 2 + elevations * 2 + arcs * 2) * 6];
        int t = 0;
        for (int shell = 0; shell < 2; shell++)
            for (int a = 0; a < arcs; a++)
                for (int e = 0; e < elevations; e++)
                {
                    int i = shell * surfaceSize + a * (elevations + 1) + e;
                    Quad(ref t, i, i + elevations + 1, i + elevations + 2, i + 1, shell == 1);
                }
        for (int side = 0; side < 2; side++)
            for (int e = 0; e < elevations; e++)
            {
                int i = surfaceSize * 2 + side * (elevations + 1) * 2 + e * 2;
                Quad(ref t, i, i + 1, i + 3, i + 2, side == 1);
            }
        for (int cap = 0; cap < 2; cap++)
            for (int a = 0; a < arcs; a++)
            {
                int i = capStart + cap * (arcs + 1) * 2 + a * 2;
                Quad(ref t, i, i + 1, i + 3, i + 2, cap == 0);
            }
        mesh = new Mesh { name = "Shockwave translating wall (local)", hideFlags = HideFlags.DontSave };
        mesh.vertices = vertices; mesh.triangles = triangles;
        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
        properties = new MaterialPropertyBlock();
    }

    private void Quad(ref int t, int a, int b, int c, int d, bool reverse)
    {
        triangles[t++] = a; triangles[t++] = reverse ? c : b; triangles[t++] = reverse ? b : c;
        triangles[t++] = a; triangles[t++] = reverse ? d : c; triangles[t++] = reverse ? c : d;
    }

    private void OnDestroy()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
    }
}
