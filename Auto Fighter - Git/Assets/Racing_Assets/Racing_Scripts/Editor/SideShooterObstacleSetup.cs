#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates simple box prefabs for the roadside side-shooter obstacle + its projectile.
/// </summary>
public static class SideShooterObstacleSetup
{
    private const string Folder = "Assets/Racing_Assets";
    private const string ShooterPath = Folder + "/Obstacle Side Shooter.prefab";
    private const string ProjectilePath = Folder + "/Side Shooter Projectile.prefab";

    [MenuItem("Tools/Racing/Create Side Shooter Prefabs")]
    public static void CreatePrefabs()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            EditorUtility.DisplayDialog("Side Shooter Setup", $"Folder missing: {Folder}", "OK");
            return;
        }

        // 1) Projectile asset
        GameObject projectileTemp = CreateProjectileTemp();
        PrefabUtility.SaveAsPrefabAsset(projectileTemp, ProjectilePath);
        Object.DestroyImmediate(projectileTemp);
        var projectileAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);

        // 2) Shooter asset wired to projectile asset
        GameObject shooterTemp = CreateShooterTemp(projectileAsset);
        PrefabUtility.SaveAsPrefabAsset(shooterTemp, ShooterPath);
        Object.DestroyImmediate(shooterTemp);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var shooterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShooterPath);
        EditorGUIUtility.PingObject(shooterAsset);

        EditorUtility.DisplayDialog(
            "Side Shooter Prefabs Created",
            "Created:\n• Obstacle Side Shooter.prefab\n• Side Shooter Projectile.prefab\n\n" +
            "Add 'Obstacle Side Shooter' to TrackObstacleSpawner → Obstacle Types (like Shuttle).",
            "OK");
    }

    private static GameObject CreateProjectileTemp()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Side Shooter Projectile";
        go.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

        Object.DestroyImmediate(go.GetComponent<SphereCollider>());
        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.5f;

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        go.AddComponent<SideShooterProjectile>();
        return go;
    }

    private static GameObject CreateShooterTemp(GameObject projectileAsset)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Obstacle Side Shooter";
        go.transform.localScale = new Vector3(1.1f, 1.4f, 1.1f);

        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        go.layer = obstacleLayer >= 0 ? obstacleLayer : 0;

        var box = go.GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = false;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        var racing = go.AddComponent<RacingObstacle>();
        var soRacing = new SerializedObject(racing);
        var typeProp = soRacing.FindProperty("obstacleType");
        if (typeProp != null)
        {
            // SideShooter is last enum value
            typeProp.enumValueIndex = (int)ObstacleTyping.SideShooter;
            soRacing.ApplyModifiedPropertiesWithoutUndo();
        }

        var identity = go.AddComponent<CrashObstacleIdentity>();
        var soId = new SerializedObject(identity);
        var kindProp = soId.FindProperty("kind");
        if (kindProp != null)
        {
            kindProp.enumValueIndex = (int)CrashObstacleKind.SideShooter;
            soId.ApplyModifiedPropertiesWithoutUndo();
        }

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(go.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.35f, 0.65f);

        var shooter = go.AddComponent<TrackSideShooterObstacle>();
        var so = new SerializedObject(shooter);
        so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        so.FindProperty("projectilePrefab").objectReferenceValue = projectileAsset;
        so.ApplyModifiedPropertiesWithoutUndo();

        return go;
    }

    [MenuItem("Tools/Racing/Create Side Shooter Prefabs", true)]
    private static bool CreatePrefabsValidate() => !Application.isPlaying;
}
#endif
