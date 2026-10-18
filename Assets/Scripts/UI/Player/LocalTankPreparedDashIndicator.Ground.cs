using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class LocalTankPreparedDashIndicator
{
    Mesh groundMesh;
    Material groundMaterial;
    MaterialPropertyBlock groundProperties;
    bool missingShaderReported;
    RaycastHit[] groundHits = new RaycastHit[8];
    readonly List<Vector3> groundVertices = new List<Vector3>(512);
    readonly List<int> groundTriangles = new List<int>(768);
    readonly List<Color> groundColors = new List<Color>(512);
    readonly Vector3[] groundPoints = new Vector3[129];
    readonly bool[] groundValid = new bool[129];
    static readonly int GroundTint = Shader.PropertyToID("_Tint");

    void RefreshGround(TankPreparedDashPreviewFrame frame, TankPreparedDashIndicatorSettings settings)
    {
        if (groundMesh == null) { groundMesh = new Mesh { name = "Local Tank ground distance" }; groundMesh.MarkDynamic(); }
        groundVertices.Clear(); groundTriangles.Clear(); groundColors.Clear();
        int segments = Mathf.Clamp(settings.ringSegments, 32, 128);
        float radius = Mathf.Max(.1f, frame.Range);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 sample = frame.Position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            groundValid[i] = TryGroundSample(frame, settings, sample, out groundPoints[i]);
        }
        groundValid[segments] = groundValid[0]; groundPoints[segments] = groundPoints[0];
        Vector3 forward = Vector3.ProjectOnPlane(frame.Direction, Vector3.up).normalized;
        float brightDot = Mathf.Cos(Mathf.Clamp(frame.HalfAngle, 2f, 25f) * Mathf.Deg2Rad);
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = groundPoints[i], b = groundPoints[i + 1];
            if (!TankPreparedDashIndicatorRules.CanJoinGroundSamples(groundValid[i], groundValid[i + 1], a, b, settings.groundJoinHeight)) continue;
            // A midpoint probe keeps an unsampled gap from becoming a false continuous strip.
            if (!TryGroundSample(frame, settings, (a + b) * .5f, out var middle) ||
                !TankPreparedDashIndicatorRules.CanJoinGroundSamples(true, true, a, middle, settings.groundJoinHeight) ||
                !TankPreparedDashIndicatorRules.CanJoinGroundSamples(true, true, middle, b, settings.groundJoinHeight)) continue;
            Vector3 radial = Vector3.ProjectOnPlane((a + b) * .5f - frame.Position, Vector3.up).normalized;
            bool bright = Vector3.Dot(radial, forward) >= brightDot;
            Vector3 side = radial * Mathf.Max(.005f, settings.ringWidth) * (bright ? .85f : .5f);
            Vector3 lift = Vector3.up * .025f;
            int start = groundVertices.Count;
            groundVertices.Add(a - side + lift); groundVertices.Add(a + side + lift);
            groundVertices.Add(b + side + lift); groundVertices.Add(b - side + lift);
            Color color = new Color(1f, 1f, 1f, bright ? 1f : .58f);
            for (int j = 0; j < 4; j++) groundColors.Add(color);
            groundTriangles.Add(start); groundTriangles.Add(start + 1); groundTriangles.Add(start + 2);
            groundTriangles.Add(start); groundTriangles.Add(start + 2); groundTriangles.Add(start + 3);
        }
        groundMesh.Clear(); groundMesh.SetVertices(groundVertices); groundMesh.SetColors(groundColors);
        groundMesh.SetTriangles(groundTriangles, 0); groundMesh.RecalculateBounds();
    }

    bool TryGroundSample(TankPreparedDashPreviewFrame frame, TankPreparedDashIndicatorSettings settings, Vector3 sample, out Vector3 point)
    {
        point = default;
        float tolerance = Mathf.Max(.05f, settings.groundHeightTolerance);
        sample.y = frame.Position.y + tolerance;
        int mask = frame.GroundMask.value & ~frame.EnemyMask.value & ~((1 << 5) | (1 << 7) | (1 << 8));
        int count = ActiveAbilityPhysics.SphereCastSorted(frame.Owner.Runner.GetPhysicsScene(), sample, Vector3.down,
            tolerance * 2f, 0f, mask, frame.Owner.Object, ref groundHits);
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (ActiveAbilityPhysics.IsPlayerCollider(hit.collider) || hit.collider.GetComponentInParent<TestDamageReceiver>() != null) continue;
            if (hit.normal.y < .55f) return false;
            point = hit.point; return true;
        }
        return false;
    }

    void DrawGround(Camera camera, TankPreparedDashIndicatorSettings settings, float alpha)
    {
        if (alpha <= .001f || groundMesh == null || groundMesh.vertexCount == 0) return;
        if (groundMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("Presentation/TankGroundRange");
            if (shader == null)
            {
                if (!missingShaderReported) { missingShaderReported = true; Debug.LogWarning("Tank ground distance shader is missing.", this); }
                return;
            }
            groundMaterial = new Material(shader) { name = "Local Tank range material", hideFlags = HideFlags.HideAndDontSave };
            groundProperties = new MaterialPropertyBlock();
        }
        Color tint = settings.ringColor; tint.a *= alpha; groundProperties.SetColor(GroundTint, tint);
        // No shared Renderer: submit only to the local world camera, never to teammate, spectator or reflection cameras.
        Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0, camera, 0, groundProperties,
            ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
    }

    void ReleaseGround()
    {
        if (groundMesh != null) { Destroy(groundMesh); groundMesh = null; }
        if (groundMaterial != null) { Destroy(groundMaterial); groundMaterial = null; }
        groundProperties = null;
    }
}
