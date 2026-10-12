using Fusion;
using UnityEngine;

/// <summary>可重播的前搖／停格進度。兩種時間只屬於本次左鍵；傷害仍由 TankMeleeCombo 決定。</summary>
public struct TankMeleePreparationProgress : INetworkStruct
{
    public float CursorSeconds;
    public float ButtonHeldSeconds;
    public float PreparedSeconds;
    public NetworkBool Released;
    public NetworkBool IsHolding;

    public static TankMeleePreparationProgress Begin(bool held, float bufferedHeldSeconds = 0f) =>
        new TankMeleePreparationProgress { Released = !held, ButtonHeldSeconds = Mathf.Max(0f, bufferedHeldSeconds) };

    /// <returns>本 Tick 是否應正式出手。呼叫方出手後不得再次呼叫本方法。</returns>
    public bool Advance(float deltaTime, bool held, float preparationSeconds, float damageSeconds)
    {
        float dt = Mathf.Max(0f, deltaTime);
        if (!Released)
        {
            if (held) ButtonHeldSeconds += dt;
            else Released = true; // 放開後重新按下不能把同一刀變回預備。
        }
        if (IsHolding)
        {
            if (Released)
            {
                IsHolding = false;
                CursorSeconds = damageSeconds;
                return true;
            }
            PreparedSeconds += dt;
            return false;
        }
        float next = CursorSeconds + dt;
        if (!Released && next >= preparationSeconds)
        {
            CursorSeconds = preparationSeconds;
            IsHolding = true;
            PreparedSeconds = Mathf.Max(0f, next - preparationSeconds);
            return false;
        }
        CursorSeconds = Mathf.Min(next, damageSeconds);
        return next >= damageSeconds;
    }
}
