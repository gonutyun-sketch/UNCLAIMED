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

// An explicit request runs one real CharacterController walk from the van through all six floors.
[InitializeOnLoad]
public static class ApartmentNavigationValidation
{
    private const string CheckDirectory = "UserSettings/ApartmentLayoutCheck";
    private const string RequestPath = CheckDirectory + "/run.request";
    private const string ReportPath = CheckDirectory + "/report.txt";
    private static IEnumerator route;
    private static bool active;
    private static bool completing;
    private static bool passed;
    private static double startedAt;
    private static int runtimeErrors;
    private static Action pendingInput;
    private static Mouse disabledMouse;
    private static bool restoreMouse;
    private static FirstPersonController player;
    private static CharacterController controller;
    private static ParcelCarrier carrier;
    private static Transform camera;
    private static string currentLeg;
    private static Vector3 currentTarget;

    private struct Waypoint
    {
        public string Name;
        public Vector3 Position;
        public string Screenshot;

        public Waypoint(string name, Vector3 position, string screenshot = null)
        {
            Name = name;
            Position = position;
            Screenshot = screenshot;
        }
    }

    static ApartmentNavigationValidation()
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
            File.WriteAllText(ReportPath, "Apartment navigation: real FPC W input, no position teleporting.\n");
            active = true;
            runtimeErrors = 0;
            restoreMouse = false;
            disabledMouse = null;
            currentLeg = "Starting Play Mode";
            startedAt = EditorApplication.timeSinceStartup;
            route = WalkRoute();
            Application.logMessageReceived += RecordError;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
            return;
        }

        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 180d)
                throw new TimeoutException("Overall navigation timeout. " + PositionReport());
            if (!route.MoveNext()) Complete(true, "Carried parcel from the van to room 402 and onward to the sixth floor.");
        }
        catch (Exception error)
        {
            Complete(false, error + "\n" + PositionReport());
        }
    }

    private static IEnumerator WalkRoute()
    {
        while (!EditorApplication.isPlaying) yield return null;
        int readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;

        var van = UnityEngine.Object.FindFirstObjectByType<VanOpeningController>();
        player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
        carrier = UnityEngine.Object.FindFirstObjectByType<ParcelCarrier>();
        var pickup = UnityEngine.Object.FindFirstObjectByType<ParcelPickup>();
        Require(van && player && carrier && pickup, "Opening, player, carrier and pickup exist.");
        controller = player.GetComponent<CharacterController>();
        camera = GameObject.Find("Main Camera").transform;

        // The existing opening cleanup restores its saved outside spawn and walking controls.
        van.enabled = false;
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(player.isActiveAndEnabled && controller.enabled && camera.IsChildOf(player.transform),
            "Opening cleanup restored walking and the player camera.");
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        disabledMouse = Mouse.current;
        if (disabledMouse != null && disabledMouse.enabled)
        {
            InputSystem.DisableDevice(disabledMouse);
            restoreMouse = true;
        }
        ResetPitch();
        pickup.PickUp();
        readyFrame = Time.frameCount + 2;
        while (Time.frameCount < readyFrame) yield return null;
        Require(carrier.IsCarrying && !carrier.BlocksPlayerActions,
            "Navigation fixture collects the parcel through the existing pickup method.");
        Require(Keyboard.current != null, "Input System keyboard exists.");
        Log("Start position: " + player.transform.position.ToString("F3"));

        foreach (Waypoint waypoint in BuildRoute())
        {
            IEnumerator leg = WalkLeg(waypoint);
            while (leg.MoveNext()) yield return leg.Current;
            if (waypoint.Screenshot == "stairs.png") FaceYaw(180f);
            if (waypoint.Screenshot == "floor4.png") FaceYaw(0f);
            if (waypoint.Screenshot == "floor6.png") FaceYaw(-90f);
            if (waypoint.Screenshot != null)
            {
                readyFrame = Time.frameCount + 2;
                while (Time.frameCount < readyFrame) yield return null;
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(CheckDirectory, waypoint.Screenshot)));
                readyFrame = Time.frameCount + 2;
                while (Time.frameCount < readyFrame) yield return null;
            }
        }

        var signs = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true));
        Require(signs.Any(sign => sign.text.Contains("402") &&
            sign.transform.position.y >= 9f && sign.transform.position.y < 12f),
            "A room 402 sign exists at fourth-floor height.");
        Require(Mathf.Abs(player.transform.position.y - 15.05f) <= 0.25f && carrier.IsCarrying,
            "The player reaches the sixth floor while still carrying the parcel.");
        Require(runtimeErrors == 0, "No runtime errors were logged during navigation.");
    }

    private static IEnumerable<Waypoint> BuildRoute()
    {
        yield return new Waypoint("Clear van", new Vector3(-11f, 0.05f, 8f));
        yield return new Waypoint("Entrance approach", new Vector3(0f, 0.05f, 14.5f));
        yield return new Waypoint("Lobby", new Vector3(0f, 0.05f, 19f), "lobby.png");
        yield return new Waypoint("Stair approach", new Vector3(-1.6f, 0.05f, 23.8f));
        for (int i = 0; i < 3; i++)
        {
            float y = i * 3f;
            string prefix = "Floor " + (i + 1) + " to " + (i + 2) + ": ";
            yield return new Waypoint(prefix + "A start", new Vector3(-1.6f, y + 0.05f, 24f));
            yield return new Waypoint(prefix + "A top", new Vector3(-1.6f, y + 1.55f, 28.2f));
            yield return new Waypoint(prefix + "mid landing", new Vector3(1.6f, y + 1.55f, 28.2f),
                i == 0 ? "stairs.png" : null);
            yield return new Waypoint(prefix + "B top", new Vector3(1.6f, y + 3.05f, 23.8f));
            yield return new Waypoint(prefix + "front landing", new Vector3(0f, y + 3.05f, 23.3f));
        }
        yield return new Waypoint("Fourth-floor corridor", new Vector3(0f, 9.05f, 19f));
        yield return new Waypoint("Room 402 approach", new Vector3(-13f, 9.05f, 19f), "floor4.png");
        yield return new Waypoint("Return from room 402", new Vector3(0f, 9.05f, 19f));
        yield return new Waypoint("Fourth-floor stair approach", new Vector3(-1.6f, 9.05f, 23.8f));
        for (int i = 3; i < 5; i++)
        {
            float y = i * 3f;
            string prefix = "Floor " + (i + 1) + " to " + (i + 2) + ": ";
            yield return new Waypoint(prefix + "A start", new Vector3(-1.6f, y + 0.05f, 24f));
            yield return new Waypoint(prefix + "A top", new Vector3(-1.6f, y + 1.55f, 28.2f));
            yield return new Waypoint(prefix + "mid landing", new Vector3(1.6f, y + 1.55f, 28.2f));
            yield return new Waypoint(prefix + "B top", new Vector3(1.6f, y + 3.05f, 23.8f));
            yield return new Waypoint(prefix + "front landing", new Vector3(0f, y + 3.05f, 23.3f));
        }
        yield return new Waypoint("Sixth-floor corridor", new Vector3(0f, 15.05f, 19f), "floor6.png");
    }

    private static IEnumerator WalkLeg(Waypoint waypoint)
    {
        currentLeg = waypoint.Name;
        currentTarget = waypoint.Position;
        double beganAt = EditorApplication.timeSinceStartup;
        double lastProgressAt = beganAt;
        float startHeight = player.transform.position.y;
        float bestDistance = HorizontalDistance(player.transform.position, waypoint.Position);
        ResetPitch();
        FacePoint(waypoint.Position);
        QueueW(true);
        while (HorizontalDistance(player.transform.position, waypoint.Position) > 0.18f)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped during the route.");
            if (!Application.isFocused || Cursor.lockState != CursorLockMode.Locked)
                throw new InvalidOperationException("Game focus or cursor lock was interrupted. " + PositionReport());
            if (carrier.BlocksPlayerActions || !carrier.IsCarrying || !player.isActiveAndEnabled || !controller.enabled)
                throw new InvalidOperationException("Walking or carrying state changed during the route. " + PositionReport());
            if (Keyboard.current != null && (Keyboard.current.aKey.isPressed || Keyboard.current.sKey.isPressed ||
                Keyboard.current.dKey.isPressed || Keyboard.current.fKey.isPressed || Keyboard.current.escapeKey.isPressed))
                throw new InvalidOperationException("Unexpected manual keyboard input interrupted the route.");

            FacePoint(waypoint.Position);
            float distance = HorizontalDistance(player.transform.position, waypoint.Position);
            if (distance < bestDistance - 0.025f)
            {
                bestDistance = distance;
                lastProgressAt = EditorApplication.timeSinceStartup;
            }
            if (player.transform.position.y < Mathf.Max(-0.6f, startHeight - 0.6f))
                throw new InvalidOperationException("The player fell below the route. " + PositionReport());
            if (EditorApplication.timeSinceStartup - beganAt > 15d ||
                EditorApplication.timeSinceStartup - lastProgressAt > 3d)
                throw new TimeoutException("Walking stalled or exceeded the leg timeout. " + PositionReport());
            yield return null;
        }

        QueueW(false);
        int stopFrame = Time.frameCount + 3;
        while (Time.frameCount < stopFrame) yield return null;
        Require(Mathf.Abs(player.transform.position.y - waypoint.Position.y) <= 0.25f,
            waypoint.Name + " reached expected height; actual=" + player.transform.position.ToString("F3") +
            ", target=" + waypoint.Position.ToString("F3") + ", carrying=" + carrier.IsCarrying);
    }

    private static void FacePoint(Vector3 point)
    {
        Vector3 direction = point - player.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            player.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private static void FaceYaw(float yaw)
    {
        player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        ResetPitch();
    }

    private static void ResetPitch()
    {
        typeof(FirstPersonController).GetField("cameraPitch", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(player, 0f);
        camera.localRotation = Quaternion.identity;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static void QueueW(bool pressed)
    {
        if (pendingInput != null) InputSystem.onBeforeUpdate -= pendingInput;
        var keyboard = Keyboard.current;
        if (keyboard == null) throw new InvalidOperationException("The keyboard disconnected.");
        pendingInput = () =>
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.onBeforeUpdate -= pendingInput;
            pendingInput = null;
            InputSystem.QueueStateEvent(keyboard, pressed ? new KeyboardState(Key.W) : new KeyboardState());
        };
        InputSystem.onBeforeUpdate += pendingInput;
    }

    private static string PositionReport()
    {
        return "Leg=" + currentLeg + "; target=" + currentTarget.ToString("F3") +
            "; position=" + (player ? player.transform.position.ToString("F3") : "unknown") +
            "; grounded=" + (controller && controller.isGrounded) +
            "; floor=" + (player ? (Mathf.FloorToInt((player.transform.position.y + 0.2f) / 3f) + 1).ToString() : "unknown") +
            "; carrying=" + (carrier && carrier.IsCarrying);
    }

    private static void Complete(bool success, string message)
    {
        Log(message);
        active = false;
        route = null;
        passed = success;
        completing = true;
        Application.logMessageReceived -= RecordError;
        if (pendingInput != null) InputSystem.onBeforeUpdate -= pendingInput;
        pendingInput = null;
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        if (restoreMouse && disabledMouse != null && disabledMouse.added)
        {
            InputSystem.ResetDevice(disabledMouse);
            InputSystem.EnableDevice(disabledMouse);
        }
        restoreMouse = false;
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
