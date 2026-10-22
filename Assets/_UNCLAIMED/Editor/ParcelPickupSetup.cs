using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ParcelPickupSetup
{
    private const string ScenePath = "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity";
    private const string RequestPath = "UserSettings/ParcelPickupCheck/setup.request";
    private const string ReportPath = "UserSettings/ParcelPickupCheck/setup.txt";
    private const string MaterialsPath = "Assets/_UNCLAIMED/Materials/Prototype";
    private static double nextCheck;

    static ParcelPickupSetup()
    {
        EditorApplication.update += ApplyPendingRequest;
    }

    private static void ApplyPendingRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
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

    [MenuItem("UNCLAIMED/Parcel/Set Up Van Pickup")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open Opening_Apartment in Edit Mode before setup.");

        Transform Find(string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(item => item.name == name);
        Transform van = Find("DeliveryVan");
        Transform player = Find("Player");
        Transform camera = Find("Main Camera");
        Transform cargo = Find("CargoBox");
        Transform canvas = Find("GameUI");
        var interactor = player.GetComponent<PlayerInteractor>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular SDF.asset");
        if (!interactor || !font) throw new InvalidOperationException("Player interaction or Korean font is missing.");

        EnsureFolder(MaterialsPath);
        Material cardboard = GetMaterial("ParcelCardboard", new Color(0.46f, 0.29f, 0.14f));
        Material tape = GetMaterial("ParcelTape", new Color(0.68f, 0.48f, 0.25f));
        Material label = GetMaterial("ParcelLabel", new Color(0.86f, 0.83f, 0.75f));
        Material shell = GetMaterial("VanCargoBlockout", new Color(0.35f, 0.38f, 0.40f));

        // Replace only the solid placeholder's rendering and collision with an open rear shell.
        Undo.RecordObjects(new UnityEngine.Object[] { cargo.GetComponent<MeshRenderer>(),
            cargo.GetComponent<BoxCollider>() }, "Open van cargo blockout");
        cargo.GetComponent<MeshRenderer>().enabled = false;
        cargo.GetComponent<BoxCollider>().enabled = false;
        Transform interior = Child(van, "CargoInterior_Blockout");
        SetTransform(interior, Vector3.zero, Vector3.one);
        Cube(interior, "CargoFloor", new Vector3(0f, 0.34f, -1f), new Vector3(2.2f, 0.16f, 3.6f), shell, true);
        Cube(interior, "CargoRoof", new Vector3(0f, 2.74f, -1f), new Vector3(2.2f, 0.12f, 3.6f), shell, true);
        Cube(interior, "CargoLeft", new Vector3(-1.04f, 1.55f, -1f), new Vector3(0.12f, 2.5f, 3.6f), shell, true);
        Cube(interior, "CargoRight", new Vector3(1.04f, 1.55f, -1f), new Vector3(0.12f, 2.5f, 3.6f), shell, true);
        Cube(interior, "CargoFront", new Vector3(0f, 1.55f, 0.74f), new Vector3(2.2f, 2.5f, 0.12f), shell, true);

        Transform parcelInVan = Child(interior, "ParcelInVan");
        SetTransform(parcelInVan, new Vector3(0f, 0.65f, -2.46f), Vector3.one);
        BuildParcel(parcelInVan, cardboard, tape, label);
        parcelInVan.gameObject.SetActive(true);

        Transform holdPoint = Child(camera, "ParcelHoldPoint");
        SetTransform(holdPoint, new Vector3(0.27f, -0.36f, 0.85f), Vector3.one);
        Transform heldParcel = Child(holdPoint, "HeldParcel");
        SetTransform(heldParcel, Vector3.zero, Vector3.one);
        heldParcel.localRotation = Quaternion.Euler(-5f, -10f, 2f);
        BuildParcel(heldParcel, cardboard, tape, label);
        foreach (Transform part in heldParcel.GetComponentsInChildren<Transform>(true))
            part.gameObject.layer = 2; // Ignore Raycast; the model is visual only.
        heldParcel.gameObject.SetActive(false);

        var carrier = player.GetComponent<ParcelCarrier>();
        if (!carrier) carrier = Undo.AddComponent<ParcelCarrier>(player.gameObject);
        var carrierSettings = new SerializedObject(carrier);
        carrierSettings.FindProperty("holdPoint").objectReferenceValue = holdPoint;
        carrierSettings.FindProperty("heldParcelVisual").objectReferenceValue = heldParcel.gameObject;
        carrierSettings.ApplyModifiedProperties();

        Transform pickup = Child(van, "ParcelPickupArea");
        SetTransform(pickup, van.InverseTransformPoint(parcelInVan.position), Vector3.one);
        var area = pickup.GetComponent<BoxCollider>();
        if (!area) area = Undo.AddComponent<BoxCollider>(pickup.gameObject);
        Undo.RecordObject(area, "Set parcel pickup area");
        area.enabled = true;
        area.isTrigger = true;
        area.size = new Vector3(0.68f, 0.50f, 0.54f);
        var interaction = pickup.GetComponent<InteractableObject>();
        if (!interaction) interaction = Undo.AddComponent<InteractableObject>(pickup.gameObject);
        var interactionSettings = new SerializedObject(interaction);
        interactionSettings.FindProperty("prompt").stringValue = "택배 꺼내기";
        interactionSettings.ApplyModifiedProperties();
        interaction.enabled = true;
        var source = pickup.GetComponent<ParcelPickup>();
        if (!source) source = Undo.AddComponent<ParcelPickup>(pickup.gameObject);
        var pickupSettings = new SerializedObject(source);
        pickupSettings.FindProperty("carrier").objectReferenceValue = carrier;
        pickupSettings.FindProperty("parcelInVan").objectReferenceValue = parcelInVan.gameObject;
        pickupSettings.ApplyModifiedProperties();

        Transform promptTransform = canvas.Find("InteractionPrompt");
        if (!promptTransform)
        {
            var promptObject = new GameObject("InteractionPrompt", typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(promptObject, "Create interaction prompt");
            promptObject.transform.SetParent(canvas, false);
            promptTransform = promptObject.transform;
        }
        var prompt = promptTransform.GetComponent<TextMeshProUGUI>();
        Undo.RecordObjects(new UnityEngine.Object[] { prompt, prompt.rectTransform }, "Set interaction prompt");
        var rect = prompt.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 130f);
        rect.sizeDelta = new Vector2(1500f, 80f);
        rect.localScale = Vector3.one;
        prompt.font = font;
        prompt.fontSharedMaterial = font.material;
        prompt.fontSize = 30f;
        prompt.enableAutoSizing = false;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.raycastTarget = false;
        prompt.text = "";

        var interactorSettings = new SerializedObject(interactor);
        interactorSettings.FindProperty("interactionPrompt").objectReferenceValue = prompt;
        interactorSettings.ApplyModifiedProperties();
        if (!font.TryAddCharacters("[E] 택배 꺼내기", out string missing))
            throw new InvalidOperationException("Missing Korean glyphs: " + missing);
        EditorUtility.SetDirty(font);
        foreach (Texture2D atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save pickup scene.");

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath,
            "SETUP PASS\nScene saved: " + scene.path + "\n" +
            "Rear cargo open; original mass renderer/collider disabled.\n" +
            "Pickup trigger: centered on the parcel, size (0.68,0.50,0.54).\n" +
            "Held parcel: camera child; hidden initially; no colliders.\n" +
            "Carrier, pickup, and Korean TMP prompt references assigned.\n");
        Debug.Log("UNCLAIMED: Rear parcel pickup configured and scene saved.");
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing) return existing;
        var item = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(item, "Create parcel setup object");
        item.transform.SetParent(parent, false);
        return item.transform;
    }

    private static void SetTransform(Transform item, Vector3 position, Vector3 scale)
    {
        Undo.RecordObject(item, "Place parcel setup object");
        item.localPosition = position;
        item.localRotation = Quaternion.identity;
        item.localScale = scale;
    }

    private static void Cube(Transform parent, string name, Vector3 position, Vector3 scale,
        Material material, bool solid)
    {
        Transform part = parent.Find(name);
        if (!part)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Undo.RegisterCreatedObjectUndo(cube, "Create parcel blockout geometry");
            cube.transform.SetParent(parent, false);
            part = cube.transform;
        }
        SetTransform(part, position, scale);
        var renderer = part.GetComponent<MeshRenderer>();
        Undo.RecordObject(renderer, "Set parcel blockout material");
        renderer.sharedMaterial = material;
        var collider = part.GetComponent<BoxCollider>();
        if (!solid && collider) Undo.DestroyObjectImmediate(collider);
    }

    private static void BuildParcel(Transform root, Material cardboard, Material tape, Material label)
    {
        Cube(root, "Body", Vector3.zero, new Vector3(0.64f, 0.46f, 0.5f), cardboard, false);
        Cube(root, "TapeTop", new Vector3(0f, 0.232f, 0f), new Vector3(0.10f, 0.004f, 0.5f), tape, false);
        Cube(root, "TapeFront", new Vector3(0f, 0f, -0.252f), new Vector3(0.10f, 0.46f, 0.004f), tape, false);
        Cube(root, "TapeBack", new Vector3(0f, 0f, 0.252f), new Vector3(0.10f, 0.46f, 0.004f), tape, false);
        Cube(root, "ShippingLabel", new Vector3(0f, 0.236f, -0.02f), new Vector3(0.50f, 0.004f, 0.34f), label, false);
    }

    private static Material GetMaterial(string name, Color color)
    {
        string path = MaterialsPath + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is missing.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(material);
        return material;
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
