using UnityEngine;

namespace Purgers.Map
{
    public static class MinimapRules
    {
        // Fixed world-height bands: small decoration does not produce contour noise.
        public static Color32 HeightColor(byte kind, float heightAboveBase)
        {
            float band = Mathf.Floor(Mathf.Max(0, heightAboveBase) / 3f) * 3f;
            byte value = kind == 0 ? (byte)12 : kind == 1
                ? (byte)Mathf.Clamp(208 + band * 0.35f, 208, 224)
                : (byte)Mathf.Clamp(88 + band * 3f, 88, 176);
            return new Color32(value, value, (byte)Mathf.Min(255, value + 5), 255);
        }

        // Single-layer map: flight above a surface is valid; floors far above the player are not.
        public static bool CanExploreHeight(float playerHeight, float surfaceHeight, float tolerance)
            => surfaceHeight - playerHeight <= tolerance;

        public static Color32 TerrainColor(byte kind, bool explored, bool nearby)
            => kind == 2
                ? (explored ? (nearby ? new Color32(160, 208, 194, 255) : new Color32(112, 157, 153, 255))
                            : new Color32(102, 120, 139, 255))
                : (explored ? new Color32(65, 83, 91, 255) : new Color32(49, 59, 75, 255));

        public static bool CanShowMarker(bool explored, float horizontalDistance,
            float verticalDistance, bool visible, float discoveryRadius, float verticalTolerance)
            => explored && visible && horizontalDistance <= discoveryRadius &&
               CanExploreHeight(0, verticalDistance, verticalTolerance);

        public static Vector2 WorldToViewport(Vector3 world, Vector3 player, float worldWidth)
            => new Vector2(0.5f + (world.x - player.x) / worldWidth,
                           0.5f + (world.z - player.z) / worldWidth);
    }
}
