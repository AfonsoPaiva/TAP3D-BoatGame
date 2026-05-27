using UnityEngine;

public class CaveInteriorReveal : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Renderer da mesh INTERIOR da gruta (usa Custom/CaveInterior).")]
    public Renderer interiorRenderer;

    [Tooltip("Transform do barco. Detectado automaticamente por tag 'Player' se vazio.")]
    public Transform boatTransform;

    [Tooltip("CaveLightingManager na Main Camera. Auto-detectado se vazio.")]
    public CaveLightingManager caveManager;

    [Header("Reveal por Proximidade (em unidades mundo)")]
    [Tooltip("Raio a partir do barco onde a textura fica totalmente visivel.")]
    public float revealRadius = 20f;

    [Tooltip("Zona de transicao (falloff) alem do raio — suaviza a borda.")]
    public float revealFalloff = 12f;

    [Header("Transicao Global")]
    [Tooltip("Velocidade do fade-in/out ao entrar/sair da gruta.")]
    public float fadeSpeed = 1.5f;

    // ------------------------------------------------------------------ //
    // Internos

    // Intensidade global [0..1] — 0 fora da gruta, 1 dentro
    private float revealIntensity = 0f;

    // Cache dos IDs dos shader properties
    private static readonly int BoatWorldPosID    = Shader.PropertyToID("_BoatWorldPos");
    private static readonly int RevealRadiusID    = Shader.PropertyToID("_RevealRadius");
    private static readonly int RevealFalloffID   = Shader.PropertyToID("_RevealFalloff");
    private static readonly int RevealIntensityID = Shader.PropertyToID("_RevealIntensity");

    // Instancia privada do material (evita modificar o asset partilhado)
    private Material interiorMat;

    // ------------------------------------------------------------------ //

    void Start()
    {
        // Localizar CaveLightingManager
        if (caveManager == null && Camera.main != null)
            caveManager = Camera.main.GetComponent<CaveLightingManager>();

        if (caveManager == null)
            Debug.LogWarning("[CaveInteriorReveal] CaveLightingManager nao encontrado. " +
                             "O fade global nao funcionara.");

        // Localizar barco
        if (boatTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) boatTransform = player.transform;
        }

        if (boatTransform == null)
            Debug.LogError("[CaveInteriorReveal] Barco nao encontrado! " +
                           "Arrasta o Transform do barco para o campo 'Boat Transform'.");

        // Localizar renderer
        if (interiorRenderer == null)
            interiorRenderer = GetComponent<Renderer>();

        if (interiorRenderer == null)
        {
            Debug.LogError("[CaveInteriorReveal] Nenhum Renderer encontrado! " +
                           "Arrasta a mesh interior para o campo 'Interior Renderer'.");
            return;
        }

        // Criar instancia privada do material
        interiorMat = interiorRenderer.material; // .material cria instancia automaticamente

        // Estado inicial: escuro, raio configurado
        interiorMat.SetFloat(RevealIntensityID, 0f);
        interiorMat.SetFloat(RevealRadiusID,    revealRadius);
        interiorMat.SetFloat(RevealFalloffID,   revealFalloff);
    }

    // ------------------------------------------------------------------ //

    void Update()
    {
        if (interiorMat == null || boatTransform == null) return;

        // Fade global: 0 fora da gruta, 1 dentro
        bool inside = caveManager != null && caveManager.isInsideCave;
        float target = inside ? 1f : 0f;
        revealIntensity = Mathf.MoveTowards(revealIntensity, target,
                                            Time.deltaTime * fadeSpeed);

        // Atualizar posicao do barco no shader (cada frame)
        interiorMat.SetVector(BoatWorldPosID,    boatTransform.position);
        interiorMat.SetFloat (RevealIntensityID, revealIntensity);

        // Permite ajustar o raio em runtime via Inspector
        interiorMat.SetFloat(RevealRadiusID,  revealRadius);
        interiorMat.SetFloat(RevealFalloffID, revealFalloff);
    }

    // ------------------------------------------------------------------ //

    void OnDisable()
    {
        // Ao desativar o script, escurece o interior
        if (interiorMat != null)
            interiorMat.SetFloat(RevealIntensityID, 0f);
    }

    // Gizmo para visualizar o raio de reveal no Editor
    void OnDrawGizmosSelected()
    {
        if (boatTransform == null) return;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(boatTransform.position, revealRadius);
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.2f);
        Gizmos.DrawWireSphere(boatTransform.position, revealRadius + revealFalloff);
    }
}
