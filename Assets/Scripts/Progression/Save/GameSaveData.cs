using System;
using System.Collections.Generic;

namespace Purgers.Progression
{
    public static class GameSaveSchema
    {
        public const int CurrentVersion = 1;
        public const int DefaultCycleLength = 4;
        public const string HostPlayerId = "host";
    }

    [Serializable]
    public sealed class GameSaveData
    {
        public int SaveVersion = GameSaveSchema.CurrentVersion;
        public string SaveId = string.Empty;
        public string DisplayName = string.Empty;
        public string CreatedUtc = string.Empty;
        public string LastPlayedUtc = string.Empty;
        public int CycleLengthSnapshot = GameSaveSchema.DefaultCycleLength;
        public PermanentProgressionData PermanentProgression = new PermanentProgressionData();
        public RunProgressionData RunProgression = RunProgressionData.CreateDefault();

        public static GameSaveData CreateNew(string saveId, string displayName, DateTime utcNow)
        {
            string timestamp = utcNow.ToUniversalTime().ToString("O");
            return new GameSaveData
            {
                SaveVersion = GameSaveSchema.CurrentVersion,
                SaveId = saveId,
                DisplayName = displayName,
                CreatedUtc = timestamp,
                LastPlayedUtc = timestamp,
                CycleLengthSnapshot = GameSaveSchema.DefaultCycleLength,
                PermanentProgression = new PermanentProgressionData(),
                RunProgression = RunProgressionData.CreateDefault()
            };
        }

        public void ResetRunProgression()
        {
            RunProgression = RunProgressionData.CreateDefault();
        }

        public void Normalize()
        {
            PermanentProgression = PermanentProgression ?? new PermanentProgressionData();
            PermanentProgression.Normalize();

            RunProgression = RunProgression ?? RunProgressionData.CreateDefault();
            RunProgression.Normalize();

            if (CycleLengthSnapshot < 1)
                CycleLengthSnapshot = GameSaveSchema.DefaultCycleLength;
        }
    }

    [Serializable]
    public sealed class PermanentProgressionData
    {
        public int PermanentPoints;
        public List<string> CosmeticUnlockIds = new List<string>();
        public List<string> SafeHouseTalentIds = new List<string>();

        public void Normalize()
        {
            CosmeticUnlockIds = CosmeticUnlockIds ?? new List<string>();
            SafeHouseTalentIds = SafeHouseTalentIds ?? new List<string>();
            PermanentPoints = Math.Max(0, PermanentPoints);
        }
    }

    [Serializable]
    public sealed class RunProgressionData
    {
        public int StageLevel = 1;
        public List<PlayerRunProgressionData> PlayerProgressionEntries =
            new List<PlayerRunProgressionData>();

        public static RunProgressionData CreateDefault()
        {
            return new RunProgressionData
            {
                StageLevel = 1,
                PlayerProgressionEntries = new List<PlayerRunProgressionData>
                {
                    PlayerRunProgressionData.CreateDefault(GameSaveSchema.HostPlayerId)
                }
            };
        }

        public void Normalize()
        {
            StageLevel = Math.Max(1, StageLevel);
            PlayerProgressionEntries =
                PlayerProgressionEntries ?? new List<PlayerRunProgressionData>();

            for (int i = PlayerProgressionEntries.Count - 1; i >= 0; i--)
            {
                PlayerRunProgressionData entry = PlayerProgressionEntries[i];
                if (entry == null)
                {
                    PlayerProgressionEntries.RemoveAt(i);
                    continue;
                }

                entry.Normalize();
            }

            if (PlayerProgressionEntries.Count == 0)
            {
                PlayerProgressionEntries.Add(
                    PlayerRunProgressionData.CreateDefault(GameSaveSchema.HostPlayerId));
            }
        }
    }

    [Serializable]
    public sealed class PlayerRunProgressionData
    {
        public string StablePlayerId = string.Empty;
        public int PlayerLevel = 1;
        public int CurrentExperience;
        public string EquippedGrappleHitId = string.Empty;
        public string EquippedGrappleFocusId = string.Empty;
        public string EquippedWeaponId = string.Empty;
        public List<string> AcquiredCharacterBonusIds = new List<string>();
        public List<string> InventoryItemIds = new List<string>();
        public List<string> DebuffIds = new List<string>();
        public List<string> OneShotAbilityIds = new List<string>();

        public static PlayerRunProgressionData CreateDefault(string stablePlayerId)
        {
            return new PlayerRunProgressionData
            {
                StablePlayerId = stablePlayerId,
                PlayerLevel = 1,
                CurrentExperience = 0
            };
        }

        public void Normalize()
        {
            StablePlayerId = StablePlayerId ?? string.Empty;
            PlayerLevel = Math.Max(1, PlayerLevel);
            CurrentExperience = Math.Max(0, CurrentExperience);
            EquippedGrappleHitId = EquippedGrappleHitId ?? string.Empty;
            EquippedGrappleFocusId = EquippedGrappleFocusId ?? string.Empty;
            EquippedWeaponId = EquippedWeaponId ?? string.Empty;
            AcquiredCharacterBonusIds =
                AcquiredCharacterBonusIds ?? new List<string>();
            InventoryItemIds = InventoryItemIds ?? new List<string>();
            DebuffIds = DebuffIds ?? new List<string>();
            OneShotAbilityIds = OneShotAbilityIds ?? new List<string>();
        }
    }
}
