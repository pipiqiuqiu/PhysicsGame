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
    public int HeightCommand => heightCommand;
    public float JumpStartClearance => jumpStartClearance;
    public float ForwardTangentSign => forwardSign;

    [Header("References")]
    [SerializeField] private CylinderGravityField gravityField;
    [SerializeField] private Transform hand1;
    [SerializeField] private Transform hand2;
    [SerializeField] private Camera previewCamera;

    [Header("Tangential movement")]
    [SerializeField] private Vector3 forwardDirection = Vector3.back;
    [Min(0f)] [SerializeField] private float moveSpeed = 2.2f;
    [Min(0f)] [SerializeField] private float driveResponse = 8f;

    [Header("Distance from surface")]
    [Min(0f)] [SerializeField] private float surfaceClearance = 0f;
    [Min(1f)] [SerializeField] private float raisedClearanceMultiplier = 2f;
    [Range(0f, 1f)] [SerializeField] private float loweredClearanceMultiplier = 0.5f;
    [Min(0f)] [SerializeField] private float hoverSpring = 25f;
    [Min(0f)] [SerializeField] private float hoverDamping = 10f;

    [Header("Jump and orientation")]
    [Min(0f)] [SerializeField] private float jumpImpulse = 8f;
    [FormerlySerializedAs("bothHandsRearJumpBonus")]
    [Min(0f)] [SerializeField] private float rearHandJumpBonus = 6f;
    [Min(0f)] [SerializeField] private float orientationSpring = 35f;
    [Min(0f)] [SerializeField] private float orientationDamping = 8f;

    [Header("Jump rotation")]
    [Min(0f)] [SerializeField] private float jumpSpinImpulse = 4f;
    [Min(0f)] [SerializeField] private float airSpinAcceleration = 18f;
    [Min(0f)] [SerializeField] private float maxAirSpinSpeed = 6f;

    [Header("Hand steps")]
    [Min(0.1f)] [SerializeField] private float stepCyclesPerSecond = 1.35f;
    [Min(0f)] [SerializeField] private float liftHeight = 0.65f;
    [Min(0f)] [SerializeField] private float stepLead = 0.35f;
    [Min(0f)] [SerializeField] private float contactInset = 0.06f;
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
    private float jumpSpinInput;
    private float originalMaxAngularVelocity;
    private bool jumpRequested;
    private bool airborne;
    private bool spinJumpInProgress;
    private bool standingOnRear;

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
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame &&
            (rearController == null || !rearController.CanJumpFromFront))
        {
            jumpRequested = true;
            jumpSpinInput = movementInput;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        stepTime += dt;

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
        bool onRear = heightCommand == 0 && physicallyOnRear;
        Vector3 tangent = Tangent(normal);
        float tangentialSpeed = Vector3.Dot(frontBody.linearVelocity, tangent);

        frontBody.AddForce(tangent * ((movementInput * moveSpeed - tangentialSpeed) * driveResponse),
                           ForceMode.Acceleration);

        float heightMultiplier = heightCommand > 0 ? raisedClearanceMultiplier :
                                 heightCommand < 0 ? loweredClearanceMultiplier : 1f;
        float targetClearance = baseClearance * heightMultiplier;
        if (jumpRequested && !airborne &&
            (left.sensor.IsGrounded || right.sensor.IsGrounded || physicallyOnRear))
        {
            // Either planted hand touching the rear body adds the bonus impulse.
            bool handOnRear = rearCollider != null &&
                ((left.plantedSupport == rearCollider && left.sensor.IsOnPlatform) ||
                 (right.plantedSupport == rearCollider && right.sensor.IsOnPlatform));
            float impulse = jumpImpulse + (handOnRear ? rearHandJumpBonus : 0f);
            frontBody.AddForce(normal * impulse, ForceMode.Impulse);
            if (jumpSpinInput != 0f)
                frontBody.AddTorque(gravityField.Axis * (jumpSpinInput * jumpSpinImpulse),
                                    ForceMode.Impulse);
            airborne = true;
            spinJumpInProgress = true;
            standingOnRear = false;
            airTime = 0f;
            jumpStartClearance = clearance;
        }
        jumpRequested = false;
        jumpSpinInput = 0f;

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
            float acceleration = gravityField.GravityAcceleration +
                                 (targetClearance - clearance) * hoverSpring -
                                 radialSpeed * hoverDamping;
            frontBody.AddForce(normal * acceleration, ForceMode.Acceleration);
        }

        if (!airborne)
        {
            spinJumpInProgress = false;
            frontBody.maxAngularVelocity = originalMaxAngularVelocity;
        }
        if (spinJumpInProgress)
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

    private void AlignWithSurface(Vector3 normal, Vector3 tangent)
    {
        Quaternion targetRotation = Quaternion.LookRotation(tangent, normal);
        Quaternion error = targetRotation * Quaternion.Inverse(frontBody.rotation);
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;

        Vector3 torque = axis * (angle * Mathf.Deg2Rad * orientationSpring) -
                         frontBody.angularVelocity * orientationDamping;
        frontBody.AddTorque(torque, ForceMode.Acceleration);
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
        gravityField.SampleSupportSurface(nearSurface, rearCollider,
                                          out Vector3 point, out Vector3 normal,
                                          out Collider support,
                                          liftHeight + state.halfHeight + 0.75f);
        return new SurfacePose
        {
            position = point + normal * (state.halfHeight - contactInset),
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
        state.plantedLocalPosition = pose.support.transform.InverseTransformPoint(pose.position);
        state.plantedLocalRotation = Quaternion.Inverse(pose.support.transform.rotation) * pose.rotation;
        state.plantedLocalNormal = pose.support.transform.InverseTransformDirection(pose.normal);
        state.swinging = false;
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
        Vector3 position = transform.TransformPoint(state.restLocalPosition) + normal * 0.2f;
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
