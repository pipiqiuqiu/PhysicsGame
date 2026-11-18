using System.Collections.Generic;
using UnityEngine;

/// <summary>Shatters stones after two quick charges, or black hole triggers after one.</summary>
public sealed class HumanBuilding : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float gravityAcceleration = 1f;
    [Min(0f)] [SerializeField] private float doubleChargeWindow = 0.5f;
    [Min(0f)] [SerializeField] private float fragmentExplosionImpulse = 12f;
    [Min(0f)] [SerializeField] private float fragmentDestroyDelay = 1f;
    [SerializeField] private GameObject fragmentPrefab;
    [Header("Optional black hole trigger")]
    [SerializeField] private bool keepOriginalPiecesKinematic;
    [Tooltip("An existing black hole object used as the visual and physics template.")]
    [SerializeField] private GameObject blackHoleTemplate;
    [Tooltip("World X position of the spawned black hole. Its Y/Z position comes from the shattered piece.")]
    public float blackHoleSpawnX = -24f;
    [Min(0.01f)] [Tooltip("Spawned black hole size relative to the template. 1 keeps its current size.")]
    public float blackHoleSize = 1f;

    private readonly List<Rigidbody> releasedPieces = new List<Rigidbody>();
    private readonly HashSet<Rigidbody> originalPieces = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, int> fragmentCreationCharges = new Dictionary<Rigidbody, int>();
    private readonly Dictionary<Rigidbody, ChargeHit> recentHits = new Dictionary<Rigidbody, ChargeHit>();
    private CylinderGravityField gravityField;
    private bool blackHoleSpawned;

    private struct ChargeHit
    {
        public int chargeId;
        public float time;
    }

    private void Awake()
    {
        foreach (Rigidbody piece in GetComponentsInChildren<Rigidbody>(true))
        {
            originalPieces.Add(piece);
            if (keepOriginalPiecesKinematic)
                piece.isKinematic = true;
        }

        gravityField = FindAnyObjectByType<CylinderGravityField>();
        if (gravityField != null && !gravityField.Initialize())
            gravityField = null;
    }

    private void FixedUpdate()
    {
        for (int i = releasedPieces.Count - 1; i >= 0; i--)
        {
            Rigidbody piece = releasedPieces[i];
            if (piece == null)
            {
                releasedPieces.RemoveAt(i);
                continue;
            }
            if (gravityAcceleration <= 0f || piece.isKinematic ||
                !piece.gameObject.activeInHierarchy)
                continue;

            Vector3 direction = gravityField != null
                ? -gravityField.OutwardNormal(piece.worldCenterOfMass)
                : (Physics.gravity.sqrMagnitude > 0f ? Physics.gravity.normalized : Vector3.down);
            piece.AddForce(direction * gravityAcceleration, ForceMode.Acceleration);
        }
    }

    public void RegisterChargeHit(Rigidbody piece, int chargeId)
    {
        if (piece == null)
            return;

        if (fragmentCreationCharges.TryGetValue(piece, out int creationChargeId))
        {
            // Fragments can overlap the detector during the charge that created them.
            if (chargeId > creationChargeId)
            {
                fragmentCreationCharges.Remove(piece);
                // Keep the fragment visible and physical until the delay expires.
                Destroy(piece.gameObject, fragmentDestroyDelay);
                SoundSystem.Instance?.PlayFragmentDestroy();
            }
            return;
        }

        if (!originalPieces.Contains(piece))
            return;

        float now = Time.fixedTime;
        bool hasPreviousHit = recentHits.TryGetValue(piece, out ChargeHit previousHit);
        if (hasPreviousHit && previousHit.chargeId == chargeId)
            return;

        SoundSystem.Instance?.PlayChargeImpact();

        // A configured black hole trigger breaks on the first charge. Ordinary
        // stones still need two distinct charges within the time window.
        if ((blackHoleTemplate != null ||
             (hasPreviousHit && now - previousHit.time <= doubleChargeWindow)) &&
            ShatterPiece(piece, chargeId))
        {
            recentHits.Remove(piece);
            return;
        }

        recentHits[piece] = new ChargeHit { chargeId = chargeId, time = now };
        ReleasePiece(piece);
    }

    private void ReleasePiece(Rigidbody piece)
    {
        if (keepOriginalPiecesKinematic)
            return;
        if (!piece.isKinematic)
            return;

        piece.constraints |= RigidbodyConstraints.FreezePositionX;
        piece.useGravity = false;
        piece.isKinematic = false;
        piece.WakeUp();
        releasedPieces.Add(piece);
    }

    private bool ShatterPiece(Rigidbody piece, int chargeId)
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
            fragmentCreationCharges.Add(body, chargeId);
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

        if (!blackHoleSpawned && blackHoleTemplate != null)
        {
            Vector3 holePosition = targetBounds.center;
            holePosition.x = blackHoleSpawnX;
            GameObject hole = Instantiate(blackHoleTemplate, holePosition,
                                          blackHoleTemplate.transform.rotation);
            hole.transform.localScale = blackHoleTemplate.transform.localScale *
                                        Mathf.Max(0.01f, blackHoleSize);
            blackHoleSpawned = true;
            SoundSystem.Instance?.PlayBlackHoleShatter();
        }
        else
            SoundSystem.Instance?.PlayStoneShatter();

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
