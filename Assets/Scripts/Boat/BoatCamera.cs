using UnityEngine;

/// <summary>
/// Which local axis of the boat model points towards the BOW (front).
/// Change this in the Inspector until the camera sits behind the stern.
/// </summary>
public enum BoatForwardAxis
{
    PositiveX,   //  transform.right   (default – most FBX imports)
    NegativeX,   // -transform.right
    PositiveZ,   //  transform.forward (Unity default forward)
    NegativeZ,   // -transform.forward
}

public class BoatCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Boat Model Axis")]
    [Tooltip("Which local axis of the boat points towards the BOW (front)? " +
             "Try each option until the camera sits BEHIND the stern.")]
    public BoatForwardAxis boatForwardAxis = BoatForwardAxis.PositiveZ;

    [Header("Position")]
    public float distance         = 10f;
    public float height           = 4f;
    public float followSmoothness = 8f;

    // ── helpers ──────────────────────────────────────────────────────────
    Vector3 GetBoatForward()
    {
        switch (boatForwardAxis)
        {
            case BoatForwardAxis.PositiveX:  return  target.right;
            case BoatForwardAxis.NegativeX:  return -target.right;
            case BoatForwardAxis.PositiveZ:  return  target.forward;
            case BoatForwardAxis.NegativeZ:  return -target.forward;
            default:                         return  target.right;
        }
    }

    Vector3 GetDesiredPosition()
    {
        // Camera sits BEHIND the boat: opposite of its forward direction
        Vector3 back   = -GetBoatForward();
        Vector3 offset = back * distance + Vector3.up * height;
        return target.position + offset;
    }

    // ── Unity callbacks ───────────────────────────────────────────────────
    void Start()
    {
        if (target == null) return;
        transform.position = GetDesiredPosition();
        transform.LookAt(target.position + Vector3.up * 1f);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired    = GetDesiredPosition();
        transform.position = Vector3.Lerp(transform.position, desired,
                                          followSmoothness * Time.deltaTime);

        transform.LookAt(target.position + Vector3.up * 1f);
    }
}