using UnityEngine;

using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

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
        {
            provider.locomotionEnded += OnLocomotionEnded;
        }
    }

    private void OnDisable()
    {

        if (provider != null)
        {
            provider.locomotionEnded -= OnLocomotionEnded;
        }
    }

    private void OnLocomotionEnded(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider prov)
    {

        var xrOrigin = prov.mediator.xrOrigin;
        if (!xrOrigin)
        {
            Debug.LogError("[Forwarder] xrOrigin is NULL in mediator!");
            return;
        }

        Vector3 pos = xrOrigin.transform.position;

        LocomotionEventRelay.Instance.ReportLocomotionComplete(pos);
    }
}
