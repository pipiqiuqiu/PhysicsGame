using UnityEngine;

/// <summary>Reports contact between one limb trigger and either walkable surface.</summary>
public sealed class HandGroundSensor : MonoBehaviour
{
    private Collider ground;
    private Collider platform;
    private bool touchingGround;
    private bool touchingPlatform;
    private float lastContactTime = float.NegativeInfinity;
    private float lastGroundContactTime = float.NegativeInfinity;
    private float lastPlatformContactTime = float.NegativeInfinity;

    public bool IsGrounded => (touchingGround || touchingPlatform) &&
                              Time.fixedTime - lastContactTime <= Time.fixedDeltaTime * 1.5f;
    public bool IsOnGround => touchingGround &&
                              Time.fixedTime - lastGroundContactTime <= Time.fixedDeltaTime * 1.5f;
    public bool IsOnPlatform => touchingPlatform &&
                                Time.fixedTime - lastPlatformContactTime <= Time.fixedDeltaTime * 1.5f;

    public void Bind(Collider groundCollider, Collider platformCollider)
    {
        ground = groundCollider;
        platform = platformCollider;
        touchingGround = false;
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
        if (other == ground)
        {
            touchingGround = false;
            lastGroundContactTime = float.NegativeInfinity;
        }
        if (other == platform)
        {
            touchingPlatform = false;
            lastPlatformContactTime = float.NegativeInfinity;
        }
        if (!touchingGround && !touchingPlatform)
            lastContactTime = float.NegativeInfinity;
    }

    private void RecordContact(Collider other)
    {
        if (other == ground)
        {
            touchingGround = true;
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
