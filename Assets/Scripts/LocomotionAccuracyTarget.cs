using UnityEngine;

public class LocomotionTarget : MonoBehaviour
{
    [SerializeField] private Transform targetPoint;

    private void Awake()
    {
        if (targetPoint == null)
            Debug.LogError("[Target] No targetPoint assigned!");
    }

    public void OnLocomotionEvent(Vector3 finalPosition)
    {
        if (targetPoint == null)
        {
            Debug.LogError("[Target] targetPoint is NULL!");
            return;
        }

        float distance = Vector3.Distance(finalPosition, targetPoint.position);
        Debug.Log($"[Target] Distance from locomotion end to target = {distance:F3} meters");
    }
}
