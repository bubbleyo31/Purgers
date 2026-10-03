using System;

namespace Purgers.GameFlow.Stage
{
    public static class StageHudText
    {
        public static string BuildStageHeader(
            int stageLevel,
            int cycleStage,
            int cycleLength,
            bool isBossStage)
        {
            int safeLevel = Math.Max(1, stageLevel);
            int safeCycleLength = Math.Max(1, cycleLength);
            int safeCycleStage = Math.Min(
                safeCycleLength,
                Math.Max(1, cycleStage));

            return isBossStage
                ? $"關卡等級  {safeLevel}　Boss 關"
                : $"關卡等級  {safeLevel}　循環  {safeCycleStage}/{safeCycleLength}";
        }
    }
}
