using UnityEngine;

/// <summary>升級增加鈎索容量時，如何補充目前能量。</summary>
public enum GrappleEnergyLevelUpMode
{
    [InspectorName("補滿至新上限")]
    RefillToMaximum = 0,
    [InspectorName("只補新舊上限差額")]
    AddCapacityDifference = 1
}

/// <summary>不依賴場景、職業或網路的鈎索使用能量規則。</summary>
public static class GrappleEnergyRules
{
    public static float MaximumEnergyForLevel(int level, float levelOneEnergy, float energyPerLevel)
    {
        double capacity = System.Math.Max(0f, levelOneEnergy) +
            (double)(System.Math.Max(1, level) - 1) * System.Math.Max(0f, energyPerLevel);
        return (float)System.Math.Min(float.MaxValue, capacity);
    }

    /// <summary>只有容量增加才補充；同級重複同步與降級不能反覆補滿。</summary>
    public static float ApplyCapacityIncrease(float energy, float oldMaximum, float newMaximum, bool refill)
    {
        float maximum = Mathf.Max(0f, newMaximum);
        float current = Mathf.Clamp(energy, 0f, Mathf.Max(0f, oldMaximum));
        if (maximum > oldMaximum)
            current = refill ? maximum : current + maximum - Mathf.Max(0f, oldMaximum);
        return Mathf.Clamp(current, 0f, maximum);
    }

    public static bool CanLaunch(float energy, float launchCost)
    {
        return energy > 0f && energy >= Mathf.Max(0f, launchCost);
    }

    /// <summary>失敗不扣款；必須足額支付一次發射，不能用不足一點的尾數發射。</summary>
    public static bool TryConsumeLaunch(float energy, float launchCost, out float remaining)
    {
        remaining = Mathf.Max(0f, energy);
        if (!CanLaunch(remaining, launchCost))
            return false;
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, launchCost));
        return true;
    }

    /// <summary>按模擬秒數連續扣點，沒有自然恢復；歸零不表示應中斷移動。</summary>
    public static float ConsumePull(float energy, float pointsPerSecond, float deltaTime)
    {
        return Mathf.Max(0f, energy - Mathf.Max(0f, pointsPerSecond) * Mathf.Max(0f, deltaTime));
    }
}
