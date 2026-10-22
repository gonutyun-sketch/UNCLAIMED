using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Runs only when a developer creates run.request after completing the scene setup.
[InitializeOnLoad]
public static class ParcelPickupValidation
{
    private const string DirectoryPath = "UserSettings/ParcelPickupCheck";
    private const string RequestPath = DirectoryPath + "/run.request";
    private const string ReportPath = DirectoryPath + "/report.txt";
    private static IEnumerator walkthrough;
    private static double startedAt;
    private static bool active;
    private static bool completing;
    private static bool passed;
    private static int runtimeErrors;
    private static Action pendingPress;
    private static Action pendingRelease;
    private static int inputTraceEndFrame;

    static ParcelPickupValidation()
    {
        EditorApplication.update += Tick;
        InputSystem.onAfterUpdate += TraceInput;
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
            if (completing || !File.Exists(RequestPath) ||
                !File.Exists(DirectoryPath + "/setup.txt") || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity" || scene.isDirty)
                return;

            File.Delete(RequestPath);
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(ReportPath, "Parcel pickup walkthrough\n");
            runtimeErrors = 0;
            active = true;
            startedAt = EditorApplication.timeSinceStartup;
            walkthrough = Walkthrough();
            Application.logMessageReceived += RecordError;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
            return;
        }

        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 120d)
                throw new TimeoutException("Walkthrough timed out; check Game View focus and input processing.");
            if (!walkthrough.MoveNext()) Complete(true, "All pickup, label inspection and movement checks passed.");
        }
        catch (Exception error)
        {
            Complete(false, error.ToString());
        }
    }

    private static IEnumerator Walkthrough()
    {
        while (!EditorApplication.isPlaying) yield return null;
        // All Awake and Start calls must finish before reading the opening state.
        int readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;

        var van = UnityEngine.Object.FindFirstObjectByType<VanOpeningController>();
        var carrier = UnityEngine.Object.FindFirstObjectByType<ParcelCarrier>();
        var pickup = UnityEngine.Object.FindFirstObjectByType<ParcelPickup>();
        Require(van && carrier && pickup, "Opening, carrier and pickup components exist.");
        var player = carrier.GetComponent<FirstPersonController>();
        var controller = carrier.GetComponent<CharacterController>();
        var interactor = carrier.GetComponent<PlayerInteractor>();
        var camera = player.GetComponentInChildren<Camera>(true);
        // The opening temporarily reparents the camera beneath DriverView.
        if (!camera) camera = GameObject.Find("Main Camera").GetComponent<Camera>();
        var held = camera.transform.Find("ParcelHoldPoint/HeldParcel");
        var prompt = GameObject.Find("InteractionPrompt").GetComponent<TMP_Text>();
        var source = new SerializedObject(pickup).FindProperty("parcelInVan").objectReferenceValue as GameObject;
        var pickupArea = pickup.GetComponent<BoxCollider>();
        Require(player && controller && interactor && camera && held && prompt && source && pickupArea,
            "All scene references exist.");
        Require(van.IsInVan && !carrier.IsCarrying && !held.gameObject.activeSelf && !interactor.enabled,
            "Seated start has no held parcel and no walking interaction.");
        Require(!carrier.TryTakeParcel(), "Cannot take a parcel while seated.");
        QueueInput(Key.F, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Require(!carrier.IsInspecting && !carrier.BlocksPlayerActions,
            "F cannot inspect a parcel while seated and empty-handed.");

        while (!van.IntroFinished)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play Mode ended during the opening.");
            yield return null;
        }
        van.ExitVehicle();
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(player.enabled && controller.enabled && interactor.enabled && !van.IsInVan,
            "Walking, collisions and interaction resume after exiting.");
        QueueInput(Key.F, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Require(!carrier.IsInspecting && !carrier.BlocksPlayerActions,
            "F cannot inspect a parcel while walking empty-handed.");

        Vector3 side = van.transform.TransformPoint(new Vector3(-2.4f, 0.05f, -2.1f));
        Vector3 towardPickup = pickup.transform.position - side;
        towardPickup.y = 0f;
        Teleport(player, controller, camera.transform, side, Quaternion.LookRotation(towardPickup));
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(string.IsNullOrEmpty(prompt.text), "Cargo side wall occludes the rear pickup prompt.");
        QueueInput(Key.E, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Require(!carrier.IsCarrying && source.activeSelf, "E cannot collect a parcel through the cargo side wall.");

        Teleport(player, controller, camera.transform,
            van.transform.TransformPoint(new Vector3(0f, 0.05f, -5f)), van.transform.rotation);
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(string.IsNullOrEmpty(prompt.text), "Looking above the parcel does not show its pickup prompt.");
        Vector3 lookAngles = Quaternion.LookRotation(source.transform.position - camera.transform.position).eulerAngles;
        float parcelPitch = Mathf.DeltaAngle(0f, lookAngles.x);
        typeof(FirstPersonController).GetField("cameraPitch",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(player, parcelPitch);
        camera.transform.localRotation = Quaternion.Euler(parcelPitch, 0f, 0f);
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(prompt.text == "[E] 택배 꺼내기", "Rear pickup prompt appears: " + prompt.text);
        Capture("pickup.png");
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;

        pickup.GetComponent<InteractableObject>().AddListener(() =>
        {
            Log("onInteract callback reached at game frame " + Time.frameCount);
            Snapshot("inside interaction event", player, interactor, carrier, pickup, held, source, pickupArea, prompt);
        });
        Snapshot("before rear E", player, interactor, carrier, pickup, held, source, pickupArea, prompt);
        QueueInput(Key.E, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Snapshot("after rear E", player, interactor, carrier, pickup, held, source, pickupArea, prompt);
        Require(carrier.IsCarrying && held.gameObject.activeInHierarchy, "E shows the held parcel.");
        Require(!source.activeSelf && !pickupArea.enabled, "The source parcel and pickup trigger are consumed.");
        Require(string.IsNullOrEmpty(prompt.text), "The pickup prompt clears after collection.");
        Require(held.GetComponentsInChildren<Collider>(true).All(c => !c.enabled),
            "Held parcel colliders are disabled.");
        Require(!carrier.TryTakeParcel(), "A second pickup cannot duplicate the carried parcel.");

        Vector3 movementStart = player.transform.position;
        QueueInput(Key.W, false);
        double stopAt = EditorApplication.timeSinceStartup + 0.4d;
        while (EditorApplication.timeSinceStartup < stopAt) yield return null;
        ReleaseInput();
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Vector3 movement = player.transform.position - movementStart;
        movement.y = 0f;
        Require(movement.magnitude > 0.2f && carrier.IsCarrying && held.gameObject.activeInHierarchy,
            "W moves the player while carrying; distance=" + movement.magnitude.ToString("F3"));
        Capture("carried.png");
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;

        Vector3 carryPosition = held.parent.localPosition;
        Quaternion carryRotation = held.localRotation;
        var label = held.GetComponentInChildren<ParcelLabelDisplay>(true);
        Require(label && label.LabelText && carrier.CurrentDelivery != null,
            "The carried parcel has delivery data and a world-space label.");

        // A real target in front of the camera makes the inspection E-block check meaningful.
        var probe = new GameObject("InspectionInteractionProbe");
        probe.transform.position = camera.transform.position + camera.transform.forward;
        var probeCollider = probe.AddComponent<BoxCollider>();
        probeCollider.size = Vector3.one * 0.3f;
        probeCollider.isTrigger = true;
        var probeInteraction = probe.AddComponent<InteractableObject>();
        int probeInteractions = 0;
        probeInteraction.AddListener(() => probeInteractions++);
        Physics.SyncTransforms();
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(prompt.text == "[E] " + probeInteraction.Prompt,
            "The inspection interaction probe is reachable before inspection.");

        QueueInput(Key.F, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Require(carrier.IsInspecting && carrier.BlocksPlayerActions,
            "F raises the parcel and blocks walking and interaction.");
        double settleAt = EditorApplication.timeSinceStartup + 0.45d;
        while (EditorApplication.timeSinceStartup < settleAt) yield return null;
        Require(Vector3.Distance(held.parent.localPosition, new Vector3(0f, -0.02f, 0.65f)) < 0.02f &&
            Quaternion.Angle(held.localRotation, Quaternion.Euler(-90f, 0f, 0f)) < 2f,
            "Inspection settles into the raised, label-facing-camera pose.");
        label.LabelText.ForceMeshUpdate();
        Require(label.LabelText.text.Contains(carrier.CurrentDelivery.DisplayAddress),
            "The physical label contains the current delivery address: " + carrier.CurrentDelivery.DisplayAddress);
        string labelCharacters = new string(label.LabelText.GetParsedText().Where(c => !char.IsControl(c)).ToArray());
        Require(label.LabelText.font.HasCharacters(labelCharacters, out uint[] missingGlyphs, true, false),
            "The label has no missing glyphs" + (missingGlyphs == null ? "." : ": " + string.Join(",", missingGlyphs)));
        Require(!label.LabelText.isTextOverflowing, "The delivery label text does not overflow.");
        Require(string.IsNullOrEmpty(prompt.text), "The interaction prompt is hidden during inspection.");
        Capture("inspection.png");

        Vector3 inspectingPosition = player.transform.position;
        QueueInput(Key.W, false);
        stopAt = EditorApplication.timeSinceStartup + 0.35d;
        while (EditorApplication.timeSinceStartup < stopAt) yield return null;
        ReleaseInput();
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Vector3 blockedMovement = player.transform.position - inspectingPosition;
        blockedMovement.y = 0f;
        Require(blockedMovement.magnitude < 0.03f,
            "W cannot move the player during inspection; distance=" + blockedMovement.magnitude.ToString("F3"));
        QueueInput(Key.E, true);
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        Require(probeInteractions == 0 && string.IsNullOrEmpty(prompt.text) && carrier.IsInspecting,
            "E does not invoke a reachable interaction while inspecting.");
        UnityEngine.Object.Destroy(probe);

        QueueInput(Key.F, true);
        settleAt = EditorApplication.timeSinceStartup + 0.5d;
        while (EditorApplication.timeSinceStartup < settleAt) yield return null;
        Require(!carrier.IsInspecting && !carrier.BlocksPlayerActions && carrier.IsCarrying,
            "F finishes inspection while retaining the carried parcel.");
        Require(Vector3.Distance(held.parent.localPosition, carryPosition) < 0.02f &&
            Quaternion.Angle(held.localRotation, carryRotation) < 2f,
            "The normal carrying pose is restored after inspection.");

        movementStart = player.transform.position;
        QueueInput(Key.W, false);
        stopAt = EditorApplication.timeSinceStartup + 0.35d;
        while (EditorApplication.timeSinceStartup < stopAt) yield return null;
        ReleaseInput();
        readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        movement = player.transform.position - movementStart;
        movement.y = 0f;
        Require(movement.magnitude > 0.2f && carrier.IsCarrying,
            "Walking resumes after closing inspection; distance=" + movement.magnitude.ToString("F3"));
        Require(runtimeErrors == 0, "No runtime errors were logged.");
    }

    private static void Teleport(FirstPersonController player, CharacterController controller,
        Transform camera, Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        camera.localRotation = Quaternion.identity;
        controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Physics.SyncTransforms();
    }

    private static void QueueInput(Key key, bool releaseNextUpdate)
    {
        Require(Keyboard.current != null, "Input System keyboard exists for " + key + ".");
        RemovePendingInput();
        var keyboard = Keyboard.current;
        pendingPress = () =>
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.onBeforeUpdate -= pendingPress;
            pendingPress = null;
            int pressedAtFrame = Time.frameCount;
            inputTraceEndFrame = pressedAtFrame + 5;
            Log("Before queue at Dynamic frame " + pressedAtFrame + ": E down=" +
                keyboard.eKey.isPressed + ", E pressedThisFrame=" + keyboard.eKey.wasPressedThisFrame);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            Log("Queued Unity Input System key: " + key);
            if (!releaseNextUpdate) return;
            pendingRelease = () =>
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic || Time.frameCount <= pressedAtFrame) return;
                InputSystem.onBeforeUpdate -= pendingRelease;
                pendingRelease = null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Log("Queued release at Dynamic frame " + Time.frameCount);
            };
            InputSystem.onBeforeUpdate += pendingRelease;
        };
        InputSystem.onBeforeUpdate += pendingPress;
    }

    private static void RemovePendingInput()
    {
        if (pendingPress != null) InputSystem.onBeforeUpdate -= pendingPress;
        if (pendingRelease != null) InputSystem.onBeforeUpdate -= pendingRelease;
        pendingPress = null;
        pendingRelease = null;
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

    private static void Complete(bool success, string message)
    {
        Log(message);
        active = false;
        walkthrough = null;
        passed = success;
        completing = true;
        Application.logMessageReceived -= RecordError;
        RemovePendingInput();
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.ExitPlaymode();
        else
        {
            Log("RESULT=" + (passed ? "PASS" : "FAIL"));
            completing = false;
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Log("PASS: " + description);
    }

    private static void TraceInput()
    {
        if (!active || InputState.currentUpdateType != InputUpdateType.Dynamic ||
            Time.frameCount > inputTraceEndFrame || Keyboard.current == null) return;
        Log("After Dynamic frame " + Time.frameCount + ": E down=" + Keyboard.current.eKey.isPressed +
            ", E pressedThisFrame=" + Keyboard.current.eKey.wasPressedThisFrame +
            ", focus=" + Application.isFocused + ", cursor=" + Cursor.lockState);
    }

    private static void Snapshot(string label, FirstPersonController player, PlayerInteractor interactor,
        ParcelCarrier carrier, ParcelPickup pickup, Transform held, GameObject source,
        BoxCollider pickupArea, TMP_Text prompt)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var target = typeof(PlayerInteractor).GetField("currentTarget", fields)?.GetValue(interactor) as InteractableObject;
        object collected = typeof(ParcelPickup).GetField("collected", fields)?.GetValue(pickup);
        var carrierPlayer = typeof(ParcelCarrier).GetField("playerController", fields)?.GetValue(carrier) as FirstPersonController;
        Log("Snapshot " + label + ", frame=" + Time.frameCount + ", player=" + player.isActiveAndEnabled +
            ", interactor=" + interactor.isActiveAndEnabled + ", target=" + (target ? target.name : "null") +
            ", prompt=" + prompt.text + ", carrier=" + carrier.isActiveAndEnabled +
            ", carrierPlayerAssigned=" + (carrierPlayer != null) + ", carrying=" + carrier.IsCarrying +
            ", heldSelf=" + held.gameObject.activeSelf + ", heldHierarchy=" + held.gameObject.activeInHierarchy +
            ", source=" + source.activeSelf + ", pickup=" + pickup.isActiveAndEnabled +
            ", collected=" + collected + ", area=" + pickupArea.enabled +
            ", focus=" + Application.isFocused + ", cursor=" + Cursor.lockState);
    }

    private static void RecordError(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        runtimeErrors++;
        Log("Runtime " + type + ": " + condition + "\n" + stackTrace);
    }

    private static void Capture(string filename)
    {
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(DirectoryPath, filename)));
    }

    private static void Log(string text)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.AppendAllText(ReportPath, text + "\n");
    }
}
