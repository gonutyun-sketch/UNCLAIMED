using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Explicit, local verification of the delivery interaction; the apartment walk is tested separately.
[InitializeOnLoad]
public static class ParcelPlacementValidation
{
    private const string CheckDirectory = "UserSettings/ParcelPlacementCheck";
    private const string RequestPath = CheckDirectory + "/run.request";
    private const string ReportPath = CheckDirectory + "/report.txt";
    private static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    private static bool active;
    private static bool completing;
    private static bool passed;
    private static double startedAt;
    private static int runtimeErrors;
    private static Action pendingPress;
    private static Action pendingRelease;
    private static Mouse disabledMouse;
    private static bool restoreMouse;
    private static ParcelDeliveryInfo wrongDelivery;

    static ParcelPlacementValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !completing) return;
            Log("Edit Mode restored: true");
            Log("RESULT=" + (passed ? "PASS" : "FAIL"));
            completing = false;
        };
    }

    private static void Tick()
    {
        if (!active)
        {
            if (completing || !File.Exists(RequestPath) || !File.Exists(CheckDirectory + "/setup.txt") ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity" || scene.isDirty) return;
            File.Delete(RequestPath);
            Directory.CreateDirectory(CheckDirectory);
            File.WriteAllText(ReportPath, "Parcel placement: actual E/F input near the existing temporary 402 door.\n");
            active = true;
            runtimeErrors = 0;
            restoreMouse = false;
            disabledMouse = null;
            startedAt = EditorApplication.timeSinceStartup;
            steps.Clear();
            steps.Push(Walkthrough());
            Application.logMessageReceived += RecordError;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
            return;
        }

        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 90d)
                throw new TimeoutException("Parcel placement verification timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext())
                {
                    steps.Pop();
                    continue;
                }
                if (step.Current is IEnumerator nested)
                {
                    steps.Push(nested);
                    continue;
                }
                return;
            }
            Complete(true, "Placement guards, real E interaction, placed parcel and continued walking passed.");
        }
        catch (Exception error)
        {
            Complete(false, error.ToString());
        }
    }

    private static IEnumerator Walkthrough()
    {
        while (!EditorApplication.isPlaying) yield return null;
        yield return Frames(2);
        var van = UnityEngine.Object.FindAnyObjectByType<VanOpeningController>();
        var player = UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
        var carrier = UnityEngine.Object.FindAnyObjectByType<ParcelCarrier>();
        var point = UnityEngine.Object.FindAnyObjectByType<ParcelDeliveryPoint>();
        Require(van && player && carrier && point, "Opening, walking, carrier and delivery point exist.");
        var controller = player.GetComponent<CharacterController>();
        var camera = GameObject.Find("Main Camera").transform;
        var pointFields = new SerializedObject(point);
        var delivery = pointFields.FindProperty("deliveryInfo").objectReferenceValue as ParcelDeliveryInfo;
        var anchor = pointFields.FindProperty("placementAnchor").objectReferenceValue as Transform;
        var placed = pointFields.FindProperty("placedParcelVisual").objectReferenceValue as GameObject;
        var held = new SerializedObject(carrier).FindProperty("heldParcelVisual").objectReferenceValue as GameObject;
        var hint = GameObject.Find("CarryHint").GetComponent<TMP_Text>();
        var prompt = GameObject.Find("InteractionPrompt").GetComponent<TMP_Text>();
        var area = point.GetComponent<BoxCollider>();
        var interaction = point.GetComponent<InteractableObject>();
        Require(controller && camera && delivery && anchor && placed && held && hint && prompt && area && interaction,
            "Delivery data, visuals, trigger and HUD references exist.");
        Require(!point.IsPlaced && !carrier.IsCarrying && !placed.activeSelf,
            "A new delivery starts without a placed parcel.");
        van.enabled = false;
        yield return Frames(2);
        Require(player.isActiveAndEnabled && controller.enabled && camera.IsChildOf(player.transform),
            "Opening cleanup restores walking controls.");
        disabledMouse = Mouse.current;
        if (disabledMouse != null && disabledMouse.enabled)
        {
            InputSystem.DisableDevice(disabledMouse);
            restoreMouse = true;
        }

        // This fixture changes the start position only. Movement through the building has its own check.
        Vector3 nearPosition = new Vector3(anchor.position.x, 9.05f, 18.2f);
        PlaceFixture(player, controller, camera, nearPosition, area.transform.position);
        yield return Frames(3);
        Require(!point.CanPlaceParcel && !point.TryPlaceParcel() && !placed.activeSelf && !interaction.enabled,
            "Empty hands cannot place a parcel or display a delivery interaction.");
        QueueInput(Key.E, true);
        yield return Frames(3);
        Require(!point.IsPlaced && !carrier.IsCarrying, "Empty-hand E leaves the door untouched.");

        wrongDelivery = ScriptableObject.CreateInstance<ParcelDeliveryInfo>();
        Require(carrier.TryTakeParcel(wrongDelivery), "The fixture can carry a different delivery job.");
        yield return Frames(2);
        Require(!point.CanPlaceParcel && !point.TryPlaceParcel() && carrier.IsCarrying && !placed.activeSelf,
            "A different delivery job is rejected without losing the carried parcel.");
        Require(carrier.TryReleaseParcel(out ParcelDeliveryInfo rejected) && rejected == wrongDelivery,
            "The wrong-job fixture releases cleanly.");
        Require(carrier.TryTakeParcel(delivery), "The matching delivery can be carried.");
        yield return Frames(3);

        PlaceFixture(player, controller, camera, nearPosition + Vector3.left * 5f, area.transform.position);
        yield return Frames(2);
        Require(!point.CanPlaceParcel && !point.TryPlaceParcel() && carrier.IsCarrying,
            "Calling placement beyond the permitted distance preserves the parcel.");
        PlaceFixture(player, controller, camera, nearPosition, area.transform.position);
        yield return Frames(3);

        // Exercise missing references without allowing an inventory mutation.
        pointFields.Update();
        pointFields.FindProperty("placedParcelVisual").objectReferenceValue = null;
        pointFields.ApplyModifiedPropertiesWithoutUndo();
        Require(!point.TryPlaceParcel() && carrier.IsCarrying && !point.IsPlaced,
            "An unavailable placed visual cannot consume the parcel.");
        pointFields.Update();
        pointFields.FindProperty("placedParcelVisual").objectReferenceValue = placed;
        pointFields.ApplyModifiedPropertiesWithoutUndo();
        yield return Frames(3);
        Require(point.CanPlaceParcel && interaction.enabled && area.enabled && prompt.text.Contains(interaction.Prompt),
            "The matching door presents a reachable placement prompt: " + prompt.text);

        // A longer temporary duration ensures the closing-animation guard is sampled deterministically.
        var carrierFields = new SerializedObject(carrier);
        float normalDuration = carrierFields.FindProperty("inspectionDuration").floatValue;
        carrierFields.FindProperty("inspectionDuration").floatValue = 0.8f;
        carrierFields.ApplyModifiedPropertiesWithoutUndo();
        QueueInput(Key.F, true);
        yield return Frames(3);
        Require(carrier.IsInspecting && carrier.BlocksPlayerActions && !point.CanPlaceParcel,
            "Label inspection suppresses delivery placement.");
        QueueInput(Key.E, true);
        yield return Frames(3);
        Require(carrier.IsCarrying && !point.IsPlaced && !placed.activeSelf && string.IsNullOrEmpty(prompt.text),
            "E during label inspection keeps the parcel in the player's hands.");
        yield return Seconds(0.85d);
        QueueInput(Key.F, true);
        yield return Frames(2);
        Require(!carrier.IsInspecting && carrier.BlocksPlayerActions && !point.TryPlaceParcel() && carrier.IsCarrying,
            "Closing the label inspection also blocks an early placement.");
        yield return Seconds(0.9d);
        carrierFields.Update();
        carrierFields.FindProperty("inspectionDuration").floatValue = normalDuration;
        carrierFields.ApplyModifiedPropertiesWithoutUndo();
        yield return Frames(3);
        Require(point.CanPlaceParcel && prompt.text.Contains(interaction.Prompt),
            "The placement prompt returns after inspection has fully closed.");

        QueueInput(Key.E, true);
        yield return Frames(4);
        Require(point.IsPlaced && !carrier.IsCarrying && carrier.CurrentDelivery == null && !held.activeSelf,
            "Actual E moves the matching parcel out of the player's hands.");
        Require(placed.activeInHierarchy && point.ParcelInfo == delivery &&
            Vector3.Distance(placed.transform.position, anchor.position) < 0.01f,
            "One parcel is placed at the configured door anchor with the correct delivery job.");
        Require(!interaction.enabled && !area.enabled && string.IsNullOrEmpty(prompt.text) && string.IsNullOrEmpty(hint.text),
            "Placement consumes the target interaction and clears carry/interaction hints.");
        var label = placed.GetComponentInChildren<ParcelLabelDisplay>(true);
        Require(label && label.Data == delivery && label.LabelText && label.LabelText.text.Contains(delivery.DisplayAddress),
            "The placed parcel retains its physical delivery label: " + delivery.DisplayAddress);
        Require(placed.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger),
            "The placed parcel has a solid world collider.");
        Require(held.GetComponentsInChildren<Collider>(true).All(c => !c.enabled),
            "The hidden carried visual keeps its colliders disabled.");
        int countBefore = anchor.GetComponentsInChildren<ParcelLabelDisplay>(true).Length;
        Vector3 placedPosition = placed.transform.position;
        QueueInput(Key.E, true);
        yield return Frames(3);
        Require(!point.TryPlaceParcel() && point.IsPlaced && placed.activeInHierarchy &&
            anchor.GetComponentsInChildren<ParcelLabelDisplay>(true).Length == countBefore &&
            Vector3.Distance(placed.transform.position, placedPosition) < 0.001f,
            "Repeated E and a repeated placement call cannot create or move another parcel.");

        Aim(player, camera, placed.transform.position);
        yield return Frames(2);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(CheckDirectory + "/placed.png"));
        yield return Frames(3);
        player.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        SetPitch(player, camera, 0f);
        Vector3 beforeWalking = player.transform.position;
        QueueInput(Key.W, false);
        yield return Seconds(0.3d);
        ReleaseInput();
        yield return Frames(3);
        Vector3 walked = player.transform.position - beforeWalking;
        walked.y = 0f;
        Require(walked.magnitude > 0.2f && !carrier.IsCarrying && point.IsPlaced,
            "Walking continues after placement; distance=" + walked.magnitude.ToString("F3"));
        Require(runtimeErrors == 0, "No runtime errors were logged during placement.");
    }

    private static IEnumerator Frames(int count)
    {
        int until = Time.frameCount + count;
        while (Time.frameCount < until) yield return null;
    }

    private static IEnumerator Seconds(double count)
    {
        double until = EditorApplication.timeSinceStartup + count;
        while (EditorApplication.timeSinceStartup < until) yield return null;
    }

    private static void PlaceFixture(FirstPersonController player, CharacterController controller,
        Transform camera, Vector3 position, Vector3 target)
    {
        controller.enabled = false;
        player.transform.position = position;
        controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Aim(player, camera, target);
        Physics.SyncTransforms();
    }

    private static void Aim(FirstPersonController player, Transform camera, Vector3 target)
    {
        Vector3 direction = target - camera.position;
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        SetPitch(player, camera, -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg);
    }

    private static void SetPitch(FirstPersonController player, Transform camera, float pitch)
    {
        typeof(FirstPersonController).GetField("cameraPitch", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(player, pitch);
        camera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private static void QueueInput(Key key, bool releaseNextFrame)
    {
        RemovePendingInput();
        var keyboard = Keyboard.current;
        if (keyboard == null) throw new InvalidOperationException("Input System keyboard is unavailable.");
        pendingPress = () =>
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.onBeforeUpdate -= pendingPress;
            pendingPress = null;
            int pressedAtFrame = Time.frameCount;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            if (!releaseNextFrame) return;
            pendingRelease = () =>
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic || Time.frameCount <= pressedAtFrame) return;
                InputSystem.onBeforeUpdate -= pendingRelease;
                pendingRelease = null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            };
            InputSystem.onBeforeUpdate += pendingRelease;
        };
        InputSystem.onBeforeUpdate += pendingPress;
    }

    private static void ReleaseInput()
    {
        RemovePendingInput();
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        pendingRelease = () =>
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.onBeforeUpdate -= pendingRelease;
            pendingRelease = null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        };
        InputSystem.onBeforeUpdate += pendingRelease;
    }

    private static void RemovePendingInput()
    {
        if (pendingPress != null) InputSystem.onBeforeUpdate -= pendingPress;
        if (pendingRelease != null) InputSystem.onBeforeUpdate -= pendingRelease;
        pendingPress = null;
        pendingRelease = null;
    }

    private static void Complete(bool success, string message)
    {
        Log(message);
        active = false;
        steps.Clear();
        passed = success;
        completing = true;
        Application.logMessageReceived -= RecordError;
        RemovePendingInput();
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        if (restoreMouse && disabledMouse != null && disabledMouse.added)
        {
            InputSystem.ResetDevice(disabledMouse);
            InputSystem.EnableDevice(disabledMouse);
        }
        restoreMouse = false;
        if (wrongDelivery) UnityEngine.Object.DestroyImmediate(wrongDelivery);
        wrongDelivery = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else
        {
            Log("RESULT=" + (passed ? "PASS" : "FAIL"));
            completing = false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Log("PASS: " + message);
    }

    private static void RecordError(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        runtimeErrors++;
        Log("Runtime " + type + ": " + condition + "\n" + stackTrace);
    }

    private static void Log(string text)
    {
        Directory.CreateDirectory(CheckDirectory);
        File.AppendAllText(ReportPath, text + "\n");
    }
}
