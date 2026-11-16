using System.Collections.Generic;
using UnityEngine;

/// <summary>Releases hit stones and shatters them after two quick charges.</summary>
public sealed class HumanBuilding : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float gravityAcceleration = 1f;
    [Min(0f)] [SerializeField] private float doubleChargeWindow = 0.5f;
    [Min(0f)] [SerializeField] private float fragmentExplosionImpulse = 12f;
    [SerializeField] private GameObject fragmentPrefab;

    private readonly List<Rigidbody> releasedPieces = new List<Rigidbody>();
    private readonly HashSet<Rigidbody> originalPieces = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, ChargeHit> recentHits = new Dictionary<Rigidbody, ChargeHit>();
    private CylinderGravityField gravityField;

    private struct ChargeHit
    {
        public int chargeId;
        public float time;
    }

    private void Awake()
    {
        foreach (Rigidbody piece in GetComponentsInChildren<Rigidbody>(true))
            originalPieces.Add(piece);

        gravityField = FindAnyObjectByType<CylinderGravityField>();
        if (gravityField != null && !gravityField.Initialize())
            gravityField = null;
    }

    private void FixedUpdate()
    {
        if (gravityAcceleration <= 0f)
            return;

        foreach (Rigidbody piece in releasedPieces)
        {
            if (piece == null || piece.isKinematic || !piece.gameObject.activeInHierarchy)
                continue;

            Vector3 direction = gravityField != null
                ? -gravityField.OutwardNormal(piece.worldCenterOfMass)
                : (Physics.gravity.sqrMagnitude > 0f ? Physics.gravity.normalized : Vector3.down);
            piece.AddForce(direction * gravityAcceleration, ForceMode.Acceleration);
        }
    }

    public void RegisterChargeHit(Rigidbody piece, int chargeId)
    {
        if (piece == null || !originalPieces.Contains(piece))
            return;

        float now = Time.fixedTime;
        bool hasPreviousHit = recentHits.TryGetValue(piece, out ChargeHit previousHit);
        if (hasPreviousHit && previousHit.chargeId == chargeId)
            return;

        if (hasPreviousHit && now - previousHit.time <= doubleChargeWindow &&
            ShatterPiece(piece))
        {
            recentHits.Remove(piece);
            return;
        }

        recentHits[piece] = new ChargeHit { chargeId = chargeId, time = now };
        ReleasePiece(piece);
    }

    private void ReleasePiece(Rigidbody piece)
    {
        if (!piece.isKinematic)
            return;

        piece.constraints |= RigidbodyConstraints.FreezePositionX;
        piece.useGravity = false;
        piece.isKinematic = false;
        piece.WakeUp();
        releasedPieces.Add(piece);
    }

    private bool ShatterPiece(Rigidbody piece)
    {
        if (fragmentPrefab == null)
        {
            Debug.LogError("HumanBuilding needs a fragment prefab to shatter stones.", this);
            return false;
        }

        Collider pieceCollider = piece.GetComponentInChildren<Collider>();
        Renderer pieceRenderer = piece.GetComponentInChildren<Renderer>();
        Bounds targetBounds = pieceCollider != null ? pieceCollider.bounds :
                              pieceRenderer != null ? pieceRenderer.bounds :
                              new Bounds(piece.worldCenterOfMass, Vector3.one);
        Vector3 inheritedVelocity = piece.linearVelocity;
        Vector3 inheritedAngularVelocity = piece.angularVelocity;

        GameObject source = Instantiate(fragmentPrefab);
        // This instance supplies mesh data only. Disable its colliders now so
        // it cannot overlap the live fragments before Destroy runs at frame end.
        foreach (Collider templateCollider in source.GetComponentsInChildren<Collider>(true))
            templateCollider.enabled = false;
        source.transform.SetPositionAndRotation(Vector3.zero, piece.rotation);
        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0 || !TryGetBounds(filters, out Bounds sourceBounds))
        {
            Debug.LogError("The human building fragment model has no visible meshes.", this);
            Destroy(source);
            return false;
        }

        Vector3 sourceSize = sourceBounds.size;
        Vector3 targetSize = targetBounds.size;
        float scale = Mathf.Min(targetSize.x / Mathf.Max(sourceSize.x, 0.001f),
                                targetSize.y / Mathf.Max(sourceSize.y, 0.001f),
                                targetSize.z / Mathf.Max(sourceSize.z, 0.001f));
        source.transform.localScale *= scale;
        TryGetBounds(filters, out sourceBounds);
        source.transform.position += targetBounds.center - sourceBounds.center;

        Vector3 explosionCenter = sourceBounds.center;
        foreach (MeshFilter filter in filters)
        {
            if (filter.name.StartsWith("柱体.043") && filter.GetComponent<Renderer>() is Renderer centerRenderer)
            {
                explosionCenter = centerRenderer.bounds.center;
                break;
            }
        }

        List<Rigidbody> fragments = new List<Rigidbody>(filters.Length);
        foreach (MeshFilter filter in filters)
        {
            MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || sourceRenderer == null)
                continue;

            GameObject fragment = new GameObject(filter.name + " fragment");
            fragment.transform.position = sourceRenderer.bounds.center;
            fragment.transform.SetParent(transform, true);

            GameObject visual = new GameObject("Mesh");
            visual.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
            visual.transform.localScale = filter.transform.lossyScale;
            visual.transform.SetParent(fragment.transform, true);
            visual.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;

            MeshCollider collider = visual.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;

            Rigidbody body = fragment.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionX |
                               RigidbodyConstraints.FreezeRotationY |
                               RigidbodyConstraints.FreezeRotationZ;
            body.linearVelocity = new Vector3(0f, inheritedVelocity.y, inheritedVelocity.z);
            body.angularVelocity = Vector3.right * inheritedAngularVelocity.x;
            releasedPieces.Add(body);
            fragments.Add(body);
        }

        Destroy(source);
        if (fragments.Count == 0)
            return false;

        // Replacing one stone must not multiply its physical mass by the
        // number of meshes in the fragment model.
        float fragmentMass = piece.mass / fragments.Count;
        foreach (Rigidbody fragment in fragments)
            fragment.mass = fragmentMass;

        originalPieces.Remove(piece);
        releasedPieces.Remove(piece);
        piece.gameObject.SetActive(false);
        Destroy(piece.gameObject);

        for (int i = 0; i < fragments.Count; i++)
        {
            Rigidbody fragment = fragments[i];
            Vector3 direction = fragment.worldCenterOfMass - explosionCenter;
            direction.x = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                float angle = 2f * Mathf.PI * i / fragments.Count;
                direction = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
            }
            fragment.AddForce(direction.normalized * (fragmentExplosionImpulse * fragment.mass),
                              ForceMode.Impulse);
        }
        return true;
    }

    private static bool TryGetBounds(MeshFilter[] filters, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (MeshFilter filter in filters)
        {
            Renderer renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh == null || renderer == null)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }
}
