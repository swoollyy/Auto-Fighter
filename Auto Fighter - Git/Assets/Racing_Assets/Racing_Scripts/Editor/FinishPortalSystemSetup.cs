using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Builds / refreshes the Finish Portal system as editable scene objects.
/// Menu: Racing → Setup Finish Portal System In Open Scene
/// </summary>
public static class FinishPortalSystemSetup
{
#if UNITY_EDITOR
    public const string RootName = "FinishPortalSystem";

    [MenuItem("Racing/Setup Finish Portal System In Open Scene")]
    public static void SetupInOpenScene()
    {
        var root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Finish Portal System");
        }

        var director = root.GetComponent<FinishPortalDirector>();
        if (director == null)
            director = Undo.AddComponent<FinishPortalDirector>(root);

        // --- Portal gate ---
        Transform gateT = root.transform.Find("FinishPortalGate");
        GameObject gateGo = gateT != null ? gateT.gameObject : null;
        if (gateGo == null)
        {
            gateGo = new GameObject("FinishPortalGate");
            Undo.RegisterCreatedObjectUndo(gateGo, "Create FinishPortalGate");
            gateGo.transform.SetParent(root.transform, false);
        }

        var gate = gateGo.GetComponent<FinishPortalGate>();
        if (gate == null)
            gate = Undo.AddComponent<FinishPortalGate>(gateGo);

        EnsurePortalVisuals(gateGo);
        gate.EditorAssignBuiltVisuals();

        // --- Hyper tunnel VFX ---
        Transform vfxT = root.transform.Find("FinishHyperTunnelVfx");
        GameObject vfxGo = vfxT != null ? vfxT.gameObject : null;
        if (vfxGo == null)
        {
            vfxGo = new GameObject("FinishHyperTunnelVfx");
            Undo.RegisterCreatedObjectUndo(vfxGo, "Create FinishHyperTunnelVfx");
            vfxGo.transform.SetParent(root.transform, false);
        }

        var vfx = vfxGo.GetComponent<FinishHyperTunnelVfx>();
        if (vfx == null)
            vfx = Undo.AddComponent<FinishHyperTunnelVfx>(vfxGo);

        vfx.EditorRebuildHierarchy();

        // Wire director refs via SerializedObject so private fields serialize.
        var so = new SerializedObject(director);
        so.FindProperty("portal").objectReferenceValue = gate;
        so.FindProperty("tunnelVfx").objectReferenceValue = vfx;
        so.FindProperty("trackGenerator").objectReferenceValue =
            Object.FindObjectOfType<ProceduralTrackGenerator>(true);
        so.FindProperty("distanceMeter").objectReferenceValue =
            Object.FindObjectOfType<TrackDistanceMeter>(true);
        so.FindProperty("gameManager").objectReferenceValue =
            Object.FindObjectOfType<GameManager_Racing>(true);
        so.FindProperty("uiManager").objectReferenceValue =
            Object.FindObjectOfType<UIManager_Racing>(true);
        so.FindProperty("cameraFollow").objectReferenceValue =
            Object.FindObjectOfType<CameraFollow>(true);
        so.FindProperty("postFx").objectReferenceValue =
            Object.FindObjectOfType<ForcefieldPostFXController>(true);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Keep inactive until a run places / plays them.
        gateGo.SetActive(false);
        // Canvases start disabled inside VFX.

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[FinishPortalSystemSetup] FinishPortalSystem is in the scene. Select it to edit timings, portal visuals, and tunnel FX.");
    }

    private static void EnsurePortalVisuals(GameObject gateGo)
    {
        if (gateGo.GetComponent<BoxCollider>() == null)
        {
            var box = Undo.AddComponent<BoxCollider>(gateGo);
            box.isTrigger = true;
        }

        Transform visual = gateGo.transform.Find("PortalVisual");
        if (visual == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(q, "PortalVisual");
            q.name = "PortalVisual";
            q.transform.SetParent(gateGo.transform, false);
            q.transform.localScale = new Vector3(5f, 6f, 1f);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            ApplyDefaultPortalMaterial(q.GetComponent<MeshRenderer>(), new Color(0.15f, 0.85f, 1f, 0.55f));
        }

        Transform swirl = gateGo.transform.Find("PortalSwirl");
        if (swirl == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(q, "PortalSwirl");
            q.name = "PortalSwirl";
            q.transform.SetParent(gateGo.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            q.transform.localScale = new Vector3(4.6f, 5.5f, 1f);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            ApplyDefaultPortalMaterial(q.GetComponent<MeshRenderer>(), new Color(1f, 0.25f, 0.9f, 0.35f));
        }
    }

    private static void ApplyDefaultPortalMaterial(MeshRenderer rend, Color tint)
    {
        if (rend == null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        mat.renderQueue = 3000;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
    }
#endif
}
