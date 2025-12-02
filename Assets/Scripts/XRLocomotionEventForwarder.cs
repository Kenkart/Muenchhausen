using UnityEngine;
using System.Collections;

public class XRLocomotionEventForwarder : MonoBehaviour
{
    public BallisticTeleport ballisticTeleport;

    private void OnEnable()
    {
        if (ballisticTeleport != null)
            ballisticTeleport.OnLocomotionEnded += HandleLocomotionEnded;
    }

    private void OnDisable()
    {
        if (ballisticTeleport != null)
            ballisticTeleport.OnLocomotionEnded -= HandleLocomotionEnded;
    }

    private void HandleLocomotionEnded(Vector3 finalPosition)
    {
        StartCoroutine(ReportAfterFrame(finalPosition));
    }

    private IEnumerator ReportAfterFrame(Vector3 pos)
    {
        // Wait one frame to ensure transforms are fully updated
        yield return null;
        LocomotionEventRelay.Instance.ReportLocomotionComplete(pos);
    }
}
