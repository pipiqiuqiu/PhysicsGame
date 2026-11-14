using UnityEngine;

/// <summary>Applies gravity toward the center axis of the scene's ground cylinder.</summary>
public sealed class CylinderGravityField : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float gravityAcceleration = 9.81f;

    private Transform cylinder;
    private Collider groundCollider;
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
            nextRefreshTime = Time.fixedTime + 0.5f;
        }

        foreach (Rigidbody body in bodies)
        {
            if (body == null || body.isKinematic)
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

    public Vector3 OutwardNormal(Vector3 position)
    {
        Vector3 radial = Vector3.ProjectOnPlane(position - Center, Axis);
        return radial.sqrMagnitude > 0.0001f ? radial.normalized : cylinder.up;
    }

    public bool SampleSurface(Vector3 position, out Vector3 point, out Vector3 normal)
    {
        normal = OutwardNormal(position);
        Vector3 axis = Axis;
        Vector3 axisPoint = Center + axis * Vector3.Dot(position - Center, axis);
        float worldRadius = cylinder.TransformVector(Vector3.up * localRadius).magnitude;
        Ray ray = new Ray(axisPoint + normal * (worldRadius + 30f), -normal);

        if (groundCollider.Raycast(ray, out RaycastHit hit, 60f))
        {
            point = hit.point;
            return true;
        }

        point = axisPoint + normal * worldRadius;
        return false;
    }

    /// <summary>Finds the outermost walkable surface beneath a point on the cylinder.</summary>
    public bool SampleSupportSurface(Vector3 position, Collider platform,
                                     out Vector3 point, out Vector3 normal,
                                     out Collider support,
                                     float maxPlatformDistance = float.PositiveInfinity)
    {
        normal = OutwardNormal(position);
        Vector3 axisPoint = Center + Axis * Vector3.Dot(position - Center, Axis);
        float worldRadius = cylinder.TransformVector(Vector3.up * localRadius).magnitude;
        Ray ray = new Ray(axisPoint + normal * (worldRadius + 30f), -normal);

        bool foundGround = groundCollider.Raycast(ray, out RaycastHit groundHit, 60f);
        point = foundGround ? groundHit.point : axisPoint + normal * worldRadius;
        support = groundCollider;

        if (platform != null && platform.enabled &&
            platform.Raycast(ray, out RaycastHit platformHit, 60f) &&
            Vector3.Dot(platformHit.normal, normal) > 0.5f &&
            Mathf.Abs(Vector3.Dot(platformHit.point - position, normal)) <= maxPlatformDistance &&
            Vector3.Dot(platformHit.point - point, normal) > 0.05f)
        {
            point = platformHit.point;
            normal = platformHit.normal;
            support = platform;
            return true;
        }

        return foundGround;
    }
}
