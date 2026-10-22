using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

[InitializeOnLoad]
public static class VanOpeningSetup
{
    private const string ScenePath = "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity";
    private const string FontPath = "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular.otf";
    private const string FontAssetPath = "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular SDF.asset";
    private const string ReportDirectory = "Temp/VanOpeningCheck";
    private const string ArmedKey = "UNCLAIMED.VanOpeningCheck";
    private static string lastCaption;
    private static int captureIndex;
    private static bool recordedStart;
    private static double nextSnapshot;

    static VanOpeningSetup()
    {
        EditorApplication.update += ObserveOpening;
        InputSystem.onAfterUpdate += () =>
        {
            if (SessionState.GetBool(ArmedKey, false) && EditorApplication.isPlaying &&
                Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                Append("E input received; Focus: " + Application.isFocused + "; Cursor: " + Cursor.lockState);
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ArmedKey, false))
            {
                Append("Returned to Edit Mode.");
                SessionState.SetBool(ArmedKey, false);
            }
        };
    }

    [MenuItem("UNCLAIMED/Opening/Finish Subtitle Setup")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before applying subtitle setup.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open Opening_Apartment before applying subtitle setup.");

        var objects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        Transform Find(string name) => objects.Single(t => t.name == name);
        var canvas = Find("GameUI").GetComponent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        var subtitle = Find("SubtitleText").GetComponent<TextMeshProUGUI>();
        var van = Find("DeliveryVan").GetComponent<VanOpeningController>();
        var player = Find("Player").GetComponent<FirstPersonController>();
        var camera = Find("Main Camera");
        var driver = Find("DriverView");
        var exit = Find("ExitPoint");
        if (!canvas || !scaler || !subtitle || !van || !player)
            throw new InvalidOperationException("An existing opening component is missing.");

        AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceSynchronousImport);
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (!sourceFont) throw new InvalidOperationException("Korean source font is missing.");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (!font)
        {
            font = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 9,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (!font) throw new InvalidOperationException("Could not create Korean font asset.");
            font.name = "NotoSansKR-Regular SDF";
            AssetDatabase.CreateAsset(font, FontAssetPath);
            foreach (var texture in font.atlasTextures)
            {
                texture.name = "NotoSansKR-Regular Atlas";
                AssetDatabase.AddObjectToAsset(texture, font);
            }
            font.material.name = "NotoSansKR-Regular SDF Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }
        font.isMultiAtlasTexturesEnabled = true;

        Undo.RecordObjects(new UnityEngine.Object[] { canvas, scaler, subtitle,
            subtitle.rectTransform, van }, "Finish opening subtitles");
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var rect = subtitle.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 110f);
        rect.sizeDelta = new Vector2(1500f, 100f);
        rect.localScale = Vector3.one;
        subtitle.font = font;
        subtitle.fontSize = 32f;
        subtitle.enableAutoSizing = false;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.color = Color.white;
        subtitle.raycastTarget = false;
        subtitle.text = "";
        subtitle.alpha = 1f;
        // An outline keeps white subtitles readable against the bright blockout.
        font.material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
        font.material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.12f);
        subtitle.fontSharedMaterial = font.material;

        var settings = new SerializedObject(van);
        settings.FindProperty("playerController").objectReferenceValue = player;
        settings.FindProperty("viewCamera").objectReferenceValue = camera;
        settings.FindProperty("driverView").objectReferenceValue = driver;
        settings.FindProperty("exitPoint").objectReferenceValue = exit;
        settings.FindProperty("subtitleText").objectReferenceValue = subtitle;
        settings.ApplyModifiedProperties();
        string characters = "[E] 차에서 내리기";
        var lines = settings.FindProperty("openingLines");
        for (int i = 0; i < lines.arraySize; i++)
            characters += lines.GetArrayElementAtIndex(i).stringValue;
        if (!font.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("Font is missing characters: " + missing);

        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(font.material);
        foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Opening scene could not be saved.");

        Directory.CreateDirectory(ReportDirectory);
        File.WriteAllText(Path.Combine(ReportDirectory, "setup.txt"),
            "Scene: " + scene.path + "\nSaved: true\nReferences: 5/5\n" +
            "Subtitle: bottom center, 1500 x 100, Y 110, font size 32\n" +
            "Font: Noto Sans KR, Dynamic, Multi Atlas\nMissing glyphs: none\n" +
            "Opening lines: " + lines.arraySize + "\n");
        Debug.Log("UNCLAIMED: Opening subtitles configured and scene saved.");
        Selection.activeGameObject = van.gameObject;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    [MenuItem("UNCLAIMED/Opening/Observe Next Play")]
    public static void ArmObservation()
    {
        Directory.CreateDirectory(ReportDirectory);
        File.WriteAllText(Path.Combine(ReportDirectory, "play.txt"), "Opening observation armed.\n");
        lastCaption = null;
        captureIndex = 0;
        recordedStart = false;
        SessionState.SetBool(ArmedKey, true);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    [MenuItem("UNCLAIMED/Opening/Send E Through Input System")]
    public static void SendExitInput()
    {
        if (!EditorApplication.isPlaying || Keyboard.current == null)
            throw new InvalidOperationException("This input check requires Play Mode and a keyboard.");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var keyboard = Keyboard.current;
        Action press = null;
        press = () =>
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.onBeforeUpdate -= press;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            Action release = null;
            release = () =>
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
                InputSystem.onAfterUpdate -= release;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            };
            InputSystem.onAfterUpdate += release;
        };
        InputSystem.onBeforeUpdate += press;
        Append("Queued E input through Unity Input System.");
    }

    private static void ObserveOpening()
    {
        if (!SessionState.GetBool(ArmedKey, false) || !EditorApplication.isPlaying ||
            EditorApplication.isPaused || SceneManager.GetActiveScene().path != ScenePath) return;
        var van = UnityEngine.Object.FindFirstObjectByType<VanOpeningController>();
        var player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
        if (!van || !player) return;
        var settings = new SerializedObject(van);
        var subtitle = settings.FindProperty("subtitleText").objectReferenceValue as TMP_Text;
        var camera = settings.FindProperty("viewCamera").objectReferenceValue as Transform;
        if (!subtitle || !camera) return;

        if (EditorApplication.timeSinceStartup >= nextSnapshot)
        {
            nextSnapshot = EditorApplication.timeSinceStartup + 0.5;
            File.WriteAllText(Path.Combine(ReportDirectory, "current.txt"),
                "Frame: " + Time.frameCount + "; Focus: " + Application.isFocused +
                "; Cursor: " + Cursor.lockState + "; InVan: " + van.IsInVan +
                "; IntroFinished: " + van.IntroFinished + "; Caption: " + subtitle.text);
        }

        if (!recordedStart && van.IsInVan)
        {
            recordedStart = true;
            Append("Seated: true; Walking enabled: " + player.enabled +
                "; CharacterController enabled: " + player.GetComponent<CharacterController>().enabled +
                "; Camera parent: " + camera.parent.name);
        }
        if (subtitle.alpha >= 0.95f && !string.IsNullOrEmpty(subtitle.text) && subtitle.text != lastCaption)
        {
            lastCaption = subtitle.text;
            subtitle.ForceMeshUpdate();
            Append("Caption: " + subtitle.text + "; IntroFinished: " + van.IntroFinished +
                "; Lines: " + subtitle.textInfo.lineCount + "; Overflow: " + subtitle.isTextOverflowing);
            Capture("caption");
        }
        if (recordedStart && !van.IsInVan && player.enabled)
        {
            Append("Exited: true; Walking enabled: " + player.enabled +
                "; Interaction enabled: " + player.GetComponent<PlayerInteractor>().enabled +
                "; CharacterController enabled: " + player.GetComponent<CharacterController>().enabled +
                "; Camera parent: " + camera.parent.name + "; Player position: " + player.transform.position);
            Capture("outside");
            SessionState.SetBool(ArmedKey, false);
        }
    }

    private static void Capture(string label)
    {
        string path = Path.GetFullPath(Path.Combine(ReportDirectory,
            (++captureIndex).ToString("00") + "-" + label + ".png"));
        ScreenCapture.CaptureScreenshot(path);
    }

    private static void Append(string text)
    {
        Directory.CreateDirectory(ReportDirectory);
        File.AppendAllText(Path.Combine(ReportDirectory, "play.txt"), text + "\n");
    }
}
