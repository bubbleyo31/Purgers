using System;
using System.Collections.Generic;
using Fusion.Menu;

namespace MultiClimb.Menu
{
    /// <summary>沿用 MenuConfig 固定索引；Best 只用於選區，不可寫進新房間代碼。</summary>
    public static class MenuRegionRouting
    {
        public static string Normalize(string region)
        {
            // Photon 可回傳 region/cluster；房間代碼只保存 region。
            return (region ?? string.Empty).Split('/')[0].Trim().ToLowerInvariant();
        }

        public static string Encode(IFusionMenuConfig config, string code, string actualRegion)
        {
            string region = Normalize(actualRegion);
            int index = config.AvailableRegions.IndexOf(region);
            if (string.IsNullOrEmpty(region) || index < 0)
                throw new InvalidOperationException("伺服器區域尚未列入房間代碼設定：" + region + "。請選擇指定區域後重試。");
            string result = config.CodeGenerator.EncodeRegion(code, index);
            if (string.IsNullOrEmpty(result)) throw new InvalidOperationException("房間代碼的區域索引無效。");
            return result;
        }

        public static string Decode(IFusionMenuConfig config, string code)
        {
            if (!config.CodeGenerator.IsValid(code)) throw new InvalidOperationException("房間代碼格式不正確。");
            int index = config.CodeGenerator.DecodeRegion(code);
            if (index < 0 || index >= config.AvailableRegions.Count)
                throw new InvalidOperationException("房間代碼的伺服器區域不正確。");
            string region = Normalize(config.AvailableRegions[index]);
            if (string.IsNullOrEmpty(region))
                throw new InvalidOperationException("這是舊版 Best 房間代碼，無法確定伺服器區域。請房主更新遊戲並重新開房。");
            return region;
        }

        public static List<FusionMenuOnlineRegion> CreateCatalog(IFusionMenuConfig config)
        {
            var result = new List<FusionMenuOnlineRegion>();
            // SDK Party 以列表位置編碼，必須與 Config 一致；這不是測速結果。
            foreach (string region in config.AvailableRegions)
                result.Add(new FusionMenuOnlineRegion { Code = region, Ping = string.IsNullOrEmpty(region) ? 0 : int.MaxValue });
            return result;
        }
    }
}
