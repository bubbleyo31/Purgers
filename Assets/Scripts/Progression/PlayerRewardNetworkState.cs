using Fusion;

namespace Purgers.Progression
{
    public struct PlayerRewardNetworkState : INetworkStruct
    {
        public const int MaximumRewardIdLength = 32;
        public int EquippedHitLevel;
        public int EquippedFocusLevel;
        public int EquippedFocusZLevel;
        public int EquippedFocusXLevel;
        public NetworkString<_32> EquippedFocusZId;
        public NetworkString<_32> EquippedFocusXId;
        public string GetFocusId(int slot) => slot == 0 ? EquippedGrappleFocusId.ToString() :
            slot == 1 ? EquippedFocusZId.ToString() : slot == 2 ? EquippedFocusXId.ToString() : string.Empty;
        public int GetFocusLevel(int slot) => slot == 0 ? EquippedFocusLevel : slot == 1 ? EquippedFocusZLevel : EquippedFocusXLevel;
        public void SetFocus(int slot, string id, int level)
        {
            level = System.Math.Max(1, level); id = id ?? string.Empty;
            if (slot == 0) { EquippedGrappleFocusId = id; EquippedFocusLevel = level; }
            else if (slot == 1) { EquippedFocusZId = id; EquippedFocusZLevel = level; }
            else if (slot == 2) { EquippedFocusXId = id; EquippedFocusXLevel = level; }
        }
        public int ChoiceCount;
        public int DraftRevision;
        public NetworkString<_32> Choice0;
        public NetworkString<_32> Choice1;
        public NetworkString<_32> Choice2;
        public NetworkString<_32> EquippedGrappleHitId;
        public NetworkString<_32> EquippedGrappleFocusId;

        public string GetChoice(int index)
        {
            if (index < 0 || index >= ChoiceCount)
                return string.Empty;

            switch (index)
            {
                case 0: return Choice0.ToString();
                case 1: return Choice1.ToString();
                case 2: return Choice2.ToString();
                default: return string.Empty;
            }
        }

        public void SetChoices(string[] choices)
        {
            ChoiceCount = choices != null ? System.Math.Min(3, choices.Length) : 0;
            Choice0 = ChoiceCount > 0 ? choices[0] : string.Empty;
            Choice1 = ChoiceCount > 1 ? choices[1] : string.Empty;
            Choice2 = ChoiceCount > 2 ? choices[2] : string.Empty;
            DraftRevision++;
        }
    }
}
