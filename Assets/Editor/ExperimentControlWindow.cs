#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Floating EditorWindow to control the experiment during Play mode.
/// Menu: Window > Experiment Control
/// Interacts with ExperimentManager.Instance while in Play mode.
/// </summary>
public class ExperimentControlWindow : EditorWindow
{
    private string participantId = "P01";

    private Vector2 scroll;
    private string status = "Stopped";
    private int currentSet = 0;

    // condition flags shown in UI
    private bool tutorialModeFlag = false;
    private bool fadeFlag = false;
    private bool inAirFlag = false;

    // local representation of movements for the current set
    private class MovementEntry
    {
        public int Index;
        public float Distance;
        public float Duration;
        public float? Accuracy; // distance from target
        public override string ToString()
        {
            string acc = Accuracy.HasValue ? $"Acc={Accuracy.Value:F3}m" : "Acc=---";
            return $"#{Index}: Dist={Distance:F3}m Time={Duration:F3}s {acc}";
        }
    }

    private List<MovementEntry> movements = new List<MovementEntry>();

    [MenuItem("Window/Experiment Control")]
    public static void ShowWindow()
    {
        var w = GetWindow<ExperimentControlWindow>("Experiment Control");
        w.minSize = new Vector2(360, 240);
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        BindFromManager();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnbindFromManager();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        // Re-bind when entering Play mode
        if (state == PlayModeStateChange.EnteredPlayMode)
            BindFromManager();
        else if (state == PlayModeStateChange.ExitingPlayMode)
            UnbindFromManager();
    }

    private void BindFromManager()
    {
        if (!EditorApplication.isPlaying) return;

        var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
        if (mgr != null)
        {
            participantId = mgr.participantId;

            // read current condition flags
            tutorialModeFlag = mgr.tutorialMode;
            fadeFlag = mgr.fadeEnabled;
            inAirFlag = mgr.inAirControlEnabled;

            // subscribe
            mgr.OnSetStarted += OnSetStarted;
            mgr.OnSetEnded += OnSetEnded;
            mgr.OnMovementLogged += OnMovementLogged;
            mgr.OnAccuracyRecorded += OnAccuracyRecorded;

            // initialise local view
            currentSet = mgr.CurrentSetId;
            movements.Clear();
            status = currentSet == 0 ? "Stopped" : $"Set {currentSet} running";
        }
    }

    private void UnbindFromManager()
    {
        if (!EditorApplication.isPlaying) return;

        var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
        if (mgr != null)
        {
            mgr.OnSetStarted -= OnSetStarted;
            mgr.OnSetEnded -= OnSetEnded;
            mgr.OnMovementLogged -= OnMovementLogged;
            mgr.OnAccuracyRecorded -= OnAccuracyRecorded;
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Experiment Control (Editor Window)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying);
        participantId = EditorGUILayout.TextField("Participant ID", participantId);
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(6);

        // Condition controls
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
        bool newTutorial = EditorGUILayout.ToggleLeft("Tutorial Mode", tutorialModeFlag, GUILayout.Width(140));
        bool newFade = EditorGUILayout.ToggleLeft("Fade", fadeFlag, GUILayout.Width(80));
        bool newInAir = EditorGUILayout.ToggleLeft("In-Air Control", inAirFlag, GUILayout.Width(120));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        // If changed and in play mode, apply to manager
        if (EditorApplication.isPlaying)
        {
            var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
            if (mgr != null)
            {
                if (newTutorial != tutorialModeFlag)
                {
                    tutorialModeFlag = newTutorial;
                    mgr.SetTutorialMode(tutorialModeFlag);
                }
                if (newFade != fadeFlag)
                {
                    fadeFlag = newFade;
                    mgr.SetFadeEnabled(fadeFlag);
                }
                if (newInAir != inAirFlag)
                {
                    inAirFlag = newInAir;
                    mgr.SetInAirControlEnabled(inAirFlag);
                }
            }
        }

        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Set"))
        {
            if (EditorApplication.isPlaying)
            {
                var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
                if (mgr != null)
                {
                    // ensure manager uses current UI fields
                    mgr.participantId = participantId;
                    mgr.StartSet();
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Play mode required", "Please enter Play mode to control the experiment.", "OK");
            }
        }

        if (GUILayout.Button("End Set"))
        {
            if (EditorApplication.isPlaying)
            {
                var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
                mgr?.EndSet();
            }
            else
            {
                EditorUtility.DisplayDialog("Play mode required", "Please enter Play mode to control the experiment.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUILayout.Label($"Status: {status}");
        GUILayout.Label($"Current Set: {currentSet}");

        GUILayout.Space(6);
        GUILayout.Label("Movements in current set:", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(200));
        if (movements.Count == 0)
        {
            GUILayout.Label("<keine Bewegungen>");
        }
        else
        {
            foreach (var m in movements)
            {
                GUILayout.Label(m.ToString());
            }
        }
        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open CSV Folder"))
        {
            string path = Application.persistentDataPath;
            if (System.IO.Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                EditorUtility.DisplayDialog("Path not found", $"Path does not exist: {path}", "OK");
            }
        }

        if (GUILayout.Button("Ping ExperimentManager"))
        {
            var mgr = UnityEngine.Object.FindObjectOfType<ExperimentManager>();
            if (mgr != null)
                EditorUtility.DisplayDialog("Found", $"ExperimentManager found in Scene: {mgr.gameObject.name}", "OK");
            else
                EditorUtility.DisplayDialog("Not found", "No ExperimentManager instance found in the current scene.", "OK");
        }
        EditorGUILayout.EndHorizontal();
    }

    private void OnSetStarted(int setId)
    {
        status = $"Set {setId} started";
        currentSet = setId;
        movements.Clear();
        Repaint();
    }

    private void OnSetEnded(int setId, float duration)
    {
        status = $"Set {setId} ended ({duration:F2}s)";
        currentSet = 0;
        Repaint();
    }

    private void OnMovementLogged(int setId, int movementIndex, float distance, float movementDuration)
    {
        if (setId != currentSet) return;

        var entry = new MovementEntry()
        {
            Index = movementIndex,
            Distance = distance,
            Duration = movementDuration,
            Accuracy = null
        };

        movements.Add(entry);
        Repaint();
    }

    private void OnAccuracyRecorded(int setId, int movementIndex, float distanceFromTarget)
    {
        if (setId != currentSet) return;

        // find existing entry
        var e = movements.Find(x => x.Index == movementIndex);
        if (e != null)
        {
            e.Accuracy = distanceFromTarget;
        }
        else
        {
            // if not found, add a placeholder entry with accuracy only
            movements.Add(new MovementEntry()
            {
                Index = movementIndex,
                Distance = 0f,
                Duration = 0f,
                Accuracy = distanceFromTarget
            });
        }

        Repaint();
    }
}
#endif