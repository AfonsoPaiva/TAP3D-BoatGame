using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class OceanWaveManager : MonoBehaviour
{
    public static OceanWaveManager Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Boat Reference")]
    [Tooltip("The boat to follow. Auto-detected by the 'Player' tag if left empty.")]
    public Transform boat;

    [Header("Ocean Mesh")]
    [Tooltip("World-unit diameter of the ocean plane. Should exceed your view distance.")]
    public float oceanSize   = 200f;
    [Tooltip("Grid subdivisions per axis. 100 = 10 000 quads. Raise for smoother waves.")]
    public int   resolution  = 100;

    [Header("Wave Definitions")]
    public WaveParams waveA = new WaveParams(new Vector2(1f, 0f),   0.3f,  10f);
    public WaveParams waveB = new WaveParams(new Vector2(0f, 1f),   0.15f, 20f);
    public WaveParams waveC = new WaveParams(new Vector2(1f, 1f),   0.1f,  30f);

    [Range(0f, 4f)] public float waveSpeed = 1f;

    [Header("Environment")]
    [Range(0f, 2f)] public float stormIntensity = 1f;
    [Range(0f, 1f)] public float calmBlend      = 0f;

    [Header("Noise Wave Parameters")]
    public float noiseScale = 0.03f;
    public float noiseAmplitude = 0.3f;
    public float noiseSpeed = 0.2f;

    // ── Private ───────────────────────────────────────────────────────────────

    private Material _mat;

    private static readonly int ID_WaveA     = Shader.PropertyToID("_WaveA");
    private static readonly int ID_WaveB     = Shader.PropertyToID("_WaveB");
    private static readonly int ID_WaveC     = Shader.PropertyToID("_WaveC");
    private static readonly int ID_WaveSpeed = Shader.PropertyToID("_WaveSpeed");
    private static readonly int ID_NoiseScale     = Shader.PropertyToID("_NoiseScale");
    private static readonly int ID_NoiseAmplitude = Shader.PropertyToID("_NoiseAmplitude");
    private static readonly int ID_NoiseSpeed     = Shader.PropertyToID("_NoiseSpeed");

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Auto-detect boat
        if (boat == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) boat = go.transform;
        }

        // Depth texture for the shader's depth-based colour blend
        if (Camera.main != null)
            Camera.main.depthTextureMode |= DepthTextureMode.Depth;

        // Build mesh
        _mat = GetComponent<Renderer>().material;
        GetComponent<MeshFilter>().sharedMesh = BuildMesh(oceanSize, resolution);
    }

    void LateUpdate()
    {
        // ── Follow boat (XZ only — Y stays at 0) ──────────────────────────
        if (boat != null)
        {
            Vector3 p = transform.position;
            p.x = boat.position.x;
            p.z = boat.position.z;
            transform.position = p;
        }

        // ── Push wave parameters to the shader ────────────────────────────
        float storm = stormIntensity * (1f - calmBlend);
        _mat.SetVector(ID_WaveA,     ToShaderVec(waveA, storm));
        _mat.SetVector(ID_WaveB,     ToShaderVec(waveB, storm));
        _mat.SetVector(ID_WaveC,     ToShaderVec(waveC, storm));
        _mat.SetFloat (ID_WaveSpeed, waveSpeed);
        _mat.SetFloat (ID_NoiseScale,     noiseScale);
        _mat.SetFloat (ID_NoiseAmplitude, noiseAmplitude);
        _mat.SetFloat (ID_NoiseSpeed,     noiseSpeed);
    }

    ///World-space Y of the ocean surface at world XZ (x, z).
    public float GetWaveHeight(float x, float z)
    {
        float t     = Time.time * waveSpeed;
        float storm = stormIntensity * (1f - calmBlend);

        float localX = x - transform.position.x;
        float localZ = z - transform.position.z;
        float nx = localX * noiseScale + Time.time * noiseSpeed;
        float nz = localZ * noiseScale + Time.time * noiseSpeed * 0.7f;
        float noiseY = PerlinNoise(nx, nz) * noiseAmplitude;

        return transform.position.y
             + GerstnerY(waveA, storm, x, z, t)
             + GerstnerY(waveB, storm, x, z, t)
             + GerstnerY(waveC, storm, x, z, t)
             + noiseY;
    }

    /// Convenience overload — uses worldPos.x and worldPos.z.
    public float GetWaveHeight(Vector3 worldPos) => GetWaveHeight(worldPos.x, worldPos.z);

    /// Surface height and finite-difference normal at (x, z).</summary>
    public void GetWaveHeightAndNormal(float x, float z,
                                       out float height, out Vector3 normal)
    {
        const float eps = 0.5f;
        height  = GetWaveHeight(x, z);
        float hL = GetWaveHeight(x - eps, z),  hR = GetWaveHeight(x + eps, z);
        float hD = GetWaveHeight(x, z - eps),  hU = GetWaveHeight(x, z + eps);
        normal  = Vector3.Normalize(new Vector3(hL - hR, 2f * eps, hD - hU));
    }

    /// Convenience overload accepting a Vector3.</summary>
    public void GetWaveHeightAndNormal(Vector3 p, out float height, out Vector3 normal)
        => GetWaveHeightAndNormal(p.x, p.z, out height, out normal);

    // ── Environment helpers ───────────────────────────────────────────────────

    public void SetStormIntensity(float v) => stormIntensity = Mathf.Clamp(v, 0f, 2f);
    public void SetCalmBlend(float v)      => calmBlend      = Mathf.Clamp01(v);

    // ── Internal helpers ──────────────────────────────────────────────────────

    // Converts WaveParams to the shader's Vector4 format (XY=dir, Z=steepness*storm, W=wavelength)
    private static Vector4 ToShaderVec(WaveParams w, float storm)
        => new Vector4(w.direction.x, w.direction.y, w.steepness * storm, w.wavelength);

    // CPU mirror of the GPU Gerstner Y component
    private static float GerstnerY(WaveParams w, float storm, float x, float z, float t)
    {
        float k = 2f * Mathf.PI / w.wavelength;
        float c = Mathf.Sqrt(9.8f / k);
        Vector2 d = w.direction.normalized;
        float f = k * (d.x * x + d.y * z - c * t);
        return (w.steepness * storm / k) * Mathf.Sin(f);
    }

    private static Vector2 Hash2(Vector2 p)
    {
        float x = Vector2.Dot(p, new Vector2(127.1f, 311.7f));
        float y = Vector2.Dot(p, new Vector2(269.5f, 183.3f));

        float rx = Mathf.Sin(x) * 43758.5453123f;
        float ry = Mathf.Sin(y) * 43758.5453123f;

        return new Vector2(
            -1.0f + 2.0f * (rx - Mathf.Floor(rx)),
            -1.0f + 2.0f * (ry - Mathf.Floor(ry))
        );
    }

    private static float PerlinNoise(float px, float py)
    {
        Vector2 p = new Vector2(px, py);
        Vector2 i = new Vector2(Mathf.Floor(px), Mathf.Floor(py));
        Vector2 f = p - i;

        Vector2 u = new Vector2(
            f.x * f.x * f.x * (f.x * (f.x * 6.0f - 15.0f) + 10.0f),
            f.y * f.y * f.y * (f.y * (f.y * 6.0f - 15.0f) + 10.0f)
        );

        Vector2 ga = Hash2(i + new Vector2(0f, 0f));
        Vector2 gb = Hash2(i + new Vector2(1f, 0f));
        Vector2 gc = Hash2(i + new Vector2(0f, 1f));
        Vector2 gd = Hash2(i + new Vector2(1f, 1f));

        float va = Vector2.Dot(ga, f - new Vector2(0f, 0f));
        float vb = Vector2.Dot(gb, f - new Vector2(1f, 0f));
        float vc = Vector2.Dot(gc, f - new Vector2(0f, 1f));
        float vd = Vector2.Dot(gd, f - new Vector2(1f, 1f));

        return Mathf.Lerp(Mathf.Lerp(va, vb, u.x), Mathf.Lerp(vc, vd, u.x), u.y);
    }

    // Builds a flat subdivided plane centred at local origin.
    // The Ocean shader vertex stage applies Gerstner displacement at runtime.
    private static Mesh BuildMesh(float size, int res)
    {
        float half = size * 0.5f;
        float step = size / res;

        int   vCount = (res + 1) * (res + 1);
        var   verts  = new Vector3[vCount];
        var   uvs    = new Vector2[vCount];
        var   tris   = new int[res * res * 6];

        int idx = 0;
        for (int z = 0; z <= res; z++)
        for (int x = 0; x <= res; x++)
        {
            verts[idx] = new Vector3(x * step - half, 0f, z * step - half);
            uvs[idx]   = new Vector2((float)x / res, (float)z / res);
            idx++;
        }

        int t = 0;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            int bl = z * (res + 1) + x;
            tris[t++] = bl;     tris[t++] = bl + res + 1; tris[t++] = bl + 1;
            tris[t++] = bl + 1; tris[t++] = bl + res + 1; tris[t++] = bl + res + 2;
        }

        var mesh = new Mesh
        {
            name        = "OceanMesh",
            indexFormat = IndexFormat.UInt32   // supports up to ~4 billion indices
        };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        // Generous Y bounds so frustum culling survives wave displacement
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 20f, size));
        return mesh;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.25f);
        Gizmos.DrawWireCube(transform.position, new Vector3(oceanSize, 0.1f, oceanSize));
    }
#endif
}

// ── Wave parameter struct ────────────────────────────────────────────────────
[System.Serializable]
public struct WaveParams
{
    [Tooltip("XZ travel direction (normalised internally).")]
    public Vector2 direction;

    [Range(0f, 1f)]
    [Tooltip("Steepness / choppiness (Q). Keep the sum across all waves below ~1.")]
    public float steepness;

    [Tooltip("Distance between crests in world units.")]
    public float wavelength;

    public WaveParams(Vector2 dir, float steep, float waveLen)
    {
        direction  = dir;
        steepness  = steep;
        wavelength = waveLen;
    }
}
