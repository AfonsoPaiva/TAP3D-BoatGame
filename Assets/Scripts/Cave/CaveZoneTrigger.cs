using UnityEngine;

/// <summary>
/// Coloca este script num GameObject vazio dentro da gruta.
/// Adiciona um Box Collider ao mesmo objeto (o tamanho define a zona da gruta).
/// Nao precisa de Rigidbody no barco nem de tags especificas.
/// </summary>
public class CaveZoneTrigger : MonoBehaviour
{
    [Tooltip("O barco / player. Se vazio, tenta encontrar por tag 'Player'.")]
    public Transform boatTransform;

    [Tooltip("Layer mask dos colisores a detetar (deixa 'Everything' para simplicidade).")]
    public LayerMask detectionLayers = ~0; // Everything

    private CaveLightingManager caveManager;
    // CaveFogManager não precisa de ser notificado — a névoa está sempre activa
    private BoxCollider zone;

    void Start()
    {
        // Garante box collider
        zone = GetComponent<BoxCollider>();
        if (zone == null)
        {
            zone = gameObject.AddComponent<BoxCollider>();
            zone.size = new Vector3(50, 30, 80);
            Debug.Log("[CaveZone] Box Collider criado automaticamente. Ajusta o tamanho no Inspector.");
        }

        // Tenta encontrar o barco por tag
        if (boatTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
                boatTransform = p.transform;
        }

        // Adiciona CaveLightingManager à Main Camera se nao existir
        if (Camera.main != null)
        {
            caveManager = Camera.main.GetComponent<CaveLightingManager>();
            if (caveManager == null)
                caveManager = Camera.main.gameObject.AddComponent<CaveLightingManager>();
        }

        // Nota: O CaveFogManager é adicionado à câmara automaticamente pelo CaveZoneTrigger
        // mas não precisa de receber o estado isInsideCave — a névoa está sempre activa.
    }

    void Update()
    {
        if (caveManager == null || boatTransform == null || zone == null) return;

        // Verifica se o barco está dentro da caixa em World Space (sem depender de fisica nem Rigidbody)
        bool inside = IsInsideBox(boatTransform.position);

        if (inside != caveManager.isInsideCave)
            caveManager.isInsideCave = inside;
        // A névoa (CaveFogManager) está sempre activa — não precisa de sincronização
    }

    bool IsInsideBox(Vector3 worldPoint)
    {
        // Converte o ponto para espaco local do trigger (respeitando rotacao e escala)
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 halfSize   = zone.size * 0.5f;
        Vector3 center     = zone.center;

        return (localPoint.x > center.x - halfSize.x && localPoint.x < center.x + halfSize.x) &&
               (localPoint.y > center.y - halfSize.y && localPoint.y < center.y + halfSize.y) &&
               (localPoint.z > center.z - halfSize.z && localPoint.z < center.z + halfSize.z);
    }

    // Desenha a zona no Editor para facilitar o ajuste do tamanho
    void OnDrawGizmos()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.DrawCube(bc.center, bc.size);
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(bc.center, bc.size);
    }
}
