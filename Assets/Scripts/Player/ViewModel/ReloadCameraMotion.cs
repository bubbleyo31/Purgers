using UnityEngine;

/// <summary>純本地換彈旋轉狀態；不存取輸入、Transform 或 Networked 資料。</summary>
public sealed class ReloadCameraMotion
{
    private bool initialized;
    private bool previousReloading;
    private Quaternion baseline = Quaternion.identity;
    private Quaternion output = Quaternion.identity;
    private float turnWeight = 1f;
    public bool Active { get; private set; }

    // 在 Animator 本幀求值前捕捉起始姿態；中途才綁定的換彈不補播。
    public void Prepare(bool reloading, Quaternion pose)
    {
        if (!initialized)
        {
            initialized = true;
            previousReloading = reloading;
            return;
        }
        if (reloading && !previousReloading)
        {
            baseline = pose;
            Active = true;
        }
        if (!reloading) Active = false;
        previousReloading = reloading;
    }

    public Quaternion Step(Quaternion pose, Quaternion modelToCamera, Vector3 gain, float maxDegrees,
        float lookSpeed, float softenStart, float softenEnd, float minimumWeight,
        float blendSeconds, float turnResponseSeconds, float dt)
    {
        float amount = Mathf.InverseLerp(Mathf.Max(0, softenStart),
            Mathf.Max(softenStart + .01f, softenEnd), Mathf.Max(0, lookSpeed));
        float targetWeight = Mathf.Lerp(1, Mathf.Clamp01(minimumWeight), Mathf.SmoothStep(0, 1, amount));
        turnWeight = Mathf.Lerp(turnWeight, targetWeight, Blend(dt, turnResponseSeconds));
        Quaternion target = Quaternion.identity;
        if (Active)
        {
            Quaternion delta = pose * Quaternion.Inverse(baseline);
            delta = modelToCamera * delta * Quaternion.Inverse(modelToCamera);
            target = Quaternion.Slerp(Quaternion.identity, ScaleRotation(delta, gain, maxDegrees), turnWeight);
        }
        output = Quaternion.Slerp(output, target, Blend(dt, blendSeconds));
        return output;
    }

    // 最短軸角向量避免 359 -> 1 度時出現 Euler 跳變；倍率在相機座標套用。
    public static Quaternion ScaleRotation(Quaternion delta, Vector3 gain, float maxDegrees)
    {
        delta = delta.normalized;
        if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle < .0001f || maxDegrees <= 0) return Quaternion.identity;
        var vector = Vector3.Scale(axis * angle, new Vector3(Mathf.Clamp01(gain.x),
            Mathf.Clamp01(gain.y), Mathf.Clamp01(gain.z)));
        vector = Vector3.ClampMagnitude(vector, Mathf.Clamp(maxDegrees, 0, 15));
        return vector.sqrMagnitude < .00000001f ? Quaternion.identity :
            Quaternion.AngleAxis(vector.magnitude, vector.normalized);
    }

    private static float Blend(float dt, float seconds) => dt <= 0 ? 0 :
        seconds <= 0 ? 1 : 1f - Mathf.Exp(-dt / seconds);

    public void Reset()
    {
        initialized = previousReloading = Active = false;
        baseline = output = Quaternion.identity;
        turnWeight = 1;
    }
}
