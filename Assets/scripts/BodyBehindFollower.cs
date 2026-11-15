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
    public bool CanJumpFromGround => enabled && !airborne &&
        (FootOnGround(left) || FootOnGround(right));
    public bool CanJumpWhileSupportingFront => enabled && !airborne &&
        frontController != null && frontController.IsSupportedByRear;
    public bool IsPullingFront => enabled && attractionActive && pullFront;

    public void ScheduleGroundJumpAfterFront()
    {
        if (groundJumpPending || !CanJumpFromGround || CanJumpFromFront)
            return;

        groundJumpPending = true;
        groundJumpReadyTime = Time.fixedTime + groundJumpDelay;
    }

    [Header("Follow target")]
    [SerializeField] private Rigidbody frontBody;
    [SerializeField] private FrontBodyPhysics frontController;
    [SerializeField] private CylinderGravityField gravityField;
    [Min(0f)] [SerializeField] private float positionGain = 3f;
    [Min(0f)] [SerializeField] private float velocityResponse = 8f;
    [Min(0f)] [SerializeField] private float maxFollowSpeed = 6f;
    [Min(0f)] [SerializeField] private float maxAcceleration = 30f;
    [Min(0f)] [SerializeField] private float heightHoldDistance = 5f;
    [Min(0f)] [SerializeField] private float axialSpring = 20f;
    [Min(0f)] [SerializeField] private float axialDamping = 8f;
    [Min(0f)] [SerializeField] private float hoverSpring = 25f;
    [Min(0f)] [SerializeField] private float hoverDamping = 10f;
    [Min(1f)] [SerializeField] private float raisedClearanceMultiplier = 2f;
    [Range(0f, 1f)] [SerializeField] private float loweredClearanceMultiplier = 0.5f;

    [Header("Body spacing")]
    [Min(0f)] [SerializeField] private float separationSpring = 18f;
    [Min(0f)] [SerializeField] private float separationDamping = 5f;
    [Min(0f)] [SerializeField] private float maxSeparationAcceleration = 30f;

    [Header("Body attraction")]
    [Min(0f)] [SerializeField] private float attractionStartDistance = 6f;
    [Min(0f)] [SerializeField] private float attractionSpring = 50f;
    [Min(0f)] [SerializeField] private float attractionDamping = 12f;
    [Min(0f)] [SerializeField] private float maxAttractionAcceleration = 100f;
    [Min(0f)] [SerializeField] private float maxAttractionClosingSpeed = 8f;
    [Min(0f)] [SerializeField] private float attractionSpinSpeed = 4f;
    [Min(0f)] [SerializeField] private float attractionSpinResponse = 10f;
    [Min(0f)] [SerializeField] private float maxAttractionSpinAcceleration = 20f;

    [Header("Rear jump")]
    [Min(0f)] [SerializeField] private float footOnGroundJumpImpulse = 8f;
    [Min(0f)] [SerializeField] private float groundJumpDelay = 0.5f;
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
    private float defaultBodyDistance;
    private float baseClearance;
    private float forwardSign;
    private float stepTime;
    private float rearHalfHeight;
    private float airTime;
    private float horizontalInput;
    private float jumpDirectionInput;
    private float groundJumpReadyTime;
    private bool jumpRequested;
    private bool groundJumpPending;
    private bool jumpFromFrontRequested;
    private bool airborne;
    private bool pauseFollowDuringJump;
    private bool standingOnFront;
    private bool attractionActive;
    private bool pullFront;

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
        frontCollider = frontBody.GetComponent<Collider>();
        rearCollider = GetComponent<Collider>();
        if (rearCollider is BoxCollider rearBox)
            rearHalfHeight = rearBox.size.y * Mathf.Abs(rearBox.transform.lossyScale.y) * 0.5f;
        else
            rearHalfHeight = 1f;
        rearBody.isKinematic = false;
        rearBody.useGravity = false;
        Vector3 rearPosition = rearBody.position;
        rearPosition.x = 0f;
        rearBody.position = rearPosition;
        rearBody.constraints |= RigidbodyConstraints.FreezePositionX;
        rearBody.interpolation = RigidbodyInterpolation.Interpolate;
        rearBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 axis = gravityField.Axis;
        Vector3 frontNormal = gravityField.OutwardNormal(frontBody.position);
        Vector3 rearNormal = gravityField.OutwardNormal(rearBody.position);
        initialAngleOffset = Vector3.SignedAngle(frontNormal, rearNormal, axis);
        followAngleOffset = initialAngleOffset;
        initialAxialOffset = Vector3.Dot(rearBody.position - frontBody.position, axis);
        defaultBodyDistance = Vector3.Distance(frontBody.position, rearBody.position);

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
        horizontalInput = keyboard == null ? 0f :
            (keyboard.dKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed ? 1f : 0f);
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            bool jumpFromFront = CanJumpFromFront;
            bool supportingFront = CanJumpWhileSupportingFront;
            if (jumpFromFront || supportingFront)
            {
                groundJumpPending = false;
                jumpRequested = true;
                jumpFromFrontRequested = jumpFromFront;
                jumpDirectionInput = jumpFromFront ? horizontalInput : 0f;
            }
        }
    }

    private void FixedUpdate()
    {
        stepTime += Time.fixedDeltaTime;
        if (groundJumpPending && Time.fixedTime >= groundJumpReadyTime)
        {
            groundJumpPending = false;
            if (!airborne)
            {
                jumpRequested = true;
                jumpFromFrontRequested = false;
                jumpDirectionInput = 0f;
            }
        }
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
        gravityField.SampleSurface(frontBody.position, out Vector3 frontSurface, out _);
        float frontClearance = Vector3.Dot(frontBody.position - frontSurface, frontNormal);
        ApplyAttraction(frontClearance, rearClearance);
        if (attractionActive && !pullFront)
        {
            if (!airborne)
            {
                airborne = true;
                standingOnFront = false;
                airTime = 0f;
            }
        }
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
            float impulse = jumpFromFrontRequested ? footOnFrontJumpImpulse : footOnGroundJumpImpulse;
            rearBody.AddForce(rearNormal * impulse, ForceMode.Impulse);
            if (jumpFromFrontRequested && jumpDirectionInput != 0f)
            {
                Vector3 travelDirection = positiveTangent * frontController.ForwardTangentSign;
                rearBody.AddForce(travelDirection *
                                  (jumpDirectionInput * footOnFrontTravelImpulse),
                                  ForceMode.Impulse);
            }
            airborne = true;
            pauseFollowDuringJump = jumpFromFrontRequested;
            standingOnFront = false;
            airTime = 0f;
        }
        jumpRequested = false;
        jumpFromFrontRequested = false;
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
                else if ((FootOnGround(left) || FootOnGround(right)) &&
                         Vector3.Dot(rearBody.linearVelocity, rearNormal) <= 0f)
                {
                    airborne = false;
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
        // Measure separation along the cylinder so changing height alone does
        // not make the rear body resume following.
        float groundRadius = Vector3.ProjectOnPlane(rearSurface - gravityField.Center, axis).magnitude;
        float arcDistanceToFront = Vector3.Angle(frontNormal, rearNormal) * Mathf.Deg2Rad * groundRadius;
        float axialDistanceToFront = Vector3.Dot(frontBody.position - rearBody.position, axis);
        float distanceToFront = Mathf.Sqrt(arcDistanceToFront * arcDistanceToFront +
                                          axialDistanceToFront * axialDistanceToFront);
        bool holdForHeightChange = heightCommand != 0 && distanceToFront <= heightHoldDistance;
        // Keep following the front body while it is airborne after a jump.
        float desiredSpeed = frontController.IsStandingOnRear || holdForHeightChange
            ? 0f
            : Mathf.Clamp(frontSpeed + arcError * positionGain,
                          -maxFollowSpeed, maxFollowSpeed);
        float tangentialAcceleration = Mathf.Clamp((desiredSpeed - rearSpeed) * velocityResponse,
                                                    -maxAcceleration, maxAcceleration);
        if (!pauseFollowDuringJump && !attractionActive)
            rearBody.AddForce(positiveTangent * tangentialAcceleration, ForceMode.Acceleration);

        ApplySeparation(frontNormal, rearNormal, axis, rearRadius);

        float axialError = Vector3.Dot(frontBody.position - rearBody.position, axis) +
                           initialAxialOffset;
        float axialSpeed = Vector3.Dot(rearBody.linearVelocity, axis);
        if (!attractionActive)
            rearBody.AddForce(axis * (axialError * axialSpring - axialSpeed * axialDamping),
                              ForceMode.Acceleration);

        float radialSpeed = Vector3.Dot(rearBody.linearVelocity, rearNormal);
        float radialAcceleration = gravityField.GravityAcceleration +
                                   (desiredClearance - rearClearance) * hoverSpring -
                                   radialSpeed * hoverDamping;
        if (!airborne && !standingOnFront && !(attractionActive && !pullFront))
            rearBody.AddForce(rearNormal * radialAcceleration, ForceMode.Acceleration);
        if (!airborne)
        {
            pauseFollowDuringJump = false;
        }
        if (!attractionActive || pullFront)
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

    private static bool FootOnGround(FootState foot)
    {
        return foot != null && foot.sensor.IsOnGround;
    }

    private void ApplyAttraction(float frontClearance, float rearClearance)
    {
        float distance = Vector3.Distance(frontBody.position, rearBody.position);
        if (!attractionActive)
        {
            float startDistance = Mathf.Max(attractionStartDistance, defaultBodyDistance + 0.1f);
            if (distance <= startDistance)
                return;

            // At equal heights, keep the front body as the anchor.
            pullFront = frontClearance < rearClearance;
            attractionActive = true;
        }

        Rigidbody lowerBody = pullFront ? frontBody : rearBody;
        Rigidbody higherBody = pullFront ? rearBody : frontBody;
        Vector3 towardHigher = (higherBody.position - lowerBody.position).normalized;
        float closingSpeed = Vector3.Dot(lowerBody.linearVelocity - higherBody.linearVelocity,
                                         towardHigher);
        if (distance <= defaultBodyDistance + 0.15f && Mathf.Abs(closingSpeed) <= 0.5f)
        {
            attractionActive = false;
            return;
        }

        float acceleration = Mathf.Clamp(
            (distance - defaultBodyDistance) * attractionSpring -
            closingSpeed * attractionDamping,
            -maxAttractionAcceleration, maxAttractionAcceleration);
        if (maxAttractionClosingSpeed > 0f)
        {
            float speedLimitAcceleration =
                (maxAttractionClosingSpeed - closingSpeed) / Time.fixedDeltaTime;
            acceleration = Mathf.Min(acceleration,
                                     Mathf.Max(-maxAttractionAcceleration,
                                               speedLimitAcceleration));
        }
        lowerBody.AddForce(towardHigher * acceleration, ForceMode.Acceleration);

        Vector3 axis = gravityField.Axis;
        Vector3 tangent = Vector3.Cross(axis,
                                        gravityField.OutwardNormal(lowerBody.position)).normalized;
        float tangentialPull = Vector3.Dot(towardHigher, tangent);
        float desiredAngularSpeed = Mathf.Abs(tangentialPull) > 0.05f
            ? Mathf.Sign(tangentialPull) * attractionSpinSpeed : 0f;
        float currentAngularSpeed = Vector3.Dot(lowerBody.angularVelocity, axis);
        float spinAcceleration = Mathf.Clamp(
            (desiredAngularSpeed - currentAngularSpeed) * attractionSpinResponse,
            -maxAttractionSpinAcceleration, maxAttractionSpinAcceleration);
        lowerBody.AddTorque(axis * spinAcceleration, ForceMode.Acceleration);
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
        rearBody.angularVelocity = Vector3.zero;
        rearBody.MoveRotation(Quaternion.LookRotation(Tangent(normal), normal));
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
