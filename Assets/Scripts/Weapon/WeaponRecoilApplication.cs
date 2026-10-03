using UnityEngine;

/// <summary>
/// 將待施加的 Gameplay 後座力按 Fusion Tick 分攤；總角度不因平滑而改變。
/// 呼叫端保存剩餘角度及時間，供預測與重新模擬重播同一結果。
/// </summary>
public static class WeaponRecoilApplication
{
    public static Vector2 Step(
        ref float pendingPitch,
        ref float pendingYaw,
        ref float secondsRemaining,
        float deltaTime)
    {
        if (pendingPitch == 0f && pendingYaw == 0f)
        {
            secondsRemaining = 0f;
            return Vector2.zero;
        }

        if (secondsRemaining <= 0f || deltaTime >= secondsRemaining)
        {
            Vector2 finalStep = new Vector2(pendingPitch, pendingYaw);
            pendingPitch = 0f;
            pendingYaw = 0f;
            secondsRemaining = 0f;
            return finalStep;
        }

        if (deltaTime <= 0f)
            return Vector2.zero;

        float fraction = deltaTime / secondsRemaining;
        Vector2 step = new Vector2(pendingPitch * fraction, pendingYaw * fraction);
        pendingPitch -= step.x;
        pendingYaw -= step.y;
        secondsRemaining -= deltaTime;
        return step;
    }
}
