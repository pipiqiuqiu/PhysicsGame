using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Follows the front body around the cylinder using forces. The feet step on
/// the surface with a phase offset so the rear motion trails the front.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class BodyBehindFollower : MonoBehaviour
{
    public bool CanJumpFromFront => enabled && !airborne &&
        (FootOnFront(left) || FootOnFront(right));

    [Header("Follow target")]
    [SerializeField] private Rigidbody frontBody;
    [SerializeField] private FrontBodyPhysics frontController;
    [SerializeField] private CylinderGravityField gravityField;
    [Min(0f)] [SerializeField] private float positionGain = 3f;
    [Min(0f)] [SerializeField] private float velocityResponse = 8f;
    [Min(0f)] [SerializeField] private float maxFollowSpeed = 6f;
    [Min(0f)] [SerializeField] private float maxAcceleration = 30f;
    [Min(0f)] [SerializeField] private float axialSpring = 20f;
    [Min(0f)] [SerializeField] private float axialDamping = 8f;
    [Min(0f)] [SerializeField] private float hoverSpring = 25f;
    [Min(0f)] [SerializeField] private float hoverDamping = 10f;
    [Min(1f)] [SerializeField] private float raisedClearanceMultiplier = 2f;
    [Range(0f, 1f)] [SerializeField] private float loweredClearanceMultiplier = 0.5f;
    [Min(0f)] [SerializeField] private float orientationSpring = 35f;
    [Min(0f)] [SerializeField] private float orientationDamping = 8f;

    [Header("Jump rotation")]
    [Min(0f)] [SerializeField] private float jumpSpinImpulse = 4f;
    [Min(0f)] [SerializeField] private float airSpinAcceleration = 18f;
    [Min(0f)] [SerializeField] private float maxAirSpinSpeed = 6f;

    [Header("Body spacing")]
    [Min(0f)] [SerializeField] private float separationSpring = 18f;
    [Min(0f)] [SerializeField] private float separationDamping = 5f;
    [Min(0f)] [SerializeField] private float maxSeparationAcceleration = 30f;

    [Header("Rear jump")]
    [Min(0f)] [SerializeField] private float footOnFrontJumpImpulse = 8f;
    [Min(0f)] [SerializeField] private float footOnFrontTravelImpulse = 4f;

    [Header("Feet")]
    [SerializeField] private Transform foot3;
    [SerializeField] private Transform foot4;
    [Min(0.1f)] [SerializeField] private float stepCyclesPerSecond = 1.35f;
    [Min(0f)] [SerializeField] private float liftHeight = 0.65f;
    [Min(0f)] [SerializeField] private float stepLead = 0.35f;
    [Min(0f)] [SerializeField] private float contactInset = 0.06f;
    [Min(0.1f)] [SerializeField] private float strideSpeed = 2.2f;

    private sealed class FootState
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

    private Rigidbody rearBody;
    private Collider frontCollider;
    private Collider rearCollider;
    private FootState left;
    private FootState right;
    private float initialAngleOffset;
    private float followAngleOffset;
    private float initialAxialOffset;
    private float baseClearance;
    private float forwardSign;
    private float stepTime;
    private float rearHalfHeight;
    private float airTime;
    private float airSpinInput;
    private float jumpDirectionInput;
    private float originalMaxAngularVelocity;
    private bool jumpRequested;
    private bool airborne;
    private bool spinJumpInProgress;
    private bool standingOnFront;

    private void Awake()
    {
        if (frontBody == null || frontController == null || gravityField == null ||
            foot3 == null || foot4 == null || !gravityField.Initialize())
        {
            Debug.LogError("BodyBehindFollower needs the front body, gravity field and both feet.", this);
            enabled = false;
            return;
        }

        rearBody = GetComponent<Rigidbody>();
        originalMaxAngularVelocity = rearBody.maxAngularVelocity;
        frontCollider = frontBody.GetComponent<Collider>();
        rearCollider = GetComponent<Collider>();
        if (rearCollider is BoxCollider rearBox)
            rearHalfHeight = rearBox.size.y * Mathf.Abs(rearBox.transform.lossyScale.y) * 0.5f;
        else
            rearHalfHeight = 1f;
        rearBody.isKinematic = false;
        rearBody.useGravity = false;
        rearBody.interpolation = RigidbodyInterpolation.Interpolate;
        rearBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 axis = gravityField.Axis;
        Vector3 frontNormal = gravityField.OutwardNormal(frontBody.position);
        Vector3 rearNormal = gravityField.OutwardNormal(rearBody.position);
        initialAngleOffset = Vector3.SignedAngle(frontNormal, rearNormal, axis);
        followAngleOffset = initialAngleOffset;
        initialAxialOffset = Vector3.Dot(rearBody.position - frontBody.position, axis);

        gravityField.SampleSurface(rearBody.position, out Vector3 rearSurface, out _);
        baseClearance = Mathf.Max(0.5f,
            Vector3.Dot(rearBody.position - rearSurface, rearNormal));

        Vector3 positiveTangent = Vector3.Cross(axis, rearNormal).normalized;
        forwardSign = Vector3.Dot(frontBody.position - rearBody.position, positiveTangent) < 0f
            ? -1f : 1f;
        rearBody.rotation = Quaternion.LookRotation(Tangent(rearNormal), rearNormal);

        left = CreateFoot(foot3);
        right = CreateFoot(foot4);
        if (left == null || right == null)
        {
            enabled = false;
            return;
        }

        Plant(left, SurfaceFor(left, transform.TransformPoint(left.restLocalPosition)));
        Plant(right, SurfaceFor(right, transform.TransformPoint(right.restLocalPosition)));
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        airSpinInput = keyboard == null ? 0f :
            (keyboard.dKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed ? 1f : 0f);
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && CanJumpFromFront)
        {
            jumpRequested = true;
            jumpDirectionInput = airSpinInput;
        }
    }

    private void FixedUpdate()
    {
        stepTime += Time.fixedDeltaTime;
        Vector3 axis = gravityField.Axis;
        Vector3 frontNormal = gravityField.OutwardNormal(frontBody.position);
        Vector3 rearNormal = gravityField.OutwardNormal(rearBody.position);
        Vector3 positiveTangent = Vector3.Cross(axis, rearNormal).normalized;
        float rearRadius = Vector3.ProjectOnPlane(rearBody.position - gravityField.Center, axis).magnitude;
        float currentOffset = Vector3.SignedAngle(frontNormal, rearNormal, axis);
        int heightCommand = frontController.HeightCommand;

        // Once the front body passes through to the other side, keep the rear
        // body on the new trailing side instead of chasing the old offset.
        if (currentOffset * followAngleOffset < 0f &&
            Mathf.Abs(currentOffset) * Mathf.Deg2Rad * rearRadius > 0.75f)
        {
            followAngleOffset = -followAngleOffset;
        }
        Vector3 targetNormal = Quaternion.AngleAxis(followAngleOffset, axis) * frontNormal;

        gravityField.SampleSurface(rearBody.position, out Vector3 rearSurface, out _);
        float rearClearance = Vector3.Dot(rearBody.position - rearSurface, rearNormal);
        float heightMultiplier = frontController.HeightCommand > 0 ? loweredClearanceMultiplier :
                                 frontController.HeightCommand < 0 ? raisedClearanceMultiplier : 1f;
        float desiredClearance = baseClearance * heightMultiplier;
        desiredClearance = Mathf.Max(0.5f, desiredClearance);
        gravityField.SampleSupportSurface(rearBody.position, frontCollider,
                                          out Vector3 supportPoint, out Vector3 supportNormal,
                                          out Collider support);
        float supportGap = Vector3.Dot(rearBody.position - supportPoint, supportNormal);
        float relativeRadialSpeed = Vector3.Dot(rearBody.linearVelocity -
            frontBody.GetPointVelocity(supportPoint), supportNormal);
        bool physicallyOnFront = support == frontCollider && frontCollider != null &&
            supportGap >= rearHalfHeight - 0.25f &&
            supportGap <= rearHalfHeight + 0.35f &&
            relativeRadialSpeed <= 0.5f;
        bool onFront = heightCommand == 0 && physicallyOnFront;

        if (jumpRequested && !airborne)
        {
            rearBody.AddForce(rearNormal * footOnFrontJumpImpulse, ForceMode.Impulse);
            if (jumpDirectionInput != 0f)
            {
                Vector3 travelDirection = positiveTangent * frontController.ForwardTangentSign;
                rearBody.AddForce(travelDirection *
                                  (jumpDirectionInput * footOnFrontTravelImpulse),
                                  ForceMode.Impulse);
                rearBody.AddTorque(axis * (jumpDirectionInput * jumpSpinImpulse),
                                   ForceMode.Impulse);
            }
            airborne = true;
            spinJumpInProgress = true;
            standingOnFront = false;
            airTime = 0f;
        }
        jumpRequested = false;
        jumpDirectionInput = 0f;

        if (airborne)
        {
            airTime += Time.fixedDeltaTime;
            if (airTime >= 0.25f)
            {
                if (onFront)
                {
                    airborne = false;
                    standingOnFront = true;
                }
                else if (rearClearance <= desiredClearance + 0.15f &&
                         Vector3.Dot(rearBody.linearVelocity, rearNormal) <= 0f)
                {
                    airborne = false;
                }
            }
        }
        else if (onFront)
        {
            standingOnFront = true;
        }
        else if (standingOnFront)
        {
            standingOnFront = false;
            if (rearClearance > desiredClearance + 0.15f)
            {
                airborne = true;
                airTime = 0f;
            }
        }

        // The target offset changes sides when the front body passes the rear.
        float angleError = Vector3.SignedAngle(rearNormal, targetNormal, axis) * Mathf.Deg2Rad;
        float arcError = angleError * rearRadius;
        float frontSpeed = Vector3.Dot(frontBody.linearVelocity,
                                       Vector3.Cross(axis, frontNormal).normalized);
        float rearSpeed = Vector3.Dot(rearBody.linearVelocity, positiveTangent);
        // W or S changes the bodies' heights while the rear brakes in place.
        // Its normal follow behavior resumes when neither height key is held.
        bool holdForHeightChange = heightCommand != 0;
        float desiredSpeed = frontController.IsAirborne || frontController.IsStandingOnRear ||
                             holdForHeightChange
            ? 0f
            : Mathf.Clamp(frontSpeed + arcError * positionGain,
                          -maxFollowSpeed, maxFollowSpeed);
        float tangentialAcceleration = Mathf.Clamp((desiredSpeed - rearSpeed) * velocityResponse,
                                                    -maxAcceleration, maxAcceleration);
        if (!spinJumpInProgress)
            rearBody.AddForce(positiveTangent * tangentialAcceleration, ForceMode.Acceleration);

        ApplySeparation(frontNormal, rearNormal, axis, rearRadius);

        float axialError = Vector3.Dot(frontBody.position - rearBody.position, axis) +
                           initialAxialOffset;
        float axialSpeed = Vector3.Dot(rearBody.linearVelocity, axis);
        rearBody.AddForce(axis * (axialError * axialSpring - axialSpeed * axialDamping),
                          ForceMode.Acceleration);

        float radialSpeed = Vector3.Dot(rearBody.linearVelocity, rearNormal);
        float radialAcceleration = gravityField.GravityAcceleration +
                                   (desiredClearance - rearClearance) * hoverSpring -
                                   radialSpeed * hoverDamping;
        if (!airborne && !standingOnFront)
            rearBody.AddForce(rearNormal * radialAcceleration, ForceMode.Acceleration);
        if (!airborne)
        {
            spinJumpInProgress = false;
            rearBody.maxAngularVelocity = originalMaxAngularVelocity;
        }
        if (spinJumpInProgress)
            ApplyAirSpin(airSpinInput);
        else
            AlignWithSurface(rearNormal);

        if (airborne)
        {
            MoveAirborneFoot(left, rearNormal);
            MoveAirborneFoot(right, rearNormal);
        }
        else
        {
            float strideInput = Mathf.Clamp(
                Vector3.Dot(rearBody.linearVelocity, Tangent(rearNormal)) / strideSpeed, -1f, 1f);
            UpdateFoot(left, 0.25f, strideInput);
            UpdateFoot(right, 0.75f, strideInput);
        }
    }

    private bool FootOnFront(FootState foot)
    {
        return foot != null && frontCollider != null &&
               !foot.swinging && foot.plantedSupport == frontCollider &&
               foot.sensor.IsOnPlatform;
    }

    private void ApplySeparation(Vector3 frontNormal, Vector3 rearNormal,
                                 Vector3 axis, float rearRadius)
    {
        if (frontCollider == null || rearCollider == null)
            return;

        Vector3 middleNormal = (frontNormal + rearNormal).normalized;
        if (middleNormal.sqrMagnitude < 0.01f)
            return;

        float radialDistance = Mathf.Abs(Vector3.Dot(frontBody.position - rearBody.position,
                                                     middleNormal));
        float radialContactDistance = ExtentAlong(frontCollider, middleNormal) +
                                      ExtentAlong(rearCollider, middleNormal);
        if (radialDistance >= radialContactDistance - 0.15f)
            return;

        float arcDistance = Vector3.SignedAngle(rearNormal, frontNormal, axis) *
                            Mathf.Deg2Rad * rearRadius;
        float desiredDistance = Mathf.Abs(initialAngleOffset) * Mathf.Deg2Rad * rearRadius;
        float compression = desiredDistance - Mathf.Abs(arcDistance);
        if (compression <= 0f)
            return;

        float side = Mathf.Abs(arcDistance) > 0.01f
            ? Mathf.Sign(arcDistance) : -Mathf.Sign(followAngleOffset);
        Vector3 frontTangent = Vector3.Cross(axis, frontNormal).normalized;
        Vector3 rearTangent = Vector3.Cross(axis, rearNormal).normalized;
        float relativeSpeed = Vector3.Dot(frontBody.linearVelocity, frontTangent) -
                              Vector3.Dot(rearBody.linearVelocity, rearTangent);
        float closingSpeed = Mathf.Max(0f, -side * relativeSpeed);
        float acceleration = Mathf.Min(maxSeparationAcceleration,
            compression * separationSpring + closingSpeed * separationDamping);

        frontBody.AddForce(frontTangent * (side * acceleration), ForceMode.Acceleration);
        rearBody.AddForce(rearTangent * (-side * acceleration), ForceMode.Acceleration);
    }

    private static float ExtentAlong(Collider collider, Vector3 direction)
    {
        if (collider is BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            Transform support = box.transform;
            return Mathf.Abs(Vector3.Dot(support.TransformVector(Vector3.right * half.x), direction)) +
                   Mathf.Abs(Vector3.Dot(support.TransformVector(Vector3.up * half.y), direction)) +
                   Mathf.Abs(Vector3.Dot(support.TransformVector(Vector3.forward * half.z), direction));
        }

        Vector3 extent = collider.bounds.extents;
        return Vector3.Dot(extent, new Vector3(Mathf.Abs(direction.x),
                                               Mathf.Abs(direction.y), Mathf.Abs(direction.z)));
    }

    private void AlignWithSurface(Vector3 normal)
    {
        Quaternion target = Quaternion.LookRotation(Tangent(normal), normal);
        Quaternion error = target * Quaternion.Inverse(rearBody.rotation);
        error.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;

        Vector3 torque = axis * (angle * Mathf.Deg2Rad * orientationSpring) -
                         rearBody.angularVelocity * orientationDamping;
        rearBody.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyAirSpin(float input)
    {
        rearBody.maxAngularVelocity = Mathf.Max(originalMaxAngularVelocity, maxAirSpinSpeed);
        if (input == 0f || maxAirSpinSpeed <= 0f)
            return;

        Vector3 axis = gravityField.Axis;
        float remainingSpeed = maxAirSpinSpeed -
                               Vector3.Dot(rearBody.angularVelocity, axis) * input;
        if (remainingSpeed > 0f)
        {
            float acceleration = Mathf.Min(airSpinAcceleration,
                                           remainingSpeed / Time.fixedDeltaTime);
            rearBody.AddTorque(axis * (input * acceleration), ForceMode.Acceleration);
        }
    }

    private Vector3 Tangent(Vector3 normal)
    {
        return Vector3.Cross(gravityField.Axis, normal).normalized * forwardSign;
    }

    private FootState CreateFoot(Transform foot)
    {
        Rigidbody footBody = foot.GetComponent<Rigidbody>();
        BoxCollider footCollider = foot.GetComponent<BoxCollider>();
        if (footBody == null || footCollider == null)
        {
            Debug.LogError($"{foot.name} needs a Rigidbody and BoxCollider.", foot);
            return null;
        }

        footBody.isKinematic = true;
        footBody.useGravity = false;
        footBody.interpolation = RigidbodyInterpolation.Interpolate;
        footCollider.enabled = true;
        footCollider.isTrigger = true;
        HandGroundSensor sensor = foot.GetComponent<HandGroundSensor>();
        if (sensor == null)
            sensor = foot.gameObject.AddComponent<HandGroundSensor>();
        sensor.Bind(gravityField.GroundCollider, frontCollider);

        return new FootState
        {
            transform = foot,
            body = footBody,
            sensor = sensor,
            restLocalPosition = foot.localPosition,
            halfHeight = footCollider.size.y * Mathf.Abs(foot.lossyScale.y) * 0.5f
        };
    }

    private SurfacePose SurfaceFor(FootState foot, Vector3 nearSurface)
    {
        gravityField.SampleSupportSurface(nearSurface, frontCollider,
                                          out Vector3 point, out Vector3 normal,
                                          out Collider support,
                                          liftHeight + foot.halfHeight + 0.75f);
        return new SurfacePose
        {
            position = point + normal * (foot.halfHeight - contactInset),
            normal = normal,
            rotation = Quaternion.LookRotation(Tangent(normal), normal),
            support = support
        };
    }

    private void Plant(FootState foot, SurfacePose pose)
    {
        foot.plantedPosition = pose.position;
        foot.plantedRotation = pose.rotation;
        foot.plantedNormal = pose.normal;
        foot.plantedSupport = pose.support;
        foot.plantedLocalPosition = pose.support.transform.InverseTransformPoint(pose.position);
        foot.plantedLocalRotation = Quaternion.Inverse(pose.support.transform.rotation) * pose.rotation;
        foot.plantedLocalNormal = pose.support.transform.InverseTransformDirection(pose.normal);
        foot.swinging = false;
        foot.body.MovePosition(pose.position);
        foot.body.MoveRotation(pose.rotation);
    }

    private void UpdateFoot(FootState foot, float phaseOffset, float strideInput)
    {
        UpdatePlantedPose(foot);
        float phase = Mathf.Repeat(stepTime * stepCyclesPerSecond + phaseOffset, 1f);
        Vector3 nominal = transform.TransformPoint(foot.restLocalPosition);
        Vector3 landingPosition = nominal + transform.forward * (stepLead * strideInput);

        if (phase < 0.5f)
        {
            if (foot.swinging)
                Plant(foot, SurfaceFor(foot, landingPosition));
            else
            {
                foot.body.MovePosition(foot.plantedPosition);
                foot.body.MoveRotation(foot.plantedRotation);
            }
            return;
        }

        if (!foot.swinging)
        {
            foot.swingStart = foot.body.position;
            foot.swingStartRotation = foot.body.rotation;
            foot.swinging = true;
        }

        float swingT = (phase - 0.5f) * 2f;
        SurfacePose landing = SurfaceFor(foot, landingPosition);
        Vector3 liftNormal = Vector3.Slerp(foot.plantedNormal, landing.normal, swingT).normalized;
        Vector3 position = Vector3.Lerp(foot.swingStart, landing.position, swingT) +
                           liftNormal * (Mathf.Sin(Mathf.PI * swingT) * liftHeight);
        foot.body.MovePosition(position);
        foot.body.MoveRotation(Quaternion.Slerp(foot.swingStartRotation, landing.rotation, swingT));
    }

    private void UpdatePlantedPose(FootState foot)
    {
        if (foot.plantedSupport == null)
            return;

        Transform support = foot.plantedSupport.transform;
        foot.plantedPosition = support.TransformPoint(foot.plantedLocalPosition);
        foot.plantedRotation = support.rotation * foot.plantedLocalRotation;
        foot.plantedNormal = support.TransformDirection(foot.plantedLocalNormal);
    }

    private void MoveAirborneFoot(FootState foot, Vector3 normal)
    {
        Vector3 position = transform.TransformPoint(foot.restLocalPosition) + normal * 0.2f;
        foot.body.MovePosition(position);
        foot.body.MoveRotation(rearBody.rotation);
        foot.swingStart = position;
        foot.swingStartRotation = rearBody.rotation;
        foot.plantedNormal = normal;
        foot.swinging = true;
    }
}
