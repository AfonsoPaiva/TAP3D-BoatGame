using UnityEngine;

/// <summary>
/// Adiciona este script à Main Camera (ao lado do CaveLightingManager).
/// Calcula a AABB mundial do BoxCollider da gruta e passa-a ao shader
/// para que este faça interseção raio-volume e acumule névoa só no interior.
/// A névoa é sempre visível (de fora e de dentro) — sem depender de isInsideCave.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CaveFogManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────

    [Header("Referências")]
    [Tooltip("O barco. Se vazio, encontra por tag 'Player'.")]
    public Transform boatTransform;

    [Tooltip("GameObject com CaveZoneTrigger + BoxCollider. Encontrado automaticamente se vazio.")]
    public CaveZoneTrigger caveZone;

    [Header("Névoa")]
    public Color fogColor = new Color(0.04f, 0.07f, 0.14f, 1f);

    [Range(0f, 0.3f)]
    [Tooltip("Densidade por metro: 0.04 subtil, 0.12 denso, 0.25 muito denso.")]
    public float fogDensity = 0.06f;

    [Header("Reveal")]
    [Tooltip("Raio (metros) em torno do barco onde a névoa se dissipa.")]
    public float clearRadius = 10f;

    [Header("Animação")]
    public float scrollSpeed = 0.03f;
    public float noiseScale  = 0.08f;

    // ─── Privados ─────────────────────────────────────────────────────────
    private Material    fogMaterial;
    private Camera      cam;
    private BoxCollider caveBox;
    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;

        Shader s = Shader.Find("Hidden/CaveFog");
        if (s != null)
            fogMaterial = new Material(s);
        else
            Debug.LogError("[CaveFog] Shader 'Hidden/CaveFog' não encontrado!", this);

        if (boatTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) boatTransform = p.transform;
        }
    }

    void Start()
    {
        if (caveZone == null)
            caveZone = FindObjectOfType<CaveZoneTrigger>();

        if (caveZone != null)
            caveBox = caveZone.GetComponent<BoxCollider>();

        if (caveBox == null)
            Debug.LogWarning("[CaveFog] Não encontrei BoxCollider na CaveZoneTrigger.", this);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (fogMaterial == null || caveBox == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // ── Matriz inversa VP ──────────────────────────────────────────
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
        fogMaterial.SetMatrix("_InverseVP", (proj * cam.worldToCameraMatrix).inverse);

        // ── Volume da gruta (Local Space) ──────────────────────────────
        fogMaterial.SetMatrix("_CaveWorldToLocal", caveZone.transform.worldToLocalMatrix);
        
        Vector3 c = caveBox.center;
        Vector3 hs = caveBox.size * 0.5f;
        fogMaterial.SetVector("_CaveLocalMin", c - hs);
        fogMaterial.SetVector("_CaveLocalMax", c + hs);

        // ── Névoa ──────────────────────────────────────────────────────
        fogMaterial.SetColor ("_FogColor",    fogColor);
        fogMaterial.SetFloat ("_FogDensity",  fogDensity);
        fogMaterial.SetFloat ("_ClearRadius", clearRadius);
        fogMaterial.SetFloat ("_ScrollSpeed", scrollSpeed);
        fogMaterial.SetFloat ("_NoiseScale",  noiseScale);

        // ── Barco ──────────────────────────────────────────────────────
        Vector3 bp = boatTransform != null ? boatTransform.position : transform.position;
        fogMaterial.SetVector("_BoatWorldPos", new Vector4(bp.x, bp.y, bp.z, 1f));

        Graphics.Blit(source, destination, fogMaterial);
    }

    void OnDestroy()
    {
        if (fogMaterial != null) Destroy(fogMaterial);
    }
}
