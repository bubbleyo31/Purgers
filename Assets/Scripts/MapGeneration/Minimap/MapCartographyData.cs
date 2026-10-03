using UnityEngine;

namespace Purgers.Map
{
    [CreateAssetMenu(menuName = "Purgers/Map/Chunk Cartography")]
    public sealed class MapCartographyData : ScriptableObject
    {
        [SerializeField, Tooltip("同版本內容的稳定識別。重新烘焙後更新，Host／Client 必須一致。")]
        private int contentVersion;
        [SerializeField] private Vector2 minimum;
        [SerializeField] private float cellSize = 1.5f;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField, HideInInspector,
         Tooltip("0 未配置；1 地形／障礙；2 地面可通行。此版是單層投影。")]
        private byte[] cells;
        [SerializeField, HideInInspector] private float[] elevations;

        public int ContentVersion => contentVersion;
        public Vector2 Minimum => minimum;
        public float CellSize => cellSize;
        public int Width => width;
        public int Height => height;
        public int CellCount => width * height;
        public bool IsValid => width > 0 && height > 0 && cellSize > 0 &&
            cells != null && cells.Length == CellCount && elevations != null && elevations.Length == CellCount;
        public byte Kind(int cell) => cells[cell];
        public float Elevation(int cell) => elevations[cell];

        public int CellAt(Vector3 local)
        {
            int x = Mathf.FloorToInt((local.x - minimum.x) / cellSize);
            int y = Mathf.FloorToInt((local.z - minimum.y) / cellSize);
            return x < 0 || y < 0 || x >= width || y >= height ? -1 : y * width + x;
        }

        public Vector3 CellCenter(int cell) => new Vector3(
            minimum.x + (cell % width + 0.5f) * cellSize,
            elevations[cell], minimum.y + (cell / width + 0.5f) * cellSize);

#if UNITY_EDITOR
        public void SetBakedData(Vector2 origin, float spacing, int columns, int rows,
            byte[] kinds, float[] heights, int version)
        {
            minimum = origin; cellSize = spacing; width = columns; height = rows;
            cells = kinds; elevations = heights; contentVersion = version;
        }
#endif
    }
}
