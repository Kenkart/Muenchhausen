using UnityEngine;
using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Singleton that manages experiment sets, movement logging and CSV output.
/// CSV uses semicolon (';') and CultureInfo.InvariantCulture.
/// </summary>
public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance { get; private set; }

    [Header("Identification")]
    public string participantId = "P01";

    [Header("Set / Player Settings")]
    [Tooltip("World spawn position where the player will be placed at set start/end.")]
    public Vector3 worldSpawn = Vector3.zero;

    // Tutorial and condition flags
    [HideInInspector]
    public bool tutorialMode = false;

    [HideInInspector]
    public bool fadeEnabled = false;

    [HideInInspector]
    public bool inAirControlEnabled = false;

    private const char Delim = ';';

    private string csvPath;
    private StreamWriter writer;
    private bool headerWritten = false;

    private int currentSetId = 0;
    private int nextSetId = 1;
    private int movementIndex = 0;
    private float setStartTime = 0f;

    // Events
    public event Action<int> OnSetStarted;
    public event Action<int, float> OnSetEnded;
    public event Action<int, int, float, float> OnMovementLogged;
    public event Action<int, int, float> OnAccuracyRecorded;

    // Buffered movement record
    private class MovementRecord
    {
        public string Timestamp;
        public string ParticipantId;
        public int SetId;
        public int MovementIndex;
        public Vector3 Start;
        public Vector3 Target;
        public float Distance;
        public float MovementDuration;
        public float? DistanceFromTarget;
        public bool FadeEnabled;
        public bool InAirControlEnabled;
        public bool TutorialMode;
        public string Note;
    }

    private readonly Dictionary<int, MovementRecord> pendingRecords = new Dictionary<int, MovementRecord>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        CloseWriter();
    }

    private void InitWriterIfNeeded()
    {
        if (writer != null) return;
        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safeId = string.IsNullOrWhiteSpace(participantId) ? "participant" : participantId;
        string fileName = string.Format("{0}_{1}.csv", safeId, timeStamp);
        csvPath = Path.Combine(Application.persistentDataPath, fileName);
        var fs = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
        WriteHeader();
    }

    private void WriteHeader()
    {
        if (headerWritten || writer == null) return;
        string header = string.Join(Delim.ToString(), new[]
        {
            "Timestamp",
            "ParticipantId",
            "SetId",
            "MovementIndex",
            "StartX",
            "StartY",
            "StartZ",
            "TargetX",
            "TargetY",
            "TargetZ",
            "Distance",
            "MovementDuration",
            "DistanceFromTarget",
            "FadeEnabled",
            "InAirControlEnabled",
            "TutorialMode",
            "Note"
        });
        writer.WriteLine(header);
        headerWritten = true;
    }

    private void CloseWriter()
    {
        FlushAllPending();
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
            headerWritten = false;
        }
    }

    public int CurrentSetId => currentSetId;
    public int NextSetId => nextSetId;

    public void StartSet()
    {
        InitWriterIfNeeded();
        if (currentSetId != 0) return;
        currentSetId = nextSetId++;
        movementIndex = 0;
        setStartTime = Time.time;
        ResetTargets();
        PlacePlayerAtWorldSpawn();
        OnSetStarted?.Invoke(currentSetId);
    }

    public void EndSet()
    {
        if (currentSetId == 0) return;
        float setDuration = Time.time - setStartTime;
        FlushAllPending();
        if (writer != null)
        {
            string[] parts = new string[]
            {
                DateTime.Now.ToString("o"),
                participantId,
                currentSetId.ToString(CultureInfo.InvariantCulture),
                "0",
                "","","",
                "","","",
                "",
                setDuration.ToString(CultureInfo.InvariantCulture),
                "",
                fadeEnabled.ToString(CultureInfo.InvariantCulture),
                inAirControlEnabled.ToString(CultureInfo.InvariantCulture),
                tutorialMode.ToString(CultureInfo.InvariantCulture),
                "SET_END"
            };
            writer.WriteLine(string.Join(Delim.ToString(), parts));
        }
        OnSetEnded?.Invoke(currentSetId, setDuration);
        PlacePlayerAtWorldSpawn();
        var targets = FindObjectsOfType<LocomotionTarget>();
        foreach (var t in targets) t.gameObject.SetActive(true);
        currentSetId = 0;
        movementIndex = 0;
    }

    private void ResetTargets()
    {
        var targets = FindObjectsOfType<LocomotionTarget>();
        if (targets == null || targets.Length == 0) return;
        foreach (var t in targets)
        {
            t.transform.position = t.spawnCenter;
            t.gameObject.SetActive(!tutorialMode);
        }
    }

    private void PlacePlayerAtWorldSpawn()
    {
        var player = FindObjectOfType<BallisticTeleport>();
        if (player != null) player.transform.position = worldSpawn;
    }

    public void RecordMovement(Vector3 start, Vector3 target, float distance, float movementDuration)
    {
        InitWriterIfNeeded();
        FlushPendingOlderThan(movementIndex + 1);
        movementIndex++;
        var rec = new MovementRecord
        {
            Timestamp = DateTime.Now.ToString("o"),
            ParticipantId = participantId,
            SetId = currentSetId,
            MovementIndex = movementIndex,
            Start = start,
            Target = target,
            Distance = distance,
            MovementDuration = movementDuration,
            DistanceFromTarget = null,
            FadeEnabled = fadeEnabled,
            InAirControlEnabled = inAirControlEnabled,
            TutorialMode = tutorialMode,
            Note = ""
        };
        pendingRecords[movementIndex] = rec;
        OnMovementLogged?.Invoke(currentSetId, movementIndex, distance, movementDuration);
    }

    public void RecordAccuracy(float distanceFromTarget)
    {
        InitWriterIfNeeded();
        int idx = movementIndex;
        if (pendingRecords.TryGetValue(idx, out var rec))
        {
            rec.DistanceFromTarget = distanceFromTarget;
            if (!tutorialMode) WriteMovementRecord(rec);
            pendingRecords.Remove(idx);
        }
        else
        {
            if (!tutorialMode && writer != null)
            {
                string[] parts = new string[]
                {
                    DateTime.Now.ToString("o"),
                    participantId,
                    currentSetId.ToString(CultureInfo.InvariantCulture),
                    idx.ToString(CultureInfo.InvariantCulture),
                    "","","",
                    "","","",
                    "",
                    "",
                    distanceFromTarget.ToString(CultureInfo.InvariantCulture),
                    fadeEnabled.ToString(CultureInfo.InvariantCulture),
                    inAirControlEnabled.ToString(CultureInfo.InvariantCulture),
                    tutorialMode.ToString(CultureInfo.InvariantCulture),
                    "ACCURACY_ORPHAN"
                };
                writer.WriteLine(string.Join(Delim.ToString(), parts));
            }
        }
        OnAccuracyRecorded?.Invoke(currentSetId, idx, distanceFromTarget);
    }

    private void WriteMovementRecord(MovementRecord r)
    {
        if (writer == null) InitWriterIfNeeded();
        if (tutorialMode) return;
        string distFromTargetStr = r.DistanceFromTarget.HasValue ? r.DistanceFromTarget.Value.ToString(CultureInfo.InvariantCulture) : "";
        string[] parts = new string[]
        {
            r.Timestamp,
            r.ParticipantId,
            r.SetId.ToString(CultureInfo.InvariantCulture),
            r.MovementIndex.ToString(CultureInfo.InvariantCulture),
            r.Start.x.ToString(CultureInfo.InvariantCulture),
            r.Start.y.ToString(CultureInfo.InvariantCulture),
            r.Start.z.ToString(CultureInfo.InvariantCulture),
            r.Target.x.ToString(CultureInfo.InvariantCulture),
            r.Target.y.ToString(CultureInfo.InvariantCulture),
            r.Target.z.ToString(CultureInfo.InvariantCulture),
            r.Distance.ToString(CultureInfo.InvariantCulture),
            r.MovementDuration.ToString(CultureInfo.InvariantCulture),
            distFromTargetStr,
            r.FadeEnabled.ToString(CultureInfo.InvariantCulture),
            r.InAirControlEnabled.ToString(CultureInfo.InvariantCulture),
            r.TutorialMode.ToString(CultureInfo.InvariantCulture),
            r.Note ?? ""
        };
        writer.WriteLine(string.Join(Delim.ToString(), parts));
    }

    private void FlushPendingOlderThan(int thresholdExclusive)
    {
        var keys = new List<int>(pendingRecords.Keys);
        foreach (var k in keys)
        {
            if (thresholdExclusive <= 0 || k < thresholdExclusive)
            {
                var rec = pendingRecords[k];
                if (!tutorialMode) WriteMovementRecord(rec);
                pendingRecords.Remove(k);
            }
        }
    }

    private void FlushAllPending()
    {
        FlushPendingOlderThan(int.MaxValue);
    }

    public string GetCsvPath()
    {
        return csvPath;
    }

    // Condition control (dummies)
    public void SetTutorialMode(bool enabled)
    {
        tutorialMode = enabled;
        var targets = FindObjectsOfType<LocomotionTarget>();
        foreach (var t in targets) t.gameObject.SetActive(!tutorialMode);
    }

    public void SetFadeEnabled(bool enabled)
    {
        fadeEnabled = enabled;
        Debug.Log($"[ExperimentManager] Fade set to: {enabled}");
    }

    public void SetInAirControlEnabled(bool enabled)
    {
        inAirControlEnabled = enabled;
        Debug.Log($"[ExperimentManager] In-Air Control set to: {enabled}");
    }

    public void SetConditions(bool fade, bool inAir)
    {
        SetFadeEnabled(fade);
        SetInAirControlEnabled(inAir);
    }
}