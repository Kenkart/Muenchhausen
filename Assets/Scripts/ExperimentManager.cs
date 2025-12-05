using UnityEngine;
using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Singleton that manages experiment sets, movement logging and CSV output.
/// Attach one instance to a persistent GameObject in the scene.
/// CSV uses semicolon (';') as delimiter and CultureInfo.InvariantCulture for numeric formatting.
/// Filename uses participantId as prefix.
/// </summary>
public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance { get; private set; }

    [Header("Identification")]
    public string participantId = "P01";

    [Header("CSV Settings")]
    public bool appendIfExists = false;

    [Header("Set / Player Settings")]
    [Tooltip("If >0 the set will automatically end after this many movements.")]
    public int maxMovementsPerSet = 0;
    [Tooltip("World spawn position where the player will be placed at set start/end.")]
    public Vector3 worldSpawn = Vector3.zero;

    private string csvPath;
    private StreamWriter writer;
    private bool headerWritten = false;

    private int currentSetId = 0;
    private int nextSetId = 1;
    private int movementIndex = 0;
    private float setStartTime = 0f;

    // Events for UI / other systems
    public event Action<int> OnSetStarted;
    public event Action<int, float> OnSetEnded; // setId, duration
    public event Action<int, int, float, float> OnMovementLogged; // setId, movementIndex, distance, movementDuration
    public event Action<int, int, float> OnAccuracyRecorded; // setId, movementIndex, distanceFromTarget

    private const char Delim = ';';

    // --- buffering so accuracy can be written in the same CSV line ---
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
        public float? DistanceFromTarget; // nullable until accuracy reported
        public string Note;
    }

    // pending movements keyed by movementIndex
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
        string fileName = $"{safeId}_{timeStamp}.csv";
        csvPath = Path.Combine(Application.persistentDataPath, fileName);

        FileMode mode = appendIfExists && File.Exists(csvPath) ? FileMode.Append : FileMode.Create;
        var fs = new FileStream(csvPath, mode, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };

        WriteHeader();
    }

    private void WriteHeader()
    {
        if (headerWritten || writer == null) return;

        // Header uses semicolon delimiter and includes DistanceFromTarget and Note columns.
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
            "Note"
        });

        writer.WriteLine(header);
        headerWritten = true;
    }

    private void CloseWriter()
    {
        // flush pending before closing
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

        if (currentSetId != 0)
            return; // already running

        currentSetId = nextSetId;
        nextSetId++;
        movementIndex = 0;
        setStartTime = Time.time;

        // Reset all targets and place player at world spawn
        ResetTargets();
        PlacePlayerAtWorldSpawn();

        OnSetStarted?.Invoke(currentSetId);
    }

    private void ResetTargets()
    {
        var targets = FindObjectsOfType<LocomotionTarget>();
        if (targets == null || targets.Length == 0) return;

        foreach (var t in targets)
        {
            // set the target transform position to its spawn center
            // (avoid setting transform.root.position to prevent moving unrelated objects like the player)
            t.transform.position = t.spawnCenter;
        }
    }

    private void PlacePlayerAtWorldSpawn()
    {
        var player = FindObjectOfType<BallisticTeleport>();
        if (player != null)
        {
            // set world position explicitly
            player.transform.position = worldSpawn;
        }
    }

    public void EndSet()
    {
        if (currentSetId == 0) return;

        float setDuration = Time.time - setStartTime;

        // flush any pending movement (write them even without accuracy)
        FlushAllPending();

        if (writer != null)
        {
            // Keep field count consistent: fill unused numeric fields with empty strings,
            // put setDuration into MovementDuration column and note as SET_END.
            string[] parts = new string[]
            {
                DateTime.Now.ToString("o"),                                 // Timestamp
                participantId,                                              // ParticipantId
                currentSetId.ToString(CultureInfo.InvariantCulture),       // SetId
                "0",                                                        // MovementIndex
                "", "", "",                                                 // StartX/Y/Z
                "", "", "",                                                 // TargetX/Y/Z
                "",                                                         // Distance
                setDuration.ToString(CultureInfo.InvariantCulture),        // MovementDuration (used for set summary)
                "",                                                         // DistanceFromTarget
                "SET_END"                                                   // Note
            };

            writer.WriteLine(string.Join(Delim.ToString(), parts));
        }

        OnSetEnded?.Invoke(currentSetId, setDuration);

        // ensure player is placed at world spawn when set ends
        PlacePlayerAtWorldSpawn();

        currentSetId = 0;
        movementIndex = 0;
    }

    /// <summary>
    /// Record a single movement. Called by locomotion scripts.
    /// Movement is buffered and event is fired. CSV line will be written when accuracy arrives
    /// or when flushed (next movement or end of set).
    /// </summary>
    public void RecordMovement(Vector3 start, Vector3 target, float distance, float movementDuration)
    {
        if (writer == null) InitWriterIfNeeded();

        // before adding new movement, flush any older pending entries that didn't receive accuracy
        // (this ensures we don't keep infinitely many pending if accuracy never comes)
        FlushPendingOlderThan(movementIndex + 1);

        movementIndex++;

        var rec = new MovementRecord()
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
            Note = ""
        };

        pendingRecords[movementIndex] = rec;

        // fire event immediately so UI updates
        OnMovementLogged?.Invoke(currentSetId, movementIndex, distance, movementDuration);

        // Auto-stop if limit reached (flush pending before ending)
        if (maxMovementsPerSet > 0 && movementIndex >= maxMovementsPerSet)
        {
            EndSet();
        }
    }

    /// <summary>
    /// Record the distance-from-target (accuracy) for the most recent movement.
    /// When accuracy arrives we finalize and write the corresponding CSV line.
    /// </summary>
    public void RecordAccuracy(float distanceFromTarget)
    {
        if (writer == null) InitWriterIfNeeded();

        int idx = movementIndex;

        // try to find pending record for this index
        if (pendingRecords.TryGetValue(idx, out MovementRecord rec))
        {
            rec.DistanceFromTarget = distanceFromTarget;
            WriteMovementRecord(rec);
            pendingRecords.Remove(idx);
        }
        else
        {
            // no pending record found (edge case) -> write a standalone line with accuracy only
            string[] parts = new string[]
            {
                DateTime.Now.ToString("o"),
                participantId,
                currentSetId.ToString(CultureInfo.InvariantCulture),
                idx.ToString(CultureInfo.InvariantCulture),
                "", "", "",
                "", "", "",
                "", // Distance
                "", // MovementDuration
                distanceFromTarget.ToString(CultureInfo.InvariantCulture), // DistanceFromTarget
                "ACCURACY_ORPHAN"
            };
            writer.WriteLine(string.Join(Delim.ToString(), parts));
        }

        OnAccuracyRecorded?.Invoke(currentSetId, idx, distanceFromTarget);
    }

    // write a buffered movement record into CSV (fully populated, accuracy may be null)
    private void WriteMovementRecord(MovementRecord r)
    {
        if (writer == null) InitWriterIfNeeded();

        string distFromTargetStr = r.DistanceFromTarget.HasValue
            ? r.DistanceFromTarget.Value.ToString(CultureInfo.InvariantCulture)
            : "";

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
            r.Note ?? ""
        };

        writer.WriteLine(string.Join(Delim.ToString(), parts));
    }

    // flush and write any pending records with index < threshold (or all if threshold <= 0)
    private void FlushPendingOlderThan(int thresholdExclusive)
    {
        var keys = new List<int>(pendingRecords.Keys);
        foreach (var k in keys)
        {
            if (thresholdExclusive <= 0 || k < thresholdExclusive)
            {
                var rec = pendingRecords[k];
                WriteMovementRecord(rec);
                pendingRecords.Remove(k);
            }
        }
    }

    // flush all pending records
    private void FlushAllPending()
    {
        FlushPendingOlderThan(int.MaxValue);
    }

    /// <summary>
    /// Optional helper: returns current csv path (null if not created yet).
    /// </summary>
    public string GetCsvPath()
    {
        return csvPath;
    }
}