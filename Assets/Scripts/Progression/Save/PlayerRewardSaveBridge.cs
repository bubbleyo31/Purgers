using System;
using System.Collections.Generic;

namespace Purgers.Progression
{
    public readonly struct PlayerRewardSaveSnapshot
    {
        public readonly int HitLevel;
        public readonly int FocusLevel;
        public readonly string FocusZId, FocusXId;
        public readonly int FocusZLevel, FocusXLevel;
        public readonly string EquippedWeaponId;
        public readonly string EquippedGrappleHitId;
        public readonly string EquippedGrappleFocusId;
        public readonly string[] PendingCandidateIds;
        public readonly string[] AcquiredRewardIds;

        public PlayerRewardSaveSnapshot(string hitId, string focusId,
            string[] pendingIds, string[] acquiredIds, string weaponId = null, int hitLevel = 1, int focusLevel = 1,
            string focusZId = null, string focusXId = null, int focusZLevel = 1, int focusXLevel = 1)
        {
            FocusZId = focusZId ?? string.Empty; FocusXId = focusXId ?? string.Empty;
            FocusZLevel = Math.Max(1, focusZLevel); FocusXLevel = Math.Max(1, focusXLevel);
            HitLevel = Math.Max(1, hitLevel); FocusLevel = Math.Max(1, focusLevel);
            EquippedWeaponId = weaponId ?? string.Empty;
            EquippedGrappleHitId = hitId ?? string.Empty;
            EquippedGrappleFocusId = focusId ?? string.Empty;
            PendingCandidateIds = pendingIds ?? Array.Empty<string>();
            AcquiredRewardIds = acquiredIds ?? Array.Empty<string>();
        }
    }

    public static class PlayerRewardSaveBridge
    {
        public static PlayerRewardSaveSnapshot ReadHost(GameSaveData save)
        {
            PlayerRunProgressionData entry = FindHost(save);
            if (entry == null)
                return new PlayerRewardSaveSnapshot(null, null, null, null);

            entry.Normalize();
            return new PlayerRewardSaveSnapshot(
                entry.EquippedGrappleHitId,
                entry.EquippedGrappleFocusId,
                entry.PendingRewardCandidateIds.ToArray(),
                entry.AcquiredRewardIds.ToArray(), entry.EquippedWeaponId, entry.EquippedHitLevel, entry.EquippedFocusLevel,
                entry.EquippedFocusZId, entry.EquippedFocusXId, entry.EquippedFocusZLevel, entry.EquippedFocusXLevel);
        }

        public static void WriteHost(GameSaveData save,
            PlayerRewardSaveSnapshot snapshot)
        {
            if (save == null)
                return;

            save.Normalize();
            PlayerRunProgressionData entry = FindHost(save);
            if (entry == null)
            {
                entry = PlayerRunProgressionData.CreateDefault(
                    GameSaveSchema.HostPlayerId);
                save.RunProgression.PlayerProgressionEntries.Add(entry);
            }

            entry.EquippedHitLevel = snapshot.HitLevel; entry.EquippedFocusLevel = snapshot.FocusLevel;
            entry.EquippedFocusZId = snapshot.FocusZId; entry.EquippedFocusXId = snapshot.FocusXId;
            entry.EquippedFocusZLevel = snapshot.FocusZLevel; entry.EquippedFocusXLevel = snapshot.FocusXLevel;
            entry.EquippedWeaponId = snapshot.EquippedWeaponId;
            entry.EquippedGrappleHitId = snapshot.EquippedGrappleHitId;
            entry.EquippedGrappleFocusId = snapshot.EquippedGrappleFocusId;
            entry.PendingRewardCandidateIds = new List<string>(
                snapshot.PendingCandidateIds);
            entry.AcquiredRewardIds = new List<string>(
                snapshot.AcquiredRewardIds);
        }

        private static PlayerRunProgressionData FindHost(GameSaveData save)
        {
            if (save?.RunProgression?.PlayerProgressionEntries == null)
                return null;

            foreach (PlayerRunProgressionData entry in
                save.RunProgression.PlayerProgressionEntries)
            {
                if (entry != null &&
                    entry.StablePlayerId == GameSaveSchema.HostPlayerId)
                    return entry;
            }

            return null;
        }
    }
}
