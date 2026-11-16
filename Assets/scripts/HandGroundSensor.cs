using System.Collections.Generic;
using UnityEngine;

/// <summary>Reports contact between a limb trigger and nearby solid supports.</summary>
public sealed class HandGroundSensor : MonoBehaviour
{
    private Collider ground;
    private Collider platform;
    private readonly HashSet<Collider> touchingSurfaces = new HashSet<Collider>();
    private bool touchingPlatform;
    private float lastContactTime = float.NegativeInfinity;
    private float lastGroundContactTime = float.NegativeInfinity;
    private float lastPlatformContactTime = float.NegativeInfinity;

    public bool IsGrounded => (touchingSurfaces.Count > 0 || touchingPlatform) &&
                              Time.fixedTime - lastContactTime <= Time.fixedDeltaTime * 1.5f;
    public bool IsOnGround => touchingSurfaces.Count > 0 &&
                              Time.fixedTime - lastGroundContactTime <= Time.fixedDeltaTime * 1.5f;
    public bool IsOnPlatform => touchingPlatform &&
                                Time.fixedTime - lastPlatformContactTime <= Time.fixedDeltaTime * 1.5f;

    public void Bind(Collider groundCollider, Collider platformCollider)
    {
        ground = groundCollider;
        platform = platformCollider;
        touchingSurfaces.Clear();
        touchingPlatform = false;
        lastContactTime = float.NegativeInfinity;
        lastGroundContactTime = float.NegativeInfinity;
        lastPlatformContactTime = float.NegativeInfinity;
    }

    private void OnTriggerEnter(Collider other)
    {
        RecordContact(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RecordContact(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (touchingSurfaces.Remove(other))
        {
            if (touchingSurfaces.Count == 0)
                lastGroundContactTime = float.NegativeInfinity;
        }
        if (other == platform)
        {
            touchingPlatform = false;
            lastPlatformContactTime = float.NegativeInfinity;
        }
        if (touchingSurfaces.Count == 0 && !touchingPlatform)
            lastContactTime = float.NegativeInfinity;
    }

    private void RecordContact(Collider other)
    {
        if (other == ground || CylinderGravityField.IsWalkableCollider(other, transform.root))
        {
            touchingSurfaces.Add(other);
            lastGroundContactTime = Time.fixedTime;
        }
        else if (other == platform)
        {
            touchingPlatform = true;
            lastPlatformContactTime = Time.fixedTime;
        }
        else
            return;

        lastContactTime = Time.fixedTime;
    }
}
