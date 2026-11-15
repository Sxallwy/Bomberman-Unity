using UnityEngine;

/// <summary>Plays the Inspector-authored particle burst for the existing hazard's lifetime.</summary>
[DisallowMultipleComponent]
public class ParticleExplosionVisual : MonoBehaviour
{
    [SerializeField] private Transform graphics;
    [SerializeField] private ParticleSystem[] particleSystems;
    [SerializeField, Min(0.01f)] private float authoredDuration = 0.5f;

    [Header("Dynamic Lighting")]
    [SerializeField] private Light flashLight;
    [SerializeField] private float lightIntensity = 4.5f;
    [SerializeField] private float lightDuration = 0.25f;

    [Header("Impact")]
    [SerializeField] private bool triggerScreenShake = true;
    [SerializeField] private float shakeMagnitude = 0.22f;
    [SerializeField] private float shakeDuration = 0.2f;

    private bool initialized;
    private float lightElapsed = 0f;
    private float initialLightIntensity;

    public void Initialize(float duration, float tileSize)
    {
        Initialize(duration, tileSize, true);
    }

    public void Initialize(float duration, float tileSize, bool isOrigin)
    {
        if (initialized) return;
        initialized = true;

        if (graphics != null)
        {
            Vector3 scale = transform.lossyScale;
            graphics.localScale = new Vector3(
                tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                tileSize / Mathf.Max(0.0001f, Mathf.Abs(scale.z))
            );
        }

        if (isOrigin && triggerScreenShake && ArenaCamera.Instance != null)
        {
            ArenaCamera.Instance.Shake(shakeMagnitude, shakeDuration);
        }

        if (flashLight != null)
        {
            flashLight.enabled = true;
            initialLightIntensity = isOrigin ? lightIntensity : lightIntensity * 0.5f;
            flashLight.intensity = initialLightIntensity;
        }

        if (particleSystems != null)
        {
            uint seed = unchecked((uint)(transform.position.GetHashCode() ^ Time.frameCount * 397));
            for (int i = 0; i < particleSystems.Length; i++)
            {
                var system = particleSystems[i];
                if (system == null) continue;

                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.useAutoRandomSeed = false;
                system.randomSeed = (seed + (uint)i) | 1u;

                var main = system.main;
                main.simulationSpeed *= authoredDuration / Mathf.Max(0.01f, duration);
                system.Play(false);
            }
        }
    }

    void Update()
    {
        if (!initialized) return;

        if (flashLight != null && flashLight.enabled)
        {
            lightElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(lightElapsed / Mathf.Max(0.01f, lightDuration));
            flashLight.intensity = Mathf.Lerp(initialLightIntensity, 0f, t * t);
            if (t >= 1f)
                flashLight.enabled = false;
        }
    }
}