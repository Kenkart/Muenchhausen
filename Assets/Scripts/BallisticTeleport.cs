using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class BallisticTeleport : MonoBehaviour
{
    public UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor;
    public InputActionReference teleportActivateAction;
    public float maxHeight = 2.0f;
    public float maxSpeed = 10f;

    private bool isFlying = false;
    private Vector3 startPos;
    private Vector3 targetPos;
    private float flightTime;
    private float elapsedTime;

    private Vector3 pendingTarget;
    private bool targetPending = false;

    // Event for when ballistic locomotion ends
    public event Action<Vector3> OnLocomotionEnded;

    void OnEnable()
    {
        teleportActivateAction.action.Enable();
    }

    void OnDisable()
    {
        teleportActivateAction.action.Disable();
    }

    void Update()
    {
        // Update pending target while pointing at a valid hit
        if (!isFlying && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            pendingTarget = hit.point;
            targetPending = true;
        }

        // Launch on button release
        if (!isFlying && targetPending && teleportActivateAction.action.WasReleasedThisFrame())
        {
            LaunchTo(pendingTarget);
            targetPending = false;
        }

        // Move along parabola
        if (isFlying)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / flightTime);

            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += maxHeight * 4 * t * (1 - t); // Simple parabola
            transform.position = currentPos;

            if (t >= 1f)
            {
                isFlying = false;
                // Fire event when ballistic locomotion finishes
                OnLocomotionEnded?.Invoke(targetPos);
            }
        }
    }

    void LaunchTo(Vector3 target)
    {
        startPos = transform.position;
        targetPos = target;
        float distance = Vector3.Distance(startPos, targetPos);

        flightTime = Mathf.Max(distance / maxSpeed, 0.1f);
        elapsedTime = 0f;
        isFlying = true;
    }
}
