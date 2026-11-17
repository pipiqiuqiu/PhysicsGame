using System.Collections.Generic;
using UnityEngine;

/// <summary>Overrides the creature's cylinder gravity inside the projected sprite disc.</summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public sealed class BlackHoleZone : MonoBehaviour
{
    private static readonly List<BlackHoleZone> activeZones = new List<BlackHoleZone>();

    public static BlackHoleZone FindContainingCharacter()
    {
        foreach (BlackHoleZone zone in activeZones)
        {
            if (zone != null && zone.ContainsCharacter())
                return zone;
        }
        return null;
    }

    public static bool OverridesGravityFor(Rigidbody body)
    {
        foreach (BlackHoleZone zone in activeZones)
        {
            if (zone != null && zone.OverridesGravity(body))
                return true;
        }
        return false;
    }

    [SerializeField] private Rigidbody frontBody;
    [SerializeField] private Rigidbody rearBody;
    [Min(0f)] [SerializeField] private float centerAttraction = 16f;
    [Min(0f)] [SerializeField] private float velocityDamping = 0.5f;
    [Min(0f)] [SerializeField] private float maxAttractionAcceleration = 24f;
    [Min(0f)] [SerializeField] private float steeringAcceleration = 4f;
    [Header("WASD paddle motion")]
    [Min(0f)] [SerializeField] private float paddleDistance = 0.5f;
    [Min(0f)] [SerializeField] private float paddleCyclesPerSecond = 1.5f;
    [Min(0f)] [SerializeField] private float paddleBlendSpeed = 6f;

    private SpriteRenderer spriteRenderer;
    private Vector3 paddleDirection = Vector3.forward;
    private float paddleBlend;
    private float lastPaddleTime = float.NegativeInfinity;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        if (!activeZones.Contains(this))
            activeZones.Add(this);
    }

    public bool ContainsCharacter()
    {
        if (!isActiveAndEnabled || frontBody == null || spriteRenderer == null ||
            spriteRenderer.sprite == null)
            return false;

        // The sprite's local Z is its normal (world X in this scene).
        // Deliberately ignore it so the area is the visible 2D disc.
        Vector3 point = transform.InverseTransformPoint(frontBody.position);
        Bounds bounds = spriteRenderer.sprite.bounds;
        float x = (point.x - bounds.center.x) / Mathf.Max(bounds.extents.x, 0.0001f);
        float y = (point.y - bounds.center.y) / Mathf.Max(bounds.extents.y, 0.0001f);
        return x * x + y * y <= 1f;
    }

    public bool OverridesGravity(Rigidbody body)
    {
        return (body == frontBody || body == rearBody) && ContainsCharacter();
    }

    public void ApplyAttraction(Rigidbody body)
    {
        if (body == null || centerAttraction <= 0f)
            return;

        Vector3 center = transform.TransformPoint(spriteRenderer.sprite.bounds.center);
        Vector3 toCenter = center - body.position;
        toCenter.x = 0f;
        Vector3 velocity = body.linearVelocity;
        velocity.x = 0f;
        Vector3 acceleration = toCenter.normalized * centerAttraction -
                               velocity * velocityDamping;
        acceleration = Vector3.ClampMagnitude(acceleration, maxAttractionAcceleration);
        body.AddForce(acceleration, ForceMode.Acceleration);
    }

    public void ApplySteering(Vector3 direction)
    {
        if (steeringAcceleration <= 0f || !ContainsCharacter())
            return;

        direction.x = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Vector3 acceleration = direction.normalized * steeringAcceleration;
        frontBody.AddForce(acceleration, ForceMode.Acceleration);
    }

    public Vector3 GetPaddleOffset(Vector3 inputDirection)
    {
        inputDirection.x = 0f;
        float now = Time.fixedTime;
        if (lastPaddleTime != now)
        {
            if (now < lastPaddleTime || now - lastPaddleTime > Time.fixedDeltaTime * 1.5f)
                paddleBlend = 0f;
            lastPaddleTime = now;

            bool moving = inputDirection.sqrMagnitude > 0.0001f;
            if (moving)
                paddleDirection = inputDirection.normalized;
            paddleBlend = Mathf.MoveTowards(paddleBlend, moving ? 1f : 0f,
                                            paddleBlendSpeed * Time.fixedDeltaTime);
        }

        float stroke = Mathf.Sin(now * paddleCyclesPerSecond * 2f * Mathf.PI);
        return paddleDirection * (paddleDistance * paddleBlend * stroke);
    }

    private void OnDisable()
    {
        activeZones.Remove(this);
        paddleBlend = 0f;
        lastPaddleTime = float.NegativeInfinity;
    }
}
