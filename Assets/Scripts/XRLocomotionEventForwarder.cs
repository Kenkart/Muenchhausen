using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using System.Collections;

public class XRLocomotionEventForwarder : MonoBehaviour
{
    private TeleportationProvider provider;

    private void Awake()
    {
        provider = FindObjectOfType<TeleportationProvider>();

        if (!provider)
            Debug.LogError("[Forwarder] No TeleportationProvider found!");
    }

    private void OnEnable()
    {
        if (provider != null)
            provider.locomotionEnded += OnLocomotionEnded;
    }

    private void OnDisable()
    {
        if (provider != null)
            provider.locomotionEnded -= OnLocomotionEnded;
    }

    private void OnLocomotionEnded(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider prov)
    {
        StartCoroutine(ReportAfterFrame(prov));
    }

    private IEnumerator ReportAfterFrame(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider prov)
    {
        // Wait until teleportation actually applies the new position
        yield return null; // one frame delay

        var xrOrigin = prov.mediator.xrOrigin;
        if (!xrOrigin)
        {
            Debug.LogError("[Forwarder] xrOrigin is NULL in mediator!");
        }

        Vector3 pos = xrOrigin.transform.position;
        LocomotionEventRelay.Instance.ReportLocomotionComplete(pos);
    }
}
