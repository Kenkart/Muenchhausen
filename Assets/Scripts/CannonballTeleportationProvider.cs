using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.InputSystem;

public class CannonballTeleportationProvider : TeleportationProvider
{
    [Header("Cannonball Settings")]
    [Tooltip("Flight speed in meters per second. Higher = faster flight.")]
    public float flightSpeed = 3f;

    [Tooltip("Minimum travel time in seconds (prevents too-fast jumps).")]
    public float minTravelTime = 0.5f;

    [Tooltip("Maximum travel time in seconds (prevents too-slow jumps).")]
    public float maxTravelTime = 5f;

    [Tooltip("Optional CharacterController for collision-aware movement. If null, will auto-find on XROrigin.")]
    public CharacterController characterController;

    [Tooltip("Height offset to cast down for ground snapping at landing.")]
    public float groundSnapCastHeight = 3f;

    [Tooltip("Max distance to raycast down for ground snap.")]
    public float groundSnapMaxDistance = 6f;

    [Tooltip("Layer mask for ground detection.")]
    public LayerMask groundLayerMask = ~0;

    [Header("Arc Matching")]
    [Tooltip("Apex height multiplier to match the visual arc. Higher = taller arc.")]
    public float arcHeightMultiplier = 1.5f;

    [Header("Locomotion Control")]
    [Tooltip("Reference to the continuous move provider to disable during flight.")]
    public ContinuousMoveProvider continuousMoveProvider;

    [Tooltip("Enable in-air control using the left controller joystick during flight.")]
    public bool enableInAirControl = false;

    [Tooltip("Speed multiplier for in-air horizontal movement (only used if enableInAirControl is true).")]
    public float inAirControlSpeed = 3f;

    [Tooltip("Input action reference for left controller joystick (2D axis). Required for in-air control.")]
    public InputActionReference leftJoystickInput;

    // Store the launch position to prevent drift from continuous movement
    private Vector3 capturedLaunchPosition;
    private Vector3 capturedCameraOffset;
    private bool hasStoredLaunchPosition = false;

    /// <summary>
    /// Override the standard Update to intercept teleport requests and perform cannonball motion instead.
    /// </summary>
    protected override void Update()
    {
        if (!validRequest)
            return;

        if (locomotionState == LocomotionState.Idle)
        {
            // Capture the current position as the launch position BEFORE any coroutine delays
            var xrOrigin = mediator != null ? mediator.xrOrigin : null;
            if (xrOrigin != null)
            {
                // Store XR Origin position and camera offset
                capturedLaunchPosition = xrOrigin.transform.position;
                
                // Calculate camera offset from XR Origin
                if (xrOrigin.Camera != null)
                {
                    capturedCameraOffset = xrOrigin.Camera.transform.position - capturedLaunchPosition;
                }
                else
                {
                    capturedCameraOffset = Vector3.zero;
                }
                
                hasStoredLaunchPosition = true;
            }

            if (delayTime > 0f)
            {
                if (TryPrepareLocomotion())
                    StartCoroutine(DelayedCannonballLaunch());
            }
            else
            {
                TryStartLocomotionImmediately();
                StartCoroutine(PerformCannonballFlight());
            }

            validRequest = false;
        }
    }

    protected void OnEnable()
    {
        // Enable the input action if in-air control is enabled
        if (enableInAirControl && leftJoystickInput != null && leftJoystickInput.action != null)
        {
            leftJoystickInput.action.Enable();
        }
    }

    protected void OnDisable()
    {
        // Disable the input action when component is disabled
        if (leftJoystickInput != null && leftJoystickInput.action != null)
        {
            leftJoystickInput.action.Disable();
        }
    }

    IEnumerator DelayedCannonballLaunch()
    {
        yield return new WaitForSeconds(delayTime);

        if (locomotionState == LocomotionState.Preparing)
        {
            TryStartLocomotionImmediately();
            yield return PerformCannonballFlight();
        }
    }

    IEnumerator PerformCannonballFlight()
    {
        var xrOrigin = mediator != null ? mediator.xrOrigin : null;
        if (xrOrigin == null)
        {
            Debug.LogError("[CannonballTeleportationProvider] XROrigin not available.");
            TryEndLocomotion();
            yield break;
        }

        // Use the captured launch position (XR Origin + camera offset at start)
        Vector3 startOriginPos = hasStoredLaunchPosition ? capturedLaunchPosition : xrOrigin.transform.position;
        Vector3 cameraOffset = hasStoredLaunchPosition ? capturedCameraOffset : Vector3.zero;
        
        // The actual start position is where the camera/player was
        Vector3 start = startOriginPos + cameraOffset;
        Vector3 target = currentRequest.destinationPosition;

        // Reset the flag
        hasStoredLaunchPosition = false;

        // Ground snap: prefer surface below target
        Vector3 groundPosition = target;
        if (Physics.Raycast(target + Vector3.up * groundSnapCastHeight, Vector3.down, out RaycastHit groundHit, groundSnapMaxDistance, groundLayerMask))
            groundPosition = groundHit.point;
        
        // But we want to animate the camera to land at ground + camera height
        Vector3 targetCamera = groundPosition;
        targetCamera.y = groundPosition.y + cameraOffset.y;

        // Calculate horizontal distance and desired apex height using camera positions
        Vector3 horizontalDisplacement = new Vector3(targetCamera.x - start.x, 0, targetCamera.z - start.z);
        float horizontalDistance = horizontalDisplacement.magnitude;

        // Compute travel time based on distance and speed
        float T = horizontalDistance / flightSpeed;
        T = Mathf.Clamp(T, minTravelTime, maxTravelTime);

        // Compute apex height (midpoint elevation + extra height for arc)
        float apexHeight = Mathf.Max(start.y, targetCamera.y) + (horizontalDistance * 0.25f * arcHeightMultiplier);

        float time = 0f;

        // Disable CharacterController during flight to prevent physics interference
        bool wasControllerEnabled = false;
        if (characterController != null)
        {
            wasControllerEnabled = characterController.enabled;
            characterController.enabled = false;
        }

        // Always disable continuous movement provider during cannonball flight
        bool wasMoveProviderEnabled = false;
        if (continuousMoveProvider != null)
        {
            wasMoveProviderEnabled = continuousMoveProvider.enabled;
            continuousMoveProvider.enabled = false;
        }

        // Track accumulated in-air offset for in-air control
        Vector3 inAirOffset = Vector3.zero;

        // Execute flight using parabolic motion
        while (time < T)
        {
            float t = time / T; // Normalized time [0, 1]

            // Parabolic interpolation for the camera position
            // Horizontal: linear
            Vector3 currentHorizontal = start + horizontalDisplacement * t;

            // Vertical: parabolic arc through apex
            // Use quadratic formula: y = a*t^2 + b*t + c
            float heightAtT = Mathf.Lerp(start.y, targetCamera.y, t) +
                              (4f * (apexHeight - Mathf.Lerp(start.y, targetCamera.y, 0.5f))) * t * (1f - t);

            Vector3 currentCameraPosition = new Vector3(currentHorizontal.x, heightAtT, currentHorizontal.z);

            // Apply in-air control if enabled
            if (enableInAirControl && leftJoystickInput != null && leftJoystickInput.action != null)
            {
                Vector2 joystickInput = leftJoystickInput.action.ReadValue<Vector2>();
                
                if (joystickInput.sqrMagnitude > 0.01f) // Deadzone
                {
                    // Get camera forward direction (flattened on XZ plane)
                    Transform cameraTransform = xrOrigin.Camera != null ? xrOrigin.Camera.transform : xrOrigin.transform;
                    Vector3 forward = cameraTransform.forward;
                    forward.y = 0;
                    forward.Normalize();

                    Vector3 right = cameraTransform.right;
                    right.y = 0;
                    right.Normalize();

                    // Calculate movement direction relative to camera
                    Vector3 moveDirection = (forward * joystickInput.y + right * joystickInput.x);
                    
                    // Accumulate offset based on input
                    inAirOffset += inAirControlSpeed * Time.deltaTime * moveDirection;
                }
            }

            // Apply the in-air offset to the camera position
            currentCameraPosition += inAirOffset;

            // Move XROrigin to maintain camera at the desired position
            // XROrigin position = desired camera position - camera offset
            Vector3 currentCameraOffset = xrOrigin.Camera != null 
                ? xrOrigin.Camera.transform.position - xrOrigin.transform.position 
                : Vector3.zero;
            
            xrOrigin.transform.position = currentCameraPosition - currentCameraOffset;

            time += Time.deltaTime;
            yield return null;
        }

        // Snap to exact final position (including in-air offset)
        // Position XR Origin so camera ends up at the target camera position + any in-air offset
        Vector3 finalCameraOffset = xrOrigin.Camera != null 
            ? xrOrigin.Camera.transform.position - xrOrigin.transform.position 
            : Vector3.zero;
        
        Vector3 finalCameraPosition = targetCamera + inAirOffset;
        xrOrigin.transform.position = finalCameraPosition - finalCameraOffset;

        // Wait one frame before re-enabling CharacterController
        // This prevents it from immediately correcting/snapping the position
        yield return null;

        // Re-enable CharacterController
        if (characterController != null)
        {
            characterController.enabled = wasControllerEnabled;
        }

        // Re-enable continuous movement provider
        if (continuousMoveProvider != null && wasMoveProviderEnabled)
        {
            continuousMoveProvider.enabled = true;
        }

        // Apply orientation matching (same as default teleport)
        ApplyOrientation();

        // End locomotion (fires events)
        TryEndLocomotion();
    }

    void ApplyOrientation()
    {
        switch (currentRequest.matchOrientation)
        {
            case MatchOrientation.WorldSpaceUp:
                upTransformation.targetUp = Vector3.up;
                TryQueueTransformation(upTransformation);
                break;
            case MatchOrientation.TargetUp:
                upTransformation.targetUp = currentRequest.destinationRotation * Vector3.up;
                TryQueueTransformation(upTransformation);
                break;
            case MatchOrientation.TargetUpAndForward:
                upTransformation.targetUp = currentRequest.destinationRotation * Vector3.up;
                TryQueueTransformation(upTransformation);
                forwardTransformation.targetDirection = currentRequest.destinationRotation * Vector3.forward;
                TryQueueTransformation(forwardTransformation);
                break;
            case MatchOrientation.None:
                // Keep current orientation
                break;
        }
    }
}