using System.Collections.Generic;
using UnityEngine;

/// <summary>Releases only pieces touched by the charging creature's detector.</summary>
public sealed class HumanBuilding : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float gravityAcceleration = 1f;

    private readonly List<Rigidbody> releasedPieces = new List<Rigidbody>();
    private CylinderGravityField gravityField;

    private void Awake()
    {
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

    public void ReleasePiece(Rigidbody piece)
    {
        if (piece == null || !piece.isKinematic || !piece.transform.IsChildOf(transform))
            return;

        piece.constraints |= RigidbodyConstraints.FreezePositionX;
        piece.useGravity = false;
        piece.isKinematic = false;
        piece.WakeUp();
        releasedPieces.Add(piece);
    }
}
