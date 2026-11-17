using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Drives the front body with forces around a cylindrical gravity field.
/// The two kinematic hand rigidbodies provide animated contact sensors.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class FrontBodyPhysics : MonoBehaviour
{
    public bool IsAirborne => airborne;
    public bool IsStandingOnRear => standingOnRear;
    public bool IsSupportedByRear => !airborne && supportedByRear;
    public int HeightCommand => heightCommand;
    public float JumpStartClearance => jumpStartClearance;
    public float ForwardTangentSign => forwardSign;
    public Vector3 BlackHoleInputDirection
    {
        get
        {
            Vector3 screenRight = previewCamera != null ? previewCamera.transform.right : Vector3.forward;
            Vector3 screenUp = previewCamera != null ? previewCamera.transform.up : Vector3.up;
            Vector3 direction = screenRight * movementInput + screenUp * heightCommand;
            direction.x = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }
    }

    [Header("References")]
    [SerializeField] private CylinderGravityField gravityField;
    [SerializeField] private Transform hand1;
    [SerializeField] private Transform hand2;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private WallDetectorLaunch wallDetector;

    [Header("Tangential movement")]
    [SerializeField] private Vector3 forwardDirection = Vector3.back;
    [Min(0f)] [SerializeField] private float moveSpeed = 2.2f;
    [Min(0f)] [SerializeField] private float driveResponse = 8f;
    [Min(0f)] [SerializeField] private float maxDriveAcceleration = 80f;

    [Header("Collision response")]
    [Min(0f)] [SerializeField] private float maxImpactSpeed = 12f;
    [Min(0f)] [SerializeField] private float impactLogThreshold = 10f;

    [Header("Distance from surface")]
    [Min(0f)] [SerializeField] private float surfaceClearance = 0f;
    [Min(1f)] [SerializeField] private float raisedClearanceMultiplier = 2f;
    [Range(0f, 1f)] [SerializeField] private float loweredClearanceMultiplier = 0.5f;
    [Min(0f)] [SerializeField] private float hoverSpring = 25f;
    [Min(0f)] [SerializeField] private float hoverDamping = 10f;
    [Min(0f)] [SerializeField] private float maxHoverAcceleration = 60f;

    [Header("Jump")]
    [Min(0f)] [SerializeField] private float jumpImpulse = 8f;
    [FormerlySerializedAs("bothHandsRearJumpBonus")]
    [Min(0f)] [SerializeField] private float rearHandJumpBonus = 6f;
    [Min(0f)] [SerializeField] private float rearTravelImpulse = 20f;

    [Header("Stacked jump rotation")]
    [Min(0f)] [SerializeField] private float jumpSpinImpulse = 5f;
    [Min(0f)] [SerializeField] private float airSpinAcceleration = 5f;
    [Min(0f)] [SerializeField] private float maxAirSpinSpeed = 6f;

    [Header("Hand steps")]
    [Min(0.1f)] [SerializeField] private float stepCyclesPerSecond = 1.35f;
    [Min(0f)] [SerializeField] private float liftHeight = 0.65f;
    [Min(0f)] [SerializeField] private float stepLead = 0.35f;
    [Min(0f)] [SerializeField] private float contactInset = 0.06f;
    [Min(0f)] [SerializeField] private float footGroundSnapDistance = 3f;
    [Min(0f)] [SerializeField] private float cameraFollowSpeed = 5f;

    private sealed class HandState
    {
        public Transform transform;
        public Rigidbody body;
        public HandGroundSensor sensor;
        public Vector3 restLocalPosition;
        public Vector3 plantedPosition;
        public Quaternion plantedRotation;
        public Vector3 plantedNormal;
        public Collider plantedSupport;
        public Vector3 plantedLocalPosition;
        public Quaternion plantedLocalRotation;
        public Vector3 plantedLocalNormal;
        public Vector3 swingStart;
        public Quaternion swingStartRotation;
        public float halfHeight;
        public bool swinging;
    }

    private struct SurfacePose
    {
        public Vector3 position;
        public Vector3 normal;
        public Quaternion rotation;
        public Collider support;
    }

    private Rigidbody frontBody;
    private BodyBehindFollower rearController;
    private BlackHoleZone blackHoleZone;
    private Collider rearCollider;
    private float frontHalfHeight;
    private HandState left;
    private HandState right;
    private Vector3 cameraInitialOffset;
    private Quaternion cameraInitialRotation;
    private Vector3 cameraInitialNormal;
    private float forwardSign;
    private float baseClearance;
    private float movementInput;
    private float stepTime;
    private float airTime;
    private int heightCommand;
    private float jumpStartClearance;
    private float jumpTravelInput;
    private float originalMaxAngularVelocity;
    private bool jumpRequested;
    private bool jumpFromRearRequested;
    private bool jumpWithRearOnTopRequested;
    private bool airborne;
    private bool rearTravelJumpInProgress;
    private bool stackedJumpSpinInProgress;
    private bool standingOnRear;
    private bool supportedByRear;
    private float nextImpactWarningTime;

    private void Awake()
    {
        if (gravityField == null || hand1 == null || hand2 == null || !gravityField.Initialize())
        {
            Debug.LogError("FrontBodyPhysics needs the cylinder gravity field and both hands.", this);
            enabled = false;
            return;
        }

        frontBody = GetComponent<Rigidbody>();
        originalMaxAngularVelocity = frontBody.maxAngularVelocity;
        BoxCollider frontCollider = GetComponent<BoxCollider>();
        rearController = FindAnyObjectByType<BodyBehindFollower>();
        rearCollider = rearController != null ? rearController.GetComponent<Collider>() : null;
        frontHalfHeight = frontCollider != null
            ? frontCollider.size.y * Mathf.Abs(frontCollider.transform.lossyScale.y) * 0.5f
            : 1f;
        frontBody.isKinematic = false;
        frontBody.useGravity = false;
        Vector3 frontPosition = frontBody.position;
        frontPosition.x = 0f;
        frontBody.position = frontPosition;
        frontBody.constraints |= RigidbodyConstraints.FreezePositionX;
        frontBody.interpolation = RigidbodyInterpolation.Interpolate;
        frontBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 normal = gravityField.OutwardNormal(frontBody.position);
        Vector3 positiveTangent = Vector3.Cross(gravityField.Axis, normal).normalized;
        forwardSign = Vector3.Dot(forwardDirection, positiveTangent) < 0f ? -1f : 1f;

        gravityField.SampleSurface(frontBody.position, out Vector3 surfacePoint, out _);
        baseClearance = surfaceClearance > 0f
            ? surfaceClearance
            : Mathf.Max(0.5f, Vector3.Dot(frontBody.position - surfacePoint, normal));

        frontBody.rotation = Quaternion.LookRotation(Tangent(normal), normal);
        left = CreateHand(hand1);
        right = CreateHand(hand2);
        if (left == null || right == null)
        {
            enabled = false;
            return;
        }

        Plant(left, SurfaceFor(left, transform.TransformPoint(left.restLocalPosition)));
        Plant(right, SurfaceFor(right, transform.TransformPoint(right.restLocalPosition)));

        if (previewCamera != null)
        {
            cameraInitialNormal = normal;
            cameraInitialOffset = previewCamera.transform.position - frontBody.position;
            cameraInitialRotation = previewCamera.transform.rotation;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        movementInput = keyboard == null ? 0f :
            (keyboard.dKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed ? 1f : 0f);
        heightCommand = keyboard == null ? 0 :
            (keyboard.wKey.isPressed ? 1 : 0) -
            (keyboard.sKey.isPressed ? 1 : 0);
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpRequested = true;
            jumpFromRearRequested = supportedByRear || standingOnRear;
            jumpTravelInput = movementInput;
            jumpWithRearOnTopRequested = rearController != null && rearController.CanJumpFromFront;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        stepTime += dt;

        blackHoleZone = BlackHoleZone.FindContainingCharacter();
        if (blackHoleZone != null)
        {
            UpdateInsideBlackHole();
            return;
        }

        gravityField.SampleSurface(frontBody.position, out Vector3 surfacePoint, out Vector3 normal);
        float clearance = Vector3.Dot(frontBody.position - surfacePoint, normal);
        float radialSpeed = Vector3.Dot(frontBody.linearVelocity, normal);
        gravityField.SampleSupportSurface(frontBody.position, rearCollider,
                                          out Vector3 supportPoint, out Vector3 supportNormal,
                                          out Collider support);
        float supportGap = Vector3.Dot(frontBody.position - supportPoint, supportNormal);
        Rigidbody supportBody = support == rearCollider && rearCollider != null
            ? rearCollider.attachedRigidbody : null;
        float relativeSpeed = Vector3.Dot(frontBody.linearVelocity -
            (supportBody != null ? supportBody.GetPointVelocity(supportPoint) : Vector3.zero),
            supportNormal);
        bool physicallyOnRear = supportBody != null &&
            supportGap >= frontHalfHeight - 0.25f &&
            supportGap <= frontHalfHeight + 0.35f &&
            relativeSpeed <= 0.5f;
        bool handOnRear = rearCollider != null &&
            ((left.plantedSupport == rearCollider && left.sensor.IsOnPlatform) ||
             (right.plantedSupport == rearCollider && right.sensor.IsOnPlatform));
        bool rearBelowFront = IsOnTopOfRear(normal);
        supportedByRear = physicallyOnRear || handOnRear || rearBelowFront || standingOnRear;
        bool onRear = heightCommand == 0 && (physicallyOnRear || rearBelowFront);
        Vector3 tangent = Tangent(normal);
        float tangentialSpeed = Vector3.Dot(frontBody.linearVelocity, tangent);

        float heightMultiplier = heightCommand > 0 ? raisedClearanceMultiplier :
                                 heightCommand < 0 ? loweredClearanceMultiplier : 1f;
        float targetClearance = baseClearance * heightMultiplier;
        // The hands can both leave the surface during a step even while the
        // body's hover controller is supporting it. Use the airborne state
        // instead of transient hand contacts to decide whether it can jump.
        if (jumpRequested && wallDetector != null && wallDetector.TryLaunch())
        {
            airborne = true;
            standingOnRear = false;
            airTime = 0f;
            jumpStartClearance = clearance;
        }
        else if (jumpRequested && !airborne)
        {
            bool jumpingFromRear = jumpFromRearRequested || supportedByRear;
            // Either planted hand touching the rear body adds the bonus impulse.
            float impulse = jumpImpulse + (handOnRear ? rearHandJumpBonus : 0f);
            frontBody.AddForce(normal * impulse, ForceMode.Impulse);
            if (jumpingFromRear && jumpTravelInput != 0f)
            {
                frontBody.AddForce(tangent * (jumpTravelInput * rearTravelImpulse),
                                   ForceMode.Impulse);
                rearTravelJumpInProgress = true;
            }
            if (jumpingFromRear)
            {
                stackedJumpSpinInProgress = true;
                if (jumpTravelInput != 0f)
                    frontBody.AddTorque(gravityField.Axis * (jumpTravelInput * jumpSpinImpulse),
                                        ForceMode.Impulse);
            }
            airborne = true;
            standingOnRear = false;
            airTime = 0f;
            jumpStartClearance = clearance;
            if (!supportedByRear && !jumpWithRearOnTopRequested)
                rearController?.ScheduleGroundJumpAfterFront();
        }
        jumpRequested = false;
        jumpFromRearRequested = false;
        jumpWithRearOnTopRequested = false;
        jumpTravelInput = 0f;

        if (airborne)
        {
            airTime += dt;
            if (airTime >= 0.25f)
            {
                if (onRear)
                {
                    airborne = false;
                    standingOnRear = true;
                }
                else if (clearance <= targetClearance + 0.15f && radialSpeed <= 0f)
                {
                    airborne = false;
                }
            }
        }
        else if (onRear)
        {
            standingOnRear = true;
        }
        else if (standingOnRear)
        {
            standingOnRear = false;
            if (clearance > targetClearance + 0.15f)
            {
                airborne = true;
                airTime = 0f;
            }
        }

        if (!airborne && !standingOnRear)
        {
            // The gravity field pulls inward; the hover force balances it and
            // acts like a spring that maintains the chosen radial clearance.
            float acceleration = Mathf.Clamp(gravityField.GravityAcceleration +
                                 (targetClearance - clearance) * hoverSpring -
                                 radialSpeed * hoverDamping,
                                 -maxHoverAcceleration, maxHoverAcceleration);
            frontBody.AddForce(normal * acceleration, ForceMode.Acceleration);
        }

        if (!airborne)
        {
            rearTravelJumpInProgress = false;
            stackedJumpSpinInProgress = false;
            frontBody.maxAngularVelocity = originalMaxAngularVelocity;
        }
        if (!rearTravelJumpInProgress)
        {
            float driveAcceleration = Mathf.Clamp(
                (movementInput * moveSpeed - tangentialSpeed) * driveResponse,
                -maxDriveAcceleration, maxDriveAcceleration);
            frontBody.AddForce(tangent * driveAcceleration, ForceMode.Acceleration);
        }
        if (stackedJumpSpinInProgress)
            ApplyAirSpin(movementInput);
        else
            AlignWithSurface(normal, tangent);

        if (airborne)
        {
            MoveAirborneHand(left, normal);
            MoveAirborneHand(right, normal);
        }
        else
        {
            UpdateHand(left, 0f, tangent);
            UpdateHand(right, 0.5f, tangent);
        }
    }

    private void UpdateInsideBlackHole()
    {
        airborne = true;
        airTime = 0f;
        standingOnRear = false;
        supportedByRear = false;
        rearTravelJumpInProgress = false;
        stackedJumpSpinInProgress = false;
        frontBody.maxAngularVelocity = originalMaxAngularVelocity;

        if (jumpRequested && wallDetector != null)
            wallDetector.TryLaunch();

        jumpRequested = false;
        jumpFromRearRequested = false;
        jumpWithRearOnTopRequested = false;
        jumpTravelInput = 0f;

        blackHoleZone.ApplyAttraction(frontBody);
        Vector3 inputDirection = BlackHoleInputDirection;
        blackHoleZone.ApplySteering(inputDirection);
        Vector3 paddleOffset = blackHoleZone.GetPaddleOffset(inputDirection);
        Vector3 normal = gravityField.OutwardNormal(frontBody.position);
        MoveAirborneHand(left, normal, paddleOffset);
        MoveAirborneHand(right, normal, paddleOffset);
    }

    private void AlignWithSurface(Vector3 normal, Vector3 tangent)
    {
        frontBody.angularVelocity = Vector3.zero;
        frontBody.MoveRotation(Quaternion.LookRotation(tangent, normal));
    }

    private void OnCollisionEnter(Collision collision)
    {
        LimitDynamicImpact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        LimitDynamicImpact(collision);
    }

    private void LimitDynamicImpact(Collision collision)
    {
        Rigidbody otherBody = collision.rigidbody;
        if (frontBody == null || otherBody == null ||
            otherBody.isKinematic || (rearController != null &&
            otherBody == rearController.GetComponent<Rigidbody>()))
            return;

        Vector3 velocity = frontBody.linearVelocity;
        bool limited = maxImpactSpeed > 0f &&
                       velocity.sqrMagnitude > maxImpactSpeed * maxImpactSpeed;
        if (limited)
            frontBody.linearVelocity = velocity.normalized * maxImpactSpeed;
        if ((limited || collision.impulse.magnitude >= impactLogThreshold) &&
            Time.time >= nextImpactWarningTime)
        {
            Debug.LogWarning($"body_front impact with {collision.gameObject.name}: " +
                $"relative speed {collision.relativeVelocity.magnitude:F1}, " +
                $"contact impulse {collision.impulse.magnitude:F1}, " +
                $"other mass {otherBody.mass:F2}, " +
                $"front speed {velocity.magnitude:F1}" +
                (limited ? $" (limited to {maxImpactSpeed:F1})." : "."), this);
            nextImpactWarningTime = Time.time + 1f;
        }
    }

    private bool IsOnTopOfRear(Vector3 normal)
    {
        if (rearCollider == null || !rearCollider.enabled)
            return false;

        Vector3 fromRear = frontBody.position - rearCollider.ClosestPoint(frontBody.position);
        float heightAboveRear = Vector3.Dot(fromRear, normal);
        return heightAboveRear >= frontHalfHeight - 0.75f &&
               heightAboveRear <= frontHalfHeight + 0.75f &&
               Vector3.ProjectOnPlane(fromRear, normal).sqrMagnitude <= 0.25f;
    }

    private void ApplyAirSpin(float input)
    {
        frontBody.maxAngularVelocity = Mathf.Max(originalMaxAngularVelocity, maxAirSpinSpeed);
        if (input == 0f || maxAirSpinSpeed <= 0f)
            return;

        Vector3 axis = gravityField.Axis;
        float remainingSpeed = maxAirSpinSpeed -
                               Vector3.Dot(frontBody.angularVelocity, axis) * input;
        if (remainingSpeed > 0f)
        {
            float acceleration = Mathf.Min(airSpinAcceleration,
                                           remainingSpeed / Time.fixedDeltaTime);
            frontBody.AddTorque(axis * (input * acceleration), ForceMode.Acceleration);
        }
    }

    private Vector3 Tangent(Vector3 normal)
    {
        return Vector3.Cross(gravityField.Axis, normal).normalized * forwardSign;
    }

    private HandState CreateHand(Transform hand)
    {
        Rigidbody handBody = hand.GetComponent<Rigidbody>();
        BoxCollider handCollider = hand.GetComponent<BoxCollider>();
        HandGroundSensor sensor = hand.GetComponent<HandGroundSensor>();
        if (handBody == null || handCollider == null || sensor == null)
        {
            Debug.LogError($"{hand.name} needs a Rigidbody, BoxCollider and HandGroundSensor.", hand);
            return null;
        }

        handBody.isKinematic = true;
        handBody.useGravity = false;
        handBody.interpolation = RigidbodyInterpolation.Interpolate;
        handCollider.enabled = true;
        handCollider.isTrigger = true;
        sensor.Bind(gravityField.GroundCollider, rearCollider);

        return new HandState
        {
            transform = hand,
            body = handBody,
            sensor = sensor,
            restLocalPosition = hand.localPosition,
            halfHeight = handCollider.size.y * Mathf.Abs(hand.lossyScale.y) * 0.5f
        };
    }

    private SurfacePose SurfaceFor(HandState state, Vector3 nearSurface)
    {
        bool found = gravityField.SampleSupportSurface(nearSurface, rearCollider,
                                                       out Vector3 point, out Vector3 normal,
                                                       out Collider support,
                                                       Mathf.Max(footGroundSnapDistance,
                                                                 liftHeight + state.halfHeight + 0.75f));
        Vector3 groundedPosition = point + normal * (state.halfHeight - contactInset);
        if (!found || Vector3.Distance(nearSurface, groundedPosition) > footGroundSnapDistance)
        {
            normal = gravityField.OutwardNormal(nearSurface);
            support = null;
            groundedPosition = nearSurface;
        }
        return new SurfacePose
        {
            position = groundedPosition,
            normal = normal,
            rotation = Quaternion.LookRotation(Tangent(normal), normal),
            support = support
        };
    }

    private void Plant(HandState state, SurfacePose pose)
    {
        state.plantedPosition = pose.position;
        state.plantedRotation = pose.rotation;
        state.plantedNormal = pose.normal;
        state.plantedSupport = pose.support;
        if (pose.support != null)
        {
            state.plantedLocalPosition = pose.support.transform.InverseTransformPoint(pose.position);
            state.plantedLocalRotation = Quaternion.Inverse(pose.support.transform.rotation) * pose.rotation;
            state.plantedLocalNormal = pose.support.transform.InverseTransformDirection(pose.normal);
        }
        state.swinging = pose.support == null;
        state.body.MovePosition(pose.position);
        state.body.MoveRotation(pose.rotation);
    }

    private void UpdateHand(HandState state, float phaseOffset, Vector3 tangent)
    {
        UpdatePlantedPose(state);
        float phase = Mathf.Repeat(stepTime * stepCyclesPerSecond + phaseOffset, 1f);
        Vector3 nominal = transform.TransformPoint(state.restLocalPosition);
        Vector3 landingPosition = nominal + tangent * (stepLead * movementInput);

        if (phase < 0.5f)
        {
            if (state.swinging)
                Plant(state, SurfaceFor(state, landingPosition));
            else
            {
                state.body.MovePosition(state.plantedPosition);
                state.body.MoveRotation(state.plantedRotation);
            }
            return;
        }

        if (!state.swinging)
        {
            state.swingStart = state.body.position;
            state.swingStartRotation = state.body.rotation;
            state.swinging = true;
        }

        float swingT = (phase - 0.5f) * 2f;
        SurfacePose landing = SurfaceFor(state, landingPosition);
        Vector3 liftNormal = Vector3.Slerp(state.plantedNormal, landing.normal, swingT).normalized;
        Vector3 position = Vector3.Lerp(state.swingStart, landing.position, swingT) +
                           liftNormal * (Mathf.Sin(Mathf.PI * swingT) * liftHeight);
        state.body.MovePosition(position);
        state.body.MoveRotation(Quaternion.Slerp(state.swingStartRotation, landing.rotation, swingT));
    }

    private void UpdatePlantedPose(HandState state)
    {
        if (state.plantedSupport == null)
            return;

        Transform support = state.plantedSupport.transform;
        state.plantedPosition = support.TransformPoint(state.plantedLocalPosition);
        state.plantedRotation = support.rotation * state.plantedLocalRotation;
        state.plantedNormal = support.TransformDirection(state.plantedLocalNormal);
    }

    private void MoveAirborneHand(HandState state, Vector3 normal)
    {
        MoveAirborneHand(state, normal, Vector3.zero);
    }

    private void MoveAirborneHand(HandState state, Vector3 normal, Vector3 offset)
    {
        Vector3 position = transform.TransformPoint(state.restLocalPosition) + normal * 0.2f + offset;
        state.body.MovePosition(position);
        state.body.MoveRotation(frontBody.rotation);
        state.swingStart = position;
        state.swingStartRotation = frontBody.rotation;
        state.plantedNormal = normal;
        state.swinging = true;
    }

    private void LateUpdate()
    {
        if (previewCamera == null)
            return;

        // Only the body's radial position on the cylinder turns the camera.
        // Its physical yaw, pitch and wobble do not affect the view.
        Vector3 surfaceNormal = gravityField.OutwardNormal(transform.position);
        float surfaceAngle = Vector3.SignedAngle(cameraInitialNormal, surfaceNormal,
                                                 gravityField.Axis);
        Quaternion surfaceTurn = Quaternion.AngleAxis(surfaceAngle, gravityField.Axis);
        Vector3 target = transform.position + surfaceTurn * cameraInitialOffset;
        previewCamera.transform.position = Vector3.Lerp(
            previewCamera.transform.position, target,
            1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime));
        previewCamera.transform.rotation = surfaceTurn * cameraInitialRotation;
    }
}
