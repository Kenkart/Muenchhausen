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

    private bool fadeInTriggered = false;

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
                OnLocomotionEnded?.Invoke(targetPos);

                if (flightLine != null)
                    flightLine.enabled = false;
            }
        }
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

        float horiz = Vector3.Distance(
            new Vector3(startPos.x, 0, startPos.z),
            new Vector3(targetPos.x, 0, targetPos.z));

        float verticalDelta = targetPos.y - startPos.y;
        maxHeight = Mathf.Max(0.1f, verticalDelta + horiz * 0.25f);

        float distance = Vector3.Distance(startPos, targetPos);
        flightTime = Mathf.Max(distance / maxSpeed, 0.1f);

        elapsedTime = 0f;
        isFlying = true;

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
