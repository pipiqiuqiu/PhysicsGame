using System.Collections.Generic;
using UnityEngine;

/// <summary>Spawns humans at random intervals and moves each one through the corridor.</summary>
public sealed class WalkingHumanCorridor : MonoBehaviour
{
    [SerializeField] private Transform humanTemplate;
    [SerializeField] private Transform begin;
    [SerializeField] private Transform end;
    [Min(0.01f)] [SerializeField] private float walkSpeed = 10f;
    [Min(0.01f)] [SerializeField] private float minSpawnInterval = 1f;
    [Min(0.01f)] [SerializeField] private float maxSpawnInterval = 5f;
    [Tooltip("Walking plane offset along the human's local Y axis. Negative values lower the human.")]
    public float localHeightOffset = 0f;
    [Min(0f)] [SerializeField] private float bobHeight = 0.25f;
    [Min(0f)] [SerializeField] private float bobFrequency = 2f;

    private sealed class Walker
    {
        public Transform human;
        public Vector3 pathPosition;
        public Vector3 endPosition;
        public float bobTime;
    }

    private readonly List<Walker> walkers = new List<Walker>();
    private SpriteRenderer beginSprite;
    private float spawnCountdown;

    private void Awake()
    {
        if (humanTemplate == null || begin == null || end == null)
        {
            Debug.LogError("WalkingHumanCorridor needs a human template and begin/end markers.", this);
            enabled = false;
            return;
        }

        beginSprite = begin.GetComponent<SpriteRenderer>();
        if (beginSprite == null || beginSprite.sprite == null)
        {
            Debug.LogError("The begin marker needs a SpriteRenderer with a sprite.", this);
            enabled = false;
            return;
        }

        // Keep the scene object as an editable template, but only show spawned copies in play mode.
        humanTemplate.gameObject.SetActive(false);
    }

    private void Start()
    {
        ResetSpawnCountdown();
    }

    private void Update()
    {
        spawnCountdown -= Time.deltaTime;
        if (spawnCountdown <= 0f)
        {
            SpawnHuman();
            ResetSpawnCountdown();
        }

        for (int i = walkers.Count - 1; i >= 0; i--)
        {
            Walker walker = walkers[i];
            if (walker.human == null)
            {
                walkers.RemoveAt(i);
                continue;
            }

            walker.pathPosition = Vector3.MoveTowards(
                walker.pathPosition, walker.endPosition,
                Mathf.Max(0.01f, walkSpeed) * Time.deltaTime);
            walker.bobTime += Time.deltaTime;
            float bobOffset = bobHeight * 0.5f *
                              (1f - Mathf.Cos(2f * Mathf.PI * bobFrequency * walker.bobTime));
            walker.human.position = walker.pathPosition +
                                    walker.human.up * (localHeightOffset + bobOffset);

            if ((walker.pathPosition - walker.endPosition).sqrMagnitude > 0.0001f)
                continue;

            // Destroy is deferred until the end of the frame, so hide the old human immediately.
            walker.human.gameObject.SetActive(false);
            Destroy(walker.human.gameObject);
            walkers.RemoveAt(i);
        }
    }

    private void ResetSpawnCountdown()
    {
        float minimum = Mathf.Max(0.01f, minSpawnInterval);
        spawnCountdown = Random.Range(minimum, Mathf.Max(minimum, maxSpawnInterval));
    }

    private void SpawnHuman()
    {
        Bounds bounds = beginSprite.sprite.bounds;
        Vector3 localPoint = new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            0f);
        Vector3 startPosition = begin.TransformPoint(localPoint);
        Vector3 endPosition = startPosition + end.position - begin.position;

        Transform human = Instantiate(humanTemplate, transform);
        human.SetPositionAndRotation(
            startPosition + humanTemplate.up * localHeightOffset, humanTemplate.rotation);
        human.gameObject.SetActive(true);
        walkers.Add(new Walker
        {
            human = human,
            pathPosition = startPosition,
            endPosition = endPosition
        });
    }
}
