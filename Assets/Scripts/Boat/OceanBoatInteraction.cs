using UnityEngine;

/// <summary>
/// Attach this to the Boat GameObject.
/// Every frame it pushes the boat's world-space position, forward direction,
/// and horizontal speed into global shader properties read by the Ocean shader.
/// The wake is scaled to zero when the boat is stationary.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class OceanBoatInteraction : MonoBehaviour
{
    [Tooltip("Speed (m/s) at which the wake reaches full strength.")]
    public float fullWakeSpeed = 4f;

    private static readonly int ID_BoatPos   = Shader.PropertyToID("_BoatPosition");
    private static readonly int ID_BoatFwd   = Shader.PropertyToID("_BoatForward");
    private static readonly int ID_BoatSpeed = Shader.PropertyToID("_BoatSpeed");

    private Rigidbody rb;

    void Awake() => rb = GetComponent<Rigidbody>();

    void Update()
    {
        // Horizontal speed only (ignore vertical wave-following movement)
        Vector3 hVel  = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float   speed = hVel.magnitude;

        // Normalise to 0-1 so the shader can simply multiply by it
        float normalizedSpeed = Mathf.Clamp01(speed / Mathf.Max(fullWakeSpeed, 0.01f));

        Shader.SetGlobalVector(ID_BoatPos, transform.position);
        Shader.SetGlobalVector(ID_BoatFwd, transform.forward);
        Shader.SetGlobalFloat (ID_BoatSpeed, normalizedSpeed);
    }
}
