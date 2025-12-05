using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Simple UI controller to start/stop sets and display last movement info.
/// Bind Buttons and Text fields in the Inspector.
/// </summary>
public class ExperimentControlUI : MonoBehaviour
{
    public Button startSetButton;
    public Button endSetButton;

    public Text statusText;
    public Text lastMovementText;

    private void Start()
    {
        if (startSetButton != null) startSetButton.onClick.AddListener(OnStartSetClicked);
        if (endSetButton != null) endSetButton.onClick.AddListener(OnEndSetClicked);

        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnSetStarted += OnSetStarted;
            ExperimentManager.Instance.OnSetEnded += OnSetEnded;
            ExperimentManager.Instance.OnMovementLogged += OnMovementLogged;
        }

        UpdateStatus("Ready");
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnSetStarted -= OnSetStarted;
            ExperimentManager.Instance.OnSetEnded -= OnSetEnded;
            ExperimentManager.Instance.OnMovementLogged -= OnMovementLogged;
        }
    }

    private void OnStartSetClicked()
    {
        ExperimentManager.Instance?.StartSet();
    }

    private void OnEndSetClicked()
    {
        ExperimentManager.Instance?.EndSet();
    }

    private void OnSetStarted(int setId)
    {
        UpdateStatus($"Set {setId} started");
    }

    private void OnSetEnded(int setId, float duration)
    {
        UpdateStatus($"Set {setId} ended (dur: {duration:F2}s)");
    }

    private void OnMovementLogged(int setId, int movementIndex, float distance, float movementDuration)
    {
        if (lastMovementText != null)
        {
            lastMovementText.text = $"Set:{setId} Move:{movementIndex} Dist:{distance:F2}m Time:{movementDuration:F2}s";
        }
    }

    private void UpdateStatus(string s)
    {
        if (statusText != null) statusText.text = s;
    }
}