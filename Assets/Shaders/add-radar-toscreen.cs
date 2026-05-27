using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public class RadarHUD : MonoBehaviour
{
    [Header("References")]
    public Material  radarMaterial;
    [Tooltip("The boat transform used as the radar centre. If null, falls back to this GameObject.")]
    public Transform boatTransform;

    [Header("HUD Size")]
    [Range(0.10f, 0.40f)]
    public float sizeRelativeToScreen = 0.22f;
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
    [Range(1, 16)]
    public int maxBuoyBlips = 16;

    [Header("Silhouette Rendering")]
    public int atlasResolution = 512;
    [Range(0.02f, 0.15f)]
    public float blipRadarSize = 0.06f;
    [Tooltip("Layer index for RadarSilhouette layer. -1 = auto-detect by name.")]
    public int silhouetteLayerIndex = -1;
    [Range(1.0f, 2.0f)]
    public float boundsPadding = 1.2f;

    // Shader property IDs
    static readonly int ID_SweepRPS        = Shader.PropertyToID("_SweepRPS");
    static readonly int ID_SweepWidth      = Shader.PropertyToID("_SweepWidth");
    static readonly int ID_NumRings        = Shader.PropertyToID("_NumRings");
    static readonly int ID_BuoyCount       = Shader.PropertyToID("_BuoyCount");
    static readonly int ID_BuoyBlips       = Shader.PropertyToID("_BuoyBlips");
    static readonly int ID_RadarSize       = Shader.PropertyToID("_RadarSize");
    static readonly int ID_RadarMargin     = Shader.PropertyToID("_RadarMargin");
    static readonly int ID_SilhouetteAtlas = Shader.PropertyToID("_RadarSilhouetteAtlas");
    static readonly int ID_AtlasRects      = Shader.PropertyToID("_BuoyAtlasRects");
    static readonly int ID_BlipSizes       = Shader.PropertyToID("_BuoyBlipSizes");

    // Reusable arrays
    Vector4[]   buoyBlips;
    Vector4[]   buoyAtlasRects;
    float[]     buoyBlipSizes;
    Transform[] buoyCache;

    // Silhouette rendering
    Camera        silhouetteCam;
    RenderTexture silhouetteAtlas;
    RenderTexture tileRT;           // small per-tile temp RT
    Shader        silhouetteShader;
    int           gridSize;
    int           tileSize;
    bool          isInitialized;
    Material      blitMaterial;
    bool          hasSavedDebug = false;

    void Start()
    {
        buoyBlips      = new Vector4[maxBuoyBlips];
        buoyAtlasRects = new Vector4[maxBuoyBlips];
        buoyBlipSizes  = new float[maxBuoyBlips];
        buoyCache      = new Transform[0];

        SetupSilhouetteCamera();
        isInitialized = true;
        RefreshBuoyList();
    }

    void OnEnable()
    {
        if (isInitialized) RefreshBuoyList();
    }

    void LateUpdate()
    {
        if (isInitialized)
        {
            RenderAllSilhouettes(false);
        }
    }

    void OnDestroy()
    {
        if (silhouetteAtlas != null) { silhouetteAtlas.Release(); Destroy(silhouetteAtlas); }
        if (tileRT          != null) { tileRT.Release();          Destroy(tileRT); }
        if (silhouetteCam   != null)   Destroy(silhouetteCam.gameObject);
        if (blitMaterial    != null)   Destroy(blitMaterial);
    }

    /// Call when buoys are added/removed at runtime.</summary>
    public void RefreshBuoyList()
    {
        if (!isInitialized) return;

        var gos = GameObject.FindGameObjectsWithTag("Buoy");
        buoyCache = new Transform[gos.Length];
        for (int i = 0; i < gos.Length; i++)
            buoyCache[i] = gos[i].transform;

        Debug.Log($"[RadarHUD Diagnostics] RefreshBuoyList: Found {buoyCache.Length} buoy(s) tagged 'Buoy'.");
        RenderAllSilhouettes(true);
    }

    // ── Silhouette camera setup ──────────────────────────────────────────

    void SetupSilhouetteCamera()
    {
        gridSize = Mathf.CeilToInt(Mathf.Sqrt(maxBuoyBlips));
        tileSize = atlasResolution / gridSize;

        Debug.Log($"[RadarHUD Diagnostics] SetupSilhouetteCamera: AtlasResolution={atlasResolution}, GridSize={gridSize}, TileSize={tileSize}");

        // Atlas texture (the final composite with all tiles)
        silhouetteAtlas = new RenderTexture(atlasResolution, atlasResolution, 16, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        silhouetteAtlas.Create();
        ClearRT(silhouetteAtlas);

        // Per-tile temporary RT (small, rendered one buoy at a time then copied to atlas)
        tileRT = new RenderTexture(tileSize, tileSize, 16, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        tileRT.Create();

        // Shader
        silhouetteShader = Shader.Find("Hidden/RadarSilhouette");
        if (silhouetteShader == null)
            Debug.LogError("[RadarHUD Diagnostics] 'Hidden/RadarSilhouette' shader not found!");

        // Layer Auto-Detection
        if (silhouetteLayerIndex < 0)
        {
            string[] searchLayerNames = new string[] {
                "RadarSilhouette",
                "radarsillhouete", // User's custom casing
                "radarsilhouette",
                "Radar Silhouette",
                "Radar"
            };

            foreach (string layerName in searchLayerNames)
            {
                int layerIdx = LayerMask.NameToLayer(layerName);
                if (layerIdx >= 0)
                {
                    silhouetteLayerIndex = layerIdx;
                    Debug.Log($"[RadarHUD Diagnostics] Found layer match: '{layerName}' at index {layerIdx}");
                    break;
                }
            }

            if (silhouetteLayerIndex < 0)
            {
                Debug.LogWarning("[RadarHUD Diagnostics] No dedicated silhouette layer ('RadarSilhouette', 'radarsillhouete') found. Using layer 31 fallback.");
                silhouetteLayerIndex = 31;
            }
        }
        else
        {
            Debug.Log($"[RadarHUD Diagnostics] Using inspector-defined silhouette layer index: {silhouetteLayerIndex}");
        }

        // Hidden orthographic camera
        var go = new GameObject("_RadarSilhouetteCamera") { hideFlags = HideFlags.HideAndDontSave };
        silhouetteCam = go.AddComponent<Camera>();
        silhouetteCam.enabled         = false;
        silhouetteCam.orthographic    = true;
        silhouetteCam.nearClipPlane   = 0.1f;
        silhouetteCam.farClipPlane    = 500f;
        silhouetteCam.clearFlags      = CameraClearFlags.SolidColor;
        silhouetteCam.backgroundColor = Color.black;
        silhouetteCam.cullingMask     = 1 << silhouetteLayerIndex;
        silhouetteCam.allowHDR        = false;
        silhouetteCam.allowMSAA       = false;
        silhouetteCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void ClearRT(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    // ── Silhouette rendering ─────────────────────────────────────────────

    void RenderAllSilhouettes(bool verbose = false)
    {
        if (silhouetteCam == null) return;

        ClearRT(silhouetteAtlas);

        int count = Mathf.Min(buoyCache.Length, maxBuoyBlips);
        if (verbose)
            Debug.Log($"[RadarHUD Diagnostics] Rendering {count} buoy silhouette(s) to atlas.");

        for (int i = 0; i < count; i++)
        {
            if (buoyCache[i] != null)
            {
                RenderBuoySilhouette(i, buoyCache[i], verbose);
            }
        }

        // Save diagnostic PNGs to disk only once (1 second after load) to prevent frame-rate drops or infinite file writes
        if (!hasSavedDebug && Time.timeSinceLevelLoad > 1.0f)
        {
            hasSavedDebug = true;
            SaveRTToPNG(silhouetteAtlas, "debug_atlas.png");
            SaveRTToPNG(tileRT, "debug_tile.png");
        }
    }

    void RenderBuoySilhouette(int index, Transform buoy, bool verbose = false)
    {
        if (silhouetteCam == null || silhouetteShader == null) return;

        // Get bounds
        Bounds bounds = GetCompositeBounds(buoy);
        float extent = Mathf.Max(bounds.extents.x, bounds.extents.z) * boundsPadding;
        if (extent < 0.5f) extent = 0.5f;

        // Count active renderers for logging only when verbose is requested
        if (verbose)
        {
            var renderers = buoy.GetComponentsInChildren<Renderer>();
            Debug.Log($"[RadarHUD Diagnostics] Buoy [{index}]: '{buoy.name}' has {renderers.Length} Renderers. Bounds Center={bounds.center}, Extent={extent}.");
            foreach (var r in renderers)
            {
                Debug.Log($"[RadarHUD Diagnostics]   - Renderer: '{r.name}' | Active={r.enabled} | Shader='{r.sharedMaterial?.shader?.name}' | Layer='{LayerMask.LayerToName(r.gameObject.layer)}' ({r.gameObject.layer})");
            }
        }

        // Position camera above the buoy looking down
        silhouetteCam.transform.position   = bounds.center + Vector3.up * 100f;
        silhouetteCam.orthographicSize     = extent;

        // Render to the small per-tile RT (not the atlas directly)
        silhouetteCam.targetTexture = tileRT;

        // Temporarily swap buoy to silhouette layer
        int[] origLayers = SetLayersRecursive(buoy.gameObject, silhouetteLayerIndex);

        // Force a manual render with the replacement silhouette shader
        silhouetteCam.RenderWithShader(silhouetteShader, "");

        RestoreLayersRecursive(buoy.gameObject, origLayers);

        // Blit the tile RT into the correct position in the atlas
        int col = index % gridSize;
        int row = index / gridSize;

        BlitTileToAtlas(tileRT, silhouetteAtlas, col, row);
    }

    Material GetBlitMaterial()
    {
        if (blitMaterial == null)
        {
            Shader unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader != null)
            {
                blitMaterial = new Material(unlitShader);
            }
            else
            {
                Debug.LogError("[RadarHUD Diagnostics] 'Unlit/Texture' shader not found for blitting fallback.");
            }
        }
        return blitMaterial;
    }

    void BlitTileToAtlas(RenderTexture tile, RenderTexture atlas, int col, int row)
    {
        // Try hardware-accelerated direct GPU copy first (100% reliable and extremely fast on Metal/macOS)
        if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
        {
            Graphics.CopyTexture(tile, 0, 0, 0, 0, tileSize, tileSize, atlas, 0, 0, col * tileSize, row * tileSize);
            return;
        }

        // Fallback: Legacy GL Blit
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = atlas;

        Material blitMat = GetBlitMaterial();
        if (blitMat != null)
        {
            blitMat.mainTexture = tile;
            blitMat.SetPass(0);
        }

        // Normalized viewport rect coordinates [0, 1] (bottom-to-top layout matching buoyAtlasRects)
        float xMin = (float)(col * tileSize) / atlasResolution;
        float xMax = (float)((col + 1) * tileSize) / atlasResolution;
        float yMin = (float)(row * tileSize) / atlasResolution;
        float yMax = (float)((row + 1) * tileSize) / atlasResolution;

        GL.PushMatrix();
        GL.LoadOrtho();
        GL.LoadIdentity(); // Reset modelview matrix to prevent camera offset multiplication
        GL.Begin(GL.QUADS);

        // Map tile UVs [0,1] to target region of the atlas [0,1]
        GL.TexCoord2(0, 0); GL.Vertex3(xMin, yMin, 0.1f);
        GL.TexCoord2(1, 0); GL.Vertex3(xMax, yMin, 0.1f);
        GL.TexCoord2(1, 1); GL.Vertex3(xMax, yMax, 0.1f);
        GL.TexCoord2(0, 1); GL.Vertex3(xMin, yMax, 0.1f);

        GL.End();
        GL.PopMatrix();

        RenderTexture.active = prevActive;
    }

    void SaveRTToPNG(RenderTexture rt, string filename)
    {
        if (rt == null) return;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        byte[] bytes = tex.EncodeToPNG();
        DestroyImmediate(tex);

        string path = System.IO.Path.Combine(Application.dataPath, "Shaders/" + filename);
        try
        {
            System.IO.File.WriteAllBytes(path, bytes);
            Debug.Log($"[RadarHUD Diagnostics] Successfully saved debug texture to: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RadarHUD Diagnostics] Failed to save debug texture: {e.Message}");
        }
    }

    // ── Post-processing blit ─────────────────────────────────────────────

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (radarMaterial == null) { Graphics.Blit(src, dest); return; }
        PassDataToShader();
        Graphics.Blit(src, dest, radarMaterial);
    }

    /// Passes parameters and buoy positions to the shader.</summary>
    void PassDataToShader()
    {
        radarMaterial.SetFloat(ID_SweepRPS,    sweepRPS);
        radarMaterial.SetFloat(ID_SweepWidth,  sweepTrailWidth);
        radarMaterial.SetFloat(ID_NumRings,    numRings);
        radarMaterial.SetFloat(ID_RadarSize,   sizeRelativeToScreen);
        radarMaterial.SetFloat(ID_RadarMargin, margin);

        Transform centre = boatTransform != null ? boatTransform : transform;
        float boatYaw = centre.eulerAngles.y * Mathf.Deg2Rad;
        float cosY =  Mathf.Cos(-boatYaw);
        float sinY =  Mathf.Sin(-boatYaw);
        float tileNorm = 1f / gridSize;

        int count = 0;
        for (int i = 0; i < buoyCache.Length && count < maxBuoyBlips; i++)
        {
            if (buoyCache[i] == null) continue;

            Vector3 delta = buoyCache[i].position - centre.position;
            float localX =  cosY * delta.x + sinY * delta.z;
            float localZ = -sinY * delta.x + cosY * delta.z;

            buoyBlips[count]      = new Vector4(
                0.5f + (localX / radarRange) * 0.45f,
                0.5f + (localZ / radarRange) * 0.45f, 0f, 1f);
            buoyAtlasRects[count] = new Vector4(
                (count % gridSize) * tileNorm,
                (count / gridSize) * tileNorm,
                tileNorm, tileNorm);
            buoyBlipSizes[count]  = blipRadarSize;
            count++;
        }

        radarMaterial.SetInt(ID_BuoyCount,          count);
        radarMaterial.SetVectorArray(ID_BuoyBlips,  buoyBlips);
        radarMaterial.SetVectorArray(ID_AtlasRects, buoyAtlasRects);
        radarMaterial.SetFloatArray(ID_BlipSizes,   buoyBlipSizes);

        if (silhouetteAtlas != null)
            radarMaterial.SetTexture(ID_SilhouetteAtlas, silhouetteAtlas);
    }

    // ── Utility ──────────────────────────────────────────────────────────

    Bounds GetCompositeBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    int[] SetLayersRecursive(GameObject go, int layer)
    {
        var all = go.GetComponentsInChildren<Transform>(true);
        var orig = new int[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            orig[i] = all[i].gameObject.layer;
            all[i].gameObject.layer = layer;
        }
        return orig;
    }

    void RestoreLayersRecursive(GameObject go, int[] layers)
    {
        var all = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length && i < layers.Length; i++)
            all[i].gameObject.layer = layers[i];
    }
}
