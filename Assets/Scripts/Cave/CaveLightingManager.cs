using UnityEngine;

/// <summary>
/// Adiciona este script à Main Camera.
/// É ativado/desativado automaticamente pelo CaveZoneTrigger.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CaveLightingManager : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("O Transform do barco (a 'lanterna' dentro da gruta)")]
    public Transform boatTransform;

    [Header("Configurações da Gruta")]
    [Range(0f, 1f)]
    [Tooltip("O quão escuro fica dentro da gruta (0 = totalmente preto)")]
    public float caveDarkness = 0.02f;

    [Header("Configurações da Luz do Barco")]
    [Tooltip("Raio em unidades mundiais que a luz do barco ilumina na tela")]
    public float boatLightRadius = 80f;

    [Header("Transição")]
    [Tooltip("Velocidade com que a escuridão entra/sai")]
    public float fadeSpeed = 2.0f;

    // Controlado pelo CaveZoneTrigger
    [HideInInspector] public bool isInsideCave = false;

    // Intensidade do efeito: 0 = sem efeito, 1 = totalmente ativo
    private float effectIntensity = 0f;
    private Material effectMaterial;
    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;

        Shader shader = Shader.Find("Hidden/CaveLighting");
        if (shader != null)
            effectMaterial = new Material(shader);

        if (boatTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) boatTransform = p.transform;
        }
    }

    void Update()
    {
        // Fade suave de entrada e saída
        float target = isInsideCave ? 1f : 0f;
        effectIntensity = Mathf.MoveTowards(effectIntensity, target, Time.deltaTime * fadeSpeed);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Se o efeito estiver completamente inativo, passa a imagem sem alteração
        if (effectMaterial == null || effectIntensity <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // Posição do barco na tela (coordenadas Viewport 0..1)
        Vector3 vp = cam.WorldToViewportPoint(boatTransform != null ? boatTransform.position : transform.position);
        float aspectRatio = (float)Screen.width / Screen.height;

        effectMaterial.SetVector("_BoatScreenPos", new Vector4(vp.x, vp.y, 0, 0));
        // Raio em espaço de viewport corrigido pelo aspect
        effectMaterial.SetFloat("_LightRadius", (boatLightRadius / Screen.height) * effectIntensity);
        // Escuridão escalada pelo intensity para o fade funcionar
        effectMaterial.SetFloat("_Darkness", (1f - caveDarkness) * effectIntensity);
        effectMaterial.SetFloat("_AspectRatio", aspectRatio);

        Graphics.Blit(source, destination, effectMaterial);
    }
}
