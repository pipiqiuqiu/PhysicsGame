using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class WallDetectorLaunch : MonoBehaviour
{
    [SerializeField] private Rigidbody frontBody;
    [SerializeField] private Rigidbody rearBody;
    [Min(0f)] [SerializeField] private float launchImpulse = 20f;

    private BoxCollider detectorCollider;
    private Vector3 initialDirection;
    private Vector3 launchDirection;
    private Quaternion initialRotation;
    private float distanceFromFront;
    private readonly Collider[] overlapBuffer = new Collider[32];

    public bool IsTouchingWall { get; private set; }

    private void Awake()
    {
        detectorCollider = GetComponent<BoxCollider>();
        if (frontBody == null)
            frontBody = GetComponentInParent<Rigidbody>();
        if (frontBody == null || rearBody == null || detectorCollider == null)
        {
            Debug.LogError("WallDetectorLaunch needs the front body, rear body, and a BoxCollider.", this);
            enabled = false;
            return;
        }

        Vector3 offset = Vector3.ProjectOnPlane(transform.position - frontBody.position,
                                                Vector3.right);
        distanceFromFront = offset.magnitude;
        initialDirection = distanceFromFront > 0.001f ? offset / distanceFromFront : Vector3.back;
        launchDirection = initialDirection;
        initialRotation = transform.rotation;
        UpdatePose();
    }

    private void FixedUpdate()
    {
        UpdatePose();
        IsTouchingWall = HasWallOverlap();
    }

    private void LateUpdate()
    {
        UpdatePose();
    }

    public bool TryLaunch()
    {
        if (!isActiveAndEnabled || launchImpulse <= 0f || !detectorCollider.enabled)
            return false;

        UpdatePose();
        IsTouchingWall = HasWallOverlap();
        if (!IsTouchingWall)
            return false;

        frontBody.AddForce(launchDirection * launchImpulse, ForceMode.Impulse);
        return true;
    }

    private void UpdatePose()
    {
        Vector3 direction = Vector3.ProjectOnPlane(frontBody.position - rearBody.position,
                                                   Vector3.right);
        if (direction.sqrMagnitude > 0.000001f)
            launchDirection = direction.normalized;

        float angle = Vector3.SignedAngle(initialDirection, launchDirection, Vector3.right);
        transform.SetPositionAndRotation(frontBody.position + launchDirection * distanceFromFront,
                                         Quaternion.AngleAxis(angle, Vector3.right) * initialRotation);
    }

    private bool HasWallOverlap()
    {
        Vector3 scale = transform.lossyScale;
        Vector3 size = detectorCollider.size;
        Vector3 halfExtents = new Vector3(Mathf.Abs(size.x * scale.x),
                                          Mathf.Abs(size.y * scale.y),
                                          Mathf.Abs(size.z * scale.z)) * 0.5f;
        Vector3 center = transform.TransformPoint(detectorCollider.center);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer,
                                               transform.rotation, ~0,
                                               QueryTriggerInteraction.Collide);
        if (count == overlapBuffer.Length)
        {
            Collider[] allOverlaps = Physics.OverlapBox(center, halfExtents, transform.rotation,
                                                        ~0, QueryTriggerInteraction.Collide);
            return ContainsWall(allOverlaps, allOverlaps.Length);
        }

        return ContainsWall(overlapBuffer, count);
    }

    private bool ContainsWall(Collider[] overlaps, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (other == null || other == detectorCollider || other.attachedRigidbody == frontBody)
                continue;

            for (Transform current = other.transform; current != null; current = current.parent)
            {
                if (current.CompareTag("wall"))
                    return true;
            }
        }
        return false;
    }
}
