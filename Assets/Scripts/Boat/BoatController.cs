using UnityEngine;
using UnityEngine.InputSystem;


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

        // ── Target Y (averaged wave surface) ──────────────────────────────
        float targetY = avgY;

        // ── Hull waterline constraint ─────────────────────────────────────
        foreach (Transform hp in hullPoints)
        {
            if (hp == null) continue;
            // Use world XZ of the hull point but sample independently
            float waveAtHP  = _ocean.GetWaveHeight(hp.position.x, hp.position.z);
            // World Y offset of this hull point relative to the pivot (accounts for scale and rotation)
            float worldOffY = hp.position.y - pos.y;
            // Minimum world Y for the pivot so the hull point ends up AT the surface
            float minPivot  = waveAtHP - worldOffY;
            if (minPivot > targetY) targetY = minPivot;
        }

        // ── Position Y ───────────────────────────────────────────────────
        Vector3 currentPos = _rb.position;
        _rb.MovePosition(new Vector3(currentPos.x, targetY, currentPos.z));

        // Kill vertical velocity so physics doesn't fight the smoothing
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);

        // ── Smooth tilt to align with wave normal ─────────────────────────
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, waveNormal) * _rb.rotation;
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * tiltSmooth));
    }

    // -----------------------------------------------------------------------
    // Public helpers
    // -----------------------------------------------------------------------

    ///Horizontal speed in m/s.
    public float HorizontalSpeed
        => new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;

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