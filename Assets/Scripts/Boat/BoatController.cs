using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physics-based boat controller.
/// Queries OceanWaveManager for the CPU-side Gerstner surface height so that
/// buoyancy and alignment match the GPU vertex displacement exactly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BoatController : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------
    [Header("Movement")]
    public float forwardSpeed  = 10f;
    public float reverseSpeed  = 5f;
    public float turnSpeed     = 2f;

    [Header("Water Following")]
    [Tooltip("Height offset above the wave surface. Negative = partially submerged.")]
    public float heightOffset  = 0f;
    [Tooltip("How quickly the boat snaps to the wave height. 25 = very responsive.")]
    public float heightSmooth  = 25f;
    [Tooltip("How smoothly the boat tilts with the wave normal.")]
    public float tiltSmooth    = 8f;
    [Tooltip("Distance from centre to bow/stern probe points (~half boat length).")]
    public float bowSternProbe = 3f;
    [Tooltip("Distance from centre to port/starboard probe points (~half beam).")]
    public float beamProbe     = 1.5f;

    [Header("Hull Waterline Points")]
    [Tooltip("Exactly 3 child Transforms placed on the hull at the desired waterline. " +
             "The water surface will never rise above any of these points.")]
    public Transform[] hullPoints = new Transform[3];

    // -----------------------------------------------------------------------
    // Private
    // -----------------------------------------------------------------------
    private Rigidbody        _rb;
    private OceanWaveManager _ocean;   // cached reference — set in Start()

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------
    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity     = false;
        _rb.constraints    = RigidbodyConstraints.None;
        _rb.linearDamping  = 1.5f;
        _rb.angularDamping = 3f;
    }

    void Start()
    {
        // Try singleton first, then scene search, then log a clear error.
        _ocean = OceanWaveManager.Instance
              ?? FindFirstObjectByType<OceanWaveManager>();

        if (_ocean == null)
            Debug.LogError(
                "[BoatController] No OceanWaveManager found in the scene! "
              + "Create an Ocean GameObject, add MeshRenderer + OceanWaveManager, "
              + "and assign the Ocean material.", this);
    }

    void FixedUpdate()
    {
        HandleInput();
        FollowWaveSurface();
    }

    // -----------------------------------------------------------------------
    // Input + propulsion
    // -----------------------------------------------------------------------
    void HandleInput()
    {
        var kb = Keyboard.current;
        float fwd  = 0f;
        float turn = 0f;

        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    fwd  =  1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  fwd  = -1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn =  1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  turn = -1f;
        }

        float speed = fwd > 0 ? forwardSpeed : reverseSpeed;
        _rb.AddForce(transform.forward * fwd * speed, ForceMode.Acceleration);

        float dirMult = fwd < 0 ? -1f : 1f;
        _rb.AddTorque(transform.up * turn * turnSpeed * dirMult, ForceMode.Acceleration);

        // Pitch damping — cancel local-X angular velocity to prevent nose-diving
        Vector3 localAngVel = transform.InverseTransformDirection(_rb.angularVelocity);
        localAngVel.x = 0f;
        _rb.angularVelocity = transform.TransformDirection(localAngVel);

        // Lateral drag — prevent ice-skating sideways
        float sideVel = Vector3.Dot(_rb.linearVelocity, transform.right);
        _rb.AddForce(-transform.right * sideVel * 5f, ForceMode.Acceleration);
    }

    // -----------------------------------------------------------------------
    // Wave surface following
    // -----------------------------------------------------------------------
    void FollowWaveSurface()
    {
        if (_ocean == null) return;

        Vector3 pos = _rb.position;

        // ── Sample wave height at 5 points (centre + bow/stern/port/starboard) ──
        Vector3 bowPos    = pos + transform.forward *  bowSternProbe;
        Vector3 sternPos  = pos + transform.forward * -bowSternProbe;
        Vector3 portPos   = pos - transform.right   *  beamProbe;
        Vector3 starboard = pos + transform.right   *  beamProbe;

        float centreY    = _ocean.GetWaveHeight(pos);
        float bowY       = _ocean.GetWaveHeight(bowPos);
        float sternY     = _ocean.GetWaveHeight(sternPos);
        float portY      = _ocean.GetWaveHeight(portPos);
        float starboardY = _ocean.GetWaveHeight(starboard);

        float avgY = (centreY + bowY + sternY + portY + starboardY) / 5f;

        // ── Build wave surface normal from the 4 probe world positions ──────
        Vector3 pBow   = new(bowPos.x,    bowY,        bowPos.z);
        Vector3 pStern = new(sternPos.x,  sternY,      sternPos.z);
        Vector3 pPort  = new(portPos.x,   portY,       portPos.z);
        Vector3 pStar  = new(starboard.x, starboardY,  starboard.z);

        Vector3 longAxis   = (pBow   - pStern).normalized;
        Vector3 sideAxis   = (pStar  - pPort ).normalized;
        Vector3 waveNormal = Vector3.Cross(sideAxis, longAxis).normalized;
        if (waveNormal.y < 0) waveNormal = -waveNormal;

        // ── Target Y (averaged wave surface + offset) ─────────────────────
        float targetY = avgY + heightOffset;

        // ── Hull waterline constraint ─────────────────────────────────────
        // For each hull probe: find the minimum pivot Y that keeps it AT or
        // above the wave surface. Only raises the floor — never the ceiling.
        // NOTE: only add hullPoints if they mark the actual waterline of the
        // mesh; leave the array empty to skip this constraint entirely.
        foreach (Transform hp in hullPoints)
        {
            if (hp == null) continue;
            // Use world XZ of the hull point but sample independently
            float waveAtHP  = _ocean.GetWaveHeight(hp.position.x, hp.position.z);
            // Local Y offset of this hull point relative to the pivot (fixed in local space)
            float localOffY = transform.InverseTransformPoint(hp.position).y;
            // Minimum world Y for the pivot so localOffY ends up AT the surface
            float minPivot  = waveAtHP - localOffY;
            if (minPivot > targetY) targetY = minPivot;
        }

        // ── Smooth Y ─────────────────────────────────────────────────────
        float smoothedY = Mathf.Lerp(pos.y, targetY, Time.fixedDeltaTime * heightSmooth);

        Vector3 currentPos = _rb.position;
        _rb.MovePosition(new Vector3(currentPos.x, smoothedY, currentPos.z));

        // Kill vertical velocity so physics doesn't fight the smoothing
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);

        // ── Smooth tilt to align with wave normal ─────────────────────────
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, waveNormal) * _rb.rotation;
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * tiltSmooth));
    }

    // -----------------------------------------------------------------------
    // Public helpers
    // -----------------------------------------------------------------------

    /// <summary>Horizontal speed in m/s.</summary>
    public float HorizontalSpeed
        => new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;

    /// <summary>Externally trigger a wave-impact impulse (explosion, large wake, etc.).</summary>
    public void ApplyWaveImpact(Vector3 worldPoint, float forceMagnitude)
        => _rb.AddForceAtPosition(Vector3.up * forceMagnitude, worldPoint, ForceMode.Impulse);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (hullPoints != null)
            foreach (var hp in hullPoints)
                if (hp != null) Gizmos.DrawWireSphere(hp.position, 0.15f);

        Gizmos.color = Color.yellow;
        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p + transform.forward  *  bowSternProbe, 0.15f);
        Gizmos.DrawWireSphere(p + transform.forward  * -bowSternProbe, 0.15f);
        Gizmos.DrawWireSphere(p - transform.right    *  beamProbe,     0.15f);
        Gizmos.DrawWireSphere(p + transform.right    *  beamProbe,     0.15f);
    }
#endif
}