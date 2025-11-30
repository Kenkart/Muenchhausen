using UnityEngine;

public class LocomotionTarget : MonoBehaviour
{
    [Header("Existing Accuracy Target")]
    [SerializeField] private Transform targetPoint;

    public GameObject markerPrefab;
    public bool showMarker = true;

    [Header("Spawn Settings")]
    public float spawnRadius = 60f;
    public Vector3 spawnCenter = Vector3.zero;

    private Transform rootTransform;

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
        if (targetPoint == null)
        {
            Debug.LogError("[Target] targetPoint is NULL!");
            return;
        }

        // 1) Compute accuracy 
        Vector2 a = new(finalPosition.x, finalPosition.z);
        Vector2 b = new(targetPoint.position.x, targetPoint.position.z);
        float distance = Vector2.Distance(a, b);

        // 2) Show distance in UI
        // DistanceFeedbackUI.Instance?.ShowDistance(distance);

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
        // Pick random XZ inside circle
        Vector2 r = RandomInsideCircle(spawnRadius);
        Vector3 newPos = new Vector3(r.x, 0f, r.y) + spawnCenter;

        // Snap Y to terrain height if available
        if (Terrain.activeTerrain != null)
        {
            float y = Terrain.activeTerrain.SampleHeight(newPos)
                      + Terrain.activeTerrain.transform.position.y;
            newPos.y = y;
        }

        // Teleport the PREFAB ROOT
        rootTransform.position = newPos;

#if UNITY_EDITOR
        Debug.Log($"[Target] Teleported root to {newPos}");
#endif
    }

    private Vector2 RandomInsideCircle(float radius)
    {
        float angle = Random.value * 2f * Mathf.PI;
        float dist = Mathf.Sqrt(Random.value) * radius;
        return new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
    }
}
