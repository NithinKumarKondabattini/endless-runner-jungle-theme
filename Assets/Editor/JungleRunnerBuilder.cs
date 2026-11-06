#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One-click builder: Jungle Runner > Build Everything
// Creates meshes, materials, prefabs, both scenes, all UI, and wires every Inspector reference.
public static partial class JungleRunnerBuilder
{
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Mesh coneMesh, pyramidMesh, gemMesh;
    static Sprite roundedSprite, knobSprite;
    static PhysicsMaterial zeroFriction;

    // true only when a Universal RP asset is really active; otherwise we use Unity's Standard shaders
    static bool UseURP { get { return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null; } }

    public static bool TmpReady()
    {
        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF") != null;
    }

    [MenuItem("Jungle Runner/Build Everything")]
    public static void BuildEverything()
    {
        BuildAll(true);
    }

    public static void BuildAll(bool interactive)
    {
        if (!TmpReady())
        {
            if (interactive)
                EditorUtility.DisplayDialog("Import TextMeshPro first",
                    "Please run: Window > TextMeshPro > Import TMP Essential Resources, click Import, then run this menu again.", "OK");
            else
                Debug.LogWarning("Jungle Runner: TextMeshPro essentials missing. Import them, then use Jungle Runner > Build Everything.");
            return;
        }

        mats.Clear();
        EnsureFolders();
        CreateMeshes();
        CreateMaterials();
        CreatePrefabs();
        BuildMenuScene();
        BuildGameScene();
        FinishSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(MenuScenePath);
        Debug.Log("Jungle Runner: build complete. Press Play!");
        if (interactive)
            EditorUtility.DisplayDialog("Jungle Runner", "Build complete!\n\nThe MainMenu scene is open. Press Play.", "OK");
    }

    // ---------------------------------------------------------------- folders
    static void EnsureFolders()
    {
        string[] f = { "Animations", "Audio", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Textures", "UI" };
        foreach (string s in f)
            if (!AssetDatabase.IsValidFolder("Assets/" + s)) AssetDatabase.CreateFolder("Assets", s);
    }

    // ---------------------------------------------------------------- meshes
    static void AddTri(List<Vector3> v, List<int> idx, Vector3 a, Vector3 b, Vector3 c, Vector3 center)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f) { Vector3 t = b; b = c; c = t; }
        idx.Add(v.Count); v.Add(a);
        idx.Add(v.Count); v.Add(b);
        idx.Add(v.Count); v.Add(c);
    }

    static Mesh SaveMesh(string name, List<Vector3> v, List<int> idx)
    {
        Mesh m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetTriangles(idx, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        string path = "Assets/Models/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Cone: base radius 0.5 at y=0, apex at y=1 (flat shaded). 4 sides = pyramid.
    static Mesh MakeCone(string name, int sides, bool rotate45)
    {
        List<Vector3> v = new List<Vector3>(); List<int> idx = new List<int>();
        Vector3 center = new Vector3(0f, 0.3f, 0f), apex = new Vector3(0f, 1f, 0f);
        float off = rotate45 ? Mathf.PI / sides : 0f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = off + i * Mathf.PI * 2f / sides, a1 = off + (i + 1) * Mathf.PI * 2f / sides;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            AddTri(v, idx, p0, p1, apex, center);
            AddTri(v, idx, p0, p1, Vector3.zero, center);
        }
        return SaveMesh(name, v, idx);
    }

    static Mesh MakeGem()
    {
        List<Vector3> v = new List<Vector3>(); List<int> idx = new List<int>();
        Vector3 center = new Vector3(0f, 0.05f, 0f), bottom = new Vector3(0f, -0.5f, 0f), tc = new Vector3(0f, 0.38f, 0f);
        const int n = 8;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 g0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0.15f, Mathf.Sin(a0) * 0.5f);
            Vector3 g1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0.15f, Mathf.Sin(a1) * 0.5f);
            Vector3 t0 = new Vector3(Mathf.Cos(a0) * 0.28f, 0.38f, Mathf.Sin(a0) * 0.28f);
            Vector3 t1 = new Vector3(Mathf.Cos(a1) * 0.28f, 0.38f, Mathf.Sin(a1) * 0.28f);
            AddTri(v, idx, g0, g1, t0, center);
            AddTri(v, idx, g1, t1, t0, center);
            AddTri(v, idx, t0, t1, tc, center);
            AddTri(v, idx, g0, g1, bottom, center);
        }
        return SaveMesh("GemMesh", v, idx);
    }

    static void CreateMeshes()
    {
        coneMesh = MakeCone("ConeMesh", 16, false);
        pyramidMesh = MakeCone("PyramidMesh", 4, true);
        gemMesh = MakeGem();
    }

    // ---------------------------------------------------------------- materials
    static Material M(string key, Color c, float smooth = 0.25f, float metallic = 0f, Color? emission = null, string shader = "Universal Render Pipeline/Lit")
    {
        string path = "Assets/Materials/" + key + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        string resolved = shader;
        if (!UseURP) resolved = shader.Contains("Particles") ? "Particles/Standard Unlit" : "Standard";
        Shader sh = Shader.Find(resolved);
        if (sh == null) sh = Shader.Find("Standard");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        mats[key] = m;
        return m;
    }

    static void CreateMaterials()
    {
        M("Stone", new Color(1f, 0.88f, 0.74f), 0.3f);
        M("StonePeach", new Color(1f, 0.7f, 0.62f), 0.3f);
        M("StoneDark", new Color(0.78f, 0.6f, 0.6f));
        M("Grass", new Color(0.22f, 0.92f, 0.32f), 0.4f);
        M("GrassFar", new Color(0.3f, 0.9f, 0.45f), 0.3f);
        M("Rock", new Color(0.55f, 0.55f, 0.75f), 0.4f);
        M("Wood", new Color(0.75f, 0.42f, 0.18f));
        M("WoodDark", new Color(0.5f, 0.27f, 0.12f));
        M("Trunk", new Color(0.62f, 0.36f, 0.2f));
        M("LeafGreen", new Color(0.2f, 0.85f, 0.35f), 0.4f);
        M("LeafPink", new Color(1f, 0.45f, 0.75f), 0.4f);
        M("LeafPurple", new Color(0.75f, 0.4f, 1f), 0.4f);
        M("LeafYellow", new Color(1f, 0.9f, 0.25f), 0.4f);
        M("LeafTeal", new Color(0.1f, 0.9f, 0.85f), 0.4f);
        M("LeafOrange", new Color(1f, 0.6f, 0.15f), 0.4f);
        M("FlowerPink", new Color(1f, 0.3f, 0.65f), 0.5f, 0f, new Color(0.25f, 0.03f, 0.1f));
        M("FlowerPurple", new Color(0.7f, 0.25f, 1f), 0.5f, 0f, new Color(0.15f, 0.03f, 0.25f));
        M("FlowerYellow", new Color(1f, 0.92f, 0.2f), 0.5f, 0f, new Color(0.25f, 0.2f, 0f));
        M("FlowerBlue", new Color(0.25f, 0.55f, 1f), 0.5f, 0f, new Color(0.03f, 0.1f, 0.3f));
        M("FlowerOrange", new Color(1f, 0.5f, 0.1f), 0.5f, 0f, new Color(0.3f, 0.1f, 0f));
        M("FlowerWhite", new Color(1f, 1f, 1f), 0.5f, 0f, new Color(0.2f, 0.2f, 0.2f));
        M("Stem", new Color(0.15f, 0.75f, 0.25f));
        M("Gold", new Color(1f, 0.82f, 0.1f), 0.85f, 0.8f, new Color(0.6f, 0.4f, 0f));
        M("Gem", new Color(1f, 0.15f, 0.55f), 0.95f, 0.2f, new Color(0.9f, 0.05f, 0.4f));
        M("Lamp", new Color(1f, 0.9f, 0.4f), 0.5f, 0f, new Color(2.2f, 1.4f, 0.4f));
        M("Banner", new Color(0.65f, 0.15f, 0.85f));
        M("BannerPink", new Color(1f, 0.25f, 0.55f));
        M("BannerTeal", new Color(0.1f, 0.8f, 0.85f));
        M("Pink", new Color(1f, 0.3f, 0.6f), 0.4f);
        M("DeepPink", new Color(0.9f, 0.15f, 0.5f), 0.4f);
        M("Skin", new Color(1f, 0.8f, 0.68f), 0.3f);
        M("Hair", new Color(0.28f, 0.15f, 0.08f), 0.4f);
        M("Cream", new Color(1f, 0.96f, 0.92f));
        M("RoofPink", new Color(1f, 0.3f, 0.65f), 0.5f);
        M("Gate", new Color(0.5f, 0.2f, 0.75f));
        M("Water", new Color(0.4f, 0.9f, 1f), 0.9f, 0f, new Color(0.2f, 0.7f, 1f));
        M("Mountain", new Color(0.55f, 0.45f, 1f));
        M("MountainPink", new Color(1f, 0.55f, 0.85f));
        M("MountainTeal", new Color(0.3f, 0.85f, 0.95f));
        M("Earth", new Color(0.75f, 0.5f, 0.4f));
        M("Cloud", new Color(1f, 0.95f, 1f), 0.1f, 0f, new Color(0.5f, 0.45f, 0.5f));
        Color[] rain = { new Color(1f,0.15f,0.15f), new Color(1f,0.55f,0.1f), new Color(1f,0.95f,0.15f), new Color(0.2f,0.9f,0.25f),
                         new Color(0.2f,0.7f,1f), new Color(0.35f,0.3f,1f), new Color(0.75f,0.3f,0.95f) };
        for (int i = 0; i < 7; i++) M("Rainbow" + i, rain[i], 0.2f, 0f, rain[i] * 0.7f);
        M("Particle_Gold", new Color(1f, 0.85f, 0.3f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        M("Particle_Pink", new Color(1f, 0.4f, 0.8f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        M("Particle_White", new Color(1f, 1f, 1f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");


        // JUNGLE RUNNER material palette: tropical canopy, moss, aged stone, bark and treasure.
        M("Stone", new Color(0.42f, 0.40f, 0.31f), 0.18f);
        M("StonePeach", new Color(0.30f, 0.32f, 0.23f), 0.15f);
        M("StoneDark", new Color(0.19f, 0.22f, 0.15f), 0.12f);
        M("Grass", new Color(0.055f, 0.30f, 0.075f), 0.25f);
        M("GrassFar", new Color(0.035f, 0.20f, 0.065f), 0.25f);
        M("Rock", new Color(0.32f, 0.35f, 0.30f), 0.2f);
        M("Wood", new Color(0.28f, 0.13f, 0.055f), 0.2f);
        M("WoodDark", new Color(0.12f, 0.055f, 0.025f), 0.15f);
        M("Trunk", new Color(0.20f, 0.095f, 0.035f), 0.18f);
        M("LeafGreen", new Color(0.035f, 0.32f, 0.055f), 0.28f);
        M("LeafPink", new Color(0.06f, 0.40f, 0.08f), 0.28f);
        M("LeafPurple", new Color(0.08f, 0.26f, 0.055f), 0.28f);
        M("LeafYellow", new Color(0.18f, 0.43f, 0.07f), 0.28f);
        M("LeafTeal", new Color(0.025f, 0.38f, 0.24f), 0.35f);
        M("LeafOrange", new Color(0.12f, 0.34f, 0.055f), 0.28f);
        M("FlowerPink", new Color(0.82f, 0.10f, 0.16f), 0.35f);
        M("FlowerPurple", new Color(0.58f, 0.20f, 0.78f), 0.35f);
        M("FlowerYellow", new Color(1f, 0.72f, 0.06f), 0.35f);
        M("FlowerBlue", new Color(0.10f, 0.48f, 0.85f), 0.35f);
        M("FlowerOrange", new Color(1f, 0.34f, 0.04f), 0.35f);
        M("FlowerWhite", new Color(0.92f, 0.94f, 0.76f), 0.35f);
        M("Stem", new Color(0.035f, 0.26f, 0.055f));
        M("Gold", new Color(1f, 0.68f, 0.04f), 0.82f, 0.72f, new Color(0.35f, 0.20f, 0.01f));
        M("Gem", new Color(0.05f, 0.95f, 0.45f), 0.9f, 0.25f, new Color(0.02f, 0.3f, 0.08f));
        M("Lamp", new Color(1f, 0.82f, 0.28f), 0.55f, 0f, new Color(1.1f, 0.7f, 0.1f));
        M("Banner", new Color(0.20f, 0.32f, 0.10f));
        M("BannerPink", new Color(0.42f, 0.10f, 0.045f));
        M("BannerTeal", new Color(0.06f, 0.28f, 0.20f));
        M("Pink", new Color(0.38f, 0.25f, 0.11f));
        M("DeepPink", new Color(0.20f, 0.12f, 0.055f));
        M("RoofPink", new Color(0.24f, 0.31f, 0.14f));
        M("Gate", new Color(0.055f, 0.12f, 0.045f));
        M("Water", new Color(0.12f, 0.58f, 0.72f), 0.85f, 0f, new Color(0.03f, 0.18f, 0.25f));
        M("Mountain", new Color(0.12f, 0.23f, 0.10f));
        M("MountainPink", new Color(0.18f, 0.29f, 0.12f));
        M("MountainTeal", new Color(0.10f, 0.30f, 0.22f));
        M("Earth", new Color(0.23f, 0.13f, 0.055f));
        M("Cloud", new Color(0.72f, 0.85f, 0.70f));
        M("Shirt", new Color(0.42f, 0.30f, 0.12f));
        M("Pants", new Color(0.17f, 0.24f, 0.10f));
        M("Leather", new Color(0.22f, 0.095f, 0.035f));
        M("Bedroll", new Color(0.12f, 0.22f, 0.10f));
        M("Hat", new Color(0.28f, 0.14f, 0.045f));
        M("Boot", new Color(0.095f, 0.055f, 0.025f));
        M("Moss", new Color(0.10f, 0.28f, 0.055f));
        M("Ruins", new Color(0.31f, 0.32f, 0.23f));
        M("WaterLight", new Color(0.34f, 0.78f, 0.83f), 0.8f);

        // skybox
        string skyPath = "Assets/Materials/JungleSky.mat";
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetColor("_SkyTint", new Color(0.30f, 0.56f, 0.48f));
        sky.SetColor("_GroundColor", new Color(0.20f, 0.30f, 0.16f));
        sky.SetFloat("_Exposure", 1.25f);
        sky.SetFloat("_AtmosphereThickness", 1.1f);
        sky.SetFloat("_SunSize", 0.05f);
        EditorUtility.SetDirty(sky);
        mats["Sky"] = sky;

        // friction-free material so the explorer never sticks to walls or seams
        string pmPath = "Assets/Materials/ZeroFriction.asset";
        zeroFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
        if (zeroFriction == null)
        {
            zeroFriction = new PhysicsMaterial("ZeroFriction");
            AssetDatabase.CreateAsset(zeroFriction, pmPath);
        }
        zeroFriction.dynamicFriction = 0f;
        zeroFriction.staticFriction = 0f;
        zeroFriction.bounciness = 0f;
        zeroFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(zeroFriction);
    }

    // ---------------------------------------------------------------- helpers
    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, string mat, bool keepCollider = false)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        g.name = name;
        if (parent != null) g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mats[mat];
        if (!keepCollider) { Collider c = g.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
        return g;
    }

    static GameObject MeshObj(Mesh m, string name, Transform parent, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default(Vector3))
    {
        GameObject g = new GameObject(name);
        if (parent != null) g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localEulerAngles = euler;
        g.AddComponent<MeshFilter>().sharedMesh = m;
        g.AddComponent<MeshRenderer>().sharedMaterial = mats[mat];
        return g;
    }

    static GameObject SavePrefab(GameObject g, string name)
    {
        string path = "Assets/Prefabs/" + name + ".prefab";
        GameObject p = PrefabUtility.SaveAsPrefabAsset(g, path);
        Object.DestroyImmediate(g);
        return p;
    }

    static GameObject LoadPrefab(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    }

    static AudioClip Clip(string file)
    {
        return AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + file);
    }

    static void AddBox(GameObject g, Vector3 center, Vector3 size)
    {
        BoxCollider b = g.AddComponent<BoxCollider>();
        b.center = center; b.size = size; b.isTrigger = true;
    }

    // ---------------------------------------------------------------- prefabs
    static void CreatePrefabs()
    {
        MakeSparkle("Sparkle_Gold", "Particle_Gold");
        MakeSparkle("Sparkle_Pink", "Particle_Pink");
        MakeCoin();
        MakeGemPrefab();
        MakeObstacles();
        MakeDecor();
        MakeSegment();
    }

    static void MakeSparkle(string name, string matKey)
    {
        GameObject g = new GameObject(name);
        ParticleSystem ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.6f;
        main.startSpeed = 3f;
        main.startSize = 0.18f;
        main.gravityModifier = 0f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 24) });
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.2f;
        ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        g.GetComponent<ParticleSystemRenderer>().sharedMaterial = mats[matKey];
        SavePrefab(g, name);
    }

    static void MakeCoin()
    {
        GameObject root = new GameObject("Coin");
        // disc faces forward; the root script spins it around the vertical axis
        Prim(PrimitiveType.Cylinder, "Disc", root.transform, Vector3.zero, new Vector3(0.8f, 0.06f, 0.8f), "Gold").transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        Prim(PrimitiveType.Cylinder, "Emblem", root.transform, new Vector3(0f, 0f, -0.04f), new Vector3(0.4f, 0.03f, 0.4f), "Gem").transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        SphereCollider c = root.AddComponent<SphereCollider>();
        c.isTrigger = true; c.radius = 0.6f;
        CoinCollection cc = root.AddComponent<CoinCollection>();
        cc.collectEffect = LoadPrefab("Sparkle_Gold");
        SavePrefab(root, "Coin");
    }

    static void MakeGemPrefab()
    {
        GameObject root = new GameObject("Gem");
        MeshObj(gemMesh, "GemMesh", root.transform, Vector3.zero, Vector3.one * 0.75f, "Gem");
        SphereCollider c = root.AddComponent<SphereCollider>();
        c.isTrigger = true; c.radius = 0.6f;
        GemCollection gc = root.AddComponent<GemCollection>();
        gc.collectEffect = LoadPrefab("Sparkle_Pink");
        SavePrefab(root, "Gem");
    }

    static void MakeObstacles()
    {
        // ---- LOW obstacles (jumpable, height <= 0.8)
        GameObject rock = new GameObject("Rock");
        Prim(PrimitiveType.Sphere, "Body", rock.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.5f, 0.7f, 1.2f), "Rock");
        Prim(PrimitiveType.Sphere, "Bump", rock.transform, new Vector3(0.4f, 0.35f, 0.2f), new Vector3(0.8f, 0.55f, 0.7f), "Rock");
        AddBox(rock, new Vector3(0f, 0.35f, 0f), new Vector3(1.5f, 0.7f, 1.2f));
        rock.AddComponent<ObstacleCollision>();
        SavePrefab(rock, "Obstacle_Rock");

        GameObject log = new GameObject("Log");
        Prim(PrimitiveType.Cylinder, "Trunk", log.transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.7f, 1.1f, 0.7f), "Wood").transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        Prim(PrimitiveType.Cylinder, "RingL", log.transform, new Vector3(-1.1f, 0.35f, 0f), new Vector3(0.6f, 0.02f, 0.6f), "WoodDark").transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        Prim(PrimitiveType.Cylinder, "RingR", log.transform, new Vector3(1.1f, 0.35f, 0f), new Vector3(0.6f, 0.02f, 0.6f), "WoodDark").transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        AddBox(log, new Vector3(0f, 0.35f, 0f), new Vector3(2.2f, 0.7f, 0.7f));
        log.AddComponent<ObstacleCollision>();
        SavePrefab(log, "Obstacle_Log");

        GameObject crate = new GameObject("Crate");
        Prim(PrimitiveType.Cube, "Box", crate.transform, new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f), "Wood");
        Prim(PrimitiveType.Cube, "BarA", crate.transform, new Vector3(0f, 0.4f, -0.61f), new Vector3(1.25f, 0.12f, 0.05f), "WoodDark");
        Prim(PrimitiveType.Cube, "BarB", crate.transform, new Vector3(0f, 0.4f, -0.61f), new Vector3(0.12f, 0.85f, 0.05f), "WoodDark");
        AddBox(crate, new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f));
        crate.AddComponent<ObstacleCollision>();
        SavePrefab(crate, "Obstacle_Crate");

        // ---- TALL obstacles (must be dodged)
        GameObject block = new GameObject("StoneBlock");
        Prim(PrimitiveType.Cube, "Block", block.transform, new Vector3(0f, 0.9f, 0f), new Vector3(1.5f, 1.8f, 1.5f), "StoneDark");
        Prim(PrimitiveType.Cube, "Cap", block.transform, new Vector3(0f, 1.85f, 0f), new Vector3(1.65f, 0.15f, 1.65f), "Stone");
        AddBox(block, new Vector3(0f, 0.95f, 0f), new Vector3(1.5f, 1.9f, 1.5f));
        block.AddComponent<ObstacleCollision>();
        SavePrefab(block, "Obstacle_StoneBlock");

        GameObject tree = new GameObject("TreeObstacle");
        Prim(PrimitiveType.Cylinder, "Trunk", tree.transform, new Vector3(0f, 1f, 0f), new Vector3(0.45f, 1f, 0.45f), "Trunk");
        Prim(PrimitiveType.Sphere, "Leaves", tree.transform, new Vector3(0f, 2.9f, 0f), new Vector3(2f, 2f, 2f), "LeafGreen");
        AddBox(tree, new Vector3(0f, 1.5f, 0f), new Vector3(1.3f, 3f, 1.3f));
        tree.AddComponent<ObstacleCollision>();
        SavePrefab(tree, "Obstacle_Tree");
    }

    static readonly string[] TreeNames = { "Green", "Pink", "Purple", "Yellow", "Teal", "Orange" };
    static readonly string[] FlowerNames = { "Pink", "Purple", "Yellow", "Blue", "Orange", "White" };

    static void MakeDecor()
    {
        foreach (string n in TreeNames)
        {
            GameObject t = new GameObject("DecorTree_" + n);
            Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(0f, 1.2f, 0f), new Vector3(0.5f, 1.2f, 0.5f), "Trunk");
            Prim(PrimitiveType.Sphere, "Leaves", t.transform, new Vector3(0f, 3.4f, 0f), new Vector3(3.4f, 3.0f, 3.4f), "Leaf" + n);
            Prim(PrimitiveType.Sphere, "Leaves2", t.transform, new Vector3(0.7f, 2.7f, 0.3f), new Vector3(2.2f, 1.9f, 2.2f), "Leaf" + n);
            Prim(PrimitiveType.Sphere, "Leaves3", t.transform, new Vector3(-0.6f, 3.9f, -0.2f), new Vector3(1.9f, 1.7f, 1.9f), "Leaf" + n);
            SavePrefab(t, "DecorTree_" + n);
        }
        foreach (string n in FlowerNames)
        {
            GameObject f = new GameObject("DecorFlower_" + n);
            Prim(PrimitiveType.Cylinder, "Stem", f.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.05f, 0.25f, 0.05f), "Stem");
            Prim(PrimitiveType.Sphere, "Bloom", f.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.38f, 0.26f, 0.38f), "Flower" + n);
            Prim(PrimitiveType.Sphere, "Bloom2", f.transform, new Vector3(0.18f, 0.4f, 0.05f), new Vector3(0.27f, 0.2f, 0.27f), "Flower" + n);
            Prim(PrimitiveType.Sphere, "Bloom3", f.transform, new Vector3(-0.15f, 0.35f, -0.08f), new Vector3(0.27f, 0.2f, 0.27f), "Flower" + n);
            Prim(PrimitiveType.Sphere, "Center", f.transform, new Vector3(0f, 0.68f, 0f), new Vector3(0.14f, 0.1f, 0.14f), "Gold");
            SavePrefab(f, "DecorFlower_" + n);
        }
    }


    static void MakeSegment()
    {
        const float L = 40f;
        GameObject seg = new GameObject("JungleStonePathSegment");
        Prim(PrimitiveType.Cube, "AncientStoneTrail", seg.transform, new Vector3(0f, -0.5f, L / 2f), new Vector3(9f, 1f, L + 0.05f), "Stone", true);
        Prim(PrimitiveType.Cube, "MossSeamL", seg.transform, new Vector3(-1.25f, 0.012f, L / 2f), new Vector3(0.045f, 0.025f, L), "Moss");
        Prim(PrimitiveType.Cube, "MossSeamR", seg.transform, new Vector3(1.25f, 0.012f, L / 2f), new Vector3(0.045f, 0.025f, L), "Moss");
        for (int i = 0; i < 10; i++)
            Prim(PrimitiveType.Cube, "StonePaverJoint" + i, seg.transform, new Vector3(0f, 0.012f, i * 4f + 0.03f), new Vector3(9f, 0.025f, 0.07f), "StoneDark");
        Prim(PrimitiveType.Cube, "JungleFloorL", seg.transform, new Vector3(-19.5f, -0.55f, L / 2f), new Vector3(30f, 1f, L + 0.05f), "Grass");
        Prim(PrimitiveType.Cube, "JungleFloorR", seg.transform, new Vector3(19.5f, -0.55f, L / 2f), new Vector3(30f, 1f, L + 0.05f), "Grass");

        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 4.85f;
            Prim(PrimitiveType.Cube, "MossyStoneCurb", seg.transform, new Vector3(x, 0.28f, L / 2f), new Vector3(0.65f, 0.58f, L + 0.05f), "StoneDark");
            for (int k = 0; k < 5; k++)
            {
                float z = 4f + k * 8f;
                Prim(PrimitiveType.Cube, "BrokenTemplePillar", seg.transform, new Vector3(side * 6.2f, 1.35f, z), new Vector3(0.8f, 2.7f, 0.8f), "Ruins");
                Prim(PrimitiveType.Cube, "PillarCap", seg.transform, new Vector3(side * 6.2f, 2.75f, z), new Vector3(1.15f, 0.25f, 1.15f), "Stone");
                Prim(PrimitiveType.Sphere, "MossClump", seg.transform, new Vector3(side * 6.2f, 2.95f, z + 0.1f), new Vector3(1.2f, 0.45f, 1.1f), "LeafGreen");
            }
            for (int k = 0; k < 12; k++)
            {
                float z = 1.5f + k * 3.2f;
                Prim(PrimitiveType.Cylinder, "HangingVine", seg.transform, new Vector3(side * 5.45f, 1.35f, z), new Vector3(0.045f, 0.8f, 0.045f), "LeafGreen");
                Prim(PrimitiveType.Sphere, "VineLeaf", seg.transform, new Vector3(side * 5.45f, 0.62f, z), new Vector3(0.35f, 0.55f, 0.22f), "LeafTeal");
            }
        }
        SegmentDecorator d = seg.AddComponent<SegmentDecorator>();
        d.segmentLength = L;
        d.trees = new GameObject[TreeNames.Length];
        for (int i = 0; i < TreeNames.Length; i++) d.trees[i] = LoadPrefab("DecorTree_" + TreeNames[i]);
        d.flowers = new GameObject[FlowerNames.Length];
        for (int i = 0; i < FlowerNames.Length; i++) d.flowers[i] = LoadPrefab("DecorFlower_" + FlowerNames[i]);
        d.treesPerSide = 8;
        d.flowersPerSide = 15;
        SavePrefab(seg, "PathSegment");
    }

    // ---------------------------------------------------------------- explorer character (procedural jungle adventurer)
    static Transform BuildExplorer(Transform parent)
    {
        GameObject m = new GameObject("ExplorerModel");
        m.transform.SetParent(parent, false);
        Transform t = m.transform;
        Prim(PrimitiveType.Capsule, "Torso", t, new Vector3(0f, 1.0f, 0f), new Vector3(0.62f, 0.72f, 0.42f), "Shirt");
        Prim(PrimitiveType.Sphere, "Head", t, new Vector3(0f, 1.72f, 0.02f), new Vector3(0.38f, 0.42f, 0.38f), "Skin");
        Prim(PrimitiveType.Cylinder, "HatBrim", t, new Vector3(0f, 1.98f, 0f), new Vector3(0.66f, 0.07f, 0.66f), "Hat");
        Prim(PrimitiveType.Cylinder, "HatCrown", t, new Vector3(0f, 2.13f, 0f), new Vector3(0.39f, 0.25f, 0.39f), "Hat");
        Prim(PrimitiveType.Cube, "Backpack", t, new Vector3(0f, 1.05f, -0.34f), new Vector3(0.72f, 0.78f, 0.34f), "Leather");
        Prim(PrimitiveType.Cylinder, "Bedroll", t, new Vector3(0f, 1.48f, -0.38f), new Vector3(0.15f, 0.82f, 0.15f), "Bedroll").transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        Prim(PrimitiveType.Cube, "BackpackPocket", t, new Vector3(0f, 0.82f, -0.54f), new Vector3(0.42f, 0.32f, 0.06f), "WoodDark");
        Prim(PrimitiveType.Capsule, "ArmL", t, new Vector3(-0.43f, 1.02f, 0f), new Vector3(0.21f, 0.56f, 0.21f), "Shirt");
        Prim(PrimitiveType.Capsule, "ArmR", t, new Vector3(0.43f, 1.02f, 0f), new Vector3(0.21f, 0.56f, 0.21f), "Shirt");
        Prim(PrimitiveType.Capsule, "LegL", t, new Vector3(-0.2f, 0.38f, 0f), new Vector3(0.25f, 0.58f, 0.27f), "Pants");
        Prim(PrimitiveType.Capsule, "LegR", t, new Vector3(0.2f, 0.38f, 0f), new Vector3(0.25f, 0.58f, 0.27f), "Pants");
        Prim(PrimitiveType.Cube, "BootL", t, new Vector3(-0.2f, 0.08f, 0.12f), new Vector3(0.32f, 0.18f, 0.48f), "Boot");
        Prim(PrimitiveType.Cube, "BootR", t, new Vector3(0.2f, 0.08f, 0.12f), new Vector3(0.32f, 0.18f, 0.48f), "Boot");
        Prim(PrimitiveType.Cube, "Belt", t, new Vector3(0f, 0.73f, 0.01f), new Vector3(0.65f, 0.12f, 0.44f), "Leather");
        ExplorerRunAnimation run = m.AddComponent<ExplorerRunAnimation>();
        run.leftArm = t.Find("ArmL"); run.rightArm = t.Find("ArmR");
        run.leftLeg = t.Find("LegL"); run.rightLeg = t.Find("LegR");
        return t;
    }

    // ---------------------------------------------------------------- backdrop (castle, mountains, islands)
    static void BuildCastle(Transform parent, Vector3 pos, float s)
    {
        Transform c = new GameObject("Castle").transform;
        c.SetParent(parent, false);
        c.localPosition = pos;
        c.localScale = Vector3.one * s;
        Prim(PrimitiveType.Cube, "Keep", c, new Vector3(0f, 6f, 0f), new Vector3(14f, 12f, 8f), "Cream");
        Prim(PrimitiveType.Cylinder, "CenterTower", c, new Vector3(0f, 11f, 0f), new Vector3(6f, 11f, 6f), "Cream");
        MeshObj(coneMesh, "CenterRoof", c, new Vector3(0f, 22f, 0f), new Vector3(7.5f, 9f, 7.5f), "RoofPink");
        foreach (float x in new float[] { -8f, 8f })
        {
            Prim(PrimitiveType.Cylinder, "Tower", c, new Vector3(x, 9f, 0f), new Vector3(4f, 9f, 4f), "Cream");
            MeshObj(coneMesh, "TowerRoof", c, new Vector3(x, 18f, 0f), new Vector3(5.2f, 7f, 5.2f), "RoofPink");
        }
        foreach (float x in new float[] { -12f, 12f })
        {
            Prim(PrimitiveType.Cylinder, "SmallTower", c, new Vector3(x, 6f, 0f), new Vector3(3f, 6f, 3f), "Cream");
            MeshObj(coneMesh, "SmallRoof", c, new Vector3(x, 12f, 0f), new Vector3(4f, 5f, 4f), "RoofPink");
        }
        Prim(PrimitiveType.Cube, "Gate", c, new Vector3(0f, 2.5f, -4.05f), new Vector3(3f, 5f, 0.2f), "Gate");
        Prim(PrimitiveType.Cube, "FlagPole", c, new Vector3(0f, 32.5f, 0f), new Vector3(0.15f, 3f, 0.15f), "StoneDark");
        Prim(PrimitiveType.Cube, "Flag", c, new Vector3(0.9f, 33.3f, 0f), new Vector3(1.6f, 1f, 0.1f), "Banner");
    }


    static GameObject BuildBackdrop()
    {
        Random.InitState(19);
        GameObject root = new GameObject("JungleBackdrop");
        Transform r = root.transform;
        Prim(PrimitiveType.Cube, "JungleCliffWall", r, new Vector3(0f, 18f, 270f), new Vector3(620f, 42f, 14f), "Mountain");
        Prim(PrimitiveType.Cube, "CliffMossBand", r, new Vector3(0f, 1f, 260f), new Vector3(620f, 5f, 16f), "LeafGreen");
        Prim(PrimitiveType.Cube, "AncientTempleFacade", r, new Vector3(0f, 9f, 245f), new Vector3(32f, 18f, 5f), "Ruins");
        Prim(PrimitiveType.Cube, "TempleDoorShadow", r, new Vector3(0f, 4.5f, 242.2f), new Vector3(8f, 9f, 0.35f), "WoodDark");
        Prim(PrimitiveType.Cube, "TempleLintel", r, new Vector3(0f, 14.2f, 242f), new Vector3(13f, 1.4f, 1.2f), "Stone");
        for (int side = -1; side <= 1; side += 2)
        {
            Prim(PrimitiveType.Cube, "TempleColumn", r, new Vector3(side * 6.2f, 8f, 241.8f), new Vector3(1.8f, 16f, 1.8f), "Stone");
            Prim(PrimitiveType.Cube, "TempleColumnCap", r, new Vector3(side * 6.2f, 16.2f, 241.8f), new Vector3(2.8f, 1f, 2.8f), "Moss");
            Prim(PrimitiveType.Sphere, "TempleVines", r, new Vector3(side * 8f, 13f, 240f), new Vector3(5f, 3f, 2f), "LeafGreen");
        }
        for (int i = -3; i <= 3; i++)
        {
            float x = i * 34f;
            Prim(PrimitiveType.Cube, "WaterfallRibbon", r, new Vector3(x, 1f, 248f), new Vector3(8f, 30f, 1.5f), "Water");
            Prim(PrimitiveType.Cube, "WaterfallFoam", r, new Vector3(x, -13f, 247f), new Vector3(13f, 1.3f, 3f), "WaterLight");
        }
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 15; i++)
            {
                float z = 12f + i * 17f;
                float x = side * (17f + (i % 4) * 8f);
                float h = 9f + (i % 3) * 2.5f;
                Prim(PrimitiveType.Cylinder, "BackdropTreeTrunk", r, new Vector3(x, h * 0.5f, z), new Vector3(1.1f, h * 0.5f, 1.1f), "Trunk");
                Prim(PrimitiveType.Sphere, "BackdropTreeCanopy", r, new Vector3(x, h + 2f, z), new Vector3(9f, 6f, 9f), "LeafGreen");
                Prim(PrimitiveType.Sphere, "CanopyHighlight", r, new Vector3(x + side * 3f, h + 4f, z + 1f), new Vector3(6f, 4f, 6f), "LeafTeal");
            }
        }
        Vector4[] ridges = {
            new Vector4(-95f, 65f, 38f, 150f), new Vector4(95f, 70f, 42f, 175f),
            new Vector4(-170f, 85f, 55f, 225f), new Vector4(170f, 90f, 58f, 230f)
        };
        foreach (Vector4 ridge in ridges)
        {
            MeshObj(coneMesh, "JungleRockRidge", r, new Vector3(ridge.x, 0f, ridge.w),
                new Vector3(ridge.y, ridge.z, ridge.y * 0.8f), "Mountain");
            Prim(PrimitiveType.Sphere, "RidgeCanopy", r, new Vector3(ridge.x, ridge.z * 0.45f, ridge.w),
                new Vector3(ridge.y * 0.8f, ridge.z * 0.55f, ridge.y * 0.8f), "LeafGreen");
        }
        for (int i = 0; i < 30; i++)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            float x = side * Random.Range(8f, 28f);
            float y = Random.Range(2f, 9f);
            float z = Random.Range(12f, 230f);
            Prim(PrimitiveType.Sphere, "JungleFirefly", r, new Vector3(x, y, z),
                Vector3.one * Random.Range(0.12f, 0.22f), "Lamp");
        }
        return root;
    }

    static void SetupEnvironment()
    {
        Light l = Object.FindFirstObjectByType<Light>();
        if (l != null)
        {
            l.type = LightType.Directional;
            l.color = new Color(0.86f, 1f, 0.78f);
            l.intensity = 1.25f;
            l.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }
        RenderSettings.skybox = mats["Sky"];
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.36f, 0.56f, 0.39f);
        RenderSettings.fogStartDistance = 95f;
        RenderSettings.fogEndDistance = 360f;
        DynamicGI.UpdateEnvironment();
    }

    static AudioManager MakeAudio()
    {
        GameObject g = new GameObject("AudioManager");
        AudioManager a = g.AddComponent<AudioManager>();
        a.music = Clip("music.wav");
        a.run = Clip("run.wav");
        a.jump = Clip("jump.wav");
        a.coin = Clip("coin.wav");
        a.gem = Clip("gem.wav");
        a.hit = Clip("hit.wav");
        a.gameOver = Clip("gameover.wav");
        a.click = Clip("click.wav");
        return a;
    }

    static void MakeEventSystem()
    {
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.UI.InputSystemUIInputModule mod = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        mod.AssignDefaultActions();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    // ---------------------------------------------------------------- UI helpers
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);
    static readonly Color PanelPurple = new Color(0.025f, 0.10f, 0.045f, 0.94f);

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return g.GetComponent<RectTransform>();
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = anchor; r.anchorMax = anchor; r.pivot = anchor;
        r.anchoredPosition = pos; r.sizeDelta = size;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    static Image Img(Transform parent, string name, Color c, Vector2 anchor, Vector2 pos, Vector2 size, Sprite sprite = null)
    {
        RectTransform r = NewRect(name, parent);
        Place(r, anchor, pos, size);
        Image i = r.gameObject.AddComponent<Image>();
        i.color = c;
        i.raycastTarget = false;
        if (sprite != null)
        {
            i.sprite = sprite;
            if (sprite == roundedSprite) { i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 2.5f; }
        }
        return i;
    }

    static TextMeshProUGUI Txt(Transform parent, string name, string text, float size, Color c, Vector2 anchor, Vector2 pos, Vector2 sz,
                               TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        RectTransform r = NewRect(name, parent);
        Place(r, anchor, pos, sz);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        return t;
    }

    static Button Btn(Transform parent, string name, string label, Color c, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize = 48f)
    {
        Image img = Img(parent, name, c, anchor, pos, size, roundedSprite);
        img.raycastTarget = true;
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        img.gameObject.AddComponent<ButtonHoverEffect>();
        TextMeshProUGUI t = Txt(img.transform, "Label", label, fontSize, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Stretch(t.rectTransform);
        return b;
    }

    static GameObject Panel(Transform canvas, string name)
    {
        Image dim = Img(canvas, name, new Color(0f, 0f, 0f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;     // blocks clicks on things behind the panel
        dim.gameObject.AddComponent<PanelFader>();
        return dim.gameObject;
    }

    static Transform MakeCanvas()
    {
        GameObject go = new GameObject("Canvas");
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        cs.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return go.transform;
    }

    static void LoadSprites()
    {
        roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }

    // ---------------------------------------------------------------- MAIN MENU scene
    static void BuildMenuScene()
    {
        LoadSprites();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SetupEnvironment();

        Camera cam = Object.FindFirstObjectByType<Camera>();
        cam.transform.position = new Vector3(0f, 1.7f, -5.5f);
        cam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
        cam.fieldOfView = 50f;
        cam.farClipPlane = 1500f;

        BuildBackdrop();
        GameObject segPrefab = LoadPrefab("PathSegment");
        for (int i = 0; i < 3; i++)
        {
            GameObject s = (GameObject)PrefabUtility.InstantiatePrefab(segPrefab);
            s.transform.position = new Vector3(0f, 0f, -20f + i * 40f);
        }

        GameObject explorer = new GameObject("Explorer");
        explorer.transform.position = new Vector3(-1.7f, 0f, 0f);
        explorer.transform.rotation = Quaternion.Euler(0f, 165f, 0f);
        explorer.transform.localScale = Vector3.one * 1.25f;
        BuildExplorer(explorer.transform);
        explorer.AddComponent<IdleBob>();

        MakeAudio();

        Transform canvas = MakeCanvas();
        Vector2 top = new Vector2(0.5f, 1f), mid = new Vector2(0.5f, 0.5f);

        Image titleBg = Img(canvas, "TitlePanel", new Color(0.035f, 0.16f, 0.07f, 0.96f), top, new Vector2(0f, -30f), new Vector2(1300f, 250f), roundedSprite);
        Txt(titleBg.transform, "Title", "JUNGLE RUNNER", 120f, Gold, top, new Vector2(0f, -15f), new Vector2(1250f, 150f));
        Txt(titleBg.transform, "Subtitle", "RUN  •  JUMP  •  COLLECT  •  AVOID  •  SURVIVE", 38f, Color.white, top, new Vector2(0f, -170f), new Vector2(1250f, 60f));

        Button start = Btn(canvas, "StartButton", "START GAME", new Color(0.2f, 0.75f, 0.25f), mid, new Vector2(480f, 40f), new Vector2(520f, 120f), 52f);
        Button how = Btn(canvas, "InstructionsButton", "INSTRUCTIONS", new Color(0.15f, 0.34f, 0.12f), mid, new Vector2(480f, -100f), new Vector2(520f, 110f), 44f);
        Button exit = Btn(canvas, "ExitButton", "EXIT", new Color(0.85f, 0.22f, 0.32f), mid, new Vector2(480f, -230f), new Vector2(520f, 110f), 44f);

        GameObject panel = Panel(canvas, "InstructionsPanel");
        Image card = Img(panel.transform, "Card", PanelPurple, mid, Vector2.zero, new Vector2(1000f, 700f), roundedSprite);
        Txt(card.transform, "Heading", "JUNGLE SURVIVAL GUIDE", 80f, Gold, top, new Vector2(0f, -30f), new Vector2(900f, 100f));
        Txt(card.transform, "Body",
            "A / Left Arrow  -  Move Left\nD / Right Arrow  -  Move Right\nSpace  -  Jump\nEscape  -  Pause\n\nCollect coins and pink gems.\nJump over rocks, logs and crates.\nDodge trees and stone blocks!",
            40f, Color.white, mid, new Vector2(0f, 10f), new Vector2(900f, 460f));
        Button close = Btn(card.transform, "CloseButton", "CLOSE", new Color(0.12f, 0.38f, 0.2f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(360f, 90f), 44f);

        GameObject ui = new GameObject("MainMenuController");
        MainMenuUI mm = ui.AddComponent<MainMenuUI>();
        mm.startButton = start; mm.instructionsButton = how; mm.exitButton = exit;
        mm.closeInstructionsButton = close; mm.instructionsPanel = panel;
        panel.SetActive(false);

        MakeEventSystem();
        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }

    // ---------------------------------------------------------------- GAME scene
    static void BuildGameScene()
    {
        LoadSprites();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SetupEnvironment();

        // ---- Player
        GameObject player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.05f, 0f);
        Rigidbody rb = player.AddComponent<Rigidbody>();
        rb.mass = 1f; rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        CapsuleCollider cap = player.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0f, 0.9f, 0f); cap.height = 1.8f; cap.radius = 0.4f;
        cap.sharedMaterial = zeroFriction;
        Transform model = BuildExplorer(player.transform);
        PlayerController pc = player.AddComponent<PlayerController>();
        pc.model = model;

        // ---- Camera
        Camera cam = Object.FindFirstObjectByType<Camera>();
        cam.transform.position = new Vector3(0f, 4.2f, -7f);
        cam.transform.rotation = Quaternion.Euler(13f, 0f, 0f);
        cam.fieldOfView = 60f;
        cam.farClipPlane = 1500f;
        CameraFollow cf = cam.gameObject.AddComponent<CameraFollow>();
        cf.target = player.transform;

        // floating magic sparkles around the path
        GameObject fx = new GameObject("JungleFireflies");
        fx.transform.SetParent(cam.transform, false);
        fx.transform.localPosition = new Vector3(0f, 1f, 16f);
        ParticleSystem sps = fx.AddComponent<ParticleSystem>();
        sps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule sm = sps.main;
        sm.loop = true; sm.prewarm = true;
        sm.startLifetime = 6f; sm.startSpeed = 0.3f;
        sm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 1f, 0.35f), new Color(1f, 0.82f, 0.25f));
        sm.maxParticles = 300;
        sm.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule sem = sps.emission;
        sem.rateOverTime = 40f;
        ParticleSystem.ShapeModule ssh = sps.shape;
        ssh.shapeType = ParticleSystemShapeType.Box;
        ssh.scale = new Vector3(24f, 10f, 30f);
        fx.GetComponent<ParticleSystemRenderer>().sharedMaterial = mats["Particle_White"];

        // ---- Managers
        GameObject mgr = new GameObject("GameManager");
        GameManager gm = mgr.AddComponent<GameManager>();
        gm.player = player.transform;

        GameObject path = new GameObject("PathGenerator");
        ObstacleSpawner sp = path.AddComponent<ObstacleSpawner>();
        sp.coinPrefab = LoadPrefab("Coin");
        sp.gemPrefab = LoadPrefab("Gem");
        sp.lowObstacles = new GameObject[] { LoadPrefab("Obstacle_Rock"), LoadPrefab("Obstacle_Log"), LoadPrefab("Obstacle_Crate") };
        sp.tallObstacles = new GameObject[] { LoadPrefab("Obstacle_StoneBlock"), LoadPrefab("Obstacle_Tree") };
        EndlessPathGenerator gen = path.AddComponent<EndlessPathGenerator>();
        gen.segmentPrefab = LoadPrefab("PathSegment");
        gen.spawner = sp;
        gen.player = player.transform;

        GameObject bd = BuildBackdrop();
        bd.AddComponent<BackdropFollow>().target = player.transform;

        MakeAudio();

        // ---- UI
        Transform canvas = MakeCanvas();
        Vector2 tl = new Vector2(0f, 1f), tc = new Vector2(0.5f, 1f), tr = new Vector2(1f, 1f), mid = new Vector2(0.5f, 0.5f), bc = new Vector2(0.5f, 0f);
        Color white = Color.white;

        // HUD: coins, gems, distance (top-left)
        Image hud = Img(canvas, "HudPanel", new Color(0.025f, 0.11f, 0.045f, 0.82f), tl, new Vector2(30f, -30f), new Vector2(340f, 230f), roundedSprite);
        Img(hud.transform, "CoinIcon", Gold, tl, new Vector2(25f, -20f), new Vector2(50f, 50f), knobSprite);
        TextMeshProUGUI coinText = Txt(hud.transform, "CoinText", "0", 44f, white, tl, new Vector2(95f, -20f), new Vector2(230f, 50f), TextAlignmentOptions.Left);
        Image gemIcon = Img(hud.transform, "GemIcon", new Color(0.12f, 0.85f, 0.38f), tl, Vector2.zero, new Vector2(36f, 36f));
        gemIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        gemIcon.rectTransform.anchoredPosition = new Vector2(50f, -90f);
        gemIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        TextMeshProUGUI gemText = Txt(hud.transform, "GemText", "0", 44f, white, tl, new Vector2(95f, -65f), new Vector2(230f, 50f), TextAlignmentOptions.Left);
        Txt(hud.transform, "RunIcon", "m", 40f, new Color(0.5f, 1f, 0.5f), tl, new Vector2(25f, -135f), new Vector2(50f, 50f));
        TextMeshProUGUI distText = Txt(hud.transform, "DistanceText", "0 m", 44f, white, tl, new Vector2(95f, -135f), new Vector2(230f, 50f), TextAlignmentOptions.Left);

        // Score (top-center)
        Image scoreBg = Img(canvas, "ScorePanel", new Color(0.025f, 0.11f, 0.045f, 0.82f), tc, new Vector2(0f, -30f), new Vector2(400f, 130f), roundedSprite);
        Txt(scoreBg.transform, "ScoreLabel", "SCORE", 30f, Gold, tc, new Vector2(0f, -8f), new Vector2(380f, 40f));
        TextMeshProUGUI scoreText = Txt(scoreBg.transform, "ScoreValue", "0", 64f, white, tc, new Vector2(0f, -45f), new Vector2(380f, 80f));

        // Pause button (top-right)
        Button pauseBtn = Btn(canvas, "PauseButton", "II", new Color(0.15f, 0.34f, 0.12f), tr, new Vector2(-30f, -30f), new Vector2(100f, 100f), 52f);

        // Game Over panel
        GameObject goPanel = Panel(canvas, "GameOverPanel");
        Image goCard = Img(goPanel.transform, "Card", PanelPurple, mid, Vector2.zero, new Vector2(900f, 780f), roundedSprite);
        Txt(goCard.transform, "Title", "GAME OVER", 110f, new Color(1f, 0.3f, 0.5f), tc, new Vector2(0f, -30f), new Vector2(850f, 140f));
        TextMeshProUGUI goScore = Txt(goCard.transform, "ScoreText", "Score: 0", 52f, white, tc, new Vector2(0f, -190f), new Vector2(800f, 70f));
        TextMeshProUGUI goCoins = Txt(goCard.transform, "CoinsText", "Coins: 0", 44f, Gold, tc, new Vector2(0f, -265f), new Vector2(800f, 60f));
        TextMeshProUGUI goGems = Txt(goCard.transform, "GemsText", "Emeralds: 0", 44f, new Color(0.35f, 1f, 0.48f), tc, new Vector2(0f, -330f), new Vector2(800f, 60f));
        TextMeshProUGUI goDist = Txt(goCard.transform, "DistanceText", "Distance: 0 m", 44f, new Color(0.65f, 1f, 0.55f), tc, new Vector2(0f, -395f), new Vector2(800f, 60f));
        TextMeshProUGUI goBest = Txt(goCard.transform, "BestText", "Best Score: 0", 38f, white, tc, new Vector2(0f, -470f), new Vector2(800f, 55f));
        Button retry = Btn(goCard.transform, "RetryButton", "RETRY", new Color(0.98f, 0.7f, 0.05f), bc, new Vector2(-210f, 70f), new Vector2(380f, 110f), 50f);
        Button toMenu = Btn(goCard.transform, "MainMenuButton", "MAIN MENU", new Color(0.12f, 0.4f, 0.95f), bc, new Vector2(210f, 70f), new Vector2(380f, 110f), 44f);

        // Pause panel
        GameObject pausePanel = Panel(canvas, "PausePanel");
        Image pCard = Img(pausePanel.transform, "Card", PanelPurple, mid, Vector2.zero, new Vector2(700f, 700f), roundedSprite);
        Txt(pCard.transform, "Title", "PAUSED", 100f, Gold, tc, new Vector2(0f, -30f), new Vector2(650f, 130f));
        Button resume = Btn(pCard.transform, "ResumeButton", "RESUME", new Color(0.2f, 0.75f, 0.25f), tc, new Vector2(0f, -200f), new Vector2(480f, 110f), 48f);
        Button restart = Btn(pCard.transform, "RestartButton", "RESTART", new Color(0.98f, 0.7f, 0.05f), tc, new Vector2(0f, -340f), new Vector2(480f, 110f), 48f);
        Button pMenu = Btn(pCard.transform, "MainMenuButton", "MAIN MENU", new Color(0.12f, 0.4f, 0.95f), tc, new Vector2(0f, -480f), new Vector2(480f, 110f), 44f);

        // UI controller object with the three UI scripts
        GameObject ui = new GameObject("UIController");
        HUDUI hudUI = ui.AddComponent<HUDUI>();
        hudUI.coinText = coinText; hudUI.gemText = gemText; hudUI.scoreText = scoreText; hudUI.distanceText = distText;
        GameOverUI gou = ui.AddComponent<GameOverUI>();
        gou.panel = goPanel; gou.scoreText = goScore; gou.coinsText = goCoins; gou.gemsText = goGems;
        gou.distanceText = goDist; gou.bestText = goBest; gou.retryButton = retry; gou.mainMenuButton = toMenu;
        PauseManager pm = ui.AddComponent<PauseManager>();
        pm.pausePanel = pausePanel; pm.pauseButton = pauseBtn; pm.resumeButton = resume;
        pm.restartButton = restart; pm.mainMenuButton = pMenu;

        goPanel.SetActive(false);
        pausePanel.SetActive(false);

        MakeEventSystem();
        EditorSceneManager.SaveScene(scene, GameScenePath);
    }

    // ---------------------------------------------------------------- build settings
    static void FinishSettings()
    {
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true)
        };
        PlayerSettings.productName = "Jungle Runner";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
    }
}
#endif
