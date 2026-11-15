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
    private HumanBuilding detectedBuilding;
    private HumanBuilding chargingBuilding;
    private float chargeEndTime;

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
        UpdateDetection();
        if (chargingBuilding != null && Time.fixedTime > chargeEndTime)
            chargingBuilding = null;
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
        UpdateDetection();
        if (!IsTouchingWall)
            return false;

        if (detectedBuilding != null)
        {
            chargingBuilding = detectedBuilding;
            chargeEndTime = Time.fixedTime + 3f;
        }

        frontBody.AddForce(launchDirection * launchImpulse, ForceMode.Impulse);
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHitBuilding(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryHitBuilding(other);
    }

    private void TryHitBuilding(Collider other)
    {
        if (chargingBuilding == null || Time.fixedTime > chargeEndTime || other == null)
            return;

        HumanBuilding hitBuilding = other.GetComponentInParent<HumanBuilding>();
        if (hitBuilding != chargingBuilding)
            return;

        hitBuilding.ReleasePiece(other.attachedRigidbody);
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

    private void UpdateDetection()
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
            ScanOverlaps(allOverlaps, allOverlaps.Length);
            return;
        }

        ScanOverlaps(overlapBuffer, count);
    }

    private void ScanOverlaps(Collider[] overlaps, int count)
    {
        HumanBuilding building = null;
        bool wallFound = false;
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (other == null || other == detectorCollider || other.attachedRigidbody == frontBody)
                continue;

            HumanBuilding candidate = other.GetComponentInParent<HumanBuilding>();
            if (candidate != null)
                building = candidate;

            for (Transform current = other.transform; current != null; current = current.parent)
            {
                if (current.CompareTag("wall"))
                {
                    wallFound = true;
                    break;
                }
            }
        }

        detectedBuilding = building;
        IsTouchingWall = wallFound || building != null;
    }
}
