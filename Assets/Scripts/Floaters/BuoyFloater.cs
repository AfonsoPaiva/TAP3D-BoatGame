using UnityEngine;

/// <summary>
/// Attach to any buoy GameObject.
///
/// Setup:
///   1. Add this component to the buoy prefab/GameObject.
///   2. In Unity's Tag Manager (Edit → Project Settings → Tags & Layers),
///      create a tag called "Buoy" and assign it to the buoy GameObject.
///   3. Optionally assign a HeadTransform for the top of the buoy if you
///      want it to tilt realistically with the waves.
///
/// Behaviour:
///   • The buoy stays at its XZ spawn position (no horizontal movement).
///   • It bobs vertically with the wave surface.
///   • It tilts (roll + pitch) to match the wave normal for realism.
///   • It is detected by the radar via the "Buoy" tag.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BuoyFloater : MonoBehaviour
{
    [Header("Wave Following")]
    [Tooltip("Vertical offset above the wave surface (negative = partially submerged).")]
    public float heightOffset  = 0f;

    [Tooltip("How snappily the buoy tracks the wave height. Higher = snappier.")]
    public float heightSmooth  = 8f;

    [Tooltip("How snappily the buoy tilts with the wave normal.")]
    public float tiltSmooth    = 5f;

    [Tooltip("Radius used to sample neighbouring wave points for the surface normal.")]
    public float probeRadius   = 0.4f;

    // Fixed XZ spawn position – never changes
    private float spawnX;
    private float spawnZ;

    private Rigidbody rb;

    // ── Unity callbacks ───────────────────────────────────────────────────

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Buoys are kinematic – waves drive them, not physics
        rb.isKinematic  = true;
        rb.useGravity   = false;

        // Lock the XZ position forever
        spawnX = transform.position.x;
        spawnZ = transform.position.z;
    }

    void FixedUpdate()
    {
        if (WaveManager.Instance == null) return;

        Vector3 pos = rb.position;

        // Always clamp back to the spawn XZ so the buoy never drifts
        pos.x = spawnX;
        pos.z = spawnZ;

        // ── Wave height & normal ──────────────────────────────────────────
        // Sample centre + 4 cardinal neighbours to get a stable surface normal
        float centreY = WaveManager.Instance.GetWaveHeight(pos);
        float northY  = WaveManager.Instance.GetWaveHeight(pos + Vector3.forward * probeRadius);
        float southY  = WaveManager.Instance.GetWaveHeight(pos - Vector3.forward * probeRadius);
        float eastY   = WaveManager.Instance.GetWaveHeight(pos + Vector3.right   * probeRadius);
        float westY   = WaveManager.Instance.GetWaveHeight(pos - Vector3.right   * probeRadius);

        // Build the surface plane from neighbour points
        Vector3 pC = new Vector3(pos.x,                  centreY, pos.z);
        Vector3 pN = new Vector3(pos.x,                  northY,  pos.z + probeRadius);
        Vector3 pE = new Vector3(pos.x + probeRadius,    eastY,   pos.z);

        Vector3 waveNormal = Vector3.Cross((pN - pC).normalized, (pE - pC).normalized).normalized;
        if (waveNormal.y < 0f) waveNormal = -waveNormal;

        // ── Height (Y) ────────────────────────────────────────────────────
        float targetY   = centreY + heightOffset;
        float smoothedY = Mathf.Lerp(pos.y, targetY, Time.fixedDeltaTime * heightSmooth);
        rb.MovePosition(new Vector3(spawnX, smoothedY, spawnZ));

        // ── Tilt to match wave surface ────────────────────────────────────
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, waveNormal) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * tiltSmooth));
    }
}
