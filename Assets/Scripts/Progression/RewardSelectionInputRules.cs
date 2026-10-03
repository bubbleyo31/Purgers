namespace Purgers.Progression
{
    /// <summary>Local input routing only; the Host still validates every claim.</summary>
    public static class RewardSelectionInputRules
    {
        public static bool IsSelectionActive(
            bool altHeld, int pendingRewards, int choiceCount) =>
            altHeld && pendingRewards > 0 && choiceCount > 0;

        public static int GetChoiceIndex(
            int choiceCount, bool leftPressed, bool middlePressed,
            bool rightPressed)
        {
            if (choiceCount < 1 || choiceCount > 3)
                return -1;
            if (leftPressed)
                return 0;
            if (choiceCount == 3 && middlePressed)
                return 1;
            if (choiceCount >= 2 && rightPressed)
                return choiceCount - 1;
            return -1;
        }
    }
}
