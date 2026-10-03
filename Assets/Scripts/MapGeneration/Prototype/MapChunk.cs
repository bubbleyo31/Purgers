using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapChunk : MonoBehaviour
{
    [SerializeField, Tooltip("此區塊的局部小地圖資料；由 Editor 烘焙，不在 Runtime 重建 NavMesh。")]
    private Purgers.Map.MapCartographyData cartography;

    public Purgers.Map.MapCartographyData Cartography => cartography;

    [Tooltip(
        "此 Map Chunk 擁有的所有 Connector。" +
        "修改 Connector 階層後，請從元件選單執行 Collect Connectors 重新收集。")]
    [SerializeField]
    private MapConnector[] connectors;

    public MapConnector GetConnector(ConnectorSide side)
    {
        if (connectors == null || connectors.Length == 0)
        {
            throw new MissingReferenceException(
                $"{name} 尚未收集任何 MapConnector。");
        }

        MapConnector result = null;

        foreach (MapConnector connector in connectors)
        {
            if (connector == null || connector.Side != side)
                continue;

            if (result != null)
            {
                throw new InvalidOperationException(
                    $"{name} 存在重複的 {side} Connector。");
            }

            result = connector;
        }

        if (result == null)
        {
            throw new MissingReferenceException(
                $"{name} 找不到 {side} Connector。");
        }

        return result;
    }

    [ContextMenu("Collect Connectors")]
    private void CollectConnectors()
    {
        connectors = GetComponentsInChildren<MapConnector>(true);
    }
}
