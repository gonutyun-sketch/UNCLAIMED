using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Wires one delivery fixture without building or changing apartment geometry.
[InitializeOnLoad]
public static class ParcelPlacementSetup
{
    private const string ScenePath = "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity";
    private const string CheckDirectory = "UserSettings/ParcelPlacementCheck";
    private const string RequestPath = CheckDirectory + "/setup.request";
    private static double nextCheck;

    static ParcelPlacementSetup()
    {
        EditorApplication.update += ApplyPendingRequest;
    }

    private static void ApplyPendingRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 0.5d;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(CheckDirectory);
        try
        {
            Apply();
            File.Delete(RequestPath);
        }
        catch (Exception error)
        {
            File.WriteAllText(CheckDirectory + "/setup.txt", "SETUP FAILED\n" + error);
            File.Move(RequestPath, RequestPath + ".failed-" + DateTime.Now.Ticks);
            Debug.LogException(error);
        }
    }

    [MenuItem("UNCLAIMED/Parcel/Set Up Doorstep Placement")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open Opening_Apartment in Edit Mode before placement setup.");
        Transform[] all = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        Transform player = all.Single(item => item.name == "Player");
        Transform source = all.Single(item => item.name == "ParcelInVan");
        var carrier = player.GetComponent<ParcelCarrier>();
        var data = AssetDatabase.LoadAssetAtPath<ParcelDeliveryInfo>(
            "Assets/_UNCLAIMED/Data/Delivery_Prototype.asset");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular SDF.asset");
        if (!carrier || !data || !font || !source.GetComponent<ParcelLabelDisplay>())
            throw new InvalidOperationException("Existing carrier, parcel, label or delivery data is missing.");
        if (!font.TryAddCharacters("[E] 문 앞에 놓기[F] 송장 닫기", out string missing))
            throw new InvalidOperationException("Missing placement prompt glyphs: " + missing);

        Transform root = scene.GetRootGameObjects().Where(item => item.name == "ParcelDeliveryPrototype")
            .Select(item => item.transform).SingleOrDefault();
        if (!root)
        {
            var item = new GameObject("ParcelDeliveryPrototype");
            Undo.RegisterCreatedObjectUndo(item, "Create delivery feature fixture");
            SceneManager.MoveGameObjectToScene(item, scene);
            root = item.transform;
        }
        Pose(root, Vector3.zero);
        Transform pointTransform = Child(root, "DeliveryPoint402");
        Pose(pointTransform, new Vector3(-13f, 9.95f, 20.1f));
        Transform anchor = Child(root, "PlacementAnchor");
        Pose(anchor, new Vector3(-13f, 9.235f, 19.8f));

        Transform placed = anchor.Find("PlacedParcel");
        if (!placed)
        {
            var visual = UnityEngine.Object.Instantiate(source.gameObject, anchor, false);
            visual.name = "PlacedParcel";
            Undo.RegisterCreatedObjectUndo(visual, "Reuse existing parcel visual for placement");
            placed = visual.transform;
        }
        Pose(placed, Vector3.zero);
        foreach (Transform part in placed.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(part.gameObject, "Set placed parcel layer");
            part.gameObject.layer = 0;
        }
        var body = placed.GetComponent<BoxCollider>();
        if (!body) body = Undo.AddComponent<BoxCollider>(placed.gameObject);
        Undo.RecordObject(body, "Configure placed parcel collision");
        body.center = Vector3.zero;
        body.size = new Vector3(0.64f, 0.46f, 0.5f);
        body.isTrigger = false;
        body.enabled = true;
        foreach (Collider extra in placed.GetComponentsInChildren<Collider>(true))
        {
            if (extra == body) continue;
            Undo.RecordObject(extra, "Disable decorative parcel collision");
            extra.enabled = false;
        }
        foreach (ParcelLabelDisplay label in placed.GetComponentsInChildren<ParcelLabelDisplay>(true))
        {
            Undo.RecordObject(label, "Set placed parcel delivery label");
            label.SetData(data);
            EditorUtility.SetDirty(label);
        }
        Undo.RecordObject(placed.gameObject, "Hide parcel until placement");
        placed.gameObject.SetActive(false);

        var area = pointTransform.GetComponent<BoxCollider>();
        if (!area) area = Undo.AddComponent<BoxCollider>(pointTransform.gameObject);
        Undo.RecordObject(area, "Configure doorstep interaction area");
        area.center = Vector3.zero;
        area.size = new Vector3(0.95f, 1.8f, 0.45f);
        area.isTrigger = true;
        area.enabled = true;
        var interaction = pointTransform.GetComponent<InteractableObject>();
        if (!interaction) interaction = Undo.AddComponent<InteractableObject>(pointTransform.gameObject);
        var interactionSettings = new SerializedObject(interaction);
        interactionSettings.FindProperty("prompt").stringValue = "문 앞에 놓기";
        interactionSettings.ApplyModifiedProperties();
        interaction.enabled = true;
        var point = pointTransform.GetComponent<ParcelDeliveryPoint>();
        if (!point) point = Undo.AddComponent<ParcelDeliveryPoint>(pointTransform.gameObject);
        var settings = new SerializedObject(point);
        settings.FindProperty("carrier").objectReferenceValue = carrier;
        settings.FindProperty("deliveryInfo").objectReferenceValue = data;
        settings.FindProperty("placementAnchor").objectReferenceValue = anchor;
        settings.FindProperty("placedParcelVisual").objectReferenceValue = placed.gameObject;
        settings.FindProperty("maximumPlacementDistance").floatValue = 3.25f;
        settings.FindProperty("maximumFloorDifference").floatValue = 1.25f;
        settings.ApplyModifiedProperties();
        point.enabled = true;

        EditorUtility.SetDirty(font);
        foreach (Texture2D atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save doorstep placement scene.");
        Directory.CreateDirectory(CheckDirectory);
        File.WriteAllText(CheckDirectory + "/setup.txt",
            "SETUP PASS\nScene saved: " + scene.path + "\n" +
            "Independent ParcelDeliveryPrototype root; existing apartment geometry unchanged.\n" +
            "Destination data: " + data.DisplayAddress + "\n" +
            "Trigger: (-13,9.95,20.1), size (0.95,1.8,0.45).\n" +
            "Placed parcel anchor: (-13,9.235,19.8); existing parcel visual reused.\n" +
            "Placed parcel initially hidden; solid 0.64x0.46x0.50 collision when active.\n" +
            "Placement is separate from photography and delivery confirmation.\n");
        Debug.Log("UNCLAIMED: Doorstep parcel placement connected without apartment design changes.");
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform item = parent.Find(name);
        if (item) return item;
        var created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create delivery feature fixture");
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static void Pose(Transform item, Vector3 position)
    {
        Undo.RecordObject(item, "Position delivery fixture");
        item.localPosition = position;
        item.localRotation = Quaternion.identity;
        item.localScale = Vector3.one;
    }
}
