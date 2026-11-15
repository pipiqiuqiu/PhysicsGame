using UnityEngine;

/// <summary>Keeps the two skinned lion body bones attached to the physics bodies.</summary>
[DefaultExecutionOrder(500)]
public sealed class LionBoneBinding : MonoBehaviour
{
    [SerializeField] private Transform lionRoot;
    [SerializeField] private Transform frontBody;
    [SerializeField] private Transform rearBody;

    private Transform frontBone;
    private Transform rearBone;
    private Vector3 frontLocalPosition;
    private Vector3 rearLocalPosition;
    private Quaternion frontLocalRotation;
    private Quaternion rearLocalRotation;
    private bool initialized;

    private void OnEnable()
    {
        // Scene Reload is disabled in this project, so Awake is not called on every Play.
        initialized = false;
        if (Application.isPlaying)
            Initialize();
    }

    private void Initialize()
    {
        frontBone = null;
        rearBone = null;

        if (lionRoot == null || frontBody == null || rearBody == null)
        {
            Debug.LogError("LionBoneBinding needs Lion, body_front and body_behind.", this);
            enabled = false;
            return;
        }

        foreach (SkinnedMeshRenderer renderer in lionRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.updateWhenOffscreen = true;
            foreach (Transform bone in renderer.bones)
                MatchBone(bone);
        }

        if (frontBone == null || rearBone == null)
        {
            foreach (Transform bone in lionRoot.GetComponentsInChildren<Transform>(true))
                MatchBone(bone);
        }

        if (frontBone == null || rearBone == null || frontBone == rearBone)
        {
            Debug.LogError("LionBoneBinding cannot find the body_1 and body_2 bones in Lion.", this);
            enabled = false;
            return;
        }

        frontLocalPosition = frontBody.InverseTransformPoint(frontBone.position);
        rearLocalPosition = rearBody.InverseTransformPoint(rearBone.position);
        frontLocalRotation = Quaternion.Inverse(frontBody.rotation) * frontBone.rotation;
        rearLocalRotation = Quaternion.Inverse(rearBody.rotation) * rearBone.rotation;
        initialized = true;
    }

    private void LateUpdate()
    {
        if (!initialized)
            Initialize();
        if (!initialized)
            return;

        Vector3 frontPosition = frontBody.TransformPoint(frontLocalPosition);
        Vector3 rearPosition = rearBody.TransformPoint(rearLocalPosition);
        Quaternion frontRotation = frontBody.rotation * frontLocalRotation;
        Quaternion rearRotation = rearBody.rotation * rearLocalRotation;

        if (frontBone.IsChildOf(rearBone))
        {
            rearBone.SetPositionAndRotation(rearPosition, rearRotation);
            frontBone.SetPositionAndRotation(frontPosition, frontRotation);
        }
        else
        {
            frontBone.SetPositionAndRotation(frontPosition, frontRotation);
            rearBone.SetPositionAndRotation(rearPosition, rearRotation);
        }
    }

    private void MatchBone(Transform candidate)
    {
        if (candidate == null)
            return;

        if (candidate.name == "body_1")
            frontBone = candidate;
        else if (candidate.name == "body_2")
            rearBone = candidate;
    }
}
