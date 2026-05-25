using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// All-in-one ocean system.
///
/// Attach to any scene GameObject.  Add a MeshRenderer and assign the
/// Ocean material — RequireComponent handles MeshFilter automatically.
///
/// Responsibilities:
///   • Creates a large flat mesh and keeps it centred on the boat every frame.
///     Because the vertex shader uses world-space XZ for wave phase, the mesh
///     can slide freely without visible seams — waves appear infinite.
///   • Pushes Gerstner parameters to the material so GPU and CPU stay in sync.
///   • Exposes GetWaveHeight / GetWaveHeightAndNormal for BoatController,
///     BuoyFloater, or any other physics script.
///
/// Scene setup:
///   1. Create an empty GameObject called "Ocean".
///   2. Add MeshRenderer, assign the Ocean material.
///   3. Add this component.
///   4. Assign the boat transform (or tag the boat "Player" for auto-detect).
///   5. Delete the old WaterManager and WaveManager GameObjects.
/// </summary>
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

    // ── Private ───────────────────────────────────────────────────────────────

    private Material _mat;

    private static readonly int ID_WaveA     = Shader.PropertyToID("_WaveA");
    private static readonly int ID_WaveB     = Shader.PropertyToID("_WaveB");
    private static readonly int ID_WaveC     = Shader.PropertyToID("_WaveC");
    private static readonly int ID_WaveSpeed = Shader.PropertyToID("_WaveSpeed");

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
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>World-space Y of the ocean surface at world XZ (x, z).</summary>
    public float GetWaveHeight(float x, float z)
    {
        float t     = Time.time * waveSpeed;
        float storm = stormIntensity * (1f - calmBlend);

        return transform.position.y
             + GerstnerY(waveA, storm, x, z, t)
             + GerstnerY(waveB, storm, x, z, t)
             + GerstnerY(waveC, storm, x, z, t);
    }

    /// <summary>Convenience overload — uses worldPos.x and worldPos.z.</summary>
    public float GetWaveHeight(Vector3 worldPos) => GetWaveHeight(worldPos.x, worldPos.z);

    /// <summary>Surface height and finite-difference normal at (x, z).</summary>
    public void GetWaveHeightAndNormal(float x, float z,
                                       out float height, out Vector3 normal)
    {
        const float eps = 0.5f;
        height  = GetWaveHeight(x, z);
        float hL = GetWaveHeight(x - eps, z),  hR = GetWaveHeight(x + eps, z);
        float hD = GetWaveHeight(x, z - eps),  hU = GetWaveHeight(x, z + eps);
        normal  = Vector3.Normalize(new Vector3(hL - hR, 2f * eps, hD - hU));
    }

    /// <summary>Convenience overload accepting a Vector3.</summary>
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
