using UnityEngine;

/// <summary>
/// Organic wave generator using a spectrum of Gerstner waves with randomized directions.
/// </summary>
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    [System.Serializable]
    public struct Wave
    {
        public float amplitude;
        public float k; // 2PI / wavelength
        public float speed;
        public Vector2 dir;
        public float phase;
    }

    [HideInInspector]
    public Wave[] waves;

    [Header("Wave Spectrum")]
    [Tooltip("Seed for the random generator. Change for a different ocean.")]
    public int seed = 42;
    [Tooltip("Number of waves. 16 is a good balance between quality and performance.")]
    [Range(1, 32)] public int numWaves = 16;
    public float baseAmplitude = 0.5f;
    public float baseWavelength = 20f;
    public float baseSpeed = 1.5f;
    [Range(0.1f, 0.9f)] public float persistence = 0.6f;
    [Range(1.1f, 3f)] public float lacunarity = 1.6f;
    public Vector2 windDirection = new Vector2(1, 0.5f);
    
    [Tooltip("How much the waves spread from the main wind direction. 0.8+ is very chaotic and oceanic.")]
    [Range(0f, 1f)] public float spread = 0.85f; 
    
    [Tooltip("How pointy the waves are. 0 = pure sine, higher = sharper crests.")]
    [Range(0f, 1f)] public float steepness = 0.4f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GenerateWaves();
    }

    public void GenerateWaves()
    {
        waves = new Wave[numWaves];
        Random.State state = Random.state;
        Random.InitState(seed);

        float amp = baseAmplitude;
        float wlen = baseWavelength;
        Vector2 baseDir = windDirection.normalized;

        for (int i = 0; i < numWaves; i++)
        {
            // Spread angle for this wave
            float angle = (Random.value * 2f - 1f) * spread * Mathf.PI;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            Vector2 dir = new Vector2(
                baseDir.x * cos - baseDir.y * sin,
                baseDir.x * sin + baseDir.y * cos
            ).normalized;

            waves[i] = new Wave
            {
                amplitude = amp,
                k = 2f * Mathf.PI / wlen,
                speed = baseSpeed * Mathf.Sqrt(baseWavelength / wlen),
                dir = dir,
                phase = Random.value * Mathf.PI * 2f
            };

            amp *= persistence;
            wlen /= lacunarity;
        }

        Random.state = state; // Restore state
    }

    public float GetWaveHeight(Vector3 worldPos)
    {
        return GetGerstnerDisplacement(worldPos).y;
    }

    public Vector3 GetGerstnerDisplacement(Vector3 worldPos)
    {
        Vector3 disp = Vector3.zero;
        float t = Time.time;

        for (int i = 0; i < waves.Length; i++)
        {
            var w = waves[i];
            float phase = w.k * (w.dir.x * worldPos.x + w.dir.y * worldPos.z) - w.speed * t + w.phase;
            float cos = Mathf.Cos(phase);
            float sin = Mathf.Sin(phase);

            disp.x += steepness * w.amplitude * w.dir.x * cos;
            disp.y += w.amplitude * sin;
            disp.z += steepness * w.amplitude * w.dir.y * cos;
        }

        return disp;
    }

#if UNITY_EDITOR
    [ContextMenu("Regenerate Waves")]
    void Regen() => GenerateWaves();
#endif
}
