using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ApartmentLayoutSetup
{
    private const string ScenePath = "Assets/_UNCLAIMED/Scenes/Opening_Apartment.unity";
    private const string RequestPath = "UserSettings/ApartmentLayoutCheck/setup.request";
    private const string ReportPath = "UserSettings/ApartmentLayoutCheck/setup.txt";
    private const string MaterialsPath = "Assets/_UNCLAIMED/Materials/Prototype";
    private static double nextCheck;

    static ApartmentLayoutSetup()
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

    [MenuItem("UNCLAIMED/Apartment/Build Common Areas")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open Opening_Apartment in Edit Mode before layout setup.");

        Transform Find(string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(item => item.name == name);
        Transform exterior = Find("Apartment_Exterior");
        Transform oldMass = Find("Apartment_Mass");
        Transform entranceMarker = Find("Entrance_Marker");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_UNCLAIMED/Fonts/NotoSansKR-Regular SDF.asset");
        if (!font) throw new InvalidOperationException("Korean font is missing.");
        if (exterior.position != Vector3.zero || exterior.rotation != Quaternion.identity ||
            exterior.lossyScale != Vector3.one)
            throw new InvalidOperationException("Apartment_Exterior must keep its identity transform.");
        if (!font.TryAddCharacters("101동계단층호0123456789–←→↑", out string missing))
            throw new InvalidOperationException("Missing apartment sign glyphs: " + missing);

        EnsureFolder(MaterialsPath);
        Material wall = GetMaterial("ApartmentWall", new Color(0.52f, 0.53f, 0.51f));
        Material floor = GetMaterial("ApartmentFloor", new Color(0.39f, 0.40f, 0.39f));
        Material door = GetMaterial("ApartmentDoor", new Color(0.30f, 0.27f, 0.23f));
        Material rail = GetMaterial("ApartmentRail", new Color(0.16f, 0.18f, 0.18f));
        Material sign = GetMaterial("ApartmentSign", new Color(0.15f, 0.21f, 0.19f));
        var signTextMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ApartmentSignText.mat");
        if (!signTextMaterial)
        {
            signTextMaterial = new Material(font.material) { name = "ApartmentSignText" };
            AssetDatabase.CreateAsset(signTextMaterial, MaterialsPath + "/ApartmentSignText.mat");
        }
        Undo.RecordObject(signTextMaterial, "Configure apartment sign lettering");
        signTextMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        signTextMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
        EditorUtility.SetDirty(signTextMaterial);

        DisableBlockout(oldMass);
        DisableBlockout(entranceMarker);
        Transform layout = Child(exterior, "Apartment_Layout_Blockout");
        Pose(layout, Vector3.zero, Vector3.one);
        Transform structure = Child(layout, "Structure");
        Pose(structure, Vector3.zero, Vector3.one);

        Cube(structure, "LeftCorridorEndWall", new Vector3(-24f, 9f, 18.7f),
            new Vector3(0.18f, 18f, 3.6f), wall);
        Cube(structure, "RightCorridorEndWall", new Vector3(24f, 9f, 18.7f),
            new Vector3(0.18f, 18f, 3.6f), wall);
        Cube(structure, "StairwellLeftWall", new Vector3(-3.15f, 9f, 26.85f),
            new Vector3(0.18f, 18f, 4.7f), wall);
        Cube(structure, "StairwellRightWall", new Vector3(3.15f, 9f, 26.85f),
            new Vector3(0.18f, 18f, 4.7f), wall);
        Cube(structure, "StairwellBackWall", new Vector3(0f, 9f, 29.2f),
            new Vector3(6.4f, 18f, 0.18f), wall);
        Cube(structure, "RearServiceMass", new Vector3(0f, 9f, 30.15f),
            new Vector3(6.4f, 18f, 1.7f), wall);
        Cube(structure, "RoofSlab", new Vector3(0f, 17.91f, 24f),
            new Vector3(48f, 0.18f, 14.2f), floor);

        Transform entrance = Child(layout, "Entrance");
        Pose(entrance, Vector3.zero, Vector3.one);
        Cube(entrance, "FrontWallLeft", new Vector3(-12.75f, 1.41f, 17f),
            new Vector3(22.5f, 2.82f, 0.18f), wall);
        Cube(entrance, "FrontWallRight", new Vector3(12.75f, 1.41f, 17f),
            new Vector3(22.5f, 2.82f, 0.18f), wall);
        Cube(entrance, "EntryLintel", new Vector3(0f, 2.635f, 17f),
            new Vector3(3f, 0.37f, 0.18f), wall);
        Cube(entrance, "EntryApron", new Vector3(0f, -0.045f, 14.5f),
            new Vector3(7f, 0.12f, 5f), floor);
        Cube(entrance, "EntryCanopy", new Vector3(0f, 2.94f, 15.7f),
            new Vector3(7f, 0.12f, 2.6f), wall);
        Cube(entrance, "BuildingSignBoard", new Vector3(0f, 2.64f, 16.86f),
            new Vector3(2.5f, 0.32f, 0.04f), sign, false);
        Text(entrance, "BuildingSign", "101동", new Vector3(0f, 2.64f, 16.83f),
            new Vector2(2.3f, 0.30f), 2.2f, false, font, signTextMaterial);
        PointLight(entrance, "LobbyLight", new Vector3(0f, 2.5f, 19f), 6f, 2.2f);

        float[] doorXs = { -20f, -13f, -7f, 7f, 13f, 20f };
        for (int i = 0; i < 6; i++)
        {
            float y = i * 3f;
            int firstRoom = i * 100 + 101;
            Transform level = Child(layout, "Floor_" + (i + 1));
            Pose(level, Vector3.zero, Vector3.one);
            Cube(level, "FrontCorridorSlab", new Vector3(0f, y - 0.09f, 18.7f),
                new Vector3(48f, 0.18f, 3.6f), floor);
            // Full-width core landing floors the future elevator pockets as well.
            Cube(level, "FrontLandingSlab", new Vector3(0f, y - 0.09f, 22.5f),
                new Vector3(12f, 0.18f, 4f), floor);
            Cube(level, "LeftApartmentWing", new Vector3(-15f, y + 1.42f, 25.8f),
                new Vector3(18f, 2.8f, 10.4f), wall);
            Cube(level, "RightApartmentWing", new Vector3(15f, y + 1.42f, 25.8f),
                new Vector3(18f, 2.8f, 10.4f), wall);
            Cube(level, "LeftCoreService", new Vector3(-4.6f, y + 1.42f, 27.75f),
                new Vector3(2.8f, 2.8f, 6.5f), wall);
            Cube(level, "RightCoreService", new Vector3(4.6f, y + 1.42f, 27.75f),
                new Vector3(2.8f, 2.8f, 6.5f), wall);
            if (i > 0)
                Cube(level, "FrontParapet", new Vector3(0f, y + 0.55f, 17f),
                    new Vector3(48f, 1.1f, 0.18f), wall);
            Cube(level, "StairCenterGapGuard", new Vector3(0f, y + 0.55f, 24.47f),
                new Vector3(0.8f, 1.1f, 0.10f), rail);

            Cube(level, "DirectionBoard", new Vector3(0f, y + 2.4f, 22.9f),
                new Vector3(3.8f, 0.62f, 0.04f), sign, false);
            Cube(level, "DirectionBoardHangerLeft", new Vector3(-1.6f, y + 2.83f, 22.9f),
                new Vector3(0.04f, 0.24f, 0.04f), rail, false);
            Cube(level, "DirectionBoardHangerRight", new Vector3(1.6f, y + 2.83f, 22.9f),
                new Vector3(0.04f, 0.24f, 0.04f), rail, false);
            string direction = "<size=2.2>" + (i + 1) + "층</size>\n<size=1.05>← " +
                firstRoom + "–" + (firstRoom + 2) + "호     " + (firstRoom + 3) + "–" +
                (firstRoom + 5) + "호 →</size>";
            string arrivalDirection = "<size=2.2>" + (i + 1) + "층</size>\n<size=1.05>← " +
                (firstRoom + 3) + "–" + (firstRoom + 5) + "호     " + firstRoom + "–" +
                (firstRoom + 2) + "호 →</size>";
            Text(level, "HallDirectionSign", direction, new Vector3(0f, y + 2.4f, 22.87f),
                new Vector2(3.6f, 0.56f), 2.2f, false, font, signTextMaterial);
            Text(level, "StairArrivalSign", arrivalDirection, new Vector3(0f, y + 2.4f, 22.93f),
                new Vector2(3.6f, 0.56f), 2.2f, true, font, signTextMaterial);
            PointLight(level, "LandingLight", new Vector3(0f, y + 2.5f, 21.2f), 5.5f, 1.8f);

            for (int j = 0; j < doorXs.Length; j++)
            {
                int room = firstRoom + j;
                Transform doorRoot = Child(level, "Door_" + room);
                Pose(doorRoot, Vector3.zero, Vector3.one);
                Cube(doorRoot, "Panel", new Vector3(doorXs[j], y + 1.075f, 20.48f),
                    new Vector3(0.95f, 2.15f, 0.10f), door);
                Cube(doorRoot, "NumberPlate", new Vector3(doorXs[j], y + 1.72f, 20.415f),
                    new Vector3(0.44f, 0.24f, 0.025f), sign, false);
                Text(doorRoot, "RoomNumber", room.ToString(), new Vector3(doorXs[j], y + 1.72f, 20.395f),
                    new Vector2(0.40f, 0.22f), 1.55f, false, font, signTextMaterial);
            }
        }

        Transform stairs = Child(layout, "Stairs");
        Pose(stairs, Vector3.zero, Vector3.one);
        float slope = Mathf.Atan2(1.5f, 3f) * Mathf.Rad2Deg;
        float railLength = Mathf.Sqrt(3f * 3f + 1.5f * 1.5f);
        for (int i = 0; i < 5; i++)
        {
            float y = i * 3f;
            Transform connection = Child(stairs, "Connection_" + (i + 1) + "_" + (i + 2));
            Pose(connection, Vector3.zero, Vector3.one);
            for (int j = 1; j <= 10; j++)
            {
                // Thin closed treads overlap each riser and retain 2.82m stacked headroom.
                Cube(connection, "FlightA_Step_" + j,
                    new Vector3(-1.6f, y + j * 0.15f - 0.09f, 24.5f + (j - 0.5f) * 0.3f),
                    new Vector3(2.4f, 0.18f, 0.3f), floor);
                Cube(connection, "FlightB_Step_" + j,
                    new Vector3(1.6f, y + 1.5f + j * 0.15f - 0.09f, 27.5f - (j - 0.5f) * 0.3f),
                    new Vector3(2.4f, 0.18f, 0.3f), floor);
            }
            Cube(connection, "MidLanding", new Vector3(0f, y + 1.41f, 28.3f),
                new Vector3(6.4f, 0.18f, 1.6f), floor);
            Transform railA = Cube(connection, "FlightA_InnerHandrail", new Vector3(-0.35f, y + 1.825f, 26f),
                new Vector3(0.08f, 0.08f, railLength), rail);
            railA.localRotation = Quaternion.Euler(-slope, 0f, 0f);
            Transform railB = Cube(connection, "FlightB_InnerHandrail", new Vector3(0.35f, y + 3.325f, 26f),
                new Vector3(0.08f, 0.08f, railLength), rail);
            railB.localRotation = Quaternion.Euler(slope, 0f, 0f);
            for (int post = 0; post < 3; post++)
            {
                float z = 25f + post;
                float aStepTop = Mathf.Ceil((z - 24.5f) / 0.3f) * 0.15f;
                float bStepTop = 1.5f + Mathf.Ceil((27.5f - z) / 0.3f) * 0.15f;
                Cube(connection, "FlightA_Post_" + post, new Vector3(-0.35f, y + aStepTop + 0.5f, z),
                    new Vector3(0.08f, 1f, 0.08f), rail);
                Cube(connection, "FlightB_Post_" + post, new Vector3(0.35f, y + bStepTop + 0.5f, z),
                    new Vector3(0.08f, 1f, 0.08f), rail);
            }
            Cube(connection, "MidLandingBackGuard", new Vector3(0f, y + 2.05f, 29.05f),
                new Vector3(6.2f, 1.1f, 0.08f), rail);
            Cube(connection, "MidLandingCenterGapGuard", new Vector3(0f, y + 2.05f, 27.53f),
                new Vector3(0.8f, 1.1f, 0.10f), rail);
            PointLight(connection, "MidLandingLight", new Vector3(0f, y + 3.95f, 28.3f), 5f, 1.6f);
        }
        Cube(stairs, "BaseFloorUnusedBGuard", new Vector3(1.6f, 0.55f, 24.47f),
            new Vector3(2.4f, 1.1f, 0.10f), rail);
        Cube(stairs, "TopFloorUnusedAGuard", new Vector3(-1.6f, 15.55f, 24.47f),
            new Vector3(2.4f, 1.1f, 0.10f), rail);

        Transform elevatorReserve = Child(layout, "ElevatorReserve");
        Pose(elevatorReserve, new Vector3(4.6f, 0f, 22.5f), Vector3.one);
        Transform route = Child(layout, "ApartmentRoute");
        Pose(route, Vector3.zero, Vector3.one);
        Waypoint(route, "EntryApproach", new Vector3(0f, 0.05f, 14.5f));
        Waypoint(route, "Lobby", new Vector3(0f, 0.05f, 19f));
        Waypoint(route, "StairStart", new Vector3(-1.6f, 0.05f, 23.8f));
        for (int i = 0; i < 5; i++)
        {
            float y = i * 3f;
            Transform connection = Child(route, "Connection_" + (i + 1) + "_" + (i + 2));
            Pose(connection, Vector3.zero, Vector3.one);
            Waypoint(connection, "AStart", new Vector3(-1.6f, y + 0.05f, 24f));
            Waypoint(connection, "ATop", new Vector3(-1.6f, y + 1.55f, 28.2f));
            Waypoint(connection, "MidCross", new Vector3(1.6f, y + 1.55f, 28.2f));
            Waypoint(connection, "BTop", new Vector3(1.6f, y + 3.05f, 23.8f));
            Waypoint(connection, "FrontCenter", new Vector3(0f, y + 3.05f, 23.3f));
        }
        Waypoint(route, "Floor4HallCenter", new Vector3(0f, 9.05f, 19f));
        Waypoint(route, "Goal402", new Vector3(-13f, 9.05f, 19f));

        EditorUtility.SetDirty(font);
        foreach (Texture2D atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save apartment layout.");
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath,
            "SETUP PASS\nScene saved: " + scene.path + "\n" +
            "Floors: 6; floor height: 3m; roof top: 18m.\n" +
            "Corridor: x[-24,24], z[16.9,20.5]; front core landing: x[-6,6], z[20.5,24.5].\n" +
            "Stairs: five U connections, 10+10 steps, rise0.15, tread0.3, thickness0.18.\n" +
            "Stacked tread and floor headroom: 2.82m; central gap and unused flights guarded.\n" +
            "Entry opening: width3m, height2.45m; continuous walking route without teleport.\n" +
            "Six room doors per floor; fourth-floor 402 at x=-13.\n" +
            "Route: Apartment_Layout_Blockout/ApartmentRoute/EntryApproach -> Lobby -> StairStart.\n" +
            "Each Connection_1_2 through Connection_5_6: AStart -> ATop -> MidCross -> BTop -> FrontCenter.\n" +
            "Floor4HallCenter: (0,9.05,19) -> Goal402: (-13,9.05,19); elevator reserve: (4.6,0,22.5).\n" +
            "Original mass and entry marker rendering/collision disabled; van/parcel/UI preserved.\n");
        Debug.Log("UNCLAIMED: Apartment entrance, corridors and six-floor staircase saved.");
    }

    private static void DisableBlockout(Transform item)
    {
        var renderer = item.GetComponent<MeshRenderer>();
        var collider = item.GetComponent<BoxCollider>();
        if (renderer)
        {
            Undo.RecordObject(renderer, "Replace apartment blockout rendering");
            renderer.enabled = false;
        }
        if (collider)
        {
            Undo.RecordObject(collider, "Replace apartment blockout collision");
            collider.enabled = false;
        }
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing) return existing;
        var item = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(item, "Create apartment common area");
        item.transform.SetParent(parent, false);
        return item.transform;
    }

    private static void Pose(Transform item, Vector3 position, Vector3 scale)
    {
        Undo.RecordObject(item, "Place apartment common area");
        item.localPosition = position;
        item.localRotation = Quaternion.identity;
        item.localScale = scale;
    }

    private static Transform Cube(Transform parent, string name, Vector3 position,
        Vector3 scale, Material material, bool solid = true)
    {
        Transform part = parent.Find(name);
        if (!part)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            Undo.RegisterCreatedObjectUndo(item, "Create apartment blockout geometry");
            item.transform.SetParent(parent, false);
            part = item.transform;
        }
        Pose(part, position, scale);
        var renderer = part.GetComponent<MeshRenderer>();
        var collider = part.GetComponent<BoxCollider>();
        if (!renderer || !collider) throw new InvalidOperationException("Generated cube components missing: " + name);
        Undo.RecordObjects(new UnityEngine.Object[] { renderer, collider }, "Configure apartment geometry");
        renderer.sharedMaterial = material;
        collider.enabled = solid;
        collider.isTrigger = false;
        return part;
    }

    private static void Text(Transform parent, string name, string content, Vector3 position,
        Vector2 size, float fontSize, bool facePositiveZ, TMP_FontAsset font, Material material)
    {
        Transform existing = parent.Find(name);
        if (!existing)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshPro));
            Undo.RegisterCreatedObjectUndo(item, "Create apartment navigation sign");
            item.transform.SetParent(parent, false);
            existing = item.transform;
        }
        var text = existing.GetComponent<TextMeshPro>();
        if (!text) throw new InvalidOperationException("Navigation sign needs TextMeshPro: " + name);
        Undo.RecordObjects(new UnityEngine.Object[] { text, text.rectTransform }, "Configure apartment navigation sign");
        var rect = text.rectTransform;
        rect.localPosition = position;
        rect.localRotation = Quaternion.Euler(0f, facePositiveZ ? 180f : 0f, 0f);
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        text.font = font;
        text.fontSharedMaterial = material;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.richText = true;
        text.raycastTarget = false;
        text.text = content;
        text.ForceMeshUpdate(true);
        EditorUtility.SetDirty(text);
    }

    private static void PointLight(Transform parent, string name, Vector3 position, float range, float intensity)
    {
        Transform item = Child(parent, name);
        Pose(item, position, Vector3.one);
        var light = item.GetComponent<Light>();
        if (!light) light = Undo.AddComponent<Light>(item.gameObject);
        Undo.RecordObject(light, "Configure common area light");
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, 0.91f, 0.80f);
        light.shadows = LightShadows.None;
        light.bounceIntensity = 1f;
    }

    private static void Waypoint(Transform parent, string name, Vector3 position)
    {
        Pose(Child(parent, name), position, Vector3.one);
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
        Undo.RecordObject(material, "Configure apartment blockout material");
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
