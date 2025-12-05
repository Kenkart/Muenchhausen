using UnityEngine;
using System.Collections.Generic;

public class LocomotionTarget : MonoBehaviour
{
    [Header("Existing Accuracy Target")]
    [SerializeField] private Transform targetPoint;

    public GameObject markerPrefab;
    public bool showMarker = true;

    [Header("Spawn Settings")]
    public float spawnRadius = 60f;
    public Vector3 spawnCenter = Vector3.zero;

    [Header("Spawn Restrictions")]
    public float minDistanceFromFinalPos = 20f;

    [Tooltip("Add circular no-spawn zones: world position + radius.")]
    public List<NoSpawnCircle> noSpawnAreas = new();

    private Transform rootTransform;
    private Vector3 lastFinalPos;

    private void Awake()
    {
        if (targetPoint == null)
            Debug.LogError("[Target] No targetPoint assigned!");

        // cached reference to the prefab root
        rootTransform = transform.root;

        // Initial placement
        TeleportRootToRandomPoint();
    }

    public void OnLocomotionEvent(Vector3 finalPosition)
    {
        lastFinalPos = finalPosition;

        if (targetPoint == null)
        {
            Debug.LogError("[Target] targetPoint is NULL!");
            return;
        }

        // 1) Compute accuracy 
        Vector2 a = new(finalPosition.x, finalPosition.z);
        Vector2 b = new(targetPoint.position.x, targetPoint.position.z);
        float distance = Vector2.Distance(a, b);

        // Write accuracy to CSV via ExperimentManager instead of Debug.Log
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.RecordAccuracy(distance);
        }
        else
        {
            Debug.Log($"[Target] Distance: {distance} (no ExperimentManager present)");
        }

        // 3) Spawn marker at previous location
        Vector3 previousPos = rootTransform.position;
        if (showMarker && markerPrefab != null)
        {
            Instantiate(markerPrefab, previousPos, Quaternion.identity);
        }

        // 4) Teleport target to new location
        TeleportRootToRandomPoint();
    }

    private void TeleportRootToRandomPoint()
    {
        Vector3 newPos = new Vector3(0f, 0f, 0f);

        int attempts = 0;
        const int maxAttempts = 200;

        do
        {
            attempts++;
            if (attempts > maxAttempts)
            {
                Debug.LogWarning("[Target] Could not find a valid spawn position!");
                break;
            }

            // Pick random XZ inside circle
            Vector2 r = RandomInsideCircle(spawnRadius);
            newPos = new Vector3(r.x, 0f, r.y) + spawnCenter;

            // Snap Y to terrain height if available
            if (Terrain.activeTerrain != null)
            {
                float y = Terrain.activeTerrain.SampleHeight(newPos)
                          + Terrain.activeTerrain.transform.position.y;
                newPos.y = y;
            }

        }
        while (
            (Vector3.Distance(newPos, lastFinalPos) < minDistanceFromFinalPos) ||
            IsInsideNoSpawnArea(newPos)
        );

        rootTransform.position = newPos;
    }

    private bool IsInsideNoSpawnArea(Vector3 point)
    {
        foreach (var zone in noSpawnAreas)
        {
            Vector2 p = new(point.x, point.z);
            Vector2 center = new(zone.center.x, zone.center.z);

            if (Vector2.Distance(p, center) < zone.radius)
                return true;
        }
        return false;
    }

    private Vector2 RandomInsideCircle(float radius)
    {
        float angle = Random.value * 2f * Mathf.PI;
        float dist = Mathf.Sqrt(Random.value) * radius;
        return new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
    }
}

[System.Serializable]
public struct NoSpawnCircle
{
    public Vector3 center;
    public float radius;

    public NoSpawnCircle(Vector3 c, float r)
    {
        center = c;
        radius = r;
    }
}
