using UnityEngine;

/// <summary>
/// Adiciona este script à Main Camera.
/// É ativado/desativado automaticamente pelo CaveZoneTrigger.
///
/// Funcionamento:
///  - Enquanto o barco está FORA da gruta: efeito inativo (effectIntensity = 0).
///  - Ao entrar na triggerbox: effectIntensity sobe suavemente de 0 → 1 (fade-in da escuridão total).
///  - Dentro da gruta: gruta completamente escura exceto um halo de luz à volta do barco.
///    O raio do halo cresce também suavemente (revelação progressiva).
///  - Ao sair: effectIntensity volta a 0 suavemente.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CaveLightingManager : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("O Transform do barco (a 'lanterna' dentro da gruta)")]
    public Transform boatTransform;

    [Header("Configurações da Luz do Barco")]
    [Tooltip("Raio máximo (em píxeis) que a luz do barco ilumina quando totalmente dentro da gruta")]
    public float boatLightRadius = 100f;

    [Tooltip("Suavidade da borda da luz (em píxeis). Evita cortes abruptos.")]
    public float lightSoftness = 50f;

    [Tooltip("Brilho mínimo no interior mesmo fora do halo (0 = totalmente preto, 0.08 = leve luz ambiente)")]
    [Range(0f, 0.5f)]
    public float minBrightness = 0.08f;

    [Header("Transição")]
    [Tooltip("Velocidade com que a escuridão entra / sai ao cruzar a triggerbox")]
    public float fadeSpeed = 1.5f;

    // Controlado pelo CaveZoneTrigger
    [HideInInspector] public bool isInsideCave = false;

    // 0 = fora da gruta (sem efeito), 1 = totalmente dentro (gruta preta + halo de luz)
    private float effectIntensity = 0f;

    private Material effectMaterial;
    private Camera cam;

    // ------------------------------------------------------------------ //

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;

        Shader shader = Shader.Find("Hidden/CaveLighting");
        if (shader != null)
            effectMaterial = new Material(shader);
        else
            Debug.LogError("[CaveLightingManager] Shader 'Hidden/CaveLighting' não encontrado!");

        if (boatTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) boatTransform = p.transform;
        }
    }

    // ------------------------------------------------------------------ //

    void Update()
    {
        // Fade suave: 0 quando fora, 1 quando dentro
        float target = isInsideCave ? 1f : 0f;
        effectIntensity = Mathf.MoveTowards(effectIntensity, target, Time.deltaTime * fadeSpeed);
    }

    // ------------------------------------------------------------------ //

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Se o efeito ainda está completamente inativo, passa a imagem sem alteração
        if (effectMaterial == null || effectIntensity <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // Posição do barco em coordenadas de Viewport (0..1)
        Vector3 vp = cam.WorldToViewportPoint(
            boatTransform != null ? boatTransform.position : transform.position);

        float aspectRatio = (float)Screen.width / Screen.height;

        // Raio em espaço de viewport (normalizado pela altura do ecrã)
        float radiusVP  = boatLightRadius  / Screen.height;
        float softnessVP = lightSoftness   / Screen.height;

        effectMaterial.SetVector("_BoatScreenPos",   new Vector4(vp.x, vp.y, 0, 0));
        effectMaterial.SetFloat ("_LightRadius",     radiusVP);
        effectMaterial.SetFloat ("_LightSoftness",   softnessVP);
        effectMaterial.SetFloat ("_AspectRatio",     aspectRatio);
        effectMaterial.SetFloat ("_EffectIntensity", effectIntensity);
        effectMaterial.SetFloat ("_Darkness",        0.92f); // escuridão máxima (~92%), nunca 100% preto
        effectMaterial.SetFloat ("_MinBrightness",   minBrightness);

        Graphics.Blit(source, destination, effectMaterial);
    }
}
