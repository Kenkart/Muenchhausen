using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
using System.Collections;

public class BallisticTeleport : MonoBehaviour
{
    [Header("XR")]
    public UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor;
    public InputActionReference teleportActivateAction;

    [Header("Movement Settings")]
    public float maxSpeed = 10f;
    private float maxHeight = 2.0f;
    private bool isFlying = false;
    private Vector3 startPos;
    private Vector3 targetPos;
    private float flightTime;
    private float elapsedTime;

    [Header("Line Renderer")]
    public LineRenderer flightLine;
    public int lineResolution = 20;

    private Vector3 pendingTarget;
    private bool targetPending = false;

    [Header("Fade Settings")]
    public bool useFade = false;
    public Image fadeImage;
    public float fadeOutTime = 0.35f;
    public float fadeInTime = 0.5f;

    [Header("In Air Control Settings")]
    public bool useInAirControl = false;
    public InputActionReference leftJoystickAction;
    public float minDistance = 0.5f; // Distance at which movement becomes very slow
    public float maxDistance = 20f;  // Distance at which movement is at max speed
    [Range(1f, 10f)]
    public float logarithmicBase = 2f; // Adjust steepness of logarithmic curve
    [Range(0.1f, 0.5f)]
    public float minSpeedMultiplier = 0.2f; // Minimum speed to ensure player can always move

    private bool fadeInTriggered = false;
    private Vector3 inAirControlOffset = Vector3.zero;

    public event Action<Vector3> OnLocomotionEnded;

    // --- added for timing/logging ---
    private float movementStartTime;

    void OnEnable()
    {
        teleportActivateAction.action.Enable();
        leftJoystickAction.action.Enable();
    }

    void OnDisable()
    {
        teleportActivateAction.action.Disable();
        leftJoystickAction.action.Disable();
    }

    void Update()
    {
        if (!isFlying && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            pendingTarget = hit.point;
            targetPending = true;
        }

        if (!isFlying && targetPending && teleportActivateAction.action.WasReleasedThisFrame())
        {
            if (useFade)
                StartCoroutine(FadeLaunch(pendingTarget));   // ← NEW
            else
                LaunchTo(pendingTarget);

            targetPending = false;
        }

        if (isFlying)
        {
            if (useInAirControl)
                ApplyInAirControl();

            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / flightTime);

            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += maxHeight * 4 * t * (1 - t);
            transform.position = currentPos;

            // Mid-flight fade-in (unchanged except we now support fade-out)
            if (useFade && !fadeInTriggered && t >= 0.5f)
            {
                fadeInTriggered = true;
                StartCoroutine(FadeIn());
            }

            if (flightLine != null)
                UpdateFlightLine(t);

            if (t >= 1f)
            {
                isFlying = false;

                // compute actual movement duration
                float movementDuration = Time.time - movementStartTime;
                float distance = Vector3.Distance(startPos, targetPos);

                // Log movement to ExperimentManager if present
                if (ExperimentManager.Instance != null)
                {
                    ExperimentManager.Instance.RecordMovement(startPos, targetPos, distance, movementDuration);
                }

                OnLocomotionEnded?.Invoke(targetPos);

                if (flightLine != null)
                    flightLine.enabled = false;
            }
        }
    }

    void ApplyInAirControl()
    {
        Vector2 joystickInput = leftJoystickAction.action.ReadValue<Vector2>();
        if (joystickInput.magnitude < 0.1f) return;

        // Camera-relative input
        Transform cam = Camera.main.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;

        Vector3 moveDir = (forward * joystickInput.y + right * joystickInput.x).normalized;

        // --- Determine remaining distance (horizontal only) ---
        float t = Mathf.Clamp01(elapsedTime / flightTime);

        Vector3 currentFlightPos = Vector3.Lerp(startPos, targetPos, t);
        currentFlightPos.y += maxHeight * 4 * t * (1 - t);

        float remainingDist =
            Vector3.Distance(new Vector3(currentFlightPos.x, 0, currentFlightPos.z),
                             new Vector3(targetPos.x, 0, targetPos.z));

        // --- Logarithmic speed falloff based on remaining distance ---
        float normalized = Mathf.InverseLerp(minDistance, maxDistance, remainingDist);
        float logSpeed = Mathf.Lerp(minSpeedMultiplier, 1f,
                         Mathf.Log(normalized + 0.01f, logarithmicBase) /
                         Mathf.Log(1.01f, logarithmicBase));

        float actualSpeed = maxSpeed * logSpeed * joystickInput.magnitude;

        // --- CHANGE: Move target instead of offset ---
        targetPos += moveDir * actualSpeed * Time.deltaTime;

        // Recompute maxHeight dynamically
        float horiz = Vector3.Distance(
            new Vector3(startPos.x, 0, startPos.z),
            new Vector3(targetPos.x, 0, targetPos.z));
        float verticalDelta = targetPos.y - startPos.y;
        maxHeight = Mathf.Max(0.1f, verticalDelta + horiz * 0.25f);
    }


    IEnumerator FadeLaunch(Vector3 target)
    {
        // Fade out BEFORE movement
        if (fadeImage != null)
            yield return StartCoroutine(FadeOut());

        // Reset fade-in flag
        fadeInTriggered = false;

        // Then launch normally
        LaunchTo(target);
    }

    void LaunchTo(Vector3 target)
    {
        startPos = transform.position;
        targetPos = target;

        float horizDist =
            Vector3.Distance(new Vector3(startPos.x, 0, startPos.z),
                             new Vector3(targetPos.x, 0, targetPos.z));

        float heightDelta = targetPos.y - startPos.y;

        // Compute apex height
        maxHeight = Mathf.Max(0.1f, heightDelta + horizDist * 0.25f);

        // NEW: Compute speed required for ballistic travel.
        // We assume a "desired" flight duration for correctness (1–2 sec)
        float desiredFlightTime = Mathf.Clamp(horizDist / maxSpeed, 0.3f, 2.5f);

        flightTime = desiredFlightTime;

        elapsedTime = 0f;
        isFlying = true;

        // Log time
        movementStartTime = Time.time;

        if (flightLine != null)
        {
            flightLine.enabled = true;
            flightLine.positionCount = lineResolution;
        }
    }


    void UpdateFlightLine(float t)
    {
        if (!flightLine) return;

        for (int i = 0; i < lineResolution; i++)
        {
            float step = Mathf.Lerp(t, 1f, (float)i / (lineResolution - 1));
            Vector3 pos = Vector3.Lerp(startPos, targetPos, step);
            pos.y += maxHeight * 4 * step * (1 - step);
            flightLine.SetPosition(i, pos);
        }
    }

    // ========================
    // FADE ROUTINES
    // ========================

    IEnumerator FadeOut()
    {
        fadeImage.enabled = true;
        Color c = fadeImage.color;

        float t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(0f, 1f, t / fadeOutTime);
            fadeImage.color = c;
            yield return null;
        }
    }

    IEnumerator FadeIn()
    {
        Color c = fadeImage.color;
        float t = 0f;

        while (t < fadeInTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / fadeInTime);
            fadeImage.color = c;
            yield return null;
        }

        fadeImage.enabled = false;
    }
}
