using System;

namespace Purgers.Progression
{
    /// <summary>既有三套武器操作的穩定保存 ID；不依賴可調整的獎勵卡 ID。</summary>
    public static class WeaponRewardRules
    {
        public static bool IsWeapon(PlayerProfessionType weapon) =>
            weapon == PlayerProfessionType.Attack || weapon == PlayerProfessionType.Tank ||
            weapon == PlayerProfessionType.Support;

        public static string GetWeaponId(PlayerProfessionType weapon)
        {
            switch (weapon)
            {
                case PlayerProfessionType.Attack: return "weapon.attack";
                case PlayerProfessionType.Tank: return "weapon.tank";
                case PlayerProfessionType.Support: return "weapon.support";
                default: return string.Empty;
            }
        }

        // 舊版未保存武器的存檔、新局與失敗重設一律使用 Attack。
        public static PlayerProfessionType ResolveSavedWeapon(string id)
        {
            if (string.Equals(id, "weapon.tank", StringComparison.OrdinalIgnoreCase))
                return PlayerProfessionType.Tank;
            if (string.Equals(id, "weapon.support", StringComparison.OrdinalIgnoreCase))
                return PlayerProfessionType.Support;
            return PlayerProfessionType.Attack;
        }

        public static bool CanOffer(PlayerProfessionType current, PlayerProfessionType candidate) =>
            IsWeapon(candidate) && IsWeapon(current) && current != candidate;
    }
}
