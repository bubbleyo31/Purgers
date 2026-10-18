using UnityEngine;

public sealed partial class BeamPresentationRuntime
{
    private ParticleSystem particles;
    private readonly System.Random random = new System.Random();
    private float Sample() => (float)random.NextDouble();

    private void EmitParticles(BeamParticleSettings settings, Vector3 start, Vector3 end)
    {
        if (!settings.enabled) return;
        if (particles == null)
        {
            if (settings.prefab != null && PlayerAbilityLocalVfxSlot.IsVisualOnly(settings.prefab.gameObject))
            {
                particles = UnityEngine.Object.Instantiate(settings.prefab, owner);
                particles.gameObject.hideFlags = HideFlags.DontSave;
            }
            else
            {
                var go = new GameObject("Beam Sparks (Local)") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(owner, false);
                particles = go.AddComponent<ParticleSystem>();
            }
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 512;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var collision = particles.collision; collision.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = particles.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (settings.material != null) renderer.sharedMaterial = settings.material;
        else if (fallbackMaterial != null) renderer.sharedMaterial = fallbackMaterial;
        bool supportsTint = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Tint");
        renderer.GetPropertyBlock(tint);
        tint.SetColor("_Tint", settings.color);
        renderer.SetPropertyBlock(tint);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.Play();
        Vector3 axis = (end - start).normalized;
        if (axis.sqrMagnitude < .5f) axis = Vector3.forward;
        Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(axis, right);
        int muzzle = Mathf.Clamp(settings.muzzleCount, 0, 128), body = Mathf.Clamp(settings.beamCount, 0, 256);
        int total = muzzle + body + Mathf.Clamp(settings.endCount, 0, 128);
        for (int i = 0; i < total; i++)
        {
            float angle = Sample() * Mathf.PI * 2;
            Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            bool along = i >= muzzle && i < muzzle + body;
            Vector3 position = i < muzzle ? start : along ? Vector3.Lerp(start, end, Sample()) : end;
            var emit = new ParticleSystem.EmitParams
            {
                position = position + radial * (Sample() * Mathf.Max(0, settings.radius)),
                velocity = (radial + axis * (Sample() - .5f)) * Mathf.Max(0, settings.speed) * (.4f + Sample()),
                startLifetime = Mathf.Max(.01f, settings.lifetime) * (.6f + Sample() * .4f),
                startSize = Mathf.Max(.001f, settings.size) * (.5f + Sample()),
                startColor = supportsTint ? Color.white : settings.color
            };
            particles.Emit(emit, 1);
        }
        BurstCount++;
    }
}

