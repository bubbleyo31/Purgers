using Fusion;

namespace Purgers.Progression
{
    public struct PlayerRewardNetworkState : INetworkStruct
    {
        public int ChoiceCount;
        public int DraftRevision;
        public NetworkString<_64> Choice0;
        public NetworkString<_64> Choice1;
        public NetworkString<_64> Choice2;
        public NetworkString<_64> EquippedGrappleHitId;
        public NetworkString<_64> EquippedGrappleFocusId;

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
