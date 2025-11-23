using UnityEngine;

public class LocomotionEventRelay : MonoBehaviour
{
    public static LocomotionEventRelay Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("[Relay] Multiple LocomotionEventRelay instances detected!");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void ReportLocomotionComplete(Vector3 finalPosition)
    {

        // Broadcast event
        LocomotionTarget[] targets = FindObjectsOfType<LocomotionTarget>();

        foreach (var t in targets)
        {
            t.OnLocomotionEvent(finalPosition);
        }
    }
}
