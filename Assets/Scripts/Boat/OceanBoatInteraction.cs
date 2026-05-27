using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class OceanBoatInteraction : MonoBehaviour
{
    [Tooltip("Speed (m/s) at which the wake reaches full strength.")]
    public float fullWakeSpeed = 4f;

    private static readonly int ID_BoatPos   = Shader.PropertyToID("_BoatPosition");
    private static readonly int ID_BoatFwd   = Shader.PropertyToID("_BoatForward");
    private static readonly int ID_BoatSpeed = Shader.PropertyToID("_BoatSpeed");

    private Rigidbody rb;
    private Vector3   lastPosition;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        lastPosition = transform.position;
    }

    void Update()
    {
        // Calculate velocity from position delta over time (robust fallback for MovePosition)
        Vector3 calculatedVelocity = Time.deltaTime > 0f ? (transform.position - lastPosition) / Time.deltaTime : Vector3.zero;
        lastPosition = transform.position;

        // Use Rigidbody velocity if it has value, otherwise fallback to calculated velocity
        Vector3 vel = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity : calculatedVelocity;

        // Horizontal velocity only
        Vector3 hVel = new Vector3(vel.x, 0f, vel.z);

        // Project velocity onto forward vector to detect if we are moving forward
        float forwardSpeed = Vector3.Dot(hVel, transform.forward);

        // Only register speed if moving forward, ignore reversing or sideways drift
        float speed = Mathf.Max(0f, forwardSpeed);

        // Normalise to 0-1 so the shader can simply multiply by it
        float normalizedSpeed = Mathf.Clamp01(speed / Mathf.Max(fullWakeSpeed, 0.01f));

        Shader.SetGlobalVector(ID_BoatPos, transform.position);
        Shader.SetGlobalVector(ID_BoatFwd, transform.forward);
        Shader.SetGlobalFloat (ID_BoatSpeed, normalizedSpeed);
    }
}
