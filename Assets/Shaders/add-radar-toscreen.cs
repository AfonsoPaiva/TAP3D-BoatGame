using UnityEngine;

/// <summary>
/// Attach this script to your Main Camera.
/// It applies the Radar Shader as a post-processing effect using Built-in RP's OnRenderImage.
///
/// Setup:
///   1. Create a Material using "Custom/RadarShader" → e.g. "RadarMaterial".
///   2. Attach this script to the Main Camera.
///   3. Drag RadarMaterial into the "Radar Material" slot.
///   4. Drag the boat's Transform into the "Boat Transform" slot.
///   5. Make sure "Allow HDR" is OFF on the camera (or it still works, but ensure
///      the camera has "Allow Post-processing" checked if needed).
///   6. Press Play – the radar appears bottom-left, composited over the rendered frame.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public class RadarHUD : MonoBehaviour
{
    [Header("References")]
    public Material  radarMaterial;
    [Tooltip("The boat transform used as the radar centre. If null, falls back to this GameObject.")]
    public Transform boatTransform;

    [Header("HUD Size")]
    [Tooltip("Radar disc diameter as a fraction of screen height.")]
    [Range(0.10f, 0.40f)]
    public float sizeRelativeToScreen = 0.22f;

    [Tooltip("Pixel margin from the screen edge.")]
    public float margin = 12f;

    [Header("Sweep")]
    [Range(0.1f, 3f)]
    public float sweepRPS = 0.4f;

    [Range(0.3f, 6.28f)]
    public float sweepTrailWidth = 1.8f;

    [Header("Rings")]
    [Range(1, 5)]
    public int numRings = 3;

    [Header("Buoy Blips")]
    [Tooltip("World-space radius that maps to the edge of the radar disc.")]
    public float radarRange = 100f;
    [Tooltip("Maximum number of buoys shown on the radar simultaneously.")]
    [Range(1, 16)]
    public int maxBuoyBlips = 16;

    [Tooltip("How fast the buoy blips blink after being revealed by the sweep (Hz).")]
    [Range(0.5f, 20f)]
    public float blipBlinkHz = 6f;

    // Shader property IDs
    private static readonly int ID_SweepRPS    = Shader.PropertyToID("_SweepRPS");
    private static readonly int ID_SweepWidth  = Shader.PropertyToID("_SweepWidth");
    private static readonly int ID_NumRings    = Shader.PropertyToID("_NumRings");
    private static readonly int ID_BuoyCount   = Shader.PropertyToID("_BuoyCount");
    private static readonly int ID_BuoyBlips   = Shader.PropertyToID("_BuoyBlips");
    private static readonly int ID_BlipBlinkHz = Shader.PropertyToID("_BlipBlinkHz");
    private static readonly int ID_RadarSize   = Shader.PropertyToID("_RadarSize");
    private static readonly int ID_RadarMargin = Shader.PropertyToID("_RadarMargin");

    // Reusable arrays – avoids GC every frame
    private Vector4[]   buoyBlips;
    private Transform[] buoyCache;

    void Start()
    {
        buoyBlips = new Vector4[maxBuoyBlips];
        buoyCache = new Transform[0];
        RefreshBuoyList();
    }

    void OnEnable()  => RefreshBuoyList();

    /// <summary>
    /// Call this whenever buoys are added or removed at runtime.
    /// </summary>
    public void RefreshBuoyList()
    {
        GameObject[] buoyGOs = GameObject.FindGameObjectsWithTag("Buoy");
        buoyCache = new Transform[buoyGOs.Length];
        for (int i = 0; i < buoyGOs.Length; i++)
            buoyCache[i] = buoyGOs[i].transform;
    }

    /// <summary>
    /// Called automatically by the Built-in RP after the camera finishes rendering.
    /// src  = what the camera just rendered.
    /// dest = what gets displayed on screen.
    /// We blit src → dest through the radar material, compositing the radar on top.
    /// </summary>
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (radarMaterial == null)
        {
            Graphics.Blit(src, dest); // passthrough if no material
            return;
        }

        UpdateMaterialProperties();

        // The Built-in Blit passes src as _MainTex into the shader automatically.
        Graphics.Blit(src, dest, radarMaterial);
    }

    private void UpdateMaterialProperties()
    {
        // ── Sweep / ring parameters ───────────────────────────────────────
        radarMaterial.SetFloat(ID_SweepRPS,    sweepRPS);
        radarMaterial.SetFloat(ID_SweepWidth,  sweepTrailWidth);
        radarMaterial.SetFloat(ID_NumRings,    numRings);
        radarMaterial.SetFloat(ID_BlipBlinkHz, blipBlinkHz);

        // ── Radar screen-space layout ─────────────────────────────────────
        radarMaterial.SetFloat(ID_RadarSize,   sizeRelativeToScreen);
        radarMaterial.SetFloat(ID_RadarMargin, margin);

        // ── Buoy blip positions in radar-UV space ─────────────────────────
        Transform centre = boatTransform != null ? boatTransform : transform;
        int count = 0;

        for (int i = 0; i < buoyCache.Length && count < maxBuoyBlips; i++)
        {
            if (buoyCache[i] == null) continue;

            // World-space offset from boat to buoy (XZ plane only)
            Vector3 delta = buoyCache[i].position - centre.position;

            // Rotate into the boat's local space so the radar is boat-relative
            float boatYaw = centre.eulerAngles.y * Mathf.Deg2Rad;
            float cosY =  Mathf.Cos(-boatYaw);
            float sinY =  Mathf.Sin(-boatYaw);
            float localX =  cosY * delta.x + sinY * delta.z;
            float localZ = -sinY * delta.x + cosY * delta.z;

            // Normalise to radar UV space: centre = 0.5, disc edge = ±0.45
            float uvX = 0.5f + (localX / radarRange) * 0.45f;
            float uvY = 0.5f + (localZ / radarRange) * 0.45f;

            buoyBlips[count] = new Vector4(uvX, uvY, 0f, 1f);
            count++;
        }

        radarMaterial.SetInt(ID_BuoyCount,        count);
        radarMaterial.SetVectorArray(ID_BuoyBlips, buoyBlips);
    }
}
