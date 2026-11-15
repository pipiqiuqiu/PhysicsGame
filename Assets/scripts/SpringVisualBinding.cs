using UnityEngine;

/// <summary>Draws a constant-width helical spring between the two body centers.</summary>
[ExecuteAlways]
[DefaultExecutionOrder(600)]
public sealed class SpringVisualBinding : MonoBehaviour
{
    [Header("Endpoints and material source")]
    [SerializeField] private Transform springRoot;
    [SerializeField] private Transform frontBody;
    [SerializeField] private Transform rearBody;

    [Header("Helix shape (world units)")]
    [Min(0f)] [SerializeField] private float coilRadius = 0.575f;
    [Min(0.005f)] [SerializeField] private float wireRadius = 0.094f;
    [Min(0.25f)] [SerializeField] private float turns = 2f;
    [Range(8, 128)] [SerializeField] private int segmentsPerTurn = 64;
    [Range(3, 32)] [SerializeField] private int tubeSides = 16;
    [Range(0f, 0.45f)] [SerializeField] private float endBlendFraction = 0.08f;

    private GameObject generatedObject;
    private Mesh generatedMesh;
    private MeshRenderer generatedRenderer;
    private Renderer[] sourceRenderers;
    private bool[] sourceForceRenderingOff;
    private Vector3[] vertices;
    private Vector3[] normals;
    private int segmentCount;
    private int ringStride;
    private bool topologyDirty = true;

    private void OnEnable()
    {
        topologyDirty = true;
        UpdateSpring();
    }

    private void OnDisable()
    {
        if (sourceRenderers != null)
        {
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                if (sourceRenderers[i] != null)
                    sourceRenderers[i].forceRenderingOff = sourceForceRenderingOff[i];
            }
        }
        sourceRenderers = null;
        sourceForceRenderingOff = null;

        if (generatedObject != null)
        {
            generatedObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(generatedObject);
            else
                DestroyImmediate(generatedObject);
        }
        if (generatedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);
        }
        generatedObject = null;
        generatedMesh = null;
        generatedRenderer = null;
    }

    private void OnValidate()
    {
        topologyDirty = true;
    }

    private void LateUpdate()
    {
        UpdateSpring();
    }

    private void UpdateSpring()
    {
        if (springRoot == null || frontBody == null || rearBody == null || !EnsureGeneratedObject())
            return;

        Vector3 start = frontBody.position;
        Vector3 difference = rearBody.position - start;
        float length = difference.magnitude;
        generatedRenderer.enabled = length > 0.001f;
        if (!generatedRenderer.enabled)
            return;

        if (topologyDirty)
            BuildTopology();

        generatedObject.transform.SetPositionAndRotation(
            start, Quaternion.FromToRotation(Vector3.up, difference / length));
        generatedObject.transform.localScale = Vector3.one;
        UpdateVertices(length);

        Material sourceMaterial = FindSourceMaterial();
        if (generatedRenderer.sharedMaterial != sourceMaterial)
            generatedRenderer.sharedMaterial = sourceMaterial;
    }

    private bool EnsureGeneratedObject()
    {
        if (generatedObject != null)
            return true;

        sourceRenderers = springRoot.GetComponentsInChildren<Renderer>(true);
        sourceForceRenderingOff = new bool[sourceRenderers.Length];
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            sourceForceRenderingOff[i] = sourceRenderers[i].forceRenderingOff;
            sourceRenderers[i].forceRenderingOff = true;
        }

        generatedObject = new GameObject("Spring curve (generated)");
        generatedObject.hideFlags = HideFlags.HideAndDontSave;
        generatedObject.transform.SetParent(transform, false);
        MeshFilter filter = generatedObject.AddComponent<MeshFilter>();
        generatedRenderer = generatedObject.AddComponent<MeshRenderer>();
        generatedRenderer.sharedMaterial = FindSourceMaterial();
        generatedMesh = new Mesh { name = "Procedural spring", hideFlags = HideFlags.HideAndDontSave };
        generatedMesh.MarkDynamic();
        filter.sharedMesh = generatedMesh;
        topologyDirty = true;
        return true;
    }

    private Material FindSourceMaterial()
    {
        if (sourceRenderers == null)
            return null;
        foreach (Renderer renderer in sourceRenderers)
        {
            if (renderer != null && renderer.sharedMaterial != null)
                return renderer.sharedMaterial;
        }
        return null;
    }

    private void BuildTopology()
    {
        segmentCount = Mathf.Max(1, Mathf.CeilToInt(turns * segmentsPerTurn));
        ringStride = tubeSides + 1;
        int vertexCount = (segmentCount + 1) * ringStride;
        vertices = new Vector3[vertexCount];
        normals = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        int[] triangles = new int[segmentCount * tubeSides * 6];

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            for (int side = 0; side <= tubeSides; side++)
                uv[i * ringStride + side] = new Vector2(t * turns, (float)side / tubeSides);
        }
        for (int i = 0; i < segmentCount; i++)
        for (int side = 0; side < tubeSides; side++)
        {
            int a = i * ringStride + side;
            int b = a + 1;
            int c = a + ringStride;
            int d = c + 1;
            int index = (i * tubeSides + side) * 6;
            triangles[index] = a;
            triangles[index + 1] = b;
            triangles[index + 2] = c;
            triangles[index + 3] = b;
            triangles[index + 4] = d;
            triangles[index + 5] = c;
        }

        generatedMesh.Clear();
        generatedMesh.vertices = vertices;
        generatedMesh.normals = normals;
        generatedMesh.uv = uv;
        generatedMesh.triangles = triangles;
        topologyDirty = false;
    }

    private void UpdateVertices(float length)
    {
        float radius = Mathf.Max(0f, coilRadius);
        float thickness = Mathf.Max(0.005f, wireRadius);
        float sampleStep = 0.5f / segmentCount;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 center = CurvePoint(t, length, radius);
            Vector3 tangent = (CurvePoint(Mathf.Min(1f, t + sampleStep), length, radius) -
                               CurvePoint(Mathf.Max(0f, t - sampleStep), length, radius)).normalized;
            float phase = Mathf.PI + 2f * Mathf.PI * turns * t;
            Vector3 outward = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase));
            Vector3 ringX = Vector3.ProjectOnPlane(outward, tangent).normalized;
            if (ringX.sqrMagnitude < 0.0001f)
                ringX = Vector3.ProjectOnPlane(Vector3.right, tangent).normalized;
            Vector3 ringZ = Vector3.Cross(tangent, ringX).normalized;

            for (int side = 0; side <= tubeSides; side++)
            {
                float angle = 2f * Mathf.PI * side / tubeSides;
                Vector3 normal = ringX * Mathf.Cos(angle) + ringZ * Mathf.Sin(angle);
                int index = i * ringStride + side;
                normals[index] = normal;
                vertices[index] = center + normal * thickness;
            }
        }

        generatedMesh.vertices = vertices;
        generatedMesh.normals = normals;
        generatedMesh.RecalculateBounds();
    }

    private Vector3 CurvePoint(float t, float length, float radius)
    {
        float blend = Mathf.Max(0.0001f, endBlendFraction);
        float endDistance = Mathf.Min(t, 1f - t);
        float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(endDistance / blend));
        float phase = Mathf.PI + 2f * Mathf.PI * turns * t;
        float r = radius * envelope;
        return new Vector3(r * Mathf.Cos(phase), length * t, r * Mathf.Sin(phase));
    }
}
