using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class BoatController : MonoBehaviour
{
    [Header("Movement")]
    public float forwardSpeed  = 10f;
    public float reverseSpeed  = 5f;
    public float turnSpeed     = 2f;

    [Header("Water Following")]
    [Tooltip("Height offset above the wave surface")]
    public float heightOffset   = 0f;
    [Tooltip("How smoothly the boat tracks the wave height (higher = snappier)")]
    public float heightSmooth   = 6f;
    [Tooltip("How smoothly the boat tilts with the wave normal")]
    public float tiltSmooth     = 4f;
    [Tooltip("Distance from centre to bow/stern probe points (use ~half the boat length)")]
    public float bowSternProbe  = 3f;
    [Tooltip("Distance from centre to port/starboard probe points (use ~half the beam)")]
    public float beamProbe      = 1.5f;

    [Header("Hull Waterline Points")]
    [Tooltip("Exactly 3 child Transforms placed on the hull at the desired waterline. " +
             "The water surface will never rise above any of these points.")]
    public Transform[] hullPoints = new Transform[3];

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity      = false;
        rb.constraints     = RigidbodyConstraints.None;
        rb.linearDamping   = 1.5f;
        rb.angularDamping  = 3f;
    }

    void FixedUpdate()
    {
        // ── 1. Input ────────────────────────────────────────────────
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

        // ── 2. Propulsion ───────────────────────────────────────────
        // transform.forward is the model's bow direction
        float speed = fwd > 0 ? forwardSpeed : reverseSpeed;
        rb.AddForce(transform.forward * fwd * speed, ForceMode.Acceleration);

        // ── 3. Steering ─────────────────────────────────────────────
        float dirMult = fwd < 0 ? -1f : 1f;
        rb.AddTorque(transform.up * turn * turnSpeed * dirMult, ForceMode.Acceleration);

        // ── 3b. Pitch damping (prevent nose-down when moving forward) ──
        // Cancel angular velocity on the local X axis (pitch)
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);
        localAngVel.x = 0f;   // zero out pitch
        rb.angularVelocity = transform.TransformDirection(localAngVel);

        // ── 4. Lateral drag (prevent ice-skating) ───────────────────
        Vector3 sideDir     = transform.right;   // beam axis (port/starboard)
        float   sideVel     = Vector3.Dot(rb.linearVelocity, sideDir);
        rb.AddForce(-sideDir * sideVel * 5f, ForceMode.Acceleration);

        // ── 5. Wave following (multi-point for long boats) ─────────────
        if (WaveManager.Instance == null) return;

        Vector3 pos = rb.position;

        // Boat axes: transform.forward = bow, transform.right = starboard
        Vector3 bowDir   = transform.forward;
        Vector3 sideDir2 = transform.right;

        // Sample the wave at 4 points: bow, stern, port, starboard
        Vector3 bowPos   = pos + bowDir   *  bowSternProbe;
        Vector3 sternPos = pos + bowDir   * -bowSternProbe;
        Vector3 portPos  = pos + sideDir2 * -beamProbe;
        Vector3 starboard= pos + sideDir2 *  beamProbe;

        float centreY    = WaveManager.Instance.GetWaveHeight(pos);
        float bowY       = WaveManager.Instance.GetWaveHeight(bowPos);
        float sternY     = WaveManager.Instance.GetWaveHeight(sternPos);
        float portY      = WaveManager.Instance.GetWaveHeight(portPos);
        float starboardY = WaveManager.Instance.GetWaveHeight(starboard);

        // Average height from all 5 samples (centre + 4 corners)
        float avgY = (centreY + bowY + sternY + portY + starboardY) / 5f;

        // Build wave normal from bow/stern/port/starboard world-space points
        Vector3 pBow  = new Vector3(bowPos.x,   bowY,       bowPos.z);
        Vector3 pStern= new Vector3(sternPos.x,  sternY,    sternPos.z);
        Vector3 pPort = new Vector3(portPos.x,   portY,     portPos.z);
        Vector3 pStar = new Vector3(starboard.x, starboardY,starboard.z);

        // Bow-to-stern vector and port-to-starboard vector define the surface plane
        Vector3 longAxis = (pBow   - pStern).normalized;
        Vector3 sideAxis = (pStar  - pPort ).normalized;
        Vector3 waveNormal = Vector3.Cross(sideAxis, longAxis).normalized;
        if (waveNormal.y < 0) waveNormal = -waveNormal;

        // Smooth Y position towards average wave surface
        float targetY = avgY + heightOffset;

        // ── 6. Hull waterline constraint ─────────────────────────────
        // For each hull point, compute the minimum boat-pivot Y that would
        // keep that hull point exactly at the wave surface (never below).
        // We take the highest such requirement across all points as the floor.
        foreach (Transform hp in hullPoints)
        {
            if (hp == null) continue;

            // World-space wave height at this hull point's XZ position
            float waveAtPoint = WaveManager.Instance.GetWaveHeight(hp.position);

            // Current vertical offset of this hull point relative to the pivot.
            // Moving the pivot up/down by ΔY moves the hull point by the same ΔY.
            float hullOffsetY = hp.position.y - pos.y;

            // Minimum pivot Y so the hull point is AT the water surface (not below)
            float minPivotY = waveAtPoint - hullOffsetY;

            targetY = Mathf.Max(targetY, minPivotY);
        }

        float smoothedY = Mathf.Lerp(pos.y, targetY, Time.fixedDeltaTime * heightSmooth);

        // Use rb.position (not cached pos) so XZ movement from AddForce is preserved.
        // We only override Y — the boat moves freely on the horizontal plane.
        Vector3 currentPos = rb.position;
        rb.MovePosition(new Vector3(currentPos.x, smoothedY, currentPos.z));

        // Zero out vertical velocity so physics doesn't fight the smoothing
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Smooth tilt to align with wave normal
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, waveNormal) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * tiltSmooth));
    }
}