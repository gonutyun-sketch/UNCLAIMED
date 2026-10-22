using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ParcelInspectionSetup
{
    private const string ScenePath = "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity";
    private const string RequestPath = "UserSettings/ParcelInspectionCheck/setup.request";
    private const string ReportPath = "UserSettings/ParcelInspectionCheck/setup.txt";
    private const string DataPath = "Assets/_UNCLAIMED/Data/Delivery_Prototype.asset";
    private const string MaterialPath = "Assets/_UNCLAIMED/Materials/Prototype/ParcelLabelText.mat";
    private static double nextCheck;

    static ParcelInspectionSetup()
    {
        EditorApplication.update += ApplyPendingRequest;
    }

    private static void ApplyPendingRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        try
        {
            Apply();
            File.Delete(RequestPath);
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "SETUP FAILED\n" + error);
            File.Move(RequestPath, RequestPath + ".failed-" + DateTime.Now.Ticks);
            Debug.LogException(error);
        }
    }

    [MenuItem("UNCLAIMED/Parcel/Set Up Label Inspection")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open Opening_Apartment in Edit Mode before inspection setup.");

        Transform Find(string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(item => item.name == name);
        Transform player = Find("Player");
        Transform canvas = Find("GameUI");
        Transform worldParcel = Find("ParcelInVan");
        Transform heldParcel = Find("HeldParcel");
        var carrier = player.GetComponent<ParcelCarrier>();
        var pickup = Find("ParcelPickupArea").GetComponent<ParcelPickup>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular SDF.asset");
        if (!carrier || !pickup || !font)
            throw new InvalidOperationException("Parcel carrier, pickup, or Korean font is missing.");

        EnsureFolder("Assets/_UNCLAIMED/Data");
        EnsureFolder("Assets/_UNCLAIMED/Materials/Prototype");
        var carrierSettings = new SerializedObject(carrier);
        var pickupSettings = new SerializedObject(pickup);
        var data = carrierSettings.FindProperty("defaultDelivery").objectReferenceValue as ParcelDeliveryInfo;
        if (!data) data = pickupSettings.FindProperty("deliveryInfo").objectReferenceValue as ParcelDeliveryInfo;
        if (!data) data = AssetDatabase.LoadAssetAtPath<ParcelDeliveryInfo>(DataPath);
        if (!data)
        {
            data = ScriptableObject.CreateInstance<ParcelDeliveryInfo>();
            data.name = "Delivery_Prototype";
            AssetDatabase.CreateAsset(data, DataPath);
        }

        var labelMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!labelMaterial)
        {
            labelMaterial = new Material(font.material) { name = "ParcelLabelText" };
            AssetDatabase.CreateAsset(labelMaterial, MaterialPath);
        }
        Undo.RecordObject(labelMaterial, "Configure parcel label text material");
        labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
        labelMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        EditorUtility.SetDirty(labelMaterial);

        string characters = "배송지" + data.DisplayAddress + data.DeliveryInstructions +
            "[F] 송장 확인[F] 상자 내리기";
        if (!font.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("Missing Korean label glyphs: " + missing);

        ConfigureLabel(worldParcel, data, font, labelMaterial);
        ConfigureLabel(heldParcel, data, font, labelMaterial);
        foreach (Transform part in heldParcel.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(part.gameObject, "Set held parcel layer");
            part.gameObject.layer = 2;
        }

        Transform hintTransform = canvas.Find("CarryHint");
        if (!hintTransform)
        {
            var item = new GameObject("CarryHint", typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(item, "Create parcel inspection hint");
            item.transform.SetParent(canvas, false);
            hintTransform = item.transform;
            item.layer = 5;
        }
        var hint = hintTransform.GetComponent<TextMeshProUGUI>();
        if (!hint) throw new InvalidOperationException("CarryHint must contain TextMeshProUGUI.");
        Undo.RecordObjects(new UnityEngine.Object[] { hint, hint.rectTransform }, "Configure parcel inspection hint");
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.anchoredPosition = new Vector2(0f, 60f);
        hintRect.sizeDelta = new Vector2(1500f, 70f);
        hintRect.localRotation = Quaternion.identity;
        hintRect.localScale = Vector3.one;
        hint.font = font;
        hint.fontSharedMaterial = font.material;
        hint.fontSize = 26f;
        hint.enableAutoSizing = false;
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = Color.white;
        hint.alpha = 1f;
        hint.raycastTarget = false;
        hint.text = "";

        carrierSettings.FindProperty("defaultDelivery").objectReferenceValue = data;
        carrierSettings.FindProperty("carryHint").objectReferenceValue = hint;
        carrierSettings.FindProperty("inspectionLocalPosition").vector3Value = new Vector3(0f, -0.02f, 0.65f);
        carrierSettings.FindProperty("inspectionEulerAngles").vector3Value = new Vector3(-90f, 0f, 0f);
        carrierSettings.FindProperty("inspectionDuration").floatValue = 0.3f;
        carrierSettings.ApplyModifiedProperties();
        pickupSettings.FindProperty("deliveryInfo").objectReferenceValue = data;
        pickupSettings.ApplyModifiedProperties();

        EditorUtility.SetDirty(font);
        foreach (Texture2D atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save label inspection scene.");

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath,
            "SETUP PASS\nScene saved: " + scene.path + "\n" +
            "Delivery data: " + AssetDatabase.GetAssetPath(data) + "\n" +
            "Destination: " + data.DisplayAddress + "\n" +
            "World and held labels share delivery data; Korean glyphs prewarmed.\n" +
            "Paper: 0.50 x 0.34; text rect: 0.46 x 0.30.\n" +
            "Inspection position: (0,-0.02,0.65); rotation: (-90,0,0); duration: 0.3.\n" +
            "Carry hint assigned; existing pickup range preserved.\n");
        Debug.Log("UNCLAIMED: Parcel label inspection configured and scene saved.");
    }

    private static void ConfigureLabel(Transform parcel, ParcelDeliveryInfo data,
        TMP_FontAsset font, Material material)
    {
        Transform paper = parcel.Find("ShippingLabel");
        if (!paper) throw new InvalidOperationException("ShippingLabel paper is missing on " + parcel.name);
        Undo.RecordObject(paper, "Resize parcel shipping label");
        paper.localPosition = new Vector3(0f, 0.236f, -0.02f);
        paper.localRotation = Quaternion.identity;
        paper.localScale = new Vector3(0.50f, 0.004f, 0.34f);

        Transform textTransform = parcel.Find("AddressText");
        if (!textTransform)
        {
            var item = new GameObject("AddressText", typeof(RectTransform), typeof(TextMeshPro));
            Undo.RegisterCreatedObjectUndo(item, "Create parcel address text");
            item.transform.SetParent(parcel, false);
            textTransform = item.transform;
        }
        var text = textTransform.GetComponent<TextMeshPro>();
        if (!text) throw new InvalidOperationException("AddressText must contain 3D TextMeshPro.");
        Undo.RecordObjects(new UnityEngine.Object[] { text, text.rectTransform }, "Configure parcel address text");
        RectTransform rect = text.rectTransform;
        rect.localPosition = new Vector3(0f, 0.240f, -0.02f);
        rect.localRotation = Quaternion.Euler(90f, 0f, 0f);
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0.46f, 0.30f);
        text.font = font;
        text.fontSharedMaterial = material;
        text.fontSize = 0.62f;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.black;
        text.alpha = 1f;
        text.richText = true;
        text.raycastTarget = false;

        foreach (Collider collider in textTransform.GetComponentsInChildren<Collider>(true))
            Undo.DestroyObjectImmediate(collider);
        var display = parcel.GetComponent<ParcelLabelDisplay>();
        if (!display) display = Undo.AddComponent<ParcelLabelDisplay>(parcel.gameObject);
        var displaySettings = new SerializedObject(display);
        displaySettings.FindProperty("deliveryInfo").objectReferenceValue = data;
        displaySettings.FindProperty("labelText").objectReferenceValue = text;
        displaySettings.ApplyModifiedProperties();
        display.Refresh();
        text.ForceMeshUpdate(true);
        EditorUtility.SetDirty(display);
        EditorUtility.SetDirty(text);
    }

    private static void EnsureFolder(string folder)
    {
        string current = "Assets";
        foreach (string segment in folder.Split('/').Skip(1))
        {
            string child = current + "/" + segment;
            if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(current, segment);
            current = child;
        }
    }
}
