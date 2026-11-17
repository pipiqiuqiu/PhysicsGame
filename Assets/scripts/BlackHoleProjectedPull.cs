using UnityEngine;

/// <summary>Pulls a black hole sprite toward a target projected onto its own plane.</summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public sealed class BlackHoleProjectedPull : MonoBehaviour
{
    private const int FalloffTextureWidth = 128;
    private static readonly int TargetUvId = Shader.PropertyToID("_TargetUV");
    private static readonly int PullStrengthId = Shader.PropertyToID("_PullStrength");
    private static readonly int FalloffTexId = Shader.PropertyToID("_FalloffTex");

    [SerializeField] private Transform target;
    [Range(0f, 0.2f)] [SerializeField] private float maxPull = 0.06f;
    [Tooltip("X: normalized distance from center (0) to rim (1). Y: pull multiplier. The center is always 1 and the rim is always 0.")]
    [SerializeField] private AnimationCurve radialFalloff = CreateDefaultFalloff();

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Texture2D falloffTexture;
    private bool falloffDirty = true;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        if (radialFalloff == null || radialFalloff.length == 0)
            radialFalloff = CreateDefaultFalloff();
    }

    private static AnimationCurve CreateDefaultFalloff() => new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.8f, 0.75f, -0.6f, -0.6f),
        new Keyframe(1f, 0f, -4f, 0f));

    private void OnValidate() => falloffDirty = true;

    private void LateUpdate()
    {
        Sprite sprite = spriteRenderer.sprite;
        if (sprite == null)
            return;

        if (falloffDirty)
            BakeFalloffTexture();

        float desiredPull = 0f;
        Vector2 targetUv = new Vector2(0.5f, 0.5f);

        if (target != null)
        {
            // The local Z coordinate is the distance along the sprite normal.
            // Discarding it projects the character onto the sprite's XY plane.
            Vector3 localPoint = transform.InverseTransformPoint(target.position);
            Bounds bounds = sprite.bounds;
            Vector2 offset = new Vector2(localPoint.x - bounds.center.x,
                                          localPoint.y - bounds.center.y);
            Vector2 radii = new Vector2(Mathf.Max(bounds.extents.x, 0.0001f),
                                         Mathf.Max(bounds.extents.y, 0.0001f));
            float projectedRadius = new Vector2(offset.x / radii.x,
                                                offset.y / radii.y).magnitude;

            desiredPull = maxPull * EvaluateFalloff(projectedRadius);
            targetUv = new Vector2(0.5f + offset.x / bounds.size.x,
                                   0.5f + offset.y / bounds.size.y);
        }

        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(TargetUvId, new Vector4(targetUv.x, targetUv.y, 0f, 0f));
        propertyBlock.SetFloat(PullStrengthId, desiredPull);
        propertyBlock.SetTexture(FalloffTexId, falloffTexture);
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    private float EvaluateFalloff(float radius)
    {
        if (radius <= 0f)
            return 1f;
        if (radius >= 1f)
            return 0f;
        return Mathf.Clamp01(radialFalloff.Evaluate(radius));
    }

    private void BakeFalloffTexture()
    {
        if (falloffTexture == null)
        {
            falloffTexture = new Texture2D(FalloffTextureWidth, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Black Hole Radial Falloff",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        Color32[] pixels = new Color32[FalloffTextureWidth];
        for (int i = 0; i < pixels.Length; i++)
        {
            float value = EvaluateFalloff(i / (float)(pixels.Length - 1));
            byte channel = (byte)Mathf.RoundToInt(value * 255f);
            pixels[i] = new Color32(channel, channel, channel, 255);
        }

        falloffTexture.SetPixels32(pixels);
        falloffTexture.Apply(false, false);
        falloffDirty = false;
    }

    private void OnDisable()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(PullStrengthId, 0f);
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    private void OnDestroy()
    {
        if (falloffTexture != null)
            Destroy(falloffTexture);
    }
}
