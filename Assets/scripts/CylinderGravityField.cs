using UnityEngine;

/// <summary>Applies gravity toward the center axis of the scene's ground cylinder.</summary>
public sealed class CylinderGravityField : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float gravityAcceleration = 9.81f;

    private Transform cylinder;
    private Collider groundCollider;
    private Collider[] floorColliders;
    private Vector3 localCenter;
    private float localRadius;
    private Rigidbody[] bodies;
    private float nextRefreshTime;

    public float GravityAcceleration => gravityAcceleration;
    public Collider GroundCollider => groundCollider;
    public Vector3 Axis => cylinder.TransformDirection(Vector3.right).normalized;
    public Vector3 Center => cylinder.TransformPoint(localCenter);

    private void Awake()
    {
        Initialize();
    }

    public bool Initialize()
    {
        if (groundCollider != null)
            return true;

        GameObject groundRoot = GameObject.Find("ground");
        MeshFilter meshFilter = groundRoot != null
            ? groundRoot.GetComponentInChildren<MeshFilter>()
            : null;
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("CylinderGravityField cannot find the ground cylinder mesh.", this);
            enabled = false;
            return false;
        }

        cylinder = meshFilter.transform;
        Mesh mesh = meshFilter.sharedMesh;
        localCenter = mesh.bounds.center;
        localRadius = Mathf.Max(mesh.bounds.extents.y, mesh.bounds.extents.z);

        groundCollider = meshFilter.GetComponent<Collider>();
        if (groundCollider == null)
        {
            MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            groundCollider = meshCollider;
        }

        RefreshFloorColliders();
        RefreshBodies();
        return true;
    }

    private void FixedUpdate()
    {
        if (groundCollider == null)
            return;

        if (Time.fixedTime >= nextRefreshTime)
        {
            RefreshBodies();
            RefreshFloorColliders();
            nextRefreshTime = Time.fixedTime + 0.5f;
        }

        foreach (Rigidbody body in bodies)
        {
            if (body == null || body.isKinematic || HasFloorTag(body.transform))
                continue;

            body.useGravity = false;
            body.AddForce(-OutwardNormal(body.worldCenterOfMass) * gravityAcceleration,
                          ForceMode.Acceleration);
        }
    }

    private void RefreshBodies()
    {
        bodies = FindObjectsByType<Rigidbody>();
    }

    private void RefreshFloorColliders()
    {
        EnsureTaggedFloorColliders();
        Collider[] allColliders = FindObjectsByType<Collider>();
        int count = 0;
        foreach (Collider candidate in allColliders)
        {
            if (candidate != null && candidate.enabled && !candidate.isTrigger &&
                (candidate == groundCollider || IsFloor(candidate)))
                count++;
        }

        floorColliders = new Collider[count];
        int index = 0;
        foreach (Collider candidate in allColliders)
        {
            if (candidate != null && candidate.enabled && !candidate.isTrigger &&
                (candidate == groundCollider || IsFloor(candidate)))
                floorColliders[index++] = candidate;
        }
    }

    private void EnsureTaggedFloorColliders()
    {
        foreach (Transform root in FindObjectsByType<Transform>())
        {
            if (!root.CompareTag("floor"))
                continue;

            bool hasSolidCollider = false;
            foreach (Collider existing in root.GetComponentsInChildren<Collider>())
            {
                if (existing.enabled && !existing.isTrigger)
                {
                    hasSolidCollider = true;
                    break;
                }
            }
            if (hasSolidCollider)
                continue;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;
                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }
    }

    public static bool IsFloor(Collider candidate)
    {
        if (candidate == null || candidate.isTrigger)
            return false;

        return HasFloorTag(candidate.transform);
    }

    private static bool HasFloorTag(Transform objectTransform)
    {
        for (Transform current = objectTransform; current != null; current = current.parent)
        {
            if (current.CompareTag("floor"))
                return true;
        }
        return false;
    }

    public Vector3 OutwardNormal(Vector3 position)
    {
        Vector3 radial = Vector3.ProjectOnPlane(position - Center, Axis);
        return radial.sqrMagnitude > 0.0001f ? radial.normalized : cylinder.up;
    }

    public bool SampleSurface(Vector3 position, out Vector3 point, out Vector3 normal)
    {
        return SampleFloorSurface(position, out point, out normal, out _);
    }

    /// <summary>Finds the outermost walkable surface beneath a point on the cylinder.</summary>
    public bool SampleSupportSurface(Vector3 position, Collider platform,
                                     out Vector3 point, out Vector3 normal,
                                     out Collider support,
                                     float maxPlatformDistance = float.PositiveInfinity)
    {
        bool foundGround = SampleFloorSurface(position, out point, out normal, out support);
        Vector3 outward = OutwardNormal(position);
        Ray ray = new Ray(position + outward * 30f, -outward);

        if (platform != null && platform.enabled &&
            platform.Raycast(ray, out RaycastHit platformHit, 60f) &&
            Vector3.Dot(platformHit.normal, outward) > 0.5f &&
            Mathf.Abs(Vector3.Dot(platformHit.point - position, outward)) <= maxPlatformDistance &&
            Vector3.Dot(platformHit.point - point, outward) > 0.05f)
        {
            point = platformHit.point;
            normal = platformHit.normal;
            support = platform;
            return true;
        }

        return foundGround;
    }

    private bool SampleFloorSurface(Vector3 position, out Vector3 point,
                                    out Vector3 normal, out Collider support)
    {
        Vector3 outward = OutwardNormal(position);
        Ray ray = new Ray(position + outward * 30f, -outward);
        float bestHeight = float.NegativeInfinity;
        bool found = false;
        point = Vector3.zero;
        normal = outward;
        support = groundCollider;

        if (floorColliders != null)
        {
            foreach (Collider floor in floorColliders)
            {
                if (floor == null || !floor.enabled || floor.isTrigger ||
                    !floor.Raycast(ray, out RaycastHit hit, 60f))
                    continue;

                float heightFromBody = Vector3.Dot(hit.point - position, outward);
                if (heightFromBody > 1f)
                    continue;

                float height = Vector3.Dot(hit.point - Center, outward);
                if (!found || height > bestHeight)
                {
                    found = true;
                    bestHeight = height;
                    point = hit.point;
                    normal = Vector3.Dot(hit.normal, outward) >= 0.35f
                        ? hit.normal : outward;
                    support = floor;
                }
            }
        }

        if (!found)
        {
            Vector3 axisPoint = Center + Axis * Vector3.Dot(position - Center, Axis);
            float worldRadius = cylinder.TransformVector(Vector3.up * localRadius).magnitude;
            point = axisPoint + outward * worldRadius;
        }
        return found;
    }
}
