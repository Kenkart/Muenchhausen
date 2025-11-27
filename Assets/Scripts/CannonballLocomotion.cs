using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

public class CannonballLocomotion : MonoBehaviour
{
    public XROrigin xrOrigin;
    public InputActionProperty cannonballButton;
    public LineRenderer lineRenderer;
    public CharacterController characterController;

    [Header("Parabola Settings")]
    public float arcHeight = 2f;
    public int segmentCount = 30;
    public float maxDistance = 10f;
    public float travelTime = 2f;
    public float groundCheckHeight = 2f;

    private bool isAiming = false;
    private Vector3 targetPoint;

    private void Start()
    {
        lineRenderer.enabled = false;
    }

    void Update()
    {
        if (cannonballButton.action.IsPressed() && !isAiming)
        {
            isAiming = true;
            lineRenderer.enabled = true;
        }

        if (isAiming && cannonballButton.action.WasReleasedThisFrame())
        {
            isAiming = false;
            lineRenderer.enabled = false;
            if (targetPoint != Vector3.zero)
            {
                StartCoroutine(FlyArc(targetPoint));
            }
        }

        if (isAiming)
        {
            UpdateParabola();
        }
    }

    void UpdateParabola()
    {
        Vector3 start = transform.position;
        Vector3 forward = transform.forward;
        Vector3 end = start + forward * maxDistance;

        if (Physics.Raycast(start, forward, out RaycastHit hit, maxDistance))
        {
            end = hit.point;
        }

        targetPoint = end;

        lineRenderer.positionCount = segmentCount + 1;
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y += arcHeight * Mathf.Sin(Mathf.PI * t);
            lineRenderer.SetPosition(i, pos);
        }
    }

    IEnumerator FlyArc(Vector3 target)
    {
        Vector3 originStart = xrOrigin.transform.position;
        Vector3 targetBase = target;

        // Ensure landing is on ground
        if (Physics.Raycast(target + Vector3.up * 3f, Vector3.down, out RaycastHit groundHit, 6f))
            targetBase = groundHit.point;

        // Midpoint of arc
        Vector3 mid = (originStart + targetBase) * 0.5f;
        mid.y += arcHeight;

        float time = 0f;

        characterController.enabled = false;

        while (time < travelTime)
        {
            float t = time / travelTime;

            // Quadratic bezier curve
            Vector3 originPos =
                Mathf.Pow(1 - t, 2) * originStart +
                2 * (1 - t) * t * mid +
                Mathf.Pow(t, 2) * targetBase;

            xrOrigin.transform.position = originPos;

            time += Time.deltaTime;
            yield return null;
        }

        xrOrigin.transform.position = targetBase;
        characterController.enabled = true;
    }

}
