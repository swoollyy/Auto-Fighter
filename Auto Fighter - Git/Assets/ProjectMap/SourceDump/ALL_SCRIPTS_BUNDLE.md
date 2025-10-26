# All Scripts Bundle
- Generated: 2025-10-26T00:12:37.9633710Z (UTC)
- Unity: 2022.3.62f2
- Files: 105

## Assets/BumperAnimScript.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[DisallowMultipleComponent]
[RequireComponent(typeof(Bumper))]
public class BumperAnimScript : MonoBehaviour
{
    private Material defMaterial;
    private Color defMatColor;

    private Bumper bumper;

    [SerializeField] private bool resetHPBarAlphaToZero = true; // hide HP bar before each flash
    [SerializeField] private Image HPBar;              // assign your HP bar image here
    [SerializeField] private Color hpFlashColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float hpFlashAlpha = 0.9f;
    [SerializeField] private float hpFlashDuration = 0.18f; // total flash time
    [SerializeField] private Vector2 hpPunchScale = new Vector2(1.08f, 1.08f);
    [SerializeField] private int hpPunchVibrato = 9;        // how �wobbly� the punch is
    [SerializeField, Range(0f, 1f)] private float hpPunchElasticity = 0.12f;
    [SerializeField, Range(0f, .1f)] private float genScale = 0.04f; // general scale reduction to keep things in check
    [SerializeField, Min(0f)] private float hpFillLerpSpeed = 2f;    // lerp speed for HP fill

    private Vector3 _defLocalScale;        // default bumper scale
    private Vector3 _hpRTDefaultScale;     // default HP bar rect scale
    private float _hpGroupDefaultAlpha;    // default canvas group alpha

    private Color _hpDefaultColor;
    private RectTransform _hpRT;

    [SerializeField] private CanvasGroup hpGroup;

    // Initializes UI alpha defaults.
    void Awake()
    {
        if (hpGroup != null) hpGroup.alpha = 0f;
    }

    // Caches materials, UI refs, and defaults for tween resets.
    void Start()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            defMaterial = renderer.material;
            defMatColor = defMaterial.color;
        }

        bumper = GetComponent<Bumper>();

        if (HPBar != null)
        {
            _hpDefaultColor = HPBar.color;
            _hpRT = HPBar.rectTransform;
        }

        if (hpGroup != null)
        {
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }

        _defLocalScale = transform.localScale;

        if (_hpRT != null)
            _hpRTDefaultScale = _hpRT.localScale;

        if (hpGroup != null)
            _hpGroupDefaultAlpha = hpGroup.alpha;
    }

    // Smoothly updates HP bar fill amount to match bumper health.
    void Update()
    {
        if (HPBar == null || bumper == null) return;

        float max = Mathf.Max(0.0001f, bumper.maxHealth);
        float target = Mathf.Clamp01(bumper.curHealth / max);
        HPBar.fillAmount = Mathf.MoveTowards(HPBar.fillAmount, target, Time.deltaTime * hpFillLerpSpeed);
    }

    // Stops all tweens and restores transforms/materials/alphas to defaults.
    public void ResetTweenState()
    {
        transform.DOKill(false);
        if (defMaterial != null) defMaterial.DOKill(false);
        if (HPBar != null) HPBar.DOKill(false);
        if (_hpRT != null) _hpRT.DOKill(false);
        if (hpGroup != null) hpGroup.DOKill(false);

        transform.localScale = _defLocalScale;
        if (_hpRT != null) _hpRT.localScale = _hpRTDefaultScale;

        if (defMaterial != null) defMaterial.color = defMatColor;
        if (HPBar != null) HPBar.color = _hpDefaultColor;

        if (hpGroup != null)
        {
            hpGroup.alpha = resetHPBarAlphaToZero ? 0f : _hpGroupDefaultAlpha;
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }
    }

    // Plays bumper hit feedback (punch scale + material flash) and flashes HP bar.
    public void BumperHit()
    {
        ResetTweenState();

        DOTween.Kill(transform);
        transform.DOPunchScale(new Vector3(.3f, .3f, .3f), 0.2f, 2, .1f);

        if (defMaterial != null)
        {
            defMaterial.DOColor(Color.white, 0.1f).OnComplete(() =>
            {
                defMaterial.DOColor(defMatColor, 0.1f);
            });
        }

        FlashHPBar();
    }

    // Flashes the HP bar: quick fade-in, color flash, optional punch, then fade-out.
    private void FlashHPBar()
    {
        if (hpGroup != null)
        {
            DOTween.Kill(hpGroup);
            hpGroup.DOFade(1f, 0.05f).SetUpdate(true);
        }

        if (HPBar == null) return;

        DOTween.Kill(HPBar);
        if (_hpRT != null) DOTween.Kill(_hpRT);

        var target = hpFlashColor;
        target.a = Mathf.Clamp01(hpFlashAlpha);

        var half = hpFlashDuration * 0.5f;

        var seq = DOTween.Sequence().SetId(HPBar);

        // 1) flash up to target color
        seq.Append(HPBar.DOColor(target, half).SetEase(Ease.OutQuad));

        // 2) punch (guarded)
        if (_hpRT != null)
        {
            seq.Join(_hpRT.DOPunchScale(
                new Vector3(hpPunchScale.x - genScale, hpPunchScale.y - genScale, 0f),
                hpFlashDuration,
                hpPunchVibrato,
                hpPunchElasticity
            ).SetEase(Ease.OutQuad));
        }

        // 3) return to default color
        seq.Append(HPBar.DOColor(_hpDefaultColor, half).SetEase(Ease.InQuad));

        // 4) fade out the whole bar
        if (hpGroup != null)
            seq.Append(hpGroup.DOFade(0f, 0.25f).SetEase(Ease.InQuad).SetUpdate(true));

        // make the whole sequence run while paused
        seq.SetUpdate(true);
    }
}
```

## Assets/Editor/ProjectSummary.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.IO; // <-- added

public static class ProjectSummary
{
    private const string OutputDir = "Assets/ProjectMap";
    private const string MdPath = OutputDir + "/PROJECT_SUMMARY.md";
    private const string JsonPath = OutputDir + "/project-summary.json";

    [MenuItem("Tools/Generate Project Summary")]
    public static void Generate()
    {
        try
        {
            System.IO.Directory.CreateDirectory(OutputDir);
            EditorUtility.DisplayProgressBar("Project Summary", "Collecting assemblies...", 0.05f);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(IsProjectAssembly)
                .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var map = new SummaryMap
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                assemblies = new List<AssemblyInfo>(),
            };

            int iAsm = 0;
            foreach (var asm in assemblies)
            {
                EditorUtility.DisplayProgressBar("Project Summary", $"Scanning {asm.GetName().Name}", 0.1f + 0.8f * (iAsm++ / Math.Max(1f, assemblies.Length)));
                var ainfo = new AssemblyInfo { name = asm.GetName().Name, types = new List<TypeInfoLite>() };

                foreach (var t in SafeGetTypes(asm))
                {
                    if (t == null || t.FullName == null) continue;
                    if (t.FullName.StartsWith("UnityEngine.") || t.FullName.StartsWith("UnityEditor.")) continue;

                    var til = new TypeInfoLite
                    {
                        name = t.Name,
                        fullName = t.FullName,
                        ns = t.Namespace ?? "",
                        baseType = t.BaseType?.FullName ?? "",
                        isMonoBehaviour = typeof(MonoBehaviour).IsAssignableFrom(t),
                        isScriptableObject = typeof(ScriptableObject).IsAssignableFrom(t),
                        implements = t.GetInterfaces().Select(x => x.FullName ?? x.Name).Distinct().OrderBy(s => s).ToList(),
                        fields = new List<MemberLite>(),
                        properties = new List<MemberLite>(),
                        methods = new List<MethodLite>(),
                        unityMessages = new List<string>(),
                        isIPowerup = t.GetInterfaces().Any(ii => ii.Name == "IPowerup" || (ii.FullName ?? "").EndsWith(".IPowerup")),
                    };

                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        bool serialized = f.IsPublic && !Attribute.IsDefined(f, typeof(NonSerializedAttribute))
                                          || Attribute.IsDefined(f, typeof(SerializeField));
                        if (serialized || f.IsPublic)
                        {
                            til.fields.Add(new MemberLite { name = f.Name, type = f.FieldType.FullName ?? f.FieldType.Name, flags = serialized ? "serialized" : (f.IsPublic ? "public" : "") });
                            if (til.fields.Count > 24) break;
                        }
                    }

                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        til.properties.Add(new MemberLite { name = p.Name, type = p.PropertyType.FullName ?? p.PropertyType.Name, flags = $"{(p.CanRead ? "get" : "")}{(p.CanWrite ? "/set" : "")}" });
                        if (til.properties.Count > 24) break;
                    }

                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        til.methods.Add(new MethodLite
                        {
                            name = m.Name,
                            ret = m.ReturnType.FullName ?? m.ReturnType.Name,
                            @params = m.GetParameters().Select(pp => pp.ParameterType.Name).ToList()
                        });
                        if (til.methods.Count > 24) break;
                    }

                    var unityMsgs = new[] { "Awake", "OnEnable", "Start", "Update", "LateUpdate", "FixedUpdate", "OnDisable", "OnDestroy", "OnCollisionEnter", "OnCollisionExit", "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnValidate", "Reset" };
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (unityMsgs.Contains(m.Name)) til.unityMessages.Add(m.Name);
                    til.unityMessages = til.unityMessages.Distinct().OrderBy(s => s).ToList();

                    ainfo.types.Add(til);
                }

                ainfo.types = ainfo.types.OrderBy(t => t.fullName, StringComparer.OrdinalIgnoreCase).ToList();
                map.assemblies.Add(ainfo);
            }

            map.summary = new Summary
            {
                assemblies = map.assemblies.Count,
                monoBehaviours = map.assemblies.Sum(a => a.types.Count(t => t.isMonoBehaviour)),
                scriptableObjects = map.assemblies.Sum(a => a.types.Count(t => t.isScriptableObject)),
                ipowerups = map.assemblies.SelectMany(a => a.types.Where(t => t.isIPowerup).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                classesNamedPowerup = map.assemblies.SelectMany(a => a.types.Where(t => t.name.EndsWith("Powerup", StringComparison.OrdinalIgnoreCase)).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                pinballTypes = map.assemblies.SelectMany(a => a.types.Where(t => t.name.Equals("Pinball", StringComparison.OrdinalIgnoreCase)).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                runContextTypes = map.assemblies.SelectMany(a => a.types.Where(t => t.name.Equals("IRunContext", StringComparison.OrdinalIgnoreCase) || t.name.EndsWith("RunContext")).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            System.IO.File.WriteAllText(MdPath, BuildMarkdown(map), new UTF8Encoding(false));

            var json = EditorJsonUtility.ToJson(map, true);
            System.IO.File.WriteAllText(JsonPath, json, new UTF8Encoding(false));

            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSummary] Generated:\n- {MdPath}\n- {JsonPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectSummary] Failed: {ex}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // NEW: Export all C# sources under Assets into a single bundle I can ingest here
    [MenuItem("Tools/Export All Scripts For Review")]
    public static void ExportAllScriptsForReview()
    {
        try
        {
            Directory.CreateDirectory(OutputDir);
            var dumpDir = Path.Combine(OutputDir, "SourceDump").Replace("\\", "/");
            Directory.CreateDirectory(dumpDir);

            string bundlePath = Path.Combine(dumpDir, "ALL_SCRIPTS_BUNDLE.md").Replace("\\", "/");
            var projectAssetsAbs = Application.dataPath.Replace("\\", "/"); // .../Project/Assets
            var projectRootAbs = Path.GetDirectoryName(projectAssetsAbs)!.Replace("\\", "/");

            var allCs = Directory.EnumerateFiles(projectAssetsAbs, "*.cs", SearchOption.AllDirectories)
                                 .Select(p => p.Replace("\\", "/"))
                                 .Where(p => !p.Contains("/ProjectMap/")) // avoid dumping the dump
                                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            using (var sw = new StreamWriter(bundlePath, false, new UTF8Encoding(false)))
            {
                sw.WriteLine("# All Scripts Bundle");
                sw.WriteLine($"- Generated: {DateTime.UtcNow:o} (UTC)");
                sw.WriteLine($"- Unity: {Application.unityVersion}");
                sw.WriteLine($"- Files: {allCs.Count}");
                sw.WriteLine();

                int i = 0;
                foreach (var abs in allCs)
                {
                    EditorUtility.DisplayProgressBar("Export All Scripts", $"Bundling {++i}/{allCs.Count}", i / (float)Math.Max(1, allCs.Count));

                    var rel = "Assets" + abs.Substring(projectAssetsAbs.Length);
                    sw.WriteLine($"## {rel}");
                    sw.WriteLine();
                    sw.WriteLine("```csharp");
                    sw.Write(File.ReadAllText(abs));
                    sw.WriteLine();
                    sw.WriteLine("```");
                    sw.WriteLine();
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSummary] Exported all scripts to: {bundlePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectSummary] ExportAllScriptsForReview failed: {ex}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static bool IsProjectAssembly(Assembly asm)
    {
        var n = asm.GetName().Name ?? "";
        string[] exclude = { "Unity.", "UnityEngine", "UnityEditor", "System", "Microsoft", "mscorlib", "netstandard", "Bee", "nunit" };
        return !exclude.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null); }
        catch { return Array.Empty<Type>(); }
    }

    private static string BuildMarkdown(SummaryMap map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project Summary");
        sb.AppendLine($"- Generated: {map.generatedAtUtc} (UTC)");
        sb.AppendLine($"- Unity: {map.unityVersion}");
        sb.AppendLine($"- Assemblies: {map.summary.assemblies}");
        sb.AppendLine($"- MonoBehaviours: {map.summary.monoBehaviours}");
        sb.AppendLine($"- ScriptableObjects: {map.summary.scriptableObjects}");
        if (map.summary.ipowerups.Any())
        {
            sb.AppendLine("- IPowerup implementations:");
            foreach (var p in map.summary.ipowerups) sb.AppendLine($"  - {p}");
        }
        if (map.summary.classesNamedPowerup.Any())
        {
            sb.AppendLine("- *Powerup classes:");
            foreach (var p in map.summary.classesNamedPowerup) sb.AppendLine($"  - {p}");
        }
        if (map.summary.pinballTypes.Any())
        {
            sb.AppendLine("- Pinball types:");
            foreach (var p in map.summary.pinballTypes) sb.AppendLine($"  - {p}");
        }
        if (map.summary.runContextTypes.Any())
        {
            sb.AppendLine("- RunContext-related types:");
            foreach (var p in map.summary.runContextTypes) sb.AppendLine($"  - {p}");
        }
        sb.AppendLine();
        foreach (var asm in map.assemblies.OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {asm.name}");
            foreach (var t in asm.types)
            {
                var tags = new List<string>();
                if (t.isMonoBehaviour) tags.Add("MonoBehaviour");
                if (t.isScriptableObject) tags.Add("ScriptableObject");
                if (t.isIPowerup) tags.Add("IPowerup");
                var tagStr = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                sb.AppendLine($"- {t.fullName}{tagStr}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Serializable]
    private class SummaryMap
    {
        public string generatedAtUtc;
        public string unityVersion;
        public List<AssemblyInfo> assemblies;
        public Summary summary;
    }
    [Serializable] private class Summary { public int assemblies, monoBehaviours, scriptableObjects; public List<string> ipowerups, classesNamedPowerup, pinballTypes, runContextTypes; }
    [Serializable] private class AssemblyInfo { public string name; public List<TypeInfoLite> types; }
    [Serializable]
    private class TypeInfoLite
    {
        public string name, fullName, ns, baseType;
        public bool isMonoBehaviour, isScriptableObject, isIPowerup;
        public List<string> implements;
        public List<MemberLite> fields;
        public List<MemberLite> properties;
        public List<MethodLite> methods;
        public List<string> unityMessages;
    }
    [Serializable] private class MemberLite { public string name, type, flags; }
    [Serializable] private class MethodLite { public string name, ret; public List<string> @params; }
}
```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleAudio.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;
using UnityEngine.Audio; // Required for AudioMixer

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleAudio
    {
        #region Shortcuts

        #region Audio

        /// <summary>Tweens an AudioSource's volume to the given value.
        /// Also stores the AudioSource as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach (0 to 1)</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFade(this AudioSource target, float endValue, float duration)
        {
            if (endValue < 0) endValue = 0;
            else if (endValue > 1) endValue = 1;
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.volume, x => target.volume = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an AudioSource's pitch to the given value.
        /// Also stores the AudioSource as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOPitch(this AudioSource target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.pitch, x => target.pitch = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region AudioMixer

        /// <summary>Tweens an AudioMixer's exposed float to the given value.
        /// Also stores the AudioMixer as the tween's target so it can be used for filtered operations.
        /// Note that you need to manually expose a float in an AudioMixerGroup in order to be able to tween it from an AudioMixer.</summary>
        /// <param name="floatName">Name given to the exposed float to set</param>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOSetFloat(this AudioMixer target, string floatName, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(()=> {
                    float currVal;
                    target.GetFloat(floatName, out currVal);
                    return currVal;
                }, x=> target.SetFloat(floatName, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #region Operation Shortcuts

        /// <summary>
        /// Completes all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens completed
        /// (meaning the tweens that don't have infinite loops and were not already complete)
        /// </summary>
        /// <param name="withCallbacks">For Sequences only: if TRUE also internal Sequence callbacks will be fired,
        /// otherwise they will be ignored</param>
        public static int DOComplete(this AudioMixer target, bool withCallbacks = false)
        {
            return DOTween.Complete(target, withCallbacks);
        }

        /// <summary>
        /// Kills all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens killed.
        /// </summary>
        /// <param name="complete">If TRUE completes the tween before killing it</param>
        public static int DOKill(this AudioMixer target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        /// <summary>
        /// Flips the direction (backwards if it was going forward or viceversa) of all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens flipped.
        /// </summary>
        public static int DOFlip(this AudioMixer target)
        {
            return DOTween.Flip(target);
        }

        /// <summary>
        /// Sends to the given position all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens involved.
        /// </summary>
        /// <param name="to">Time position to reach
        /// (if higher than the whole tween duration the tween will simply reach its end)</param>
        /// <param name="andPlay">If TRUE will play the tween after reaching the given position, otherwise it will pause it</param>
        public static int DOGoto(this AudioMixer target, float to, bool andPlay = false)
        {
            return DOTween.Goto(target, to, andPlay);
        }

        /// <summary>
        /// Pauses all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens paused.
        /// </summary>
        public static int DOPause(this AudioMixer target)
        {
            return DOTween.Pause(target);
        }

        /// <summary>
        /// Plays all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlay(this AudioMixer target)
        {
            return DOTween.Play(target);
        }

        /// <summary>
        /// Plays backwards all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlayBackwards(this AudioMixer target)
        {
            return DOTween.PlayBackwards(target);
        }

        /// <summary>
        /// Plays forward all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlayForward(this AudioMixer target)
        {
            return DOTween.PlayForward(target);
        }

        /// <summary>
        /// Restarts all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens restarted.
        /// </summary>
        public static int DORestart(this AudioMixer target)
        {
            return DOTween.Restart(target);
        }

        /// <summary>
        /// Rewinds all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens rewinded.
        /// </summary>
        public static int DORewind(this AudioMixer target)
        {
            return DOTween.Rewind(target);
        }

        /// <summary>
        /// Smoothly rewinds all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens rewinded.
        /// </summary>
        public static int DOSmoothRewind(this AudioMixer target)
        {
            return DOTween.SmoothRewind(target);
        }

        /// <summary>
        /// Toggles the paused state (plays if it was paused, pauses if it was playing) of all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens involved.
        /// </summary>
        public static int DOTogglePause(this AudioMixer target)
        {
            return DOTween.TogglePause(target);
        }

        #endregion

        #endregion

        #endregion
    }
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleEPOOutline.cs

```csharp
using UnityEngine;

#if false || EPO_DOTWEEN // MODULE_MARKER

using EPOOutline;
using DG.Tweening.Plugins.Options;
using DG.Tweening;
using DG.Tweening.Core;

namespace DG.Tweening
{
    public static class DOTweenModuleEPOOutline
    {
        public static int DOKill(this SerializedPass target, bool complete)
        {
            return DOTween.Kill(target, complete);
        }

        public static TweenerCore<float, float, FloatOptions> DOFloat(this SerializedPass target, string propertyName, float endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetFloat(propertyName), x => target.SetFloat(propertyName, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SerializedPass target, string propertyName, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.GetColor(propertyName), x => target.SetColor(propertyName, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SerializedPass target, string propertyName, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetColor(propertyName), x => target.SetColor(propertyName, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Vector4, Vector4, VectorOptions> DOVector(this SerializedPass target, string propertyName, Vector4 endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetVector(propertyName), x => target.SetVector(propertyName, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<float, float, FloatOptions> DOFloat(this SerializedPass target, int propertyId, float endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetFloat(propertyId), x => target.SetFloat(propertyId, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SerializedPass target, int propertyId, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.GetColor(propertyId), x => target.SetColor(propertyId, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SerializedPass target, int propertyId, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetColor(propertyId), x => target.SetColor(propertyId, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Vector4, Vector4, VectorOptions> DOVector(this SerializedPass target, int propertyId, Vector4 endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetVector(propertyId), x => target.SetVector(propertyId, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static int DOKill(this Outlinable.OutlineProperties target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        public static int DOKill(this Outliner target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        /// <summary>
        /// Controls the alpha (transparency) of the outline
        /// </summary>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Outlinable.OutlineProperties target, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.Color, x => target.Color = x, endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the color of the outline
        /// </summary>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Outlinable.OutlineProperties target, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.Color, x => target.Color = x, endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of blur applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOBlurShift(this Outlinable.OutlineProperties target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.BlurShift, x => target.BlurShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of blur applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOBlurShift(this Outliner target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.BlurShift, x => target.BlurShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of dilation applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DODilateShift(this Outlinable.OutlineProperties target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.DilateShift, x => target.DilateShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of dilation applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DODilateShift(this Outliner target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.DilateShift, x => target.DilateShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }
    }
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Core.Enums;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;
using UnityEngine;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModulePhysics
    {
        #region Shortcuts

        #region Rigidbody

        /// <summary>Tweens a Rigidbody's position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMove(this Rigidbody target, Vector3 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's X position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveX(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(endValue, 0, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's Y position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveY(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, endValue, 0), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's Z position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveZ(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, 0, endValue), duration);
            t.SetOptions(AxisConstraint.Z, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's rotation to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="mode">Rotation mode</param>
        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> DORotate(this Rigidbody target, Vector3 endValue, float duration, RotateMode mode = RotateMode.Fast)
        {
            TweenerCore<Quaternion, Vector3, QuaternionOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, endValue, duration);
            t.SetTarget(target);
            t.plugOptions.rotateMode = mode;
            return t;
        }

        /// <summary>Tweens a Rigidbody's rotation so that it will look towards the given position.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="towards">The position to look at</param><param name="duration">The duration of the tween</param>
        /// <param name="axisConstraint">Eventual axis constraint for the rotation</param>
        /// <param name="up">The vector that defines in which direction up is (default: Vector3.up)</param>
        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> DOLookAt(this Rigidbody target, Vector3 towards, float duration, AxisConstraint axisConstraint = AxisConstraint.None, Vector3? up = null)
        {
            TweenerCore<Quaternion, Vector3, QuaternionOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, towards, duration)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetLookAt);
            t.plugOptions.axisConstraint = axisConstraint;
            t.plugOptions.up = (up == null) ? Vector3.up : (Vector3)up;
            return t;
        }

        #region Special

        /// <summary>Tweens a Rigidbody's position to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJump(this Rigidbody target, Vector3 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, jumpPower, 0), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(() => startPosY = target.position.y);
            s.Append(DOTween.To(() => target.position, target.MovePosition, new Vector3(endValue.x, 0, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(DOTween.To(() => target.position, target.MovePosition, new Vector3(0, 0, endValue.z), duration)
                    .SetOptions(AxisConstraint.Z, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            yTween.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector3 pos = target.position;
                pos.y += DOVirtual.EasedValue(0, offsetY, yTween.ElapsedPercentage(), Ease.OutQuad);
                target.MovePosition(pos);
            });
            return s;
        }

        /// <summary>Tweens a Rigidbody's position through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations.
        /// <para>NOTE: to tween a rigidbody correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOPath.</para></summary>
        /// <param name="path">The waypoints to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path (useless in case of Linear paths): higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody target, Vector3[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, target.MovePosition, new Path(pathType, path, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        /// <summary>Tweens a Rigidbody's localPosition through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations
        /// <para>NOTE: to tween a rigidbody correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOLocalPath.</para></summary>
        /// <param name="path">The waypoint to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path: higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody target, Vector3[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), new Path(pathType, path, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }
        // Used by path editor when creating the actual tween, so it can pass a pre-compiled path
        internal static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, target.MovePosition, path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        internal static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics2D.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;
using UnityEngine;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModulePhysics2D
    {
        #region Shortcuts

        #region Rigidbody2D Shortcuts

        /// <summary>Tweens a Rigidbody2D's position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMove(this Rigidbody2D target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's X position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMoveX(this Rigidbody2D target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's Y position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMoveY(this Rigidbody2D target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's rotation to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DORotate(this Rigidbody2D target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #region Special

        /// <summary>Tweens a Rigidbody2D's position to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations.
        /// <para>IMPORTANT: a rigidbody2D can't be animated in a jump arc using MovePosition, so the tween will directly set the position</para></summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJump(this Rigidbody2D target, Vector2 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.position, x => target.position = x, new Vector2(0, jumpPower), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(() => startPosY = target.position.y);
            s.Append(DOTween.To(() => target.position, x => target.position = x, new Vector2(endValue.x, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            yTween.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector3 pos = target.position;
                pos.y += DOVirtual.EasedValue(0, offsetY, yTween.ElapsedPercentage(), Ease.OutQuad);
                target.MovePosition(pos);
            });
            return s;
        }

        /// <summary>Tweens a Rigidbody2D's position through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations.
        /// <para>NOTE: to tween a Rigidbody2D correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOPath.</para></summary>
        /// <param name="path">The waypoints to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path (useless in case of Linear paths): higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody2D target, Vector2[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            int len = path.Length;
            Vector3[] path3D = new Vector3[len];
            for (int i = 0; i < len; ++i) path3D[i] = path[i];
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, x => target.MovePosition(x), new Path(pathType, path3D, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        /// <summary>Tweens a Rigidbody2D's localPosition through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations
        /// <para>NOTE: to tween a Rigidbody2D correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOLocalPath.</para></summary>
        /// <param name="path">The waypoint to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path: higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody2D target, Vector2[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            int len = path.Length;
            Vector3[] path3D = new Vector3[len];
            for (int i = 0; i < len; ++i) path3D[i] = path[i];
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), new Path(pathType, path3D, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }
        // Used by path editor when creating the actual tween, so it can pass a pre-compiled path
        internal static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody2D target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, x => target.MovePosition(x), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        internal static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody2D target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleSprite.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleSprite
    {
        #region Shortcuts

        #region SpriteRenderer

        /// <summary>Tweens a SpriteRenderer's color to the given value.
        /// Also stores the spriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SpriteRenderer target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Material's alpha color to the given value.
        /// Also stores the spriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SpriteRenderer target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a SpriteRenderer's color using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this SpriteRenderer target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region Blendables

        #region SpriteRenderer

        /// <summary>Tweens a SpriteRenderer's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the SpriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this SpriteRenderer target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                    Color diff = x - to;
                    to = x;
                    target.color += diff;
                }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUI.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER

using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening.Core;
using DG.Tweening.Core.Enums;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Options;
using Outline = UnityEngine.UI.Outline;
using Text = UnityEngine.UI.Text;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleUI
    {
        #region Shortcuts

        #region CanvasGroup

        /// <summary>Tweens a CanvasGroup's alpha color to the given value.
        /// Also stores the canvasGroup as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFade(this CanvasGroup target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.alpha, x => target.alpha = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region Graphic

        /// <summary>Tweens an Graphic's color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Graphic target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Graphic's alpha color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Graphic target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region Image

        /// <summary>Tweens an Image's color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Image target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's alpha color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Image target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's fillAmount to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach (0 to 1)</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFillAmount(this Image target, float endValue, float duration)
        {
            if (endValue > 1) endValue = 1;
            else if (endValue < 0) endValue = 0;
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.fillAmount, x => target.fillAmount = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's colors using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Image target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region LayoutElement

        /// <summary>Tweens an LayoutElement's flexibleWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOFlexibleSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.flexibleWidth, target.flexibleHeight), x => {
                    target.flexibleWidth = x.x;
                    target.flexibleHeight = x.y;
                }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens an LayoutElement's minWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMinSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.minWidth, target.minHeight), x => {
                target.minWidth = x.x;
                target.minHeight = x.y;
            }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens an LayoutElement's preferredWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPreferredSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.preferredWidth, target.preferredHeight), x => {
                target.preferredWidth = x.x;
                target.preferredHeight = x.y;
            }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        #endregion

        #region Outline

        /// <summary>Tweens a Outline's effectColor to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Outline target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.effectColor, x => target.effectColor = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Outline's effectColor alpha to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Outline target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.effectColor, x => target.effectColor = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Outline's effectDistance to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOScale(this Outline target, Vector2 endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.effectDistance, x => target.effectDistance = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region RectTransform

        /// <summary>Tweens a RectTransform's anchoredPosition to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPos(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPosX(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPosY(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchoredPosition3D to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3D(this RectTransform target, Vector3 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DX(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(endValue, 0, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DY(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(0, endValue, 0), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D Z to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DZ(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(0, 0, endValue), duration);
            t.SetOptions(AxisConstraint.Z, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchorMax to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorMax(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchorMax, x => target.anchorMax = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchorMin to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorMin(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchorMin, x => target.anchorMin = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's pivot to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivot(this RectTransform target, Vector2 endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's pivot X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivotX(this RectTransform target, float endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's pivot Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivotY(this RectTransform target, float endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's sizeDelta to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOSizeDelta(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.sizeDelta, x => target.sizeDelta = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Punches a RectTransform's anchoredPosition towards the given direction and then back to the starting one
        /// as if it was connected to the starting position via an elastic.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="punch">The direction and strength of the punch (added to the RectTransform's current position)</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="vibrato">Indicates how much will the punch vibrate</param>
        /// <param name="elasticity">Represents how much (0 to 1) the vector will go beyond the starting position when bouncing backwards.
        /// 1 creates a full oscillation between the punch direction and the opposite direction,
        /// while 0 oscillates only between the punch and the start position</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOPunchAnchorPos(this RectTransform target, Vector2 punch, float duration, int vibrato = 10, float elasticity = 1, bool snapping = false)
        {
            return DOTween.Punch(() => target.anchoredPosition, x => target.anchoredPosition = x, punch, duration, vibrato, elasticity)
                .SetTarget(target).SetOptions(snapping);
        }

        /// <summary>Shakes a RectTransform's anchoredPosition with the given values.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="strength">The shake strength</param>
        /// <param name="vibrato">Indicates how much will the shake vibrate</param>
        /// <param name="randomness">Indicates how much the shake will be random (0 to 180 - values higher than 90 kind of suck, so beware). 
        /// Setting it to 0 will shake along a single direction.</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        /// <param name="fadeOut">If TRUE the shake will automatically fadeOut smoothly within the tween's duration, otherwise it will not</param>
        /// <param name="randomnessMode">Randomness mode</param>
        public static Tweener DOShakeAnchorPos(this RectTransform target, float duration, float strength = 100, int vibrato = 10, float randomness = 90, bool snapping = false, bool fadeOut = true, ShakeRandomnessMode randomnessMode = ShakeRandomnessMode.Full)
        {
            return DOTween.Shake(() => target.anchoredPosition, x => target.anchoredPosition = x, duration, strength, vibrato, randomness, true, fadeOut, randomnessMode)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetShake).SetOptions(snapping);
        }
        /// <summary>Shakes a RectTransform's anchoredPosition with the given values.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="strength">The shake strength on each axis</param>
        /// <param name="vibrato">Indicates how much will the shake vibrate</param>
        /// <param name="randomness">Indicates how much the shake will be random (0 to 180 - values higher than 90 kind of suck, so beware). 
        /// Setting it to 0 will shake along a single direction.</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        /// <param name="fadeOut">If TRUE the shake will automatically fadeOut smoothly within the tween's duration, otherwise it will not</param>
        /// <param name="randomnessMode">Randomness mode</param>
        public static Tweener DOShakeAnchorPos(this RectTransform target, float duration, Vector2 strength, int vibrato = 10, float randomness = 90, bool snapping = false, bool fadeOut = true, ShakeRandomnessMode randomnessMode = ShakeRandomnessMode.Full)
        {
            return DOTween.Shake(() => target.anchoredPosition, x => target.anchoredPosition = x, duration, strength, vibrato, randomness, fadeOut, randomnessMode)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetShake).SetOptions(snapping);
        }

        #region Special

        /// <summary>Tweens a RectTransform's anchoredPosition to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJumpAnchorPos(this RectTransform target, Vector2 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;

            // Separate Y Tween so we can elaborate elapsedPercentage on that insted of on the Sequence
            // (in case users add a delay or other elements to the Sequence)
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(0, jumpPower), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(()=> startPosY = target.anchoredPosition.y);
            s.Append(DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(endValue.x, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            s.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector2 pos = target.anchoredPosition;
                pos.y += DOVirtual.EasedValue(0, offsetY, s.ElapsedDirectionalPercentage(), Ease.OutQuad);
                target.anchoredPosition = pos;
            });
            return s;
        }

        #endregion

        #endregion

        #region ScrollRect

        /// <summary>Tweens a ScrollRect's horizontal/verticalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DONormalizedPos(this ScrollRect target, Vector2 endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => new Vector2(target.horizontalNormalizedPosition, target.verticalNormalizedPosition),
                x => {
                    target.horizontalNormalizedPosition = x.x;
                    target.verticalNormalizedPosition = x.y;
                }, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }
        /// <summary>Tweens a ScrollRect's horizontalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOHorizontalNormalizedPos(this ScrollRect target, float endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => target.horizontalNormalizedPosition, x => target.horizontalNormalizedPosition = x, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }
        /// <summary>Tweens a ScrollRect's verticalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOVerticalNormalizedPos(this ScrollRect target, float endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => target.verticalNormalizedPosition, x => target.verticalNormalizedPosition = x, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }

        #endregion

        #region Slider

        /// <summary>Tweens a Slider's value to the given value.
        /// Also stores the Slider as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<float, float, FloatOptions> DOValue(this Slider target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.value, x => target.value = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        #endregion

        #region Text

        /// <summary>Tweens a Text's color to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Text target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>
        /// Tweens a Text's text from one integer to another, with options for thousands separators
        /// </summary>
        /// <param name="fromValue">The value to start from</param>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="addThousandsSeparator">If TRUE (default) also adds thousands separators</param>
        /// <param name="culture">The <see cref="CultureInfo"/> to use (InvariantCulture if NULL)</param>
        public static TweenerCore<int, int, NoOptions> DOCounter(
            this Text target, int fromValue, int endValue, float duration, bool addThousandsSeparator = true, CultureInfo culture = null
        ){
            int v = fromValue;
            CultureInfo cInfo = !addThousandsSeparator ? null : culture ?? CultureInfo.InvariantCulture;
            TweenerCore<int, int, NoOptions> t = DOTween.To(() => v, x => {
                v = x;
                target.text = addThousandsSeparator
                    ? v.ToString("N0", cInfo)
                    : v.ToString();
            }, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Text's alpha color to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Text target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Text's text to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end string to tween to</param><param name="duration">The duration of the tween</param>
        /// <param name="richTextEnabled">If TRUE (default), rich text will be interpreted correctly while animated,
        /// otherwise all tags will be considered as normal text</param>
        /// <param name="scrambleMode">The type of scramble mode to use, if any</param>
        /// <param name="scrambleChars">A string containing the characters to use for scrambling.
        /// Use as many characters as possible (minimum 10) because DOTween uses a fast scramble mode which gives better results with more characters.
        /// Leave it to NULL (default) to use default ones</param>
        public static TweenerCore<string, string, StringOptions> DOText(this Text target, string endValue, float duration, bool richTextEnabled = true, ScrambleMode scrambleMode = ScrambleMode.None, string scrambleChars = null)
        {
            if (endValue == null) {
                if (Debugger.logPriority > 0) Debugger.LogWarning("You can't pass a NULL string to DOText: an empty string will be used instead to avoid errors");
                endValue = "";
            }
            TweenerCore<string, string, StringOptions> t = DOTween.To(() => target.text, x => target.text = x, endValue, duration);
            t.SetOptions(richTextEnabled, scrambleMode, scrambleChars)
                .SetTarget(target);
            return t;
        }

        #endregion

        #region Blendables

        #region Graphic

        /// <summary>Tweens a Graphic's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Graphic as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Graphic target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #region Image

        /// <summary>Tweens a Image's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Image target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #region Text

        /// <summary>Tweens a Text's color BY the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Text target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #endregion

        #region Shapes

        /// <summary>Tweens a RectTransform's anchoredPosition so that it draws a circle around the given center.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations.<para/>
        /// IMPORTANT: SetFrom(value) requires a <see cref="Vector2"/> instead of a float, where the X property represents the "from degrees value"</summary>
        /// <param name="center">Circle-center/pivot around which to rotate (in UI anchoredPosition coordinates)</param>
        /// <param name="endValueDegrees">The end value degrees to reach (to rotate counter-clockwise pass a negative value)</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="relativeCenter">If TRUE the <see cref="center"/> coordinates will be considered as relative to the target's current anchoredPosition</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, CircleOptions> DOShapeCircle(
            this RectTransform target, Vector2 center, float endValueDegrees, float duration, bool relativeCenter = false, bool snapping = false
        )
        {
            TweenerCore<Vector2, Vector2, CircleOptions> t = DOTween.To(
                CirclePlugin.Get(), () => target.anchoredPosition, x => target.anchoredPosition = x, center, duration
            );
            t.SetOptions(endValueDegrees, relativeCenter, snapping).SetTarget(target);
            return t;
        }

        #endregion

        #endregion

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        public static class Utils
        {
            /// <summary>
            /// Converts the anchoredPosition of the first RectTransform to the second RectTransform,
            /// taking into consideration offset, anchors and pivot, and returns the new anchoredPosition
            /// </summary>
            public static Vector2 SwitchToRectTransform(RectTransform from, RectTransform to)
            {
                Vector2 localPoint;
                Vector2 fromPivotDerivedOffset = new Vector2(from.rect.width * 0.5f + from.rect.xMin, from.rect.height * 0.5f + from.rect.yMin);
                Vector2 screenP = RectTransformUtility.WorldToScreenPoint(null, from.position);
                screenP += fromPivotDerivedOffset;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(to, screenP, null, out localPoint);
                Vector2 pivotDerivedOffset = new Vector2(to.rect.width * 0.5f + to.rect.xMin, to.rect.height * 0.5f + to.rect.yMin);
                return to.anchoredPosition + localPoint - pivotDerivedOffset;
            }
        }
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUnityVersion.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

using System;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
//#if UNITY_2018_1_OR_NEWER && (NET_4_6 || NET_STANDARD_2_0)
//using Task = System.Threading.Tasks.Task;
//#endif

#pragma warning disable 1591
namespace DG.Tweening
{
    /// <summary>
    /// Shortcuts/functions that are not strictly related to specific Modules
    /// but are available only on some Unity versions
    /// </summary>
	public static class DOTweenModuleUnityVersion
    {
        #region Material

        /// <summary>Tweens a Material's color using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Material target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }
        /// <summary>Tweens a Material's named color property using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param>
        /// <param name="property">The name of the material property to tween (like _Tint or _SpecColor)</param>
        /// <param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Material target, Gradient gradient, string property, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.SetColor(property, c.color);
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, property, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region CustomYieldInstructions

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or complete.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForCompletion(true);</code>
        /// </summary>
        public static CustomYieldInstruction WaitForCompletion(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForCompletion(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or rewinded.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForRewind();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForRewind(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForRewind(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForKill();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForKill(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForKill(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or has gone through the given amount of loops.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForElapsedLoops(2);</code>
        /// </summary>
        /// <param name="elapsedLoops">Elapsed loops to wait for</param>
        public static CustomYieldInstruction WaitForElapsedLoops(this Tween t, int elapsedLoops, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForElapsedLoops(t, elapsedLoops);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed
        /// or has reached the given time position (loops included, delays excluded).
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForPosition(2.5f);</code>
        /// </summary>
        /// <param name="position">Position (loops included, delays excluded) to wait for</param>
        public static CustomYieldInstruction WaitForPosition(this Tween t, float position, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForPosition(t, position);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or started
        /// (meaning when the tween is set in a playing state the first time, after any eventual delay).
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForStart();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForStart(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForStart(t);
        }

        #endregion

#if UNITY_2018_1_OR_NEWER
        #region Unity 2018.1 or Newer

        #region Material

        /// <summary>Tweens a Material's named texture offset property with the given ID to the given value.
        /// Also stores the material as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="propertyID">The ID of the material property to tween (also called nameID in Unity's manual)</param>
        /// <param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOOffset(this Material target, Vector2 endValue, int propertyID, float duration)
        {
            if (!target.HasProperty(propertyID)) {
                if (Debugger.logPriority > 0) Debugger.LogMissingMaterialProperty(propertyID);
                return null;
            }
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.GetTextureOffset(propertyID), x => target.SetTextureOffset(propertyID, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Material's named texture scale property with the given ID to the given value.
        /// Also stores the material as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="propertyID">The ID of the material property to tween (also called nameID in Unity's manual)</param>
        /// <param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOTiling(this Material target, Vector2 endValue, int propertyID, float duration)
        {
            if (!target.HasProperty(propertyID)) {
                if (Debugger.logPriority > 0) Debugger.LogMissingMaterialProperty(propertyID);
                return null;
            }
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.GetTextureScale(propertyID), x => target.SetTextureScale(propertyID, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region .NET 4.6 or Newer

#if UNITY_2018_1_OR_NEWER && (NET_4_6 || NET_STANDARD_2_0)

        #region Async Instructions

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or complete.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.WaitForCompletion();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForCompletion(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && !t.IsComplete()) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or rewinded.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForRewind();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForRewind(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && (!t.playedOnce || t.position * (t.CompletedLoops() + 1) > 0)) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForKill();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForKill(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or has gone through the given amount of loops.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForElapsedLoops();</code>
        /// </summary>
        /// <param name="elapsedLoops">Elapsed loops to wait for</param>
        public static async System.Threading.Tasks.Task AsyncWaitForElapsedLoops(this Tween t, int elapsedLoops)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && t.CompletedLoops() < elapsedLoops) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or started
        /// (meaning when the tween is set in a playing state the first time, after any eventual delay).
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForPosition();</code>
        /// </summary>
        /// <param name="position">Position (loops included, delays excluded) to wait for</param>
        public static async System.Threading.Tasks.Task AsyncWaitForPosition(this Tween t, float position)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && t.position * (t.CompletedLoops() + 1) < position) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForKill();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForStart(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && !t.playedOnce) await System.Threading.Tasks.Task.Yield();
        }

        #endregion
#endif

        #endregion

        #endregion
#endif
    }

    // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
    // ███ CLASSES █████████████████████████████████████████████████████████████████████████████████████████████████████████
    // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

    public static class DOTweenCYInstruction
    {
        public class WaitForCompletion : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && !t.IsComplete();
            }}
            readonly Tween t;
            public WaitForCompletion(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForRewind : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && (!t.playedOnce || t.position * (t.CompletedLoops() + 1) > 0);
            }}
            readonly Tween t;
            public WaitForRewind(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForKill : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active;
            }}
            readonly Tween t;
            public WaitForKill(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForElapsedLoops : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && t.CompletedLoops() < elapsedLoops;
            }}
            readonly Tween t;
            readonly int elapsedLoops;
            public WaitForElapsedLoops(Tween tween, int elapsedLoops)
            {
                t = tween;
                this.elapsedLoops = elapsedLoops;
            }
        }

        public class WaitForPosition : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && t.position * (t.CompletedLoops() + 1) < position;
            }}
            readonly Tween t;
            readonly float position;
            public WaitForPosition(Tween tween, float position)
            {
                t = tween;
                this.position = position;
            }
        }

        public class WaitForStart : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && !t.playedOnce;
            }}
            readonly Tween t;
            public WaitForStart(Tween tween)
            {
                t = tween;
            }
        }
    }
}

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUtils.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

using System;
using System.Reflection;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;

#pragma warning disable 1591
namespace DG.Tweening
{
    /// <summary>
    /// Utility functions that deal with available Modules.
    /// Modules defines:
    /// - DOTAUDIO
    /// - DOTPHYSICS
    /// - DOTPHYSICS2D
    /// - DOTSPRITE
    /// - DOTUI
    /// Extra defines set and used for implementation of external assets:
    /// - DOTWEEN_TMP ► TextMesh Pro
    /// - DOTWEEN_TK2D ► 2D Toolkit
    /// </summary>
	public static class DOTweenModuleUtils
    {
        static bool _initialized;

        #region Reflection

        /// <summary>
        /// Called via Reflection by DOTweenComponent on Awake
        /// </summary>
#if UNITY_2018_1_OR_NEWER
        [UnityEngine.Scripting.Preserve]
#endif
        public static void Init()
        {
            if (_initialized) return;

            _initialized = true;
            DOTweenExternalCommand.SetOrientationOnPath += Physics.SetOrientationOnPath;

#if UNITY_EDITOR
#if UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_5 || UNITY_2017_1
            UnityEditor.EditorApplication.playmodeStateChanged += PlaymodeStateChanged;
#else
            UnityEditor.EditorApplication.playModeStateChanged += PlaymodeStateChanged;
#endif
#endif
        }

#if UNITY_2018_1_OR_NEWER
#pragma warning disable
        [UnityEngine.Scripting.Preserve]
        // Just used to preserve methods when building, never called
        static void Preserver()
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            MethodInfo mi = typeof(MonoBehaviour).GetMethod("Stub");
        }
#pragma warning restore
#endif

        #endregion

#if UNITY_EDITOR
        // Fires OnApplicationPause in DOTweenComponent even when Editor is paused (otherwise it's only fired at runtime)
#if UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_5 || UNITY_2017_1
        static void PlaymodeStateChanged()
        #else
        static void PlaymodeStateChanged(UnityEditor.PlayModeStateChange state)
#endif
        {
            if (DOTween.instance == null) return;
            DOTween.instance.OnApplicationPause(UnityEditor.EditorApplication.isPaused);
        }
#endif

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        public static class Physics
        {
            // Called via DOTweenExternalCommand callback
            public static void SetOrientationOnPath(PathOptions options, Tween t, Quaternion newRot, Transform trans)
            {
#if true // PHYSICS_MARKER
                if (options.isRigidbody) ((Rigidbody)t.target).rotation = newRot;
                else trans.rotation = newRot;
#else
                trans.rotation = newRot;
#endif
            }

            // Returns FALSE if the DOTween's Physics2D Module is disabled, or if there's no Rigidbody2D attached
            public static bool HasRigidbody2D(Component target)
            {
#if true // PHYSICS2D_MARKER
                return target.GetComponent<Rigidbody2D>() != null;
#else
                return false;
#endif
            }

            #region Called via Reflection


            // Called via Reflection by DOTweenPathInspector
            // Returns FALSE if the DOTween's Physics Module is disabled, or if there's no rigidbody attached
#if UNITY_2018_1_OR_NEWER
            [UnityEngine.Scripting.Preserve]
#endif
            public static bool HasRigidbody(Component target)
            {
#if true // PHYSICS_MARKER
                return target.GetComponent<Rigidbody>() != null;
#else
                return false;
#endif
            }

            // Called via Reflection by DOTweenPath
#if UNITY_2018_1_OR_NEWER
            [UnityEngine.Scripting.Preserve]
#endif
            public static TweenerCore<Vector3, Path, PathOptions> CreateDOTweenPathTween(
                MonoBehaviour target, bool tweenRigidbody, bool isLocal, Path path, float duration, PathMode pathMode
            ){
                TweenerCore<Vector3, Path, PathOptions> t = null;
                bool rBodyFoundAndTweened = false;
#if true // PHYSICS_MARKER
                if (tweenRigidbody) {
                    Rigidbody rBody = target.GetComponent<Rigidbody>();
                    if (rBody != null) {
                        rBodyFoundAndTweened = true;
                        t = isLocal
                            ? rBody.DOLocalPath(path, duration, pathMode)
                            : rBody.DOPath(path, duration, pathMode);
                    }
                }
#endif
#if true // PHYSICS2D_MARKER
                if (!rBodyFoundAndTweened && tweenRigidbody) {
                    Rigidbody2D rBody2D = target.GetComponent<Rigidbody2D>();
                    if (rBody2D != null) {
                        rBodyFoundAndTweened = true;
                        t = isLocal
                            ? rBody2D.DOLocalPath(path, duration, pathMode)
                            : rBody2D.DOPath(path, duration, pathMode);
                    }
                }
#endif
                if (!rBodyFoundAndTweened) {
                    t = isLocal
                        ? target.transform.DOLocalPath(path, duration, pathMode)
                        : target.transform.DOPath(path, duration, pathMode);
                }
                return t;
            }

            #endregion
        }
    }
}

```

## Assets/PowerupPickupTween.cs

```csharp
using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PowerupPickupTween : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Visual root to animate. Defaults to self if null.")]
    public Transform model;

    [Header("Update")]
    [Tooltip("DOTween update type.")]
    public UpdateType updateType = UpdateType.Normal;
    [Tooltip("Ignore Time.timeScale (use unscaled time).")]
    public bool independentUpdate = true;

    [Header("Spawn")]
    public float spawnScaleFrom = 0.0f;
    public float spawnScaleTo = 1.0f;
    public float spawnDuration = 0.35f;
    public Ease spawnEase = Ease.OutBack;

    [Header("Spawn Scatter (world move on XZ)")]
    public bool scatterOnSpawn = true;
    [Tooltip("Random distance on XZ.")]
    public Vector2 scatterDistanceRange = new Vector2(0.6f, 2.2f);
    [Tooltip("Seconds to reach the scatter target.")]
    public float scatterDuration = 0.45f;
    public Ease scatterEase = Ease.OutQuart;
    [Tooltip("Add a small vertical arc while moving.")]
    public bool useJumpArc = true;
    [Tooltip("Vertical jump height (Y).")]
    public float jumpPower = 0.6f;

    [Tooltip("Optional preferred XZ direction (normalized). If zero, a random direction is used.")]
    public Vector3 initialDirHint; // set by spawner if you want bias; XZ used, Y ignored

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverHalfCycle = 0.6f;
    public Ease hoverEase = Ease.InOutSine;

    [Header("Rotate")]
    public float rotateSpeedDegPerSec = 90f;

    [Header("Collect Punch")]
    public float collectPunchScale = 0.25f;
    public float collectDuration = 0.18f;
    public Ease collectEase = Ease.OutBack;

    private Vector3 _baseLocalPos;
    private Tweener _hoverTw;
    private Tweener _rotateTw;
    private Tweener _spawnTw;
    private Tween _scatterTw; // unified to Tween for both DOJump/DOMove

    // Clamps invalid inspector values and normalizes ranges.
    void OnValidate()
    {
        spawnDuration = Mathf.Max(0.01f, spawnDuration);
        scatterDuration = Mathf.Max(0.01f, scatterDuration);
        collectDuration = Mathf.Max(0.05f, collectDuration);
        hoverHalfCycle = Mathf.Max(0.05f, hoverHalfCycle);
        jumpPower = Mathf.Max(0f, jumpPower);

        if (scatterDistanceRange.y < scatterDistanceRange.x)
            scatterDistanceRange = new Vector2(scatterDistanceRange.y, scatterDistanceRange.x);
    }

    // Caches default model and base local position.
    void Awake()
    {
        if (!model) model = transform;
        _baseLocalPos = model.localPosition;
    }

    // Kills all tweens and restores default local transform on disable.
    void OnDisable()
    {
        KillAllTweens();
        if (model)
        {
            model.localPosition = _baseLocalPos;
            model.localScale = Vector3.one;
        }
    }

    // Plays the spawn scale pop, small lift, optional scatter, and starts idle on complete.
    public void PlaySpawn()
    {
        if (!model) return;
        KillAllTweens();

        _baseLocalPos = model.localPosition;
        model.localScale = Vector3.one * Mathf.Max(0f, spawnScaleFrom);

        _spawnTw = model
            .DOScale(spawnScaleTo, spawnDuration)
            .SetEase(spawnEase)
            .SetUpdate(updateType, independentUpdate)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
            .OnComplete(StartIdle);

        float lift = Mathf.Abs(hoverAmplitude) * 0.5f;
        model.DOLocalMoveY(_baseLocalPos.y + lift, spawnDuration * 0.5f)
             .SetLoops(2, LoopType.Yoyo)
             .SetEase(Ease.OutSine)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

        if (scatterOnSpawn)
            StartScatter();
    }

    // Plays a quick punch scale when collected.
    public void PlayCollect()
    {
        if (!model) return;

        model.DOPunchScale(Vector3.one * collectPunchScale,
                           collectDuration, vibrato: 1, elasticity: 0.5f)
             .SetEase(collectEase)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    // Returns the punch animation duration used by collectors to time despawn.
    public float GetCollectDuration() => Mathf.Max(0.05f, collectDuration);

    // Starts idle hover and continuous Y rotation.
    private void StartIdle()
    {
        if (!model) return;

        if (Mathf.Abs(hoverAmplitude) > 0.0001f)
        {
            _hoverTw = model.DOLocalMoveY(_baseLocalPos.y + hoverAmplitude, hoverHalfCycle)
                           .SetEase(hoverEase)
                           .SetLoops(-1, LoopType.Yoyo)
                           .SetUpdate(updateType, independentUpdate)
                           .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        if (Mathf.Abs(rotateSpeedDegPerSec) > 0.01f)
        {
            float oneTurnTime = 360f / Mathf.Abs(rotateSpeedDegPerSec);
            _rotateTw = model.DOLocalRotate(
                            new Vector3(0f, Mathf.Sign(rotateSpeedDegPerSec) * 360f, 0f),
                            oneTurnTime, RotateMode.LocalAxisAdd)
                        .SetEase(Ease.Linear)
                        .SetLoops(-1, LoopType.Incremental)
                        .SetUpdate(updateType, independentUpdate)
                        .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }
    }

    // Performs an outward world-space scatter along XZ (jump arc optional).
    private void StartScatter()
    {
        var tr = transform;
        Vector3 start = tr.position;

        Vector3 dirXZ = initialDirHint;
        dirXZ.y = 0f;
        if (dirXZ.sqrMagnitude < 0.0001f)
        {
            float yaw = Random.Range(0f, 360f);
            float rad = yaw * Mathf.Deg2Rad;
            dirXZ = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        }
        dirXZ.Normalize();

        float dist = Random.Range(scatterDistanceRange.x, scatterDistanceRange.y);
        Vector3 target = start + dirXZ * dist;

        if (useJumpArc)
        {
            _scatterTw = tr.DOJump(target, jumpPower, 1, scatterDuration)
                           .SetEase(scatterEase)
                           .SetUpdate(updateType, independentUpdate)
                           .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }
        else
        {
            _scatterTw = tr.DOMove(target, scatterDuration)
                           .SetEase(scatterEase)
                           .SetUpdate(updateType, independentUpdate)
                           .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }
    }

    // Kills all active tweens and clears references.
    private void KillAllTweens()
    {
        _spawnTw?.Kill();
        _hoverTw?.Kill();
        _rotateTw?.Kill();
        _scatterTw?.Kill();
        _spawnTw = _hoverTw = _rotateTw = null;
        _scatterTw = null;
    }
}
```

## Assets/Scripts/Assassin.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Assassin : BaseCharacter
{

    protected float hpLvlIncrease = 5.78f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.7f;

    protected float endLVLinc = 2.04f;
    protected float strLVLinc = 3.44f;
    protected float agiLVLinc = 4.88f;
    protected float witLVLinc = 1.54f;
    protected float chaLVLinc = 3.67f;

    public Assassin(string name, int level)
        : base(name, "Assassin", level, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Assassin()
        : base("", "Assassin", 5, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);

    }

}

```

## Assets/Scripts/Ball.cs

```csharp
using UnityEngine;

/// Pinball ball: damage bookkeeping + stable launch/impulse helpers.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class Ball : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private float baseDamage = 5f;

    // Adds flat damage for N bounces (counted down on impact).
    private int flatBonusDamage;
    // Persistent damage multiplier (stacking additively like 1.0 + x + y).
    private float damageMultiplier = 1f;
    // Active temporary bounce-window multiplier.
    private float tempDamageMultiplier = 1f;
    // Queued temporary multiplier that activates after its countdown finishes.
    private float tempDamageMultiplierStore = 1f;
    // Active counters for the flat-bonus window.
    private int bonusBouncesNeeded, bonusBouncesRemaining;
    // Counters for the queued temp-mult window.
    private int tmpBonusBouncesNeeded, tmpBonusBouncesRemaining;

    private const float DAMAGE_BASELINE = 5f;

    [Header("Launch / Push Tuning")]
    [Tooltip("Impulse per unit mass (scaled by gravity magnitude) when fully charged.")]
    [SerializeField] private float fullChargeImpulsePerMass = 9.5f;
    [Tooltip("0..1 vertical bias applied to launch to help clear the trough.")]
    [SerializeField, Range(0f, 1f)] private float upBias = 0.15f;
    [Tooltip("Clamp to ensure the ball always leaves the launcher with at least this speed.")]
    [SerializeField] private float minLaunchSpeed = 6.0f;

    [Header("Anti-Stick")]
    [Tooltip("If speed falls below this value for longer than StickTimeout, kick the ball.")]
    [SerializeField] private float lowSpeedThreshold = 0.35f;
    [Tooltip("Seconds at low speed before we kick the ball.")]
    [SerializeField] private float lowSpeedTimeout = 0.9f;

    private Rigidbody rb;
    private float lowSpeedTimer;

    // Returns/sets the base damage (never negative).
    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    // Computes the current damage considering flat and multiplier windows.
    public float CurrentDamage =>
        Mathf.Max(0f, ((baseDamage + flatBonusDamage) * damageMultiplier) * tempDamageMultiplier);

    // Returns the ratio vs a fixed baseline to scale score/XP.
    public float ScoreXpDamageFactor =>
        Mathf.Max(0f, DAMAGE_BASELINE > 0f ? (CurrentDamage / DAMAGE_BASELINE) : 1f);

    // Cache rigidbody and reset state on enable.
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Ticks anti-stick monitor; applies a gentle directional nudge if stalled.
    private void Update()
    {
        if (rb == null) return;
        float speed = rb.velocity.magnitude;
        if (speed < lowSpeedThreshold)
        {
            lowSpeedTimer += Time.unscaledDeltaTime;
            if (lowSpeedTimer >= lowSpeedTimeout)
            {
                KickFromStall(Mathf.Max(minLaunchSpeed * 0.6f, 3.5f));
                lowSpeedTimer = 0f;
            }
        }
        else
        {
            lowSpeedTimer = 0f;
        }
    }

    // Adds a persistent damage multiplier (1.0 => no change).
    public void AddDamageMultiplier(float multiplier)
    {
        if (Mathf.Approximately(multiplier, 1f)) return;
        damageMultiplier += multiplier;
    }

    // Adds a flat damage bonus for a number of bounces (overwrites previous).
    public void AddFlatDamage(int flatDamage, int bounces)
    {
        if (flatDamage == 0 || bounces <= 0) return;
        flatBonusDamage = flatDamage;
        bonusBouncesNeeded = bounces;
        bonusBouncesRemaining = bounces;
    }

    // Queues a temporary multiplier that will flip on after its countdown.
    public void AddTempDamageMultiplier(float multiplier, int bounces)
    {
        if (bounces <= 0) return;
        tempDamageMultiplierStore = 1f + multiplier;
        tmpBonusBouncesNeeded = bounces;
        tmpBonusBouncesRemaining = bounces;
    }

    // Consumes one bounce across active windows; flips temp-mult when due.
    public void OnBounceConsumed()
    {
        if (bonusBouncesRemaining > 0 && --bonusBouncesRemaining == 0)
        {
            flatBonusDamage = 0;
        }

        if (tmpBonusBouncesRemaining > 0 && --tmpBonusBouncesRemaining == 0)
        {
            tempDamageMultiplier = tempDamageMultiplierStore;
            // start a 1-bounce window then clear on next consume
            tmpBonusBouncesNeeded = 0;
        }
        else if (tmpBonusBouncesRemaining == 0 && !Mathf.Approximately(tempDamageMultiplier, 1f))
        {
            // window elapsed � clear temp multiplier
            tempDamageMultiplier = 1f;
        }
    }

    // Applies a gravity/mass-agnostic impulse based on a [0..1] charge value.
    public void ApplyChargedLaunch(float normalizedCharge)
    {
        normalizedCharge = Mathf.Clamp01(normalizedCharge);
        if (rb == null) return;

        float g = Physics.gravity.magnitude;            // robust to gravity changes
        float mass = Mathf.Max(0.001f, rb.mass);
        float impulse = fullChargeImpulsePerMass * mass * (g / 9.81f) * normalizedCharge;

        // forward along the lane with a small up-bias to avoid re-contact
        Vector3 dir = (transform.forward + Vector3.up * upBias).normalized;
        rb.AddForce(dir * impulse, ForceMode.Impulse);

        // ensure a minimum ejection speed for consistency
        if (rb.velocity.magnitude < minLaunchSpeed)
        {
            rb.velocity = dir * minLaunchSpeed;
        }
    }

    // Applies a small corrective nudge when the ball stalls.
    public void KickFromStall(float speed)
    {
        Vector3 baseDir = rb.velocity.sqrMagnitude > 0.01f ? rb.velocity.normalized : Vector3.forward;
        baseDir.y = 0f;

        float deflect = Random.Range(120f, 160f);
        Vector3 newDir = (Quaternion.AngleAxis(deflect, Vector3.up) * baseDir).normalized;

        rb.velocity = newDir * speed;
    }

    // Applies full elemental payload on paddle hit, then applies damage effect.
    public void OnPaddleHit(PaddleEffectData effect)
    {
        var elem = GetComponent<BallElementalState>();
        if (elem != null)
        {
            switch (effect.Element)
            {
                case PaddleState.Fire:
                    elem.SetFireState(effect.FireBonusDamage, effect.FireBurnDamage, effect.FireBurnDuration,
                                      effect.FireBounceDuration, effect.FireCanExplode, effect.FireExplosionSize,
                                      effect.FireExplosionDamageFlat, effect.FireIsCursed);
                    break;
                case PaddleState.Water:
                    elem.SetWaterState(effect.WaterBonusXP, effect.WaterDamageFlat, effect.WaterDrenchDuration,
                                       effect.WaterBounceDuration, effect.WaterCanBurst, effect.WaterBurstSize,
                                       effect.WaterBurstDamageFlat, effect.WaterIsCursed);
                    break;
                case PaddleState.Earth:
                    elem.SetEarthState(effect.EarthBonusDamage, effect.EarthFissureDuration, effect.EarthXPBonus,
                                       effect.EarthScoreBonus, effect.EarthBounceDuration, effect.EarthIsCursed);
                    break;
                case PaddleState.Electric:
                    elem.SetElectricState(effect.ElectricShockDamage, effect.ElectricChainCount, effect.ElectricXPBonus,
                                          effect.ElectricScoreBonus, effect.ElectricBounceDuration, effect.ElectricIsCursed);
                    break;
            }
        }

        ApplyPaddleDamageEffect(effect);
    }

    // Forward paddle effect into this ball as flat/mult bonuses with bounce windows.
    public void ApplyPaddleDamageEffect(PaddleEffectData effect)
    {
        int flat = 0;
        int bounces = 0;

        switch (effect.Element)
        {
            case PaddleState.Fire:
                flat = effect.FireBonusDamage;
                bounces = effect.FireBounceDuration;
                break;
            case PaddleState.Water:
                flat = effect.WaterDamageFlat;
                bounces = effect.WaterBounceDuration;
                break;
            case PaddleState.Earth:
                flat = 0;
                bounces = effect.EarthBounceDuration;
                break;
            case PaddleState.Electric:
                flat = 0;
                bounces = effect.ElectricBounceDuration;
                break;
            default:
                break;
        }

        AddFlatDamage(flat, bounces);
    }

    public void EnsureUniquePhysicMaterial()
    {
        if (col == null) col = GetComponent<Collider>();
        if (col == null) return;
        if (runtimePhysMat != null) return;
        var src = col.material;

        if (src != null)
        {
            runtimePhysMat = Instantiate(src);
            runtimePhysMat.name = src.name + " (Runtime)";
        }
        else
        {
            runtimePhysMat = new PhysicMaterial("RuntimePhysMat");
        }

        col.material = runtimePhysMat;
    }

    public void AdjustBounciness(float factor)
    {
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            col.material.bounciness *= factor;
        }
    }

    // Multiplies XP forcefield radius (persistently until counter-multiplied).
    public void UpdateForcefield(float amount)
    {
        forceFieldRadius *= amount;
        forceField.endRange = forceFieldRadius;
    }
}

```

## Assets/Scripts/BallElementalState.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallElementalState : MonoBehaviour
{
    [SerializeField]
    private ElementalState initialState = ElementalState.None;

    // Pinball reference (reserved for future use/FX routing).
    private Pinball pinball;

    public ElementalState CurrentState = ElementalState.None;
    private Ball ball;
    private float originalMaxSpeed;

    // Element-combination lookup
    private static readonly Dictionary<(ElementalState, ElementalState), ElementalState> combinations =
        new()
        {
            {(ElementalState.Fire, ElementalState.Water), ElementalState.Steam},
            {(ElementalState.Water, ElementalState.Fire), ElementalState.Steam},
            {(ElementalState.Fire, ElementalState.Earth), ElementalState.Magma},
            {(ElementalState.Earth, ElementalState.Fire), ElementalState.Magma},
            {(ElementalState.Fire, ElementalState.Air), ElementalState.Wildfire},
            {(ElementalState.Air, ElementalState.Fire), ElementalState.Wildfire},
            {(ElementalState.Water, ElementalState.Earth), ElementalState.Sludge},
            {(ElementalState.Earth, ElementalState.Water), ElementalState.Sludge},
            {(ElementalState.Water, ElementalState.Air), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Water), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Earth), ElementalState.Whirlwind},
            {(ElementalState.Earth, ElementalState.Air), ElementalState.Whirlwind},
        };

    // Fire
    private float fireTempDamage;
    private float fireBurnDamage;
    private float fireBurnDuration;
    private bool fireExplode;
    private float fireExplosionSize;
    private int fireExplosionDamage;
    private bool fireEffectActive;
    private bool fireIsCursed;

    // Water
    private float waterBonusXP;
    private int waterBonusDamage;
    private float waterDrenchDuration;
    private bool waterExplode;
    private float waterBurstSize;
    private int waterExplosionDamage;
    private bool waterEffectActive;
    private bool waterIsCursed;

    // Earth
    private int earthFissureDamage;
    private float earthCrustDuration;
    private float earthBonusXP;
    private float earthBonusScore;
    private bool earthEffectActive;
    private bool earthIsCursed;

    // Electric
    private int electricShockDamage;
    private int electricChainCount;
    private float electricBonusXP;
    private float electricBonusScore;
    private bool electricEffectActive;
    private bool electricIsCursed;

    private bool areEffectsActive => fireEffectActive || waterEffectActive || earthEffectActive || electricEffectActive;

    private int fireBouncesRemaining;
    private int waterBouncesRemaining;
    private int earthBouncesRemaining;
    private int electricBouncesRemaining;

    // Public getters
    public float FireActiveTempDamage => fireTempDamage;
    public float FireBurnDamage => fireBurnDamage;
    public float FireBurnDuration => fireBurnDuration;
    public bool FireExplode => fireExplode;
    public float FireExplosionSize => fireExplosionSize;
    public int FireExplosionDamage => fireExplosionDamage;
    public int FireBouncesRemaining => fireBouncesRemaining;
    public bool FireEffectActive => fireEffectActive;
    public bool FireIsCursed => fireIsCursed;

    public float WaterBonusXP => waterBonusXP;
    public int WaterBonusDamage => waterBonusDamage;
    public float WaterDrenchDuration => waterDrenchDuration;
    public bool WaterExplode => waterExplode;
    public float WaterBurstSize => waterBurstSize;
    public int WaterExplosionDamage => waterExplosionDamage;
    public int WaterBouncesRemaining => waterBouncesRemaining;
    public bool WaterEffectActive => waterEffectActive;
    public bool WaterIsCursed => waterIsCursed;

    public int EarthFissureDamage => earthFissureDamage;
    public float EarthCrustDuration => earthCrustDuration;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;
    public bool EarthEffectActive => earthEffectActive;
    public bool EarthIsCursed => earthIsCursed;
    public int EarthBouncesRemaining => earthBouncesRemaining;

    public int ElectricShockDamage => electricShockDamage;
    public int ElectricChainCount => electricChainCount;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;
    public bool ElectricEffectActive => electricEffectActive;
    public bool ElectricIsCursed => electricIsCursed;
    public int ElectricBouncesRemaining => electricBouncesRemaining;

    // Caches required components and validates setup.
    private void Awake()
    {
        ball = GetComponent<Ball>();
        if (ball == null)
        {
            Debug.LogWarning("BallElementalState requires a Ball component on the same GameObject.");
        }

        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
    }

    // Initializes the current state and captures baseline properties.
    private void Start()
    {
        CurrentState = initialState;
        if (ball != null)
            originalMaxSpeed = ball.maxSpeed;
    }

    // Sets the current elemental state and applies related effects.
    public void SetState(ElementalState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        ApplyStateEffects();
        // TODO: trigger VFX/SFX hooks here if desired.
    }

    // Combines current state with an incoming element using the combination table.
    public void CombineWith(ElementalState newElement)
    {
        var combined = CombineElements(CurrentState, newElement);
        SetState(combined);
    }

    // Returns the combined state or falls back to the incoming element if no mapping exists.
    public ElementalState CombineElements(ElementalState existing, ElementalState incoming)
    {
        if (combinations.TryGetValue((existing, incoming), out var result))
        {
            return result;
        }
        return incoming;
    }

    // Applies simple per-state passive effects (placeholder: currently resets speed on None/Default).
    private void ApplyStateEffects()
    {
        if (ball == null) return;
        switch (CurrentState)
        {
            case ElementalState.Fire:
                break;
            case ElementalState.Water:
                break;
            case ElementalState.Earth:
                break;
            case ElementalState.Air:
                break;
            default:
                ball.maxSpeed = originalMaxSpeed;
                break;
        }
    }

    // Clears current state to None (does not clear active effect flags or counters).
    public void ClearState()
    {
        CurrentState = ElementalState.None;
        // TODO: remove VFX/SFX if added in ApplyStateEffects
    }

    // Called when the ball bounces a bumper; applies active effects to the bumper and consumes an effect bounce.
    public void OnBounce(Bumper bumper)
    {
        if (!areEffectsActive || !bumper) return;
        var elem = bumper.gameObject.GetComponent<BumperElementalState>();
        if (!elem) return;

        // Apply effect markers on the bumper (these are not the damage calculations but the status flags)
        if (fireEffectActive)
        {
            elem.ClearElement();
            elem.ApplyBurn(fireBurnDamage, fireBurnDuration);
        }
        if (waterEffectActive)
        {
            elem.ClearElement();
            elem.ApplyDrenched(waterDrenchDuration, waterBonusXP);
        }
        if (earthEffectActive)
        {
            elem.ClearElement();
            elem.ApplyCrusted(earthFissureDamage, earthCrustDuration, earthBonusXP, earthBonusScore);
        }
        if (electricEffectActive)
        {
            elem.ClearElement();
            elem.ApplyShocked(electricShockDamage, electricBonusXP, electricBonusScore);
        }

        // Consume one bounce from the active state and clear it when counters reach zero
        switch (CurrentState)
        {
            case ElementalState.Fire:
                fireBouncesRemaining--;
                if (fireBouncesRemaining <= 0)
                {
                    fireEffectActive = false;
                    ClearState();
                }
                break;
            case ElementalState.Water:
                waterBouncesRemaining--;
                if (waterBouncesRemaining <= 0)
                {
                    waterEffectActive = false;
                    ClearState();
                }
                break;
            case ElementalState.Earth:
                earthBouncesRemaining--;
                if (earthBouncesRemaining <= 0)
                {
                    earthEffectActive = false;
                    ClearState();
                }
                break;
            case ElementalState.Electric:
                electricBouncesRemaining--;
                if (electricBouncesRemaining <= 0)
                {
                    electricEffectActive = false;
                    ClearState();
                }
                break;
            default:
                break;
        }
    }

    #region Elemental State Methods

    // Applies Fire parameters to the ball and activates the Fire state.
    public void SetFireState(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionRadius, int explosionDamageFlat, bool cursed)
    {
        waterEffectActive = false;
        earthEffectActive = false;
        electricEffectActive = false;

        fireEffectActive = true;

        fireTempDamage = bonusDamage;
        fireBurnDamage = burnDamage;
        fireBurnDuration = burnDuration;
        fireBouncesRemaining += bounceDuration;
        if (fireBouncesRemaining > bounceDuration)
            fireBouncesRemaining = bounceDuration;
        fireExplode = canExplode;
        fireExplosionSize = explosionRadius;
        fireExplosionDamage = explosionDamageFlat;
        fireIsCursed = cursed;

        SetState(ElementalState.Fire);
    }

    // Applies Water parameters to the ball and activates the Water state.
    public void SetWaterState(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstRadius, int burstDamageFlat, bool cursed)
    {
        electricEffectActive = false;
        fireEffectActive = false;
        earthEffectActive = false;

        waterEffectActive = true;

        waterBonusXP = bonusXP;
        waterBonusDamage = bonusDamage;
        waterDrenchDuration = drenchDuration;
        waterBouncesRemaining += bounceDuration;
        if (waterBouncesRemaining > bounceDuration)
            waterBouncesRemaining = bounceDuration;
        waterExplode = canBurst;
        waterBurstSize = burstRadius;
        waterExplosionDamage = burstDamageFlat;
        waterIsCursed = cursed;

        SetState(ElementalState.Water);
    }

    // Applies Earth parameters to the ball and activates the Earth state.
    public void SetEarthState(int fissureDamage, float crustDuration, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = false;
        waterEffectActive = false;
        electricEffectActive = false;

        earthEffectActive = true;

        earthFissureDamage = fissureDamage;
        earthCrustDuration = crustDuration;
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        earthBouncesRemaining += bounceDuration;
        if (earthBouncesRemaining > bounceDuration)
            earthBouncesRemaining = bounceDuration;
        earthIsCursed = cursed;
        SetState(ElementalState.Earth);
    }

    // Applies Electric parameters to the ball and activates the Electric state.
    public void SetElectricState(int shockDamage, int chainCount, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = false;
        waterEffectActive = false;
        earthEffectActive = false;

        electricEffectActive = true;

        electricShockDamage = shockDamage;
        electricChainCount = chainCount;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
        electricBouncesRemaining += bounceDuration;
        if (electricBouncesRemaining > bounceDuration)
            electricBouncesRemaining = bounceDuration;
        electricIsCursed = cursed;
        SetState(ElementalState.Electric);
    }

    #endregion
}

```

## Assets/Scripts/BallElements.cs

```csharp
/// Elemental states a ball can currently embody or fuse into.
public enum ElementalState
{
    None = 0,

    // Base elements
    Fire = 1,
    Water = 2,
    Earth = 3,
    Air = 4,
    Electric = 5,

    // Fusions / advanced states
    Steam = 10,
    Magma = 11,
    Wildfire = 12,
    Sludge = 13,
    Vapor = 14,
    Whirlwind = 15,
}

```

## Assets/Scripts/BallXPBar.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Simple, deterministic XP HUD for the current ball.
[DisallowMultipleComponent]
public sealed class BallXPBar : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image xpBar;
    [SerializeField] private Image xpBarHolder;
    [SerializeField] private TMP_Text levelText;

    [Header("Behavior")]
    [SerializeField, Min(0f)] private float reduceSpeed = 2.5f;
    [SerializeField] private bool useUnscaledTime = false;

    private float _targetFill;   // desired fill (0..1), driven via UpdateXP

    // Ensures references are present and sane in-editor.
    private void OnValidate()
    {
        reduceSpeed = Mathf.Max(0f, reduceSpeed);
    }

    // Initializes UI state and warns if bindings are missing.
    private void Awake()
    {
        if (!xpBar) Debug.LogWarning("[BallXPBar] xpBar is not assigned.", this);
        if (!levelText) Debug.LogWarning("[BallXPBar] levelText is not assigned.", this);
    }

    // Smoothly moves the fill amount toward the latest target.
    private void Update()
    {
        if (!xpBar) return;
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        xpBar.fillAmount = Mathf.MoveTowards(xpBar.fillAmount, _targetFill, reduceSpeed * Mathf.Max(0.0001f, dt));
    }

    // Updates target fill and level label from the current XP snapshot.
    public void UpdateXP(float currentXP, float maxXP, int level)
    {
        float denom = Mathf.Max(0.0001f, maxXP);
        _targetFill = Mathf.Clamp01(currentXP / denom);
        if (levelText) levelText.text = $"Level: {level}";
    }

    // Immediately sets the bar to the target (skips smoothing); useful after scene loads.
    public void SnapToTarget()
    {
        if (!xpBar) return;
        xpBar.fillAmount = _targetFill;
    }

    // Allows dynamic tuning of lerp speed (powerups, slow-mo, etc.).
    public void SetReduceSpeed(float speed)
    {
        reduceSpeed = Mathf.Max(0f, speed);
    }
}

```

## Assets/Scripts/BaseCharacter.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum TraitType
{
    Endurance, //HP, Def, Res
    Strength, //MinAtk, MaxAtk, Break
    Agility, //Speed, Evasion, Crit
    Wit, //Intelligence, Mana, Luck
    Charm, // Luck, Lifesteal
}
public enum StatType
{
    Health, //E
    Mana, //W
    MinAtk, //S
    MaxAtk, //S
    Accuracy,
    Speed, // A
    Defense, //E
    Resistance, //E
    Evasion, //A
    Critical, // A
    Break, // S
    Intelligence, // W
    Luck, // C
    Lifesteal // C
}

public class BaseCharacter
{



    public string name;
    public string charClass { get; private set; }
    public int level { get; private set; }

    public float TurnMeter { get; private set; } = 0f;
    public void ConsumeTurnMeter() => TurnMeter = 0;
    public bool IsTurnReady => TurnMeter >= 100f * (((previousHits + 1) * 1.35f) * 2f);
    public int previousHits = 0;
    public void FillTurnMeter() => TurnMeter += Speed.Value;

    public  Dictionary<StatType, CharacterStat> stats = new();
    public  Dictionary<TraitType, float> traits = new();

    public CharacterStat Health => stats[StatType.Health];
    public CharacterStat Mana => stats[StatType.Mana];
    public CharacterStat MinAtk => stats[StatType.MinAtk];
    public CharacterStat MaxAtk => stats[StatType.MaxAtk];
    public CharacterStat Speed=> stats[StatType.Speed];
    public CharacterStat Defense => stats[StatType.Defense];
    public CharacterStat Accuracy => stats[StatType.Accuracy];
    public CharacterStat Critical => stats[StatType.Critical];
    public CharacterStat Break => stats[StatType.Break];
    public CharacterStat Evasion => stats[StatType.Evasion];
    public CharacterStat Resistance => stats[StatType.Resistance];
    public CharacterStat Luck => stats[StatType.Luck];

    public float Endurance;
    public float Strength;
    public float Agility;
    public float Wit;
    public float Charm;



    public BaseCharacter()
    {
        name = string.Empty;
        charClass = string.Empty;
        level = 0;
        InitializeStats();
        InitializeTraits();
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk, float spd)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();
        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
        stats[StatType.Speed].baseValue = spd;
    }

    public static BaseCharacter CreateCharacterFromClass(string className)
    {
        switch (className)
        {
            case "Warrior": return new Warrior();
            case "Mage": return new Mage();
            case "Druid": return new Druid();
            case "Assassin": return new Assassin();
            case "Tank": return new Tank();
            default: return new BaseCharacter();
        }
    }


    public virtual void InitializeStats()
    {

        foreach(StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            stats[type] = new CharacterStat(0f);
        }

        stats[StatType.Health].baseValue = 100f;
        stats[StatType.Mana].baseValue = 100f;
        stats[StatType.MinAtk].baseValue = 50f;
        stats[StatType.MaxAtk].baseValue = 50f;
        stats[StatType.Speed].baseValue = 100f;
        stats[StatType.Defense].baseValue = 0f;
    }

    public virtual void InitializeRandomStats(Dictionary<StatType, CharacterStat> p1Stats)
    {

        foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);
            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);
            float randomizedValue = p1Stats[type].Value * Random.Range(min, max);
            float finalValue = Mathf.Round((p1Stats[type].Value + randomizedValue) * 10f) / 10f;
            this.stats[type] = new CharacterStat(Mathf.Max(0.1f, finalValue));
            Debug.Log($"Random Value Generated - {randomizedValue} : New Stat Value {this.stats[type].Value}\nMin # - {min} : Max # - {max}");

        }
        this.RefillAllVitals(); 
    }

    public virtual void InitializeTraits()
    {
        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            traits[trait] = 0;
        }
    }

    public virtual void InitializeRandomTraits(Dictionary<TraitType, float> p1Traits)
    {

        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);

            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);

            float randomizedValue = p1Traits[trait] * Random.Range(min, max);
            float finalValue = Mathf.Round(p1Traits[trait] + randomizedValue);
            this.traits[trait] = Mathf.Max(0, finalValue);
        }
    }

    public virtual void ApplyTraitBonuses()
    {
        stats[StatType.Health].baseValue += traits[TraitType.Endurance] * .01f; //1% endurance value
        stats[StatType.Defense].baseValue += traits[TraitType.Endurance] * 0.5f; //half endurance value
        stats[StatType.Resistance].baseValue += traits[TraitType.Endurance] * 0.3f; //30% endurance value... etc.

        stats[StatType.MinAtk].baseValue += traits[TraitType.Strength] * 1f;
        stats[StatType.MaxAtk].baseValue += traits[TraitType.Strength] * 2f;
        stats[StatType.Break].baseValue += traits[TraitType.Strength] * 0.4f;

        stats[StatType.Speed].baseValue += traits[TraitType.Agility] * 1.2f;
        stats[StatType.Evasion].baseValue += traits[TraitType.Agility] * 0.5f;
        stats[StatType.Critical].baseValue += traits[TraitType.Agility] * 0.25f;

        stats[StatType.Intelligence].baseValue += traits[TraitType.Wit] * 1.5f;
        stats[StatType.Luck].baseValue += traits[TraitType.Wit] * 0.5f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Wit] * 2f;


        stats[StatType.Luck].baseValue += traits[TraitType.Charm] * 0.05f;
        stats[StatType.Lifesteal].baseValue += traits[TraitType.Charm] * 0.4f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Charm] * .8f;

        stats[StatType.Critical].baseValue += stats[StatType.Luck].baseValue * 0.25f;

    }

    public virtual void ApplyLevelScaling() { }

    public Dictionary<StatType, CharacterStat> GetStats()
    {
        return stats;
    }

    public Dictionary<TraitType, float> GetTraits()
    {
        return traits;
    }

    public virtual void PrintStats()
    {
        Debug.Log($"Name: {name}");
        Debug.Log($"Class: {charClass}");
        Debug.Log($"Level: {level}");
        Debug.Log($"base health: {stats[StatType.Health].baseValue}");

        foreach( var stat in stats)
        {
            Debug.Log($"{stat.Key} Value: {stat.Value.Value}");
        }
    }

    public void RefillAllVitals()
    {
        this.stats[StatType.Health].RefillToMax();
        this.stats[StatType.Mana].RefillToMax();
    }

    public void RandomizeCharacter(int playerLevel, Dictionary<StatType, CharacterStat> playerStats, Dictionary<TraitType, float> playerTraits)
    {
        this.name = "Generated";
        this.level = Random.Range(playerLevel - 2, playerLevel + 3);
        InitializeRandomStats(playerStats);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        InitializeRandomTraits(playerTraits);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyLevelScaling();
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyTraitBonuses();
        Debug.Log($"Base Value - {this.Health.baseValue}");
    }





}

```

## Assets/Scripts/Brawler.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Brawler : BaseCharacter
{

    protected float hpLvlIncrease = 6.7f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.48f;
    protected float strLVLinc = 4.37f;
    protected float agiLVLinc = 2.26f;
    protected float witLVLinc = .74f;
    protected float chaLVLinc = 1.82f;


    public Brawler(string name, int level)
        : base(name, "Brawler", level, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Brawler()
    : base("", "Brawler", 5, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);

    }
}

```

## Assets/Scripts/Bumper.cs

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BumperType
{
    Small,
    Default,
    Large
}

[DisallowMultipleComponent]
public class Bumper : MonoBehaviour
{
    [SerializeField] public float curHealth;
    [SerializeField] public float maxHealth;
    [SerializeField] public float cooldown;

    public BumperType type;

    private readonly Dictionary<int, float> lastHitTimeByBall = new();
    [SerializeField] private float hitCooldown = 0.02f;

    private Vector3 lastContactPoint;
    private float lastContactTime;
    [SerializeField] private float contactPointTimeout = 0.25f;

    private BumperElementalState bumperElemental;
    private Pinball pinball;

    // Per-hit XP/score scaling cache
    private float _lastDmgFactorForXP = 1f;

    private Vector3 normal;

    private static readonly List<Bumper> AllBumpers = new();
    public static IEnumerable<Bumper> EnumerateAll() => AllBumpers;

    // Register this bumper in the global list.
    private void OnEnable() => AllBumpers.Add(this);

    // Unregister this bumper from the global list.
    private void OnDisable() => AllBumpers.Remove(this);

    // Cache references and reset health to max.
    private void Awake()
    {
        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
        bumperElemental = GetComponent<BumperElementalState>();
        curHealth = maxHealth;
    }

    // Handles ball collision: debounces hits, computes impulse direction, forwards score/XP scaling, and applies damage.
    private void OnCollisionEnter(Collision col)
    {
        var rb = col.rigidbody;
        if (rb == null) return;

        var ballComp = rb.GetComponent<Ball>();
        var ballElem = rb.GetComponent<BallElementalState>();

        int id = rb.GetInstanceID();
        if (lastHitTimeByBall.TryGetValue(id, out float last) && Time.time - last < hitCooldown)
            return;
        lastHitTimeByBall[id] = Time.time;

        var contact = col.contacts[0];
        lastContactPoint = contact.point;
        lastContactTime = Time.time;

        normal = new Vector3(contact.normal.x, 0f, contact.normal.z).normalized;
        if (normal == Vector3.zero)
        {
            normal = (col.transform.position - transform.position);
            normal.y = 0f;
            normal.Normalize();
        }

        float totalDamage = ballComp != null ? ballComp.CurrentDamage : (pinball != null ? pinball.Damage : 0f);
        bool fireTick = ballElem != null && ballElem.CurrentState == ElementalState.Fire;

        // Factor includes flats + multipliers vs baseline (from the ball that hit).
        float dmgFactor = ballComp != null ? ballComp.ScoreXpDamageFactor : 1f;
        _lastDmgFactorForXP = dmgFactor;

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 225f : 100f;

        rb.velocity = Vector3.zero;
        ballComp?.Bump(normal, deltaV, bumperKind, this);

        Debug.DrawRay(contact.point, normal * 2f, Color.red);

        TakeDamage(totalDamage, elemDmg: fireTick, damageFactor: _lastDmgFactorForXP);

        if (pinball != null)
        {
            var dropPos = (Time.time - lastContactTime) <= contactPointTimeout ? lastContactPoint : transform.position;
            PowerupSystem.TrySpawnPickupOnHit(pinball, dropPos, pinball as IRunContext);
        }
    }

    // Finds the nearest other bumper within an optional max distance (used by chain effects).
    private Bumper FindNearestOther(float maxDistance = Mathf.Infinity)
    {
        Vector3 p = transform.position;
        Bumper nearest = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < AllBumpers.Count; i++)
        {
            var b = AllBumpers[i];
            if (b == null || b == this) continue;
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = b; }
        }
        return nearest;
    }

    // Core damage handler: applies damage, spawns feedback, emits XP (with Water override), and schedules respawn.
    public void TakeDamage(float amount, bool elemDmg, float damageFactor = 1f)
    {
        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        // Water XP override: if drenched, ignore damageFactor for XP (water controls XP output).
        bool isDrenched = bumperElemental != null && bumperElemental.CurrentState == BumperState.Drenched;

        if (pinball != null)
        {
            if (elemDmg)
            {
                if (curHealth > 0)
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true, damageFactor: isDrenched ? 1f : _lastDmgFactorForXP);
            }
            else
            {
                if (isDrenched)
                    pinball.SpawnBonusWaterXP(transform.position, bumperElemental.WaterBonusXP, damageFactor: 1f);
                else
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
            }
        }

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                // Death XP also ignores damageFactor when drenched.
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: elemDmg, damageFactor: isDrenched ? 1f : _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true; // next score tick gets bonus
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    // Applies Earth fissure tick damage and emits Earth XP, using last stored damage factor.
    public void TakeFissureDamage(float amount)
    {
        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.EarthBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true;
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    // Applies Electric shock damage (with optional propagation), emits XP using last stored factor.
    public void TakeShockDamage(float amount, bool propogate = false)
    {
        if (propogate)
        {
            var nearest = FindNearestOther();
            if (nearest) nearest.TakeShockDamage(amount, false);
        }

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(1, 4, -1);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.ElectricBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0)
        {
            curHealth = 0;
            if (pinball != null)
            {
                pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP);
                pinball.destroyedBumperBonusActive = true;
            }
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }
}
```

## Assets/Scripts/BumperElementalState.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BumperElementalState : MonoBehaviour
{
    private Bumper bumper;
    public BumperState CurrentState  = BumperState.None;

    private float fireBurnExpireAt;
    private float fireBurnNextTickAt;
    private float fireBurnDamagePerTick;
    private float fireBurnTickInterval = .5f;

    private float waterBonusXP;
    private float waterDrenchExpireAt;

    private float earthFissureDamage;
    private float earthBonusXP;
    private float earthBonusScore;
    private float earthCrustExpireAt;

    private float electricShockDamage;
    private float electricBonusXP;
    private float electricBonusScore;

    public float WaterBonusXP => waterBonusXP;

    public float EarthFissureDamage => earthFissureDamage;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;

    public float ElectricShockDamage => electricShockDamage;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;

    // Caches the bumper dependency.
    void Awake()
    {
        bumper = GetComponent<Bumper>();
    }

    // Drives active status effects per-frame.
    void Update()
    {
        switch (CurrentState)
        {
            case BumperState.None:
                break;
            case BumperState.Burning:
                HandleBurning();
                break;
            case BumperState.Drenched:
                HandleDrenched();
                break;
            case BumperState.Crusted:
                HandleCrusted();
                break;
            case BumperState.Shocked:
                HandleShocked();
                break;
            default:
                break;
        }
    }

    // Applies burning and schedules ticks.
    public void ApplyBurn(float dps, float duration)
    {
        CurrentState = BumperState.Burning;
        Debug.Log("Bumper Burn Applied");
        fireBurnExpireAt = Time.time + duration;
        fireBurnNextTickAt = Time.time + fireBurnTickInterval;
        fireBurnDamagePerTick = dps * fireBurnTickInterval;
    }

    // Ticks burn damage and expires when due.
    private void HandleBurning()
    {
        if (Time.time >= fireBurnNextTickAt)
        {
            fireBurnNextTickAt += fireBurnTickInterval;
            bumper?.TakeDamage(fireBurnDamagePerTick, elemDmg: true);
        }

        if (Time.time >= fireBurnExpireAt)
        {
            ClearBurn();
        }
    }

    // Clears burning state.
    public void ClearBurn()
    {
        fireBurnExpireAt = 0f;
        fireBurnNextTickAt = 0f;
        fireBurnDamagePerTick = 0f;
        if (CurrentState == BumperState.Burning) CurrentState = BumperState.None;
    }

    // Applies drenched and sets bonus XP.
    public void ApplyDrenched(float duration, float bonusXP)
    {
        CurrentState = BumperState.Drenched;
        Debug.Log("Bumper Drenched Applied");
        waterDrenchExpireAt = Time.time + duration;
        waterBonusXP = bonusXP;
    }

    // Expires drenched state on timeout.
    private void HandleDrenched()
    {
        if (Time.time >= waterDrenchExpireAt)
        {
            ClearDrenched();
        }
    }

    // Clears drenched state.
    public void ClearDrenched()
    {
        waterDrenchExpireAt = 0f;
        waterBonusXP = 0f;
        if (CurrentState == BumperState.Drenched) CurrentState = BumperState.None;
    }

    // Applies crusted effect and extends its expiration.
    public void ApplyCrusted(float damage, float duration, float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Crusted;
        Debug.Log("Bumper Crusted Applied");
        float newExpire = Time.time + duration;
        earthFissureDamage = damage;
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        if (newExpire > earthCrustExpireAt)
            earthCrustExpireAt = newExpire;
    }

    // Triggers fissure damage on expiry then clears.
    public void HandleCrusted()
    {
        if (Time.time >= earthCrustExpireAt)
        {
            bumper?.TakeFissureDamage(earthFissureDamage);
            ClearCrusted();
        }
    }

    // Clears crusted state and its bonuses.
    public void ClearCrusted()
    {
        earthFissureDamage = 0f;
        earthCrustExpireAt = 0f;
        earthBonusXP = 0f;
        earthBonusScore = 0f;
        if (CurrentState == BumperState.Crusted) CurrentState = BumperState.None;
    }

    // Applies shocked metadata for next tick.
    public void ApplyShocked(float damage, float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Shocked;
        electricShockDamage = damage;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
    }

    // Triggers electric damage once then clears.
    public void HandleShocked()
    {
        bumper?.TakeShockDamage(electricShockDamage, true);
        ClearShocked();
    }

    // Clears shocked state.
    public void ClearShocked()
    {
        electricShockDamage = 0f;
        electricBonusXP = 0f;
        electricBonusScore = 0f;
        if (CurrentState == BumperState.Shocked) CurrentState = BumperState.None;
    }

    // Clears whichever elemental state is active.
    public void ClearElement()
    {
        ClearBurn();
        ClearDrenched();
        ClearCrusted();
        ClearShocked();
    }
}

```

## Assets/Scripts/BumperElements.cs

```csharp
using UnityEngine;

[System.Serializable]
public enum BumperState
{
    // No elemental status is active.
    None,
    // Fire DoT is ticking (handled in BumperElementalState.HandleBurning).
    Burning,
    // Water state is active; XP emission uses water override/bonus.
    Drenched,
    // Earth crust applied; fissure damage triggers on expiry.
    Crusted,
    // Air/wind placeholder (not currently driven).
    Windswept,
    // Electric shock applied; may propagate to neighbors.
    Shocked,

    // Steam combo placeholder (unused by handlers).
    Steaming,
    // Molten combo placeholder (unused by handlers).
    Molten,
    // Blazing combo placeholder (unused by handlers).
    Blazing,

    // Sludge combo placeholder (unused by handlers).
    Sludged,
    // Mist combo placeholder (unused by handlers).
    Misted,

    // Whirlwind combo placeholder (unused by handlers).
    Whirling,
}
```

## Assets/Scripts/CharacterStat.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections.ObjectModel;

[Serializable]
public class CharacterStat
{
    public float baseValue;
    public float BaseValue => (float)Math.Round(baseValue, 1);

    public float currentValue;

    public virtual float Value
    {
        get
        {
            if (isDirty)
            {
                _value = CalculateFinalValue();
                isDirty = false;
            }
            return _value;
        }
    }

    protected bool isDirty = true;
    protected float _value;

    protected readonly List<StatModifier> statModifiers;
    public readonly ReadOnlyCollection<StatModifier> StatModifiers;

    public CharacterStat()
    {
        statModifiers = new List<StatModifier>();
        StatModifiers = statModifiers.AsReadOnly();
    }

    public CharacterStat(float value) : this()
    {
        baseValue = value;
    }

    public virtual void AddModifier(StatModifier mod)
    {
        isDirty = true;
        statModifiers.Add(mod);
        statModifiers.Sort(CompareModifierOrder);
    }



    protected virtual int CompareModifierOrder(StatModifier a, StatModifier b)
    {
        if (a.Order < b.Order)
            return -1;
        else if(a.Order > b.Order)
            return 1;
        return 0; // a.Order == b.Order
    }

    public virtual bool RemoveModifier(StatModifier mod)
    {
        if(statModifiers.Remove(mod))
        {
            isDirty = true;
            return true;
        }
        return false;
    }

    public virtual bool RemoveAllModifiersFromSource(object source)
    {
        bool didRemove = false;

        for(int i = statModifiers.Count - 1; i >= 0; i--)
        {
            if(statModifiers[i].Source == source)
            {
                isDirty = true;
                didRemove = true;
                statModifiers.RemoveAt(i);
            }
        }

        return didRemove;
    }

    protected virtual float CalculateFinalValue()
    {
        float finalValue = baseValue;
        float sumPercentAdd = 0;

        for(int i = 0; i < statModifiers.Count; i++)
        {
            StatModifier mod = statModifiers[i];

            if(mod.Type == StatModType.Flat)
            {
                finalValue += mod.Value;
            }
            else if(mod.Type == StatModType.PercentAdd)
            {
                sumPercentAdd += mod.Value;

                if(i + 1 >= statModifiers.Count || statModifiers[i + 1].Type != StatModType.PercentAdd)
                {
                    finalValue *= 1 + sumPercentAdd;
                    sumPercentAdd = 0;
                }
            }
            else if (mod.Type == StatModType.PercentMult)
            {
                finalValue *= 1 + mod.Value;
            }
        }

        return (float)Math.Round(finalValue, 1);
    }

    public void RefillToMax()
    {
        isDirty = true;
    }

}

```

## Assets/Scripts/CharacterUI.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class CharacterUI : MonoBehaviour
{

    [Header("UI Canvases")]
    public GameObject homeHUD;
    public GameObject mainMenuHUD;
    public GameObject pinballHUD;
    public GameObject mainStoryHUD;
    public GameObject battleHUD;
    public GameObject preBattleHUD;
    public GameObject winHUD;
    public GameObject loseHUD;
    public GameObject initGameHUD;
    public GameObject initNameHUD;

    [Header("Player 1 UI")]
    public TMP_Text player1Name;
    public TMP_Text player1Class;
    public TMP_Text player1Level;
    public TMP_Text player1Stats;
    public TMP_Text player1Traits;
    [Header("Player 1 Init UI")]
    public TMP_Text player1StatsInit;
    public TMP_Text player1TraitsInit;
    public TMP_Text playerNameInput;

    [Header("Player 2 UI")]
    public TMP_Text player2Name;
    public TMP_Text player2Class;
    public TMP_Text player2Level;
    public TMP_Text player2Stats;
    public TMP_Text player2Traits;

    [Header("Combat System Information")]
    public bool winner = false;

    public void SetCharacterUI(BaseCharacter character, bool isPlayer1)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {Mathf.Max(0f, stat.Value.BaseValue)} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

        if (isPlayer1)
        {
            player1Name.text = character.name;
            player1Class.text = character.charClass;
            player1Level.text = $"Lv {character.level}";
            player1Stats.text = stats;
            player1Traits.text = traits;
        }
        else
        {
            player2Name.text = character.name;
            player2Class.text = character.charClass;
            player2Level.text = $"Lv {character.level}";
            player2Stats.text = stats;
            player2Traits.text = traits;

        }

    }

    public void SetInitCharacterUI(BaseCharacter character)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {stat.Value.BaseValue} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

            player1StatsInit.text = stats;
            player1TraitsInit.text = traits;
    }

    public void UpdateUI()
    {

    }

    public void HandleInitLoad()
    {
        homeHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        mainMenuHUD.SetActive(false);
    }
    public void HandleChooseCharacter()
    {
        initGameHUD.SetActive(true);

        homeHUD.SetActive(false);
    }

    public void HandlePreBattle()
    {
        battleHUD.SetActive(true);
        preBattleHUD.SetActive(true);

        initNameHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
    }

    public void HandlePinball()
    {
        pinballHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleInitName()
    {
        initNameHUD.SetActive(true);

        initGameHUD.SetActive(false);
    }

    public void HandleMainMenu()
    {
        mainMenuHUD.SetActive(true);

        DisableAllButMM();
    }

    public void HandleBackToMM()
    {
        mainMenuHUD.SetActive(true);

        mainStoryHUD.SetActive(false);
    }

    public void HandleMainStory()
    {
        mainStoryHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleBattle()
    {
        battleHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void HandleBattleFinished()
    {
        if (winner)
            winHUD.SetActive(true);
        else
            loseHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void DisableMenu()
    {
            winHUD.SetActive(false);
        loseHUD.SetActive(false);
    }

    public void DisableAllButMM()
    {
        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        homeHUD.SetActive(false);
    }

    public void SetPlayerName(BaseCharacter player)
    {
        player.name = playerNameInput.text;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

```

## Assets/Scripts/CollectAllXPPowerup.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CollectAllXPPowerup : IPowerup
{
    public string Id => "collect-all-xp";
    public float Weight => 1.0f;
    public string DebugLabel => "Collect All XP";

    // Only trigger while actively playing.
    public bool CanTrigger(IRunContext ctx) => ctx is Pinball pb && pb.CurrentState == PinballState.Play;

    // Temporarily redirects all XP to the anchor ball by boosting its forcefield and suppressing others.
    public void Execute(Pinball pm, Vector3 triggerPos)
    {
        if (!pm) return;

        var anchor = pm.ball;
        if (!anchor)
        {
            var balls = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < balls.Length; i++)
            {
                if (balls[i] && balls[i].isActiveAndEnabled && balls[i].IsActive)
                {
                    anchor = balls[i];
                    break;
                }
            }
        }

        pm.StartCoroutine(VacuumToAnchor(pm, anchor));
    }

    // Performs the XP �vacuum� effect: boosts anchor�s XP field, damps others, and restores after a delay.
    private static IEnumerator VacuumToAnchor(Pinball pm, Ball anchor)
    {
        const float duration = 1.75f;
        const float boostFactor = 400f;

        List<Collider> snapshot = null;
        var registry = XPCollectorRegistry.I;
        var anchorCol = anchor ? anchor.GetComponent<Collider>() : null;

        if (registry != null && anchorCol != null)
        {
            snapshot = new List<Collider>(registry.collectors);
            registry.collectors.Clear();
            registry.collectors.Add(anchorCol);
        }

        if (anchor != null)
            anchor.UpdateForcefield(boostFactor);

        var others = new List<Ball>(8);
        const float dampFactor = 0.01f;
        var allBalls = Object.FindObjectsOfType<Ball>();
        for (int i = 0; i < allBalls.Length; i++)
        {
            var b = allBalls[i];
            if (!b || b == anchor) continue;
            others.Add(b);
            b.UpdateForcefield(dampFactor);
        }

        pm.ScreenShake();

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (anchor != null)
            anchor.UpdateForcefield(1f / boostFactor);

        for (int i = 0; i < others.Count; i++)
        {
            var b = others[i];
            if (b) b.UpdateForcefield(1f / dampFactor);
        }

        if (registry != null && snapshot != null)
        {
            registry.collectors.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var c = snapshot[i];
                if (c) registry.collectors.Add(c);
            }
        }
    }
}
```

## Assets/Scripts/CollectXP.cs

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class CollectXP : MonoBehaviour
{
    [Header("Trigger Binding")]
    [SerializeField, Min(1)] int maxTargets = 16;          // keep small (perf)
    [SerializeField, Range(0.05f, 1f)] float refreshInterval = 0.25f;

    [Header("XP Settings")]
    [SerializeField] int xpPerParticle = 2;

    [Header("References (drag in Inspector if available)")]
    [SerializeField] BallXPBar ballXPScript;              // where you display/accumulate XP

    ParticleSystem ps;
    ParticleSystem.TriggerModule trigger;

    // Reuse buffers to avoid GC allocations:
    static readonly List<ParticleSystem.Particle> enteredBuf = new(256);
    static readonly List<(Collider c, float d2)> sortBuf = new(64);

    float elapsed;

    // Cache particle system and trigger module.
    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        trigger = ps.trigger;
    }

    // Subscribe to registry and scene events, then bind on next frame.
    void OnEnable()
    {
        XPCollectorRegistry.OnChanged += RebindTargets;
        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(RebindNextFrame());
    }

    // Unsubscribe from events to avoid leaks.
    void OnDisable()
    {
        XPCollectorRegistry.OnChanged -= RebindTargets; 
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Rebind targets when a new scene loads.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindTargets();
    }

    // Defer initial bind to ensure other systems have initialized.
    System.Collections.IEnumerator RebindNextFrame()
    {
        yield return null;
        RebindTargets();
    }

    // Resolve UI fallback and perform initial bind.
    void Start()
    {
        if (!ballXPScript)
        {
            var go = GameObject.FindWithTag("BallXPHolder");
            if (go) ballXPScript = go.GetComponent<BallXPBar>();
        }
        RebindTargets();
    }

    // Periodically refresh bound colliders and self-destroy when empty.
    void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= refreshInterval)
        {
            elapsed = 0f;
            RebindTargets();
        }

        if (ps.particleCount == 0)
            Destroy(gameObject);
    }

    // Pick nearest collectors (up to maxTargets) and assign to the trigger module.
    void RebindTargets()
    {
        var regs = XPCollectorRegistry.I?.collectors;
        if (regs == null || regs.Count == 0) return;

        sortBuf.Clear();
        Vector3 p = transform.position;

        for (int i = 0; i < regs.Count; i++)
        {
            var c = regs[i];
            if (!c) continue;
            var center = c.bounds.center;
            float d2 = (center - p).sqrMagnitude;
            sortBuf.Add((c, d2));
        }

        sortBuf.Sort((a, b) => a.d2.CompareTo(b.d2));

        int assignCount = Mathf.Min(maxTargets, sortBuf.Count);
        for (int i = 0; i < assignCount; i++)
            trigger.SetCollider(i, sortBuf[i].c);

        for (int i = assignCount; i < maxTargets; i++)
            trigger.SetCollider(i, null);
    }

    // Award XP for particles that entered a collector this frame and kill only those particles.
    void OnParticleTrigger()
    {
        enteredBuf.Clear();
        int count = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);

        for (int i = 0; i < count; i++)
        {
            if (Pinball.Instance)
                Pinball.Instance.AddXP(xpPerParticle);

            var p = enteredBuf[i];
            p.remainingLifetime = 0f;
            enteredBuf[i] = p;
        }

        if (count > 0)
            ps.SetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);
    }
}
```

## Assets/Scripts/CombatSystem.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Game.Combat
{
    public class CombatSystem
    {

        private BaseCharacter player1;
        private BaseCharacter player2;

        public List<BaseCharacter> upcomingTurns = new List<BaseCharacter>(8);

        protected BaseCharacter firstAttacker;

        private bool firstAttackerRemoved;

        public float multiAtkChancePercent;


        private float attackerAcc;
        private float attackerBrk;
        private float attackerCrt;

        private float defenderEva;
        private float defenderDef;
        private float defenderRes;

        private float AccEvaRatio;
        private float BrkDefRatio;
        private float CrtResRatio;

        private float critRatio;
        private float blockRatio;

        private float dodgePenalty;
        private float dodgeChance;
        private float hitChance;

        private float damage;

        private float dmgPen;



        bool doOnce;

        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void Initialize(BaseCharacter p1, BaseCharacter p2)
        {
            player1 = p1;
            player2 = p2;
        }

        public BaseCharacter DetermineFirstTurn()
        {
            if(player1.Speed.Value >= player2.Speed.Value)
            {
                firstAttacker = player1;
                player1.previousHits++;
                return player1;
            }
            else if(player2.Speed.Value > player1.Speed.Value)
            {
                firstAttacker = player2;
                player2.previousHits++;
                return player2;
            }

                return null;
        }

        public void DetermineTurns()
        {


            if(player1 != null && player2 != null)
            {

                const int maxTurns = 8;
                while (upcomingTurns.Count < maxTurns)
                {
                    player1.FillTurnMeter();
                        player2.FillTurnMeter();



                    if (player1.previousHits > 0)
                    {
                        if (LuckyHit(player1, player2, player1.previousHits))
                        {
                            player2.previousHits = 0;
                            player1.previousHits++;
                            upcomingTurns.Add(player1);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P1 Lucky Hit!");
                        }
                        else if (player1.Speed.Value > (player2.Speed.Value * 5))
                        {
                            if (player1.previousHits >= player1.Speed.Value / player2.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player2);
                                player1.previousHits = 0;
                            }
                        }
                        else
                        {
                            player1.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }
                    else if (player2.previousHits > 0)
                    {
                        if (LuckyHit(player2, player1, player2.previousHits))
                        {
                            player1.previousHits = 0;
                            player2.previousHits++;
                            upcomingTurns.Add(player2);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P2 Lucky Hit!");
                        }
                        else if (player2.Speed.Value > (player1.Speed.Value * 5))
                        {
                            if (player2.previousHits >= player2.Speed.Value / player1.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player1);
                                player2.previousHits = 0;
                            }
                        }
                        else
                        {
                            player2.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }

                    if (player1.IsTurnReady && (!player2.IsTurnReady || player1.TurnMeter >= player2.TurnMeter))
                    {
                        player1.previousHits++;
                        upcomingTurns.Add(player1);
                        Debug.Log("P1 Added from turn!");
                        player2.previousHits = 0;
                        player1.ConsumeTurnMeter();
                    }
                    else if (player2.IsTurnReady)
                    {
                        player2.previousHits++;
                        upcomingTurns.Add(player2);
                        Debug.Log("P2 Added from turn!");
                        player1.previousHits = 0;
                        player2.ConsumeTurnMeter();
                    }
                }
                if (upcomingTurns[0] != null)
                    if (upcomingTurns[0].name == firstAttacker.name && !firstAttackerRemoved)
                    {
                        upcomingTurns.RemoveAt(0);
                        firstAttackerRemoved = true;
                    }



            }

        }


        public void ExecuteAttack(BaseCharacter attacker, BaseCharacter defender)
        {
            damage = Random.Range(attacker.MinAtk.Value, attacker.MaxAtk.Value);

            attackerAcc = attacker.Accuracy.Value;
            attackerBrk = attacker.Break.Value;
            attackerCrt = attacker.Critical.Value;

            defenderEva = defender.Evasion.Value;
            defenderDef = defender.Defense.Value;
            defenderRes = defender.Resistance.Value;



            //Evasion-to-Accuracy check //Dodge


            //Break-to-Defense check //Penetration or Defense


            





            if (WillAttackerHit())
              {
                Debug.Log($" {attacker.name} Can hit! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
                if(!WillDefenderDodge())
                {
                    CalculateDamage();
                    Debug.Log($"{attacker.name} Dmg Pen - {dmgPen}\nDamage - {damage}");
                        if (WillAttackerCrit())
                        {
                            damage *= 1.5f;
                            Debug.Log($" {attacker.name} Crit hit! \nCrit % - {critRatio}");
                        }
                        else if(WillDefenderBlock())
                        {
                            damage *= .5f;
                            Debug.Log($" {defender.name} Blocked hit! \nBlock % - {blockRatio}");
                        }
                    defender.Health.baseValue -= damage;
                }
                else
                    Debug.Log($" {defender.name} Dodged! \nHit % - {hitChance} \nDodge % - {dodgeChance}");

            }
              else
                Debug.Log($" {attacker.name} Missed! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
            }
        public bool LuckyHit(BaseCharacter attacker, BaseCharacter defender, int priorHits)
        {
            float decayRate = 0.2f;
            if (priorHits > 5f)
                decayRate = .3f;
            else if (priorHits > 10f)
                decayRate += .5f;
            float penaltyMultiplier = Mathf.Exp(-decayRate * priorHits); // Shrinks toward 0 over time
            float speedValueAtkr = Mathf.Max(1, attacker.Speed.Value * penaltyMultiplier);
            float speedValueDfdr = defender.Speed.Value;

            float speedRatio = Mathf.Log((speedValueAtkr / speedValueDfdr) + 1, 2f);


            float rawSpeedDiff = Mathf.Abs(speedValueAtkr - speedValueDfdr);

            float scaleFactor = Mathf.Lerp(5f, 2f, Mathf.Clamp01(rawSpeedDiff / 9999f));

            float chance = Mathf.Clamp01(speedRatio / scaleFactor);

            float luck = Mathf.Max(0f, attacker.Luck.Value);
            float luckBonus = Mathf.Clamp01(luck / 99999f) * .25f;
            
            multiAtkChancePercent = chance;

            float finalChance = Mathf.Clamp01(chance + luckBonus);

            Debug.Log($"Luck Bonus -  {attacker.name} {luckBonus}");
            Debug.Log($"Chance -  {attacker.name} {chance}");
            Debug.Log($"Combo -  {attacker.name} {luckBonus + chance}");

            return Random.value < chance;
        }

        public bool WillAttackerHit()
        {
            //Accuracy-to-Evasion check //Accuracy
            AccEvaRatio = attackerAcc / Mathf.Max(1f, defenderEva);
            hitChance = (float)System.Math.Tanh((double)(AccEvaRatio / 1.88f));
            return (Random.value < hitChance);
        }

        public bool WillDefenderDodge()
        {
            dodgePenalty = Mathf.Lerp(0f, 0.15f, 1f - hitChance);
            dodgeChance = Mathf.Clamp01(dodgeChance - dodgePenalty);
            if (defenderEva >= attackerAcc)
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = AccEvaRatio / (AccEvaRatio + 2.5f);
            }
            else
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = 0.5f * (AccEvaRatio / (AccEvaRatio + 1.5f));
            }
            dodgeChance = Mathf.Clamp01(dodgeChance);
            return (Random.value < Mathf.Clamp01(dodgeChance - dodgePenalty));
        }

        public bool WillAttackerCrit()
        {
            //Crit-to-Resistance check //Crits or Block

            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            critRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                critRatio = 0f;
            }
            else if (CrtResRatio > 1f)
            {
                critRatio = Mathf.Log10(CrtResRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }

            Debug.Log($"Crit % - {critRatio}");


            if (attackerCrt == 0f)
                return false;
            else if (attackerCrt > defenderRes)
                return (Random.value < critRatio);
            else
            {
                Debug.Log($"Buffed Crit %! - {critRatio+ .05f}");
                return (Random.value < critRatio + .05f);
            }
        }

        public bool WillDefenderBlock()
        {
            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            blockRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                blockRatio = 0f;
            }
            else if(CrtResRatio <= 1f)
            {
                float inverseRatio = defenderRes / Mathf.Max(1f, attackerCrt);
                blockRatio = Mathf.Clamp01(Mathf.Log10(inverseRatio) / Mathf.Log10(10f)); // down to -1.0 (-100%)
            }

            Debug.Log($"Block % - {blockRatio}");

            if (defenderRes == 0f)
                return false;
            else if (defenderRes >= attackerCrt)
                return (Random.value < blockRatio);
            else
            {
                Debug.Log($"Buffed Block %! - {blockRatio + .05f}");
                return (Random.value < blockRatio + .05f);
            }
        }

        public void CalculateDamage()
        {
            BrkDefRatio = attackerBrk / Mathf.Max(1f, defenderDef);

            if (Mathf.Approximately(BrkDefRatio, 1f))
            {
                dmgPen = 0f;
            }
            else if (BrkDefRatio > 1f)
            {
                dmgPen = Mathf.Log10(BrkDefRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }
            else
            {
                float inverseRatio = defenderDef / Mathf.Max(1f, attackerBrk);
                dmgPen = -Mathf.Log10(inverseRatio) / Mathf.Log10(10f); // down to -1.0 (-100%)
            }

            damage = Mathf.Round((damage + (damage * dmgPen)) * 10f) / 10f;
        }

    }



}


```

## Assets/Scripts/DamageNumberStyleSO.cs

```csharp
using UnityEngine;
using TMPro;

/// Centralized visual/timing style for tweened damage numbers.
[CreateAssetMenu(menuName = "UI/Damage Numbers/Style", fileName = "DamageNumberStyle")]
public sealed class DamageNumberStyleSO : ScriptableObject
{
    [Header("Typography")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField, Min(0.1f)] private float baseFontSize = 4f;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float duration = 0.9f;
    [SerializeField, Range(0.01f, 0.3f)] private float fadeInFraction = 0.08f;
    [SerializeField, Range(0.1f, 0.6f)] private float fadeOutFraction = 0.25f;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float riseDistance = 1.25f;

    [Header("Scale Pop")]
    [SerializeField, Min(0.01f)] private float popFromScale = 0.6f;
    [SerializeField, Min(0.01f)] private float popToScale = 1.1f;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 500;

    [Header("Update")]
    [SerializeField] private bool useUnscaledTime = false;

    // Expose read-only access for runtime consumers.
    public TMP_FontAsset Font => font;
    public Material FontMaterial => fontMaterial;
    public float BaseFontSize => baseFontSize;
    public Color DefaultColor => defaultColor;
    public float Duration => duration;
    public float FadeInFraction => fadeInFraction;
    public float FadeOutFraction => fadeOutFraction;
    public float RiseDistance => riseDistance;
    public float PopFromScale => popFromScale;
    public float PopToScale => popToScale;
    public string SortingLayerName => sortingLayerName;
    public int SortingOrder => sortingOrder;
    public bool UseUnscaledTime => useUnscaledTime;

    // Validates ranges and relationships when the asset changes in the editor.
    private void OnValidate()
    {
        // Ensure fade parts fit within duration and don�t overlap awkwardly
        duration = Mathf.Max(0.05f, duration);
        fadeInFraction = Mathf.Clamp(fadeInFraction, 0.01f, 0.95f);
        fadeOutFraction = Mathf.Clamp(fadeOutFraction, 0.01f, 0.95f);

        // Ensure pop scales make sense
        if (popToScale < popFromScale)
            popToScale = popFromScale;

        // Nudge impossible combos
        if (fadeInFraction + fadeOutFraction > 0.95f)
            fadeOutFraction = 0.95f - fadeInFraction;
    }

    // Computes absolute fade times (seconds) from the stored fractional settings.
    public void GetFadeTimings(out float fadeInSeconds, out float sustainSeconds, out float fadeOutSeconds)
    {
        fadeInSeconds = duration * fadeInFraction;
        fadeOutSeconds = duration * fadeOutFraction;
        sustainSeconds = Mathf.Max(0f, duration - fadeInSeconds - fadeOutSeconds);
    }

    // Applies only-safe fields to a TextMeshPro at runtime (optional helper).
    public void ApplyToTMP(TMP_Text text)
    {
        if (!text) return;
        text.font = font ? font : text.font;
        text.fontMaterial = fontMaterial ? fontMaterial : text.fontMaterial;
        text.fontSize = baseFontSize;
        text.color = defaultColor;
    }
}

```

## Assets/Scripts/Druid.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Druid : BaseCharacter
{

    protected float hpLvlIncrease = 5.8f;
    protected float minAtkLvlIncrease = 1.8f;
    protected float maxAtkLvlIncrease = 2.5f;

    protected float endLVLinc = 2.43f;
    protected float strLVLinc = 2.22f;
    protected float agiLVLinc = 1.65f;
    protected float witLVLinc = 4.31f;
    protected float chaLVLinc = 3.86f;

    public Druid(string name, int level)
        : base(name, "Druid", level, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 6;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 3;
        traits[TraitType.Wit] = 9;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Druid()
    : base("", "Druid", 5, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 3;
        traits[TraitType.Wit] = 6;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }

}

```

## Assets/Scripts/EndGame.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EndGame : MonoBehaviour
{
    private Pinball pinball;

    // Cache reference to the Pinball manager (singleton or tagged fallback).
    private void Awake()
    {
        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
    }

    // When a ball enters the drain, deactivate it and zero its motion (Play state only).
    private void OnCollisionEnter(Collision col)
    {
        var ball = col.gameObject.GetComponent<Ball>();
        if (!ball || pinball == null) return;

        if (pinball.CurrentState == PinballState.Play && ball.IsActive)
        {
            pinball.ballCount = Mathf.Max(0, pinball.ballCount - 1);
            ball.gameObject.SetActive(false);
        }

        var rb = col.gameObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
```

## Assets/Scripts/GameManager.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Combat;

public enum GameState
{
    InitLoad,
    ChooseCharacter,
    BeginTutorial,
    MainMenu,
    Pinball,
    MainStory,
    PreBattle,
    Battle,
    BattleFinished
}

public enum GameMode
{
    None,
    Tutorial,
    MainStory,
    Pinball
}


public class GameManager : MonoBehaviour
{

    protected CombatSystem combatSystem;

    private GameState currentState;
    public GameState CurrentState => currentState;

    private GameMode currentMode;
    public GameMode CurrentMode => currentMode;

    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;
    public Brawler brawler;


    protected BaseCharacter currentTurn;
    protected BaseCharacter tempChar;

    public CharacterUI ui;

    bool firstTurn;
    bool stopIt;

    protected int turnCount;

    private float actionTimer = 2f;
    public float actionDelay = 2f;

    public BaseCharacter player1;
    public BaseCharacter player2;


    // Start is called before the first frame update
    void Start()
    {
        ChangeState(GameState.InitLoad);
        ChangeMode(GameMode.None);

        /*warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);
        druid = new Druid("Lacroix", 6);
        assassin = new Assassin("Jinga", 5);
        tank = new Tank("Ronald", 7);
        player1 = warrior;
        */





    }

    // Update is called once per frame
    void Update()
    {

        //auto-battling function
        if(currentState == GameState.Battle)
        {
            if (combatSystem != null)
            {
                actionTimer += Time.deltaTime;
                if (actionTimer >= actionDelay)
                {
                    AutoBattle();
                    actionTimer = 0;
                }

                if (player1 != null && player2 != null)
                if (combatSystem.upcomingTurns.Count < 8 && (player1.Health.BaseValue > 0 && player2.Health.BaseValue > 0))
                {
                    combatSystem.DetermineTurns();
                }



                if (stopIt)
                    for (int i = 0; i < combatSystem.upcomingTurns.Count; i++)
                    {
                        Debug.Log($"Turn {i} - {combatSystem.upcomingTurns[i].name}");
                        if (i == 7)
                            stopIt = false;
                    }
            }
        }
        else if(currentState == GameState.BattleFinished)
        {
            actionTimer = 2;
            stopIt = false;
        }
    }

    public void ChangeState(GameState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case GameState.InitLoad:
                HandleInitLoad();
                break;
            case GameState.ChooseCharacter:
                HandleChooseCharacter();
                break;
            case GameState.MainMenu:
                HandleMainMenu();
                break;
            case GameState.Pinball:
                HandlePinball();
                break;
            case GameState.MainStory:
                HandleMainStory();
                break;
            case GameState.PreBattle:
                HandlePreBattle();
                break;
            case GameState.Battle:
                HandleBattle();
                break;
            case GameState.BattleFinished:
                HandleBattleFinished();
                break;
        }
    }

    public void ChangeMode(GameMode newMode)
    {
        currentMode = newMode;

        switch (newMode)
        {
            case GameMode.None:
                break;
            case GameMode.Tutorial:
                break;
            case GameMode.MainStory:
                break;
            case GameMode.Pinball:
                break;
        }
    }

    public void HandleInitLoad()
    {
        ui.HandleInitLoad();
    }
    public void HandleChooseCharacter()
    {
        ui.HandleChooseCharacter();
    }
    public void HandleMainMenu()
    {
        ui.DisableMenu();
        ui.HandleMainMenu();
    }

    public void HandleMainStory()
    {
        ui.HandleMainStory();
    }

    public void HandleBattleFinished()
    {
        ui.HandleBattleFinished();
    }

    public void HandlePreBattle()
    {
        if (currentMode == GameMode.Tutorial)
        {
            player2 = new BaseCharacter();
            player2.name = "Dummy";
            ui.SetCharacterUI(player2, false);

        }
        else if (currentMode == GameMode.MainStory)
        {
            player2 = new BaseCharacter();
            player2.name = "1-1";
            ui.SetCharacterUI(player2, false);
        }
        else Debug.Log($"Bozo");
            ui.SetCharacterUI(player1, true);
        ui.HandlePreBattle();
        combatSystem = new CombatSystem();
    }
    public void HandleBattle()
    {
        ui.HandleBattle();
    }

    public void HandlePinball()
    {
        ui.HandlePinball();
    }



    public void OnBeginButtonPressed()
    {
        ChangeState(GameState.ChooseCharacter);
    }

    public void OnHomeButtonPressed()
    {
        ChangeMode(GameMode.None);
        ChangeState(GameState.MainMenu);
    }
    public void OnMainStoryButtonPressed()
    {
        ChangeState(GameState.MainStory);
    }
    public void OnMSBattleButtonPressed()
    {
        ChangeMode(GameMode.MainStory);
        ChangeState(GameState.PreBattle);
    }

    public void OnWarriorButtonPressed()
    {
        tempChar = new Warrior();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnMageButtonPressed()
    {
        tempChar = new Mage();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnDruidButtonPressed()
    {
        tempChar = new Druid();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnTankButtonPressed()
    {
        tempChar = new Tank();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnAssassinButtonPressed()
    {
        tempChar = new Assassin();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnBrawlerButtonPressed()
    {
        tempChar = new Brawler();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnCharConfirmButtonPressed()
    {
        if(tempChar != null)
        {
            player1 = tempChar;
            ui.HandleInitName();
        }
    }
    public void OnNameConfirmButtonPressed()
    {
        ui.SetPlayerName(player1);
        ChangeMode(GameMode.Tutorial);
        ChangeState(GameState.PreBattle);
    }

    public void OnStartBattleButtonPressed()
    {
        ChangeState(GameState.Battle);
    }

    public void StartBattle()
    {
        ChangeState(GameState.Battle);
    }


    public void AutoBattle()
    {
        turnCount++;
        if(!firstTurn)
        {
            //player2 = GenerateEnemy(player1);
            combatSystem.Initialize(player1, player2);
            currentTurn = combatSystem.DetermineFirstTurn();
            firstTurn = true;
        }


        stopIt = true;
        HandleTurn();
    }

    public void FinishBattle(BaseCharacter winner)
    {
        //StopAutoBattle();
        if(winner == player1)
            ui.winner = true;
        else
            ui.winner = false;
        ChangeState(GameState.BattleFinished);
        turnCount = 0;
    }


    public void HandleTurn()
    {
        if(turnCount >= 2)
        {
            currentTurn = combatSystem.upcomingTurns[0];
            combatSystem.upcomingTurns.RemoveAt(0);
        }

        BaseCharacter attacker = currentTurn;
        BaseCharacter defender = currentTurn == player1 ? player2 : player1;

        combatSystem.ExecuteAttack(attacker, defender);
        ui.SetCharacterUI(player1, true);
        ui.SetCharacterUI(player2, false);

        //the attacker killed the defender first
        if(defender.Health.BaseValue <= 0)
        {
            FinishBattle(attacker);
        }
        //the attacker hit the defender, but the defender survived and the attacker died to recoil dmg at some point
        else if(attacker.Health.BaseValue <= 0 && defender.Health.BaseValue > 0)
        {
            FinishBattle(defender);
        }


    }



    private BaseCharacter GenerateEnemy(BaseCharacter player1)
    {
        string[] allClasses = new string[] { "Warrior", "Mage", "Assassin", "Druid", "Tank" };
        string chosenClass = allClasses[Random.Range(0, allClasses.Length)];
        BaseCharacter player2 = BaseCharacter.CreateCharacterFromClass(chosenClass);

        player2.RandomizeCharacter(player1.level, player1.stats, player1.traits);

        Debug.Log(player2.Health.BaseValue);
        ui.SetCharacterUI(player2, false);

        return player2;

    }

}

```

## Assets/Scripts/IDamageNumberSystem.cs

```csharp
using UnityEngine;

/// Spawns a floating damage number at a world position.
public interface IDamageNumberSystem
{
    /// Spawn a damage number with optional color override at a world position.
    void Spawn(float amount, Vector3 position, Color? overrideColor = null);
}

/// Static facade so gameplay code stays decoupled from the UI/animation system.
public static class DamageNumbers
{
    /// Register the active implementation once on startup (e.g., TweenDamageNumberSystem).
    public static IDamageNumberSystem System { get; private set; }

    /// Returns true if an implementation has registered.
    public static bool IsReady => System != null;

    /// Register/replace the active damage number system.
    public static void Register(IDamageNumberSystem system) => System = system;

    /// Try to spawn a damage number if a system is registered.
    public static void Spawn(float amount, Vector3 position, Color? overrideColor = null)
        => System?.Spawn(amount, position, overrideColor);
}

```

## Assets/Scripts/IPowerup.cs

```csharp
using UnityEngine;

/// Contract for powerups that can trigger during pinball play.
public interface IPowerup
{
    // Unique identifier for save/run tracking
    string Id { get; }

    // Relative selection weight in RNG pools
    float Weight { get; }

    // Human-friendly debug label for logs/inspector
    string DebugLabel { get; }

    // Returns true if the powerup is eligible to trigger in the current run context
    bool CanTrigger(IRunContext ctx);

    // Executes the powerup at a world position with access to Pinball systems
    void Execute(Pinball pm, Vector3 triggerPos);
}

```

## Assets/Scripts/IRunContext.cs

```csharp
using System.Collections.Generic;

/// Abstraction for �current run� state & effect application (implemented by Pinball).
public interface IRunContext
{
    // ��� Ownership / Availability / Exclusivity ���

    /// Returns true if the player already owns the reward (regardless of active state).
    bool Owns(string rewardId);

    /// Returns true if the reward is currently active (its effects are applied).
    bool IsActive(string rewardId);

    /// Returns true if the reward exists in the current pool of available rewards.
    bool IsAvailable(string rewardId);

    /// Returns true if an exclusive key is currently active in the run.
    bool HasExclusiveKeyActive(string key);

    /// Gets the set of currently active exclusivity keys.
    IEnumerable<string> ActiveKeys { get; }

    /// Marks a reward as owned by the player this run.
    void MarkOwned(string rewardId);

    /// Toggles a reward�s active state on/off.
    void SetActive(string rewardId, bool on);

    /// Toggles a reward�s availability in the current pool.
    void SetAvailable(string rewardId, bool on);

    /// Toggles an exclusivity key on/off for mutual exclusion groups.
    void SetExclusive(string key, bool on);


    // ��� Run Resources (Lives) ���

    /// Current number of lives remaining in this run.
    int Lives { get; }

    /// Maximum number of lives allowed this run.
    int MaxLives { get; }

    /// Grants additional lives (implementation should clamp to MaxLives).
    void ApplyGrantedLives(int amount);


    // ��� Scoring / XP Multipliers & Timed Bonuses ���

    /// Applies a score multiplier; cursed variants can invert/penalize internally.
    void ApplyScoreMultiplier(float multiplier, bool isCursed);

    /// Applies an XP multiplier; cursed variants can invert/penalize internally.
    void ApplyXPMultiplier(float multiplier, bool isCursed);

    /// Starts/extends a timed score bonus window (in seconds).
    void ApplyScoreBonusTime(float time, bool isCursed);

    /// Starts/extends a timed XP bonus window (in seconds).
    void ApplyXPBonusTime(float time, bool isCursed);


    // ��� Ball Size/Speed/Bounce FX ���

    /// Applies a �shrink� profile to the ball (size, speed, bounciness, etc.).
    void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);

    /// Applies a �grow� profile to the ball (size, speed, bounciness, etc.).
    void ApplyGrowFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);


    // ��� Damage Hooks ���

    /// Applies an immediate damage effect to valid targets.
    void ApplyDamageFX(float amount);

    /// Applies �damage per bounce� effect with a bounce threshold.
    void ApplyDmgPerBounceFX(float damageMult, int bouncesNeeded);


    // ��� Utility / Misc FX ���

    /// Grows XP pickup radius / attraction force field.
    void ApplyXPForcefield(float radiusIncrease);

    /// Spawns additional active balls into play.
    void ApplyAdditionalBalls(int additionalBalls);
}

```

## Assets/Scripts/Item.cs

```csharp
public class Item
{
    public void Equip(CharacterStat c)
    {
        c.AddModifier(new StatModifier(10, StatModType.Flat, this));
        c.AddModifier(new StatModifier(.1f, StatModType.PercentMult, this));
    }

    public void Unequip(CharacterStat c)
    {
        c.RemoveAllModifiersFromSource(this);
    }

}

```

## Assets/Scripts/Level Up Rewards/BallDuplicateRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Duplicate FX", fileName = "BallDuplicateReward")]
public sealed class BallDuplicateRewardSO : RewardSO
{
    [Header("Duplication")]
    [SerializeField, Min(1)] private int additionalBalls = 1;
    [SerializeField] private bool cursed = false;

    // Spawns extra balls (multi-ball) and flags ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        // Pinball implements duplication logic behind this call.
        ctx.ApplyAdditionalBalls(additionalBalls);
    }

    // Uses global eligibility + prevents 'cursed' when on last life.
    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx)) return false;

        // Simple guard to avoid soft-locking on last life if cursed.
        if (cursed && ctx.Lives <= 1) return false;

        return true;
    }
}

```

## Assets/Scripts/Level Up Rewards/BallGrowRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Grow", fileName = "BallGrowReward")]
public sealed class BallGrowRewardSO : RewardSO
{
    [Header("Grow FX")]
    [SerializeField, Min(0f)] private float size = 10f;
    [SerializeField, Range(-100f, 100f)] private float speed = 0f; // �% change
    [SerializeField, Range(-100f, 100f)] private float scoreMultiplier = 0f; // �% change
    [SerializeField, Min(0f)] private float bonusHits = 0f;
    [SerializeField, Range(-100f, 100f)] private float bounciness = 0f; // �% change
    [SerializeField, Min(0)] private int bouncesForBonusHits = 0;
    [SerializeField] private bool grantBonusWindow = false;
    [SerializeField] private bool cursed = false;

    // Applies growth FX via the run context and marks this reward state.
    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        // ctx.ApplyGrowFX(size, speed, bounciness, scoreMult, bonusHits, bounces, bonus, isCursed)
        ctx.ApplyGrowFX(size, speed, bounciness, scoreMultiplier, bonusHits, bouncesForBonusHits, grantBonusWindow, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/BallShrinkRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Shrink", fileName = "BallShrinkReward")]
public sealed class BallShrinkRewardSO : RewardSO
{
    [Header("Shrink FX")]
    [SerializeField, Min(0f)] private float size = 10f;
    [SerializeField, Range(-100f, 100f)] private float speed = 0f; // �% change
    [SerializeField, Range(-100f, 100f)] private float scoreMultiplier = 0f; // �% change
    [SerializeField, Min(0f)] private float bonusHits = 0f;
    [SerializeField, Range(-100f, 100f)] private float bounciness = 0f; // �% change
    [SerializeField, Min(0)] private int bouncesForBonusHits = 0;
    [SerializeField] private bool grantBonusWindow = false;
    [SerializeField] private bool cursed = false;

    // Applies shrink FX via the run context and marks this reward state.
    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        // ctx.ApplyShrinkFX(size, speed, bounciness, scoreMult, bonusHits, bounces, bonus, isCursed)
        ctx.ApplyShrinkFX(size, speed, bounciness, scoreMultiplier, bonusHits, bouncesForBonusHits, grantBonusWindow, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/DamagePerBounceRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/DmgPerBounce", fileName = "DamagePerBounceReward")]
public sealed class DamagePerBounceRewardSO : RewardSO
{
    [Header("Per-Bounce")]
    [SerializeField, Min(0f)] private float damageMult = 10f;
    [SerializeField, Min(1)] private int bouncesNeeded = 1;

    // Grants a damage boost that triggers after N bounces; marks ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);                 // preserved from your original
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyDmgPerBounceFX(damageMult, bouncesNeeded);
    }
}

```

## Assets/Scripts/Level Up Rewards/DamageRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Damage", fileName = "DamageReward")]
public sealed class DamageRewardSO : RewardSO
{
    [Header("Damage")]
    [SerializeField, Min(0f)] private float damageMult = 1f;

    // Applies a persistent global damage multiplier and marks ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);                 // preserved from your original
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyDamageFX(damageMult);
    }
}

```

## Assets/Scripts/Level Up Rewards/EarthPaddleRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Earth Paddle FX", fileName = "EarthPaddleReward")]
public sealed class EarthPaddleRewardSO : RewardSO
{
    [Header("Earth FX")]
    [SerializeField, Min(0)] private int fissureDamage = 1;
    [SerializeField, Min(0f)] private float crustedDuration = 1f;
    [SerializeField, Min(0f)] private float fissureHitScoreMultiplier = 1f;
    [SerializeField, Min(0f)] private float fissureHitXPMultiplier = 1f;
    [SerializeField, Min(0)] private int bounceDuration = 1;
    [SerializeField] private bool cursed = false;

    // Marks ownership/activation and flags as a paddle reward.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;
    }

    // Applies Earth parameters to the target paddle's elemental state.
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        if (!paddle) return;
        paddle.ApplyEarth(fissureDamage, crustedDuration, fissureHitScoreMultiplier, fissureHitXPMultiplier, bounceDuration, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/ElectricPaddleRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Electric Paddle FX", fileName = "ElectricPaddleReward")]
public sealed class ElectricPaddleRewardSO : RewardSO
{
    [Header("Electric FX")]
    [SerializeField, Min(0)] private int shockDamage = 1;
    [SerializeField, Min(1)] private int chainCount = 1;
    [SerializeField, Min(0)] private int bounceDuration = 1;
    [SerializeField, Min(0f)] private float xpBonus = 0.1f;
    [SerializeField, Min(0f)] private float scoreBonus = 0.1f;
    [SerializeField] private bool cursed = false;

    // Marks ownership/activation and flags as a paddle reward.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;
    }

    // Applies Electric parameters to the target paddle's elemental state.
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        if (!paddle) return;
        paddle.ApplyElectric(shockDamage, chainCount, xpBonus, scoreBonus, bounceDuration, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/FirePaddleRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Fire Paddle FX", fileName = "FirePaddleReward")]
public sealed class FirePaddleRewardSO : RewardSO
{
    [Header("Fire FX")]
    [SerializeField, Min(0)] private int bonusDamageFlat = 1;
    [SerializeField, Min(0f)] private float burnDamage = 1f;
    [SerializeField, Min(0f)] private float burnDuration = 1f;
    [SerializeField, Min(0)] private int explosionDamageFlat = 1;
    [SerializeField, Min(0)] private int bounceDuration = 1;
    [SerializeField, Min(0f)] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
    [SerializeField] private bool cursed = false;

    // Marks ownership/activation and flags as a paddle reward.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;
    }

    // Applies Fire parameters to the target paddle's elemental state.
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        if (!paddle) return;
        paddle.ApplyFire(bonusDamageFlat, burnDamage, burnDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/LifeRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Life Reward", fileName = "LifeReward")]
public sealed class LifeRewardSO : RewardSO
{
    // Optional manual override; leave 0 to use rarity mapping
    [SerializeField, Tooltip("Override grant amount. Leave 0 to use the rarity-based amount.")]
    private int overrideAmount = 0;

    // Computes lives granted based on rarity (or override if provided).
    private int Amount =>
        overrideAmount > 0 ? overrideAmount :
        Rarity switch
        {
            RewardRarity.Rare => 1,
            RewardRarity.Epic => 2,
            RewardRarity.Legendary => 3,
            RewardRarity.Artifact => 4,
            _ => 1
        };

    // Ensures this reward is only offered when it won't exceed MaxLives.
    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx)) return false;
        if (ctx == null || ctx.MaxLives <= 0) return false;
        if (ctx.Lives >= ctx.MaxLives) return false;
        if (ctx.Lives + Amount > ctx.MaxLives) return false;
        return true;
    }

    // Grants lives through the run context (manager clamps internally as well).
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.ApplyGrantedLives(Amount);
    }
}

```

## Assets/Scripts/Level Up Rewards/ScoreMultiplierRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Score Multiplier", fileName = "ScoreMultiplierReward")]
public sealed class ScoreMultiplierRewardSO : RewardSO
{
    [Header("Score Bonus")]
    [SerializeField, Min(0f)] private float multiplier = 1f;
    [SerializeField, Min(0f)] private float bonusTime = 30f;
    [SerializeField] private bool cursed = false;

    // Applies score multiplier + bonus time and marks run ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyScoreMultiplier(multiplier, cursed);
        ctx.ApplyScoreBonusTime(bonusTime, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/WaterPaddleRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Water Paddle FX", fileName = "WaterPaddleReward")]
public sealed class WaterPaddleRewardSO : RewardSO
{
    [Header("Water FX")]
    [SerializeField, Min(0f)] private float bonusXPPerc = 1f;
    [SerializeField, Min(0)] private int bonusDamageFlat = 1;
    [SerializeField, Min(0f)] private float drenchDuration = 1f;
    [SerializeField, Min(0)] private int explosionDamageFlat = 1;
    [SerializeField, Min(0)] private int bounceDuration = 1;
    [SerializeField, Min(0f)] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
    [SerializeField] private bool cursed = false;

    // Marks ownership/activation and flags as a paddle reward.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;
    }

    // Applies Water parameters to the target paddle's elemental state.
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        if (!paddle) return;
        paddle.ApplyWater(bonusXPPerc, bonusDamageFlat, drenchDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/XPGravityRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/XP Gravity")]
public class XPGravityRewardSO : RewardSO
{
    [SerializeField] private float radiusIncrease = 1f;

    // Expands XP pickup forcefield radius and marks run ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true); // kept per your original
        ctx.SetExclusive(ExclusivityKey, true);
        ctx.ApplyXPForcefield(radiusIncrease);
    }
}

```

## Assets/Scripts/Level Up Rewards/XPMultiplierRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/XP Multiplier")]
public class XPMultiplierRewardSO : RewardSO
{
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusTime = 30f;
    [SerializeField] private bool cursed = false;

    // Applies XP multiplier + bonus time and marks run ownership/activation.
    public override void Apply(IRunContext ctx)
    {
        if (ctx == null) return;
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);
        ctx.ApplyXPMultiplier(multiplier, cursed);
        ctx.ApplyXPBonusTime(bonusTime, cursed);
    }
}

```

## Assets/Scripts/Mage.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mage : BaseCharacter
{

    protected float hpLvlIncrease = 4.9f;
    protected float minAtkLvlIncrease = 2.6f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.54f;
    protected float strLVLinc = 2.26f;
    protected float agiLVLinc = 1.69f;
    protected float witLVLinc = 4.65f;
    protected float chaLVLinc = 3.84f;

    public Mage(string name, int level)
        : base(name, "Mage", level, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 10;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Mage()
    : base("", "Mage", 5, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 9;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }
}

```

## Assets/Scripts/NukeBumpersPowerup.cs

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NukeBumpersPowerup : IPowerup
{
    public string Id => "nuke-bumpers";
    public float Weight => 0.6f;
    public string DebugLabel => "Nuke Bumpers";

    // Always eligible; pickup roll occurs only during active play.
    public bool CanTrigger(IRunContext ctx) => true;

    // Deals percentage damage to all bumpers; awards score and XP at 75% of the ball�s current damage factor for balance.
    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        // Use any active ball�s factor; fall back to 1x. Then apply the 0.75 debuff for balance.
        float damageFactor = 1f;
        var anchor = pinball.ball;
        if (anchor && anchor.isActiveAndEnabled && anchor.IsActive) damageFactor = anchor.ScoreXpDamageFactor;
        else
        {
            var any = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < any.Length; i++)
            {
                if (any[i] && any[i].isActiveAndEnabled && any[i].IsActive)
                {
                    damageFactor = any[i].ScoreXpDamageFactor;
                    break;
                }
            }
        }
        const float NUKE_DEBUFF = 0.75f;
        float awardFactor = Mathf.Max(0f, damageFactor * NUKE_DEBUFF);

        const float percent = 0.35f;  // 35% of current HP
        const float minDamage = 10f;

        foreach (var bumper in Bumper.EnumerateAll())
        {
            if (!bumper) continue;

            // Apply damage (XP handled inside TakeDamage via passed factor).
            float amount = Mathf.Max(minDamage, bumper.curHealth * percent);
            bumper.TakeDamage(amount, elemDmg: false, damageFactor: awardFactor);

            // Award score per affected bumper, using tiered base similar to bumpers, scaled by awardFactor.
            int baseScore = bumper.type == BumperType.Small ? 50 : 100;
            pinball.AddScore(baseScore, bumpCount: 0, bumpCountConsec: 0, damageFactor: awardFactor);
        }

        pinball.ScreenShake();
    }
}
```

## Assets/Scripts/PaddleEffectData.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleEffectData
{
    public  PaddleState Element;

    // Fire fields (extend later for other elements)
    public  int FireBonusDamage;
    public  float FireBurnDamage;
    public  float FireBurnDuration;
    public  int FireBounceDuration;
    public  bool FireCanExplode;
    public  float FireExplosionSize;
    public  int FireExplosionDamageFlat;
    public  bool FireIsCursed;

    // Water fields (extend later for other elements)
    public  float WaterBonusXP;
    public  int WaterDamageFlat;
    public  float WaterDrenchDuration;
    public  int WaterBounceDuration;
    public  bool WaterCanBurst;
    public  float WaterBurstSize;
    public readonly int WaterBurstDamageFlat;
    public bool WaterIsCursed;

    public  int EarthBonusDamage;
    public  float EarthFissureDuration;
    public  float EarthXPBonus;
    public  float EarthScoreBonus;
    public  int EarthBounceDuration;
    public  bool EarthIsCursed;

    public  int ElectricShockDamage;
    public  int ElectricChainCount;
    public  float ElectricXPBonus;
    public  float ElectricScoreBonus;
    public  int ElectricBounceDuration;
    public  bool ElectricIsCursed;

    public PaddleEffectData(
        PaddleState element,
        int fireBonusDamage = 0,
        float fireBurnDamage = 0f,
        float fireBurnDuration = 0f,
        int fireBounceDuration = 0,
        bool fireCanExplode = false,
        float fireExplosionSize = 0f,
        int fireExplosionDamageFlat = 0,
        bool fireIsCursed = false,

        float waterBonusXP = 0,
        int waterDamageFlat = 0,
        float waterDrenchDuration = 0f,
        int waterBounceDuration = 0,
        bool waterCanBurst = false,
        float waterBurstSize = 0f,
        int waterBurstDamageFlat = 0,
        bool waterIsCursed = false,

                int earthBonusDamage = 0,
        float earthFissureDuration = 0f,
        float earthXPBonus = 0f,
        float earthScoreBonus = 0f,
        int earthBounceDuration = 0,
        bool earthIsCursed = false,
        
        int electricShockDamage = 0,
        int electricChainCount = 0,
        float electricXPBonus = 0f,
        float electricScoreBonus = 0f,
        int electricBounceDuration = 0,
        bool electricIsCursed = false
        )
    {
        Element = element;
        FireBonusDamage = fireBonusDamage;
        FireBurnDamage = fireBurnDamage;
        FireBurnDuration = fireBurnDuration;
        FireBounceDuration = fireBounceDuration;
        FireCanExplode = fireCanExplode;
        FireExplosionSize = fireExplosionSize;
        FireExplosionDamageFlat = fireExplosionDamageFlat;
        FireIsCursed = fireIsCursed;

        WaterBonusXP = waterBonusXP;
        WaterDamageFlat = waterDamageFlat;
        WaterDrenchDuration = waterDrenchDuration;
        WaterBounceDuration = waterBounceDuration;
        WaterCanBurst = waterCanBurst;
        WaterBurstSize = waterBurstSize;
        WaterBurstDamageFlat = waterBurstDamageFlat;
        WaterIsCursed = waterIsCursed;

        EarthBonusDamage = earthBonusDamage;
        EarthFissureDuration = earthFissureDuration;
        EarthXPBonus = earthXPBonus;
        EarthScoreBonus = earthScoreBonus;
        EarthBounceDuration = earthBounceDuration;
        EarthIsCursed = earthIsCursed;

        ElectricShockDamage = electricShockDamage;
        ElectricChainCount = electricChainCount;
        ElectricXPBonus = electricXPBonus;
        ElectricScoreBonus = electricScoreBonus;
        ElectricBounceDuration = electricBounceDuration;
        ElectricIsCursed = electricIsCursed;
    }
}

```

## Assets/Scripts/PaddleElementalState.cs

```csharp
using UnityEngine;

/// Tracks and applies elemental states for a paddle (one-shot bounce windows, bonuses, curses).
[DisallowMultipleComponent]
public sealed class PaddleElementalState : MonoBehaviour
{
    public enum PaddleState { None, Fire, Water, Earth, Electric }

    [Header("Runtime")]
    [SerializeField] private PaddleState current = PaddleState.None;

    // Fire
    [SerializeField] private int fireBonusFlat = 0;
    [SerializeField] private float fireBurnDamage = 0f;
    [SerializeField] private float fireBurnDuration = 0f;
    [SerializeField] private bool fireCanExplode = false;
    [SerializeField] private float fireExplosionSize = 1f;
    [SerializeField] private int fireExplosionDamageFlat = 0;
    [SerializeField] private bool fireIsCursed = false;
    [SerializeField] private int fireBounceDuration = 0;

    // Water
    [SerializeField] private float waterBonusXP = 0f;
    [SerializeField] private int waterDamageFlat = 0;
    [SerializeField] private float waterDrenchDuration = 0f;
    [SerializeField] private bool waterCanBurst = false;
    [SerializeField] private float waterBurstSize = 1f;
    [SerializeField] private int waterBurstDamageFlat = 0;
    [SerializeField] private bool waterIsCursed = false;
    [SerializeField] private int waterBounceDuration = 0;

    // Earth
    [SerializeField] private int earthFissureDamage = 0;
    [SerializeField] private float earthFissureDuration = 0f;
    [SerializeField] private float earthXPBonus = 0f;
    [SerializeField] private float earthScoreBonus = 0f;
    [SerializeField] private bool earthIsCursed = false;
    [SerializeField] private int earthBounceDuration = 0;

    // Electric
    [SerializeField] private int electricShockDamage = 0;
    [SerializeField] private int electricChainCount = 1;
    [SerializeField] private float electricXPBonus = 0f;
    [SerializeField] private float electricScoreBonus = 0f;
    [SerializeField] private bool electricIsCursed = false;
    [SerializeField] private int electricBounceDuration = 0;

    // Expose state to other systems
    public PaddleState CurrentState => current;

    // Applies Fire parameters from rewards/selection
    public void ApplyFire(int bonusDamageFlat, float burnDamage, float burnDuration, int bounceDuration,
                          bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        current = PaddleState.Fire;
        fireBonusFlat = Mathf.Max(0, bonusDamageFlat);
        fireBurnDamage = Mathf.Max(0f, burnDamage);
        fireBurnDuration = Mathf.Max(0f, burnDuration);
        fireBounceDuration = Mathf.Max(0, bounceDuration);
        fireCanExplode = canExplode;
        fireExplosionSize = Mathf.Max(0f, explosionSize);
        fireExplosionDamageFlat = Mathf.Max(0, explosionDamageFlat);
        fireIsCursed = cursed;
    }

    // Applies Water parameters from rewards/selection
    public void ApplyWater(float bonusXP, int damageFlat, float drenchDuration, int bounceDuration,
                           bool canBurst, float burstSize, int burstDamageFlat, bool cursed)
    {
        current = PaddleState.Water;
        waterBonusXP = Mathf.Max(0f, bonusXP);
        waterDamageFlat = Mathf.Max(0, damageFlat);
        waterDrenchDuration = Mathf.Max(0f, drenchDuration);
        waterBounceDuration = Mathf.Max(0, bounceDuration);
        waterCanBurst = canBurst;
        waterBurstSize = Mathf.Max(0f, burstSize);
        waterBurstDamageFlat = Mathf.Max(0, burstDamageFlat);
        waterIsCursed = cursed;
    }

    // Applies Earth parameters from rewards/selection
    public void ApplyEarth(int fissureDamage, float fissureDuration, float xpBonus, float scoreBonus,
                           int bounceDuration, bool cursed)
    {
        current = PaddleState.Earth;
        earthFissureDamage = Mathf.Max(0, fissureDamage);
        earthFissureDuration = Mathf.Max(0f, fissureDuration);
        earthXPBonus = Mathf.Max(0f, xpBonus);
        earthScoreBonus = Mathf.Max(0f, scoreBonus);
        earthBounceDuration = Mathf.Max(0, bounceDuration);
        earthIsCursed = cursed;
    }

    // Applies Electric parameters from rewards/selection
    public void ApplyElectric(int shockDamage, int chainCount, float xpBonus, float scoreBonus,
                              int bounceDuration, bool cursed)
    {
        current = PaddleState.Electric;
        electricShockDamage = Mathf.Max(0, shockDamage);
        electricChainCount = Mathf.Max(1, chainCount);
        electricXPBonus = Mathf.Max(0f, xpBonus);
        electricScoreBonus = Mathf.Max(0f, scoreBonus);
        electricBounceDuration = Mathf.Max(0, bounceDuration);
        electricIsCursed = cursed;
    }

    // Clears all elemental parameters and returns to neutral
    public void Clear()
    {
        current = PaddleState.None;
        fireBonusFlat = fireExplosionDamageFlat = 0;
        fireBurnDamage = fireBurnDuration = 0f;
        fireCanExplode = fireIsCursed = false;
        fireExplosionSize = 1f; fireBounceDuration = 0;

        waterBonusXP = waterDrenchDuration = 0f;
        waterDamageFlat = waterBurstDamageFlat = 0;
        waterCanBurst = waterIsCursed = false;
        waterBurstSize = 1f; waterBounceDuration = 0;

        earthFissureDamage = 0; earthFissureDuration = 0f;
        earthXPBonus = earthScoreBonus = 0f;
        earthIsCursed = false; earthBounceDuration = 0;

        electricShockDamage = 0; electricChainCount = 1;
        electricXPBonus = electricScoreBonus = 0f;
        electricIsCursed = false; electricBounceDuration = 0;
    }

    // Returns the current bounce-duration for the active element
    public int GetBounceDuration()
    {
        return current switch
        {
            PaddleState.Fire => fireBounceDuration,
            PaddleState.Water => waterBounceDuration,
            PaddleState.Earth => earthBounceDuration,
            PaddleState.Electric => electricBounceDuration,
            _ => 0
        };
    }

    // Copies element-specific hit effects into a struct usable by Ball/Bumper logic
    public PaddleEffectData ToEffectData()
    {
        var e = new PaddleEffectData { Element = (PaddleEffectData.PaddleState)current };

        // Fire
        e.FireBonusDamage = fireBonusFlat;
        e.FireBurnDamage = fireBurnDamage;
        e.FireBurnDuration = fireBurnDuration;
        e.FireBounceDuration = fireBounceDuration;
        e.FireCanExplode = fireCanExplode;
        e.FireExplosionSize = fireExplosionSize;
        e.FireExplosionDamageFlat = fireExplosionDamageFlat;
        e.FireIsCursed = fireIsCursed;

        // Water
        e.WaterBonusXP = waterBonusXP;
        e.WaterDamageFlat = waterDamageFlat;
        e.WaterDrenchDuration = waterDrenchDuration;
        e.WaterBounceDuration = waterBounceDuration;
        e.WaterCanBurst = waterCanBurst;
        e.WaterBurstSize = waterBurstSize;
        e.WaterBurstDamageFlat = waterBurstDamageFlat;
        e.WaterIsCursed = waterIsCursed;

        // Earth
        e.EarthBonusDamage = earthFissureDamage;
        e.EarthFissureDuration = earthFissureDuration;
        e.EarthXPBonus = earthXPBonus;
        e.EarthScoreBonus = earthScoreBonus;
        e.EarthBounceDuration = earthBounceDuration;
        e.EarthIsCursed = earthIsCursed;

        // Electric
        e.ElectricShockDamage = electricShockDamage;
        e.ElectricChainCount = electricChainCount;
        e.ElectricXPBonus = electricXPBonus;
        e.ElectricScoreBonus = electricScoreBonus;
        e.ElectricBounceDuration = electricBounceDuration;
        e.ElectricIsCursed = electricIsCursed;

        return e;
    }
}

```

## Assets/Scripts/PaddleElements.cs

```csharp
using UnityEngine;

public enum PaddleState
{
    None,
    Fire,
    Water,
    Earth,
    Air,
    Electric
}
```

## Assets/Scripts/Pinball.cs

```csharp
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

/// Pinball gameplay state machine
public enum PinballState
{
    None,
    Charging,   // player holds to charge the initial launch
    Push,       // short pre-launch "push" window inside the tube
    Play,       // normal play
    LevelUp,    // reward selection
    PaddleSelect,
    ResetBall,  // lose a ball, respawn or end
    GameOver
}

[DisallowMultipleComponent]
public class Pinball : MonoBehaviour, IRunContext
{
    public static Pinball Instance { get; private set; }

    #region Reward/RunContext state (ownership, activation, exclusivity)

    private readonly HashSet<string> owned = new();
    private readonly HashSet<string> active = new();
    private readonly HashSet<string> available = new();
    private readonly HashSet<string> exclusiveKeysActive = new();

    public bool Owns(string rewardId) => owned.Contains(rewardId);
    public bool IsActive(string rewardId) => active.Contains(rewardId);
    public bool IsAvailable(string rewardId) => available.Contains(rewardId);
    public bool HasExclusiveKeyActive(string key) =>
        !string.IsNullOrEmpty(key) && exclusiveKeysActive.Contains(key);
    public IEnumerable<string> ActiveKeys => exclusiveKeysActive;

    public void MarkOwned(string rewardId) => owned.Add(rewardId);
    public void SetActive(string rewardId, bool on) { if (on) active.Add(rewardId); else active.Remove(rewardId); }
    public void SetAvailable(string rewardId, bool on) { if (on) available.Add(rewardId); else available.Remove(rewardId); }
    public void SetExclusive(string key, bool on)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (on) exclusiveKeysActive.Add(key); else exclusiveKeysActive.Remove(key);
    }

    private static readonly Dictionary<RewardRarity, float> rarityWeights = new()
    {
        {RewardRarity.Common, 88f},
        {RewardRarity.Uncommon, 52f},
        {RewardRarity.Rare, 25f},
        {RewardRarity.Epic, 12f},
        {RewardRarity.Legendary, 3f},
        {RewardRarity.Artifact, .5f},
        {RewardRarity.Cursed, .5f},
    };
    #endregion

    #region Serialized configuration

    [Header("Input")]
    [SerializeField] private KeyCode chargeKey = KeyCode.Space;
    [SerializeField] private KeyCode leftPaddleKey = KeyCode.A;
    [SerializeField] private KeyCode rightPaddleKey = KeyCode.D;

    [Header("Progression")]
    [Min(0)] public float curXP = 0;
    [Min(1)] public float maxXP;
    public int level { get; private set; } = 1;

    [Header("References")]
    [SerializeField] public Ball ball;           // primary ball anchor (kept alive across respawns)
    [SerializeField] private PinballUIM ui;
    [SerializeField] private GameObject ballClonePrefab;
    [SerializeField] private GameObject invisWalls;
    [SerializeField] private PinballFlipper leftPaddle;
    [SerializeField] private PinballFlipper rightPaddle;

    [Header("Charge/Push Tuning")]
    [SerializeField, Min(0.05f)] private float chargeMaxCharging = 1.5f;
    [SerializeField, Min(0.05f)] private float chargeMaxPush = 1.0f;
    [SerializeField, Range(0f, 1f)] private float minChargeToLaunch = 0.10f;
    [SerializeField, Range(0f, 1f)] private float chargeToEnterPush = 0.65f;

    [Header("Balls / Lives")]
    [SerializeField, Range(0, 99)] private int startingLives = 3;
    [SerializeField, Range(1, 99)] private int maxLives = 5;
    public int Lives => lives;
    public int MaxLives => maxLives;

    [Header("Score/XP Multipliers")]
    [SerializeField] private float baseScoreMult = 1f;
    [SerializeField] private float baseXPMult = 1f;

    [Header("Powerups & Drops")]
    [SerializeField, Range(0f, 1f)]
    private float powerupDropChance = .03f;
    public float PowerupDropChance => powerupDropChance;

    [Header("Visual FX")]
    [SerializeField] private Camera mainCam;
    [SerializeField, Min(0f)] private float shakeDuration = 0.15f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.3f;
    [SerializeField, Min(1)] private int shakeVibrato = 12;
    [SerializeField, Range(0f, 180f)] private float shakeRandomness = 90f;
    [SerializeField] private ParticleSystem xpFXPrefab;

    [Header("UX/UI")]
    [SerializeField] private BallXPBar ballXPScript;

    [Header("Bumper Death FX")]
    [SerializeField] private float explosionForce = 50f;
    [SerializeField] private float explosionRadius = 3f;

    [Header("Global Physics")]
    [SerializeField] private bool overrideGlobalGravity = true;
    [SerializeField] private Vector3 gravityOverride = new Vector3(0f, 0f, -19.62f);

    #endregion

    #region Runtime state

    private PinballState currentState;
    public PinballState CurrentState => currentState;

    // Charge state (transient)
    private bool isHoldingCharge;
    private float chargeTimer;
    public float chargePercentage { get; private set; }
    private float chargeMax;

    // Score/XP & flow
    private int score;
    private int mult;
    private float scoreMultiplier = 1f;
    private float xpMultiplier = 1f;
    private float scoreBonusTimer;
    private float xpBonusTimer;
    public int Score => score;
    public int Mult => mult;
    public float ScoreMultiplier => scoreMultiplier;
    public bool IsScoreMultiplierActive => scoreMultiplier > 1f;
    public float ScoreBonusTimeRemaining => scoreBonusTimer;
    public float XPMultiplier => xpMultiplier;
    public bool IsXPMultiplierActive => xpMultiplier > 1f;
    public float XPBonusTimeRemaining => xpBonusTimer;

    // Paddles elemental window
    private bool canHitL, canHitR, hasBeenHitL, hasBeenHitR;

    // Ball tracking
    private readonly List<Ball> liveBalls = new();
    private int _primaryBallId;
    public int ballCount { get; private set; }
    private int lives;

    // "No ball" debounce in Play
    [SerializeField, Min(0f)] private float noBallResetGrace = 0.20f;
    private float _noBallTimer;

    // Respawn positioning
    private Vector3 _ballStartPos;
    private Quaternion _ballStartRot;

    // Camera shake reset
    private Vector3 _camDefaultLocalPos;
    private Quaternion _camDefaultLocalRot;

    // Reward/Level-up
    private int pendingLevelUps = 0;
    private RewardSO pendingPaddleReward;
    private readonly List<RewardSO> rewardPool = new();

    // Misc gameplay
    public bool destroyedBumperBonusActive;
    private float destroyedBumperScoreMult = 2f;
    private int xpCount = 3;
    private bool wallTimerStart;
    private float wallTimer;

    // Extra “bonus hits”
    private int bumperBouncesS;
    private int bumperBouncesG;
    private bool extraHitsS;
    private bool extraHitsG;
    private float bonusHitsS;
    private float bonusHitsG;
    private int bouncesForBonusS;
    private int bouncesForBonusG;

    private const float X2_MULT_MAGIC = 100f;
    private const float X4_MULT_MAGIC = 200f;

    #endregion

    #region Unity lifecycle

    // Initializes singleton, physics, caches, and primary ball references.
    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (overrideGlobalGravity)
            Physics.gravity = gravityOverride;

        DOTween.SetTweensCapacity(500, 50);

        if (ball != null)
        {
            RegisterBall(ball);
            _ballStartPos = ball.transform.position;
            _ballStartRot = ball.transform.rotation;
            _primaryBallId = ball.GetInstanceID();
        }

        maxXP = XPFormula.XpReq(level);
        maxLives = Mathf.Max(1, maxLives);
        startingLives = Mathf.Clamp(startingLives, 0, maxLives);
        lives = startingLives;

        if (mainCam != null)
        {
            _camDefaultLocalPos = mainCam.transform.localPosition;
            _camDefaultLocalRot = mainCam.transform.localRotation;
        }
    }

    // Loads reward assets, initializes UI, and enters Charging state.
    private async void Start()
    {
        var loader = Addressables.LoadAssetsAsync<RewardSO>("Rewards", rewardPool.Add);
        await loader.Task;

        ui?.InitLives(maxLives);
        OnLivesChanged();

        ChangeState(PinballState.Charging);

        ui?.Init(this);
        if (invisWalls) invisWalls.SetActive(false);

        ballCount = 1;
    }

    // Clears singleton and resets camera transform on destroy.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ResetCameraShakeState();
    }

    // Clamps inspector-driven values and keeps push thresholds sane.
    private void OnValidate()
    {
        if (maxLives < 1) maxLives = 1;
        if (startingLives > maxLives) startingLives = maxLives;
        if (baseScoreMult < 1f) baseScoreMult = 1f;
        if (baseXPMult < 1f) baseXPMult = 1f;
        if (noBallResetGrace < 0f) noBallResetGrace = 0f;
        if (chargeToEnterPush < minChargeToLaunch)
            chargeToEnterPush = Mathf.Clamp01(Mathf.Max(minChargeToLaunch, chargeToEnterPush));
    }

    // Main per-frame loop: UI, input, state ticks, timers.
    private void Update()
    {
        HandlePendingLevelUps();
        HandlePaddles();
        ClampBaseMultipliers();
        MaybeEnableInvisibleWalls();
        HandleGameOverRestart();
        UpdateMultipliersTimers();

        switch (currentState)
        {
            case PinballState.Play:
                HandlePlayStateDrain();
                break;
            case PinballState.Charging:
            case PinballState.Push:
                HandleChargingAndPush();
                break;
        }

        TickChargeTimer();
    }

    #endregion

    #region State machine

    // Changes current state using Exit/Enter hooks.
    public void ChangeState(PinballState newState)
    {
        if (currentState == newState) return;
        ExitState(currentState);
        currentState = newState;
        EnterState(newState);
    }

    // Handles side effects on state entry (UI, time scale, lives, flows).
    private void EnterState(PinballState state)
    {
        switch (state)
        {
            case PinballState.Charging:
                ResetChargeState(max: chargeMaxCharging);
                break;

            case PinballState.Push:
                ResetChargeState(max: chargeMaxPush);
                break;

            case PinballState.Play:
                ui?.DefaultUI();
                chargePercentage = 0;
                chargeTimer = 0;
                Time.timeScale = 1f;
                wallTimerStart = true;
                break;

            case PinballState.LevelUp:
                LevelUp();
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                Time.timeScale = 0f;
                break;

            case PinballState.PaddleSelect:
                ui?.PaddleSelect();
                Time.timeScale = 0f;
                break;

            case PinballState.ResetBall:
                lives = Mathf.Max(0, lives - 1);
                OnLivesChanged();
                ResetBallAndFlow();
                break;

            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                break;
        }
    }

    // Handles side effects on state exit (e.g., time scale restore).
    private void ExitState(PinballState state)
    {
        switch (state)
        {
            case PinballState.LevelUp:
            case PinballState.PaddleSelect:
                Time.timeScale = 1f;
                break;
        }
    }

    // Resets charge variables for Charging/Push states.
    private void ResetChargeState(float max)
    {
        isHoldingCharge = false;
        chargePercentage = 0f;
        chargeTimer = 0f;
        chargeMax = Mathf.Max(0.05f, max);
    }

    #endregion

    #region UI & Lives

    // Updates lives UI when the counter changes.
    private void OnLivesChanged()
    {
        ui?.UpdateLives(lives, maxLives);
    }

    #endregion

    #region Input/state helpers

    // -- One-line summary: Converts current-level progress into queued level-ups (no overshoot).
    private void HandlePendingLevelUps()
    {
        // Consume levels while our progress surpasses requirement
        while (curXP >= maxXP)
        {
            pendingLevelUps++;
            curXP -= maxXP;
            // Prepare next level's requirement in case we chain multiple ups
            maxXP = Mathf.Max(1f, XPFormula.XpReq(level + pendingLevelUps));
        }

        if (pendingLevelUps > 0 && CurrentState != PinballState.LevelUp)
            StartNextLevelUp();
    }

    // Drives paddles (movement, elemental single-shot window, hit forwarding to the ball).
    private void HandlePaddles()
    {
        if (leftPaddle != null)
        {
            leftPaddle.PaddleMovement(Input.GetKey(leftPaddleKey));
            var elem = leftPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(leftPaddleKey))
                HitCheck(leftPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(leftPaddleKey) || canHitL) && !hasBeenHitL)
            {
                hasBeenHitL = true;
                StartCoroutine(ResetLBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }

        if (rightPaddle != null)
        {
            rightPaddle.PaddleMovement(Input.GetKey(rightPaddleKey));
            var elem = rightPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(rightPaddleKey))
                HitCheck(rightPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(rightPaddleKey) || canHitR) && !hasBeenHitR)
            {
                hasBeenHitR = true;
                StartCoroutine(ResetRBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }
    }

    // Ensures score/xp multipliers do not fall below base values.
    private void ClampBaseMultipliers()
    {
        if (xpMultiplier < baseXPMult) xpMultiplier = baseXPMult;
        if (scoreMultiplier < baseScoreMult) scoreMultiplier = baseScoreMult;
    }

    // Arming delay before enabling invisible walls after launch.
    private void MaybeEnableInvisibleWalls()
    {
        if (!wallTimerStart) return;

        wallTimer += Time.deltaTime;
        if (wallTimer >= 0.2f)
            EnableInvisibleWalls();
    }

    // Restarts scene when in GameOver if the player presses chargeKey.
    private void HandleGameOverRestart()
    {
        if (currentState == PinballState.GameOver && Input.GetKey(chargeKey))
            SceneManager.LoadScene(0);
    }

    // Counts down temporary score/xp multiplier timers and restores base on expiry.
    private void UpdateMultipliersTimers()
    {
        if (IsScoreMultiplierActive && scoreBonusTimer > 0f)
        {
            scoreBonusTimer -= Time.unscaledDeltaTime;
            if (scoreBonusTimer <= 0f)
            {
                scoreBonusTimer = 0f;
                scoreMultiplier = baseScoreMult;
            }
        }

        if (IsXPMultiplierActive && xpBonusTimer > 0f)
        {
            xpBonusTimer -= Time.unscaledDeltaTime;
            if (xpBonusTimer <= 0f)
            {
                xpBonusTimer = 0f;
                xpMultiplier = baseXPMult;
            }
        }
    }

    // Handles Charging/Push input, launch and push commits, and state transitions.
    private void HandleChargingAndPush()
    {
        if (Input.GetKey(chargeKey))
            isHoldingCharge = true;

        chargePercentage = Mathf.Min(1f, chargeMax > 0f ? (chargeTimer / chargeMax) : 0f);

        if (Input.GetKeyUp(chargeKey))
        {
            isHoldingCharge = false;

            if (chargePercentage > minChargeToLaunch)
            {
                if (currentState == PinballState.Charging)
                {
                    ball?.Launch(chargePercentage);

                    if (chargePercentage > chargeToEnterPush)
                        ChangeState(PinballState.Push);
                }
                else if (currentState == PinballState.Push)
                {
                    if (ball != null && ball.IsInLaunchTube)
                    {
                        ball.Push(chargePercentage);
                        ChangeState(PinballState.Play);
                    }
                }
            }
        }

        if (currentState == PinballState.Push && (ball == null || (!ball.IsInLaunchTube && ball.GetComponent<Rigidbody>()?.velocity.z < 0f)))
        {
            ChangeState(PinballState.Charging);
        }
    }

    // Advances/reduces charge timer depending on input state.
    private void TickChargeTimer()
    {
        if (!isHoldingCharge)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0f) chargeTimer = 0f;
        }
        else
        {
            chargeTimer += Time.deltaTime;
            if (chargeTimer > chargeMax) chargeTimer = chargeMax;
        }
    }

    // Debounced detection of no usable balls during Play; triggers ResetBall.
    private void HandlePlayStateDrain()
    {
        bool any = HasAnyUsableBalls() || IsBallUsable(ball);

        if (!any)
        {
            _noBallTimer += Time.unscaledDeltaTime;
            if (_noBallTimer >= noBallResetGrace)
            {
                ChangeState(PinballState.ResetBall);
            }
        }
        else
        {
            _noBallTimer = 0f;
        }
    }

    #endregion

    #region Ball registry

    // Adds a ball to the live registry if not already present.
    public void RegisterBall(Ball b)
    {
        if (b != null && !liveBalls.Contains(b))
            liveBalls.Add(b);
    }

    // Removes a ball from the live registry.
    public void UnregisterBall(Ball b)
    {
        if (b != null)
            liveBalls.Remove(b);
    }

    // Verifies if a ball can be used by gameplay systems.
    private static bool IsBallUsable(Ball b)
    {
        return b != null && b.isActiveAndEnabled && b.gameObject.activeInHierarchy && b.IsActive;
    }

    // Enumerates usable balls and prunes nulls.
    private IEnumerable<Ball> GetUsableBalls()
    {
        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null)
            {
                liveBalls.RemoveAt(i);
                continue;
            }
            if (IsBallUsable(b)) yield return b;
        }
    }

    // Applies an action to each usable ball.
    private void ForEachUsableBall(System.Action<Ball> action)
    {
        foreach (var b in GetUsableBalls())
            action(b);
    }

    // Attempts to find a currently usable anchor ball.
    private bool TryGetAnchorBall(out Ball anchor)
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var candidate = liveBalls[i];
            if (candidate == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(candidate)) { anchor = candidate; return true; }
        }
        anchor = null;
        return false;
    }

    // Prefers a ball touching paddles; otherwise returns any usable anchor ball.
    private Ball GetPaddleTarget()
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(b) && b.IsTouchingPaddles)
                return b;
        }
        return TryGetAnchorBall(out var anchor) ? anchor : null;
    }

    // Ensures the public 'ball' field still refers to the primary instance.
    private void EnsurePrimaryBallRef()
    {
        if (ball != null && ball.GetInstanceID() == _primaryBallId)
            return;

        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b != null && b.GetInstanceID() == _primaryBallId)
            {
                ball = b;
                return;
            }
        }

        if (ball == null && liveBalls.Count > 0 && liveBalls[0] != null)
        {
            ball = liveBalls[0];
            _primaryBallId = ball.GetInstanceID();
        }
    }

    // Stops motion and teleports a ball to the launcher start pose.
    private void FreezeAndTeleportToStart(Ball target)
    {
        if (target == null) return;
        var t = target.transform;
        var rb = target.GetComponent<Rigidbody>();

        DOTween.Kill(t, complete: false);

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.position = _ballStartPos;
            rb.rotation = _ballStartRot;
            rb.Sleep();
            rb.isKinematic = false;
        }
        else
        {
            t.position = _ballStartPos;
            t.rotation = _ballStartRot;
        }
        target.ResetRb();
    }

    #endregion

    #region Walls

    // Enables invisible wall colliders.
    public void EnableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(true);
    }

    // Disables invisible walls and resets the arming timer.
    public void DisableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(false);
        wallTimer = 0f;
        wallTimerStart = false;
    }

    #endregion

    #region Ball duplication

    // Duplicates the current anchor ball (if any) and applies basic property copies.
    public void DupeBall()
    {
        if (!TryGetAnchorBall(out var anchor))
        {
            if (IsBallUsable(ball)) anchor = ball;
        }
        if (anchor == null || ballClonePrefab == null)
            return;

        var dupedBallGO = Instantiate(ballClonePrefab, anchor.transform.position, Quaternion.identity);
        var dupedBall = dupedBallGO.GetComponent<Ball>();
        if (dupedBall != null)
        {
            dupedBall.BaseDamage = anchor.BaseDamage;
            dupedBall.maxSpeed = anchor.maxSpeed;
            dupedBall.transform.localScale = anchor.transform.localScale;

            var anchorCol = anchor.GetComponent<Collider>();
            var dupedCol = dupedBall.GetComponent<Collider>();
            if (anchorCol != null && dupedCol != null && anchorCol.material != null)
                dupedCol.material = Instantiate(anchorCol.material);

            dupedBall.ResetRb();
        }
        ballCount++;
    }

    #endregion

    #region Score/XP

    // Adds score for a game event; damageFactor scales by the ball's actual damage (flats + multipliers).
    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec, float damageFactor)
    {
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier * Mathf.Max(0f, damageFactor));

        if (extraHitsS)
        {
            bumperBouncesS++;
            if (bumperBouncesS > bouncesForBonusS)
            {
                finalPoints = Mathf.RoundToInt(finalPoints * bonusHitsS);
                bumperBouncesS = 0;
            }
        }
        if (extraHitsG)
        {
            bumperBouncesG++;
            if (bumperBouncesG > bouncesForBonusG)
            {
                xpCount = Mathf.RoundToInt(xpCount * bonusHitsG);
                bumperBouncesG = -1;
            }
            if (bumperBouncesG == 0)
                xpCount = Mathf.RoundToInt(xpCount / bonusHitsG);
        }

        if (destroyedBumperBonusActive)
        {
            score += Mathf.RoundToInt(finalPoints * destroyedBumperScoreMult);
            destroyedBumperBonusActive = false;
        }
        else score += finalPoints;

        ui?.UpdateScore(score, bumpCount, bumpCountConsec);
    }

    // Checks across all balls whether any usable one exists.
    private bool HasAnyUsableBalls()
    {
        foreach (var _ in GetUsableBalls()) return true;

        if (IsBallUsable(ball))
        {
            if (!liveBalls.Contains(ball))
                RegisterBall(ball);
            return true;
        }
        return false;
    }

    // Clears extra balls, reseats main ball, and transitions to Charging or GameOver.
    private void ResetBallAndFlow()
    {
        EnsurePrimaryBallRef();
        _noBallTimer = 0f;

        DisableInvisibleWalls();
        ResetCameraShakeState();

        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); continue; }
            if (b != ball)
            {
                liveBalls.RemoveAt(i);
                Destroy(b.gameObject);
            }
        }

        if (ball != null)
        {
            ball.gameObject.SetActive(true);
            var rb = ball.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            FreezeAndTeleportToStart(ball);
            if (!liveBalls.Contains(ball)) RegisterBall(ball);
        }

        isHoldingCharge = false;
        chargeTimer = 0f;
        chargePercentage = 0f;

        ballCount = 1;

        ChangeState(lives > 0 ? PinballState.Charging : PinballState.GameOver);
    }

    // -- One-line summary: Adds XP with global multiplier and nudges the UI immediately.
    public void AddXP(float xp)
    {
        float finalXP = xp * xpMultiplier;
        curXP += finalXP;
        // Guard against degenerate maxXP (editor misconfig, formula edge cases)
        float safeMax = Mathf.Max(1f, maxXP);
        ballXPScript?.UpdateXP(Mathf.RoundToInt(curXP), Mathf.RoundToInt(safeMax), level);
    }
    // Emits base XP particles (rounded) scaled by supplied damageFactor (0..∞).
    public void SpawnXP(Vector3 pos, bool isDead, bool isTakingElemDamage, float damageFactor = 1f)
    {
        if (!xpFXPrefab) return;

        int count;
        if (isDead) count = xpCount * 2;
        else if (isTakingElemDamage) count = Mathf.RoundToInt(xpCount / 3f);
        else count = xpCount;

        count = Mathf.Max(0, Mathf.RoundToInt(count * Mathf.Max(0f, damageFactor)));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(count);
    }

    // Emits bonus XP for Water drenched state; external callers may override damageFactor (e.g., water ignores factor).
    public void SpawnBonusWaterXP(Vector3 pos, float waterBonusXP, float damageFactor = 1f)
    {
        if (!xpFXPrefab) return;

        float bonus = Mathf.Max(0f, waterBonusXP) / 100f;
        int count = Mathf.RoundToInt(xpCount * (1f + bonus));
        count = Mathf.Max(0, Mathf.RoundToInt(count * Mathf.Max(0f, damageFactor)));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(count);
    }

    // Emits bonus XP for Earth effects (fissure/electric); scaled by damageFactor.
    public void SpawnBonusEarthXP(Vector3 pos, float earthBonusXP, float damageFactor = 1f)
    {
        if (!xpFXPrefab) return;

        float bonus = Mathf.Max(0f, earthBonusXP) / 100f;
        int count = Mathf.RoundToInt(xpCount * (1f + bonus));
        count = Mathf.Max(0, Mathf.RoundToInt(count * Mathf.Max(0f, damageFactor)));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(count);
    }

    // Begins a level-up UI flow if any are pending.
    private void StartNextLevelUp()
    {
        if (pendingLevelUps <= 0) return;
        ChangeState(PinballState.LevelUp);
    }


    // -- One-line summary: Increments level, re-derives XP requirement, refreshes UI, and applies bonuses.
    public void LevelUp()
    {
        level = Mathf.Max(1, level + 1);
        maxXP = Mathf.Max(1f, XPFormula.XpReq(level));
        ballXPScript?.UpdateXP(curXP, maxXP, level);
        ApplyLevelBonuses();
    }

    // -- One-line summary: Resets maxXP from formula (call if you ever resync level externally).
    private void RefreshXpRequirementFromLevel()
    {
        maxXP = Mathf.Max(1f, XPFormula.XpReq(level));
    }

    // Applies per-level base score/xp multipliers (bigger boost every 5 levels).
    public void ApplyLevelBonuses()
    {
        if (level % 5 == 0)
        {
            baseScoreMult += 0.5f;
            baseXPMult += 0.5f;
        }
        else
        {
            baseScoreMult += 0.15f;
            baseXPMult += 0.15f;
        }
    }

    #endregion

    #region Paddle single-shot elemental hit window

    // Starts a temporary single-shot hit window for a paddle.
    private void HitCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) StartCoroutine(RegCheck(leftPaddleKey));
        else StartCoroutine(RegCheck(rightPaddleKey));
    }

    // Implements the 0.6s window where an elemental hit can be applied once.
    private IEnumerator RegCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) canHitL = true; else canHitR = true;
        yield return new WaitForSeconds(0.6f);
        if (paddle == leftPaddleKey) canHitL = false; else canHitR = false;
    }

    // Resets L paddle one-shot latch after a cooldown.
    private IEnumerator ResetLBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitL = false; }
    // Resets R paddle one-shot latch after a cooldown.
    private IEnumerator ResetRBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitR = false; }

    #endregion

    #region Camera shake

    // Stops active tweens and restores camera to default local transform.
    public void ResetCameraShakeState()
    {
        if (mainCam == null) return;
        var t = mainCam.transform;
        t.DOKill(false);
        t.localPosition = _camDefaultLocalPos;
        t.localRotation = _camDefaultLocalRot;
    }

    // Plays a position shake on the camera with configured parameters.
    public void ScreenShake()
    {
        if (!mainCam) return;

        ResetCameraShakeState();

        mainCam.transform
            .DOShakePosition(
                shakeDuration,
                shakeStrength,
                shakeVibrato,
                shakeRandomness,
                snapping: false,
                fadeOut: true
            )
            .SetEase(Ease.OutQuad)
            .SetUpdate(false);
    }

    #endregion

    #region Bumper respawn/explosion

    // Plays bumper explosion feedback, disables it, waits its cooldown, and restores it.
    public IEnumerator RespawnRoutine(Bumper bumper)
    {
        if (!bumper || !ball) yield break;

        float f = explosionForce;
        float r = explosionRadius;
        if (bumper.type == BumperType.Small) { f *= 0.5f; r *= 0.5f; }
        else if (bumper.type == BumperType.Large) { f *= 1.5f; r *= 1.5f; }

        var rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddExplosionForce(f, bumper.transform.position, r, 0f, ForceMode.Impulse);

        var col = bumper.GetComponent<Collider>();
        var mr = bumper.GetComponent<MeshRenderer>();
        if (col) col.enabled = false;
        if (mr) mr.enabled = false;

        yield return new WaitForSeconds(bumper.cooldown);

        bumper.curHealth = bumper.maxHealth;
        bumper.gameObject.SetActive(true);
        if (col) col.enabled = true;
        if (mr) mr.enabled = true;
    }

    #endregion

    #region Reward application (IRunContext impl)

    // Applies an XP multiplier (with special cursed behavior).
    public void ApplyXPMultiplier(float multiplier, bool cursed)
    {
        if (cursed)
        {
            xpCount += 1;
            if (xpBonusTimer > 5) xpBonusTimer = 5;
            xpMultiplier *= 2;
        }
        else xpMultiplier += multiplier;
    }

    // Adds time to the XP bonus timer (cursed caps it).
    public void ApplyXPBonusTime(float time, bool cursed)
    {
        if (!IsXPMultiplierActive) return;
        if (cursed) { if (xpBonusTimer > 3) xpBonusTimer = 3; }
        else
        {
            xpBonusTimer += time;
            if (xpBonusTimer > 30) xpBonusTimer = 30f;
        }
    }

    // Applies a score multiplier or special cursed overrides.
    public void ApplyScoreMultiplier(float multiplier, bool cursed)
    {
        if (Mathf.Approximately(multiplier, X2_MULT_MAGIC)) scoreMultiplier *= 2f;
        else if (cursed) scoreMultiplier *= 4f;
        else scoreMultiplier += multiplier;
    }

    // Adds time to the score bonus timer (cursed shrinks it).
    public void ApplyScoreBonusTime(float time, bool cursed)
    {
        if (!IsScoreMultiplierActive) return;
        if (cursed) scoreBonusTimer *= 0.1f;
        else scoreBonusTimer += time;
    }

    // Shrinks balls and applies optional score mult, extra hits, and physics changes.
    public void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + scoreMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (scoreMult != 0) baseScoreMult *= Mult;

        if (bonus)
        {
            extraHitsS = true;
            bouncesForBonusS = bounces;
            bonusHitsS = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f + Speed;
        });
    }

    // Grows balls and applies optional XP mult, extra hits, and physics changes.
    public void ApplyGrowFX(float size, float speed, float bounciness, float xpMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + xpMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (xpMult != 0) baseXPMult *= Mult;

        if (bonus)
        {
            extraHitsG = true;
            bouncesForBonusG = bounces;
            bonusHitsG = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f - Speed;
        });
    }

    // Multiplies each ball's XP forcefield radius.
    public void ApplyXPForcefield(float amount)
    {
        float Amount = (100f + amount) / 100f;
        ForEachUsableBall(b => b.UpdateForcefield(Amount));
    }

    // Spawns additional balls (or doubles if amount is 100 per your rule).
    public void ApplyAdditionalBalls(int additionalBalls)
    {
        if (additionalBalls != 100)
        {
            for (int i = 0; i < additionalBalls; i++) { DupeBall(); }
        }
        else
        {
            int curBallCount = ballCount;
            for (int i = 0; i < curBallCount; i++) { DupeBall(); }
        }
    }

    // Adds granted lives and updates UI.
    public void ApplyGrantedLives(int amount)
    {
        lives = Mathf.Clamp(lives + amount, 0, maxLives);
        OnLivesChanged();
    }

    // Adds persistent damage multiplier to all usable balls.
    public void ApplyDamageFX(float amount)
    {
        float Amount = 0.01f * amount;
        ForEachUsableBall(b => b.AddDamageMultiplier(Amount));
    }

    // Queues a temp damage multiplier window (bounces) to each ball.
    public void ApplyDmgPerBounceFX(float amount, int bounces)
    {
        float Amount = (100f + amount) / 100f;
        ForEachUsableBall(b => b.AddTempDamageMultiplier(Amount, bounces));
    }

    // Applies a pending paddle reward to left/right or returns to Play.
    public void SetPaddleState(bool isLeft)
    {
        var paddle = isLeft ? leftPaddle : rightPaddle;
        var paddleElem = paddle ? paddle.GetComponent<PaddleElementalState>() : null;

        if (paddleElem == null || pendingPaddleReward == null)
        {
            if (pendingLevelUps > 0)
            {
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                ui?.ClosePaddleSelect(true);
            }
            else
            {
                ui?.ClosePaddleSelect(false);
                ChangeState(PinballState.Play);
            }
            return;
        }

        pendingPaddleReward.ApplyToPaddle(paddleElem);
        pendingPaddleReward = null;

        if (pendingLevelUps > 0)
        {
            var choices = GetRewardChoices();
            ui?.ShowRewardPopup(choices);
            ui?.ClosePaddleSelect(true);
        }
        else
        {
            ui?.ClosePaddleSelect(false);
            ChangeState(PinballState.Play);
        }
    }

    #endregion

    #region Reward selection flow

    // Applies chosen rewards (with upgrade replacement rules) and advances the selection flow.
    public void OnRewardChosen(RewardSO reward)
    {
        if (reward == null) { ChangeState(PinballState.Play); return; }

        if (reward.isPaddleReward)
        {
            var leftElem = leftPaddle ? leftPaddle.GetComponent<PaddleElementalState>() : null;
            var rightElem = rightPaddle ? rightPaddle.GetComponent<PaddleElementalState>() : null;

            bool leftHasElem = leftElem != null && leftElem.CurrentState != PaddleElementalState.PaddleState.None;
            bool rightHasElem = rightElem != null && rightElem.CurrentState != PaddleElementalState.PaddleState.None;

            pendingPaddleReward = reward;

            if (leftHasElem && !rightHasElem) { SetPaddleState(false); return; }
            if (!leftHasElem && rightHasElem) { SetPaddleState(true); return; }

            reward.Apply(this);
            pendingLevelUps--;
            ChangeState(PinballState.PaddleSelect);
            return;
        }

        if (reward.Scalable && reward.ReplacesReward != null)
        {
            var old = reward.ReplacesReward;
            if (Owns(old.Id))
            {
                active.Remove(old.Id);
                owned.Remove(old.Id);

                reward.Apply(this);
                pendingLevelUps--;
                ChangeState(pendingLevelUps > 0 ? PinballState.LevelUp : PinballState.Play);
                return;
            }
        }

        reward.Apply(this);
        pendingLevelUps--;
        ChangeState(pendingLevelUps > 0 || currentState == PinballState.PaddleSelect ? PinballState.LevelUp : PinballState.Play);
    }

    // Picks one reward from a rarity bucket.
    private RewardSO PickOneWeighted(List<RewardSO> pool, RewardRarity rarity)
    {
        var candidates = pool.Where(r => r.Rarity == rarity).ToList();
        if (candidates.Count == 0) return null;
        int index = Random.Range(0, candidates.Count);
        return candidates[index];
    }

    // Rolls a reward rarity based on configured weights.
    private RewardRarity RollRarity()
    {
        float total = rarityWeights.Values.Sum();
        float roll = Random.Range(0f, total);

        float cumulative = 0f;
        foreach (var kv in rarityWeights)
        {
            cumulative += kv.Value;
            if (roll <= cumulative) return kv.Key;
        }
        return RewardRarity.Common;
    }

    // Builds a set of unique reward choices for the current pick.
    private List<RewardSO> GetRewardChoices()
    {
        var eligible = rewardPool.Where(r => r.IsEligible(this)).ToList();
        if (eligible.Count == 0) return new List<RewardSO>();

        var picks = new List<RewardSO>();
        var localEligible = new List<RewardSO>(eligible);

        for (int i = 0; i < 6; i++)
        {
            RewardSO choice = null;
            for (int j = 0; j < 10 && choice == null; j++)
            {
                var tier = RollRarity();
                choice = PickOneWeighted(localEligible, tier);
            }
            if (choice == null) break;
            picks.Add(choice);
            localEligible.Remove(choice);
            if (localEligible.Count == 0) break;
        }
        return picks;
    }

    #endregion
}
```

## Assets/Scripts/PinballFlipper.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PinballFlipper : MonoBehaviour
{
    [Header("References")]
    public HingeJoint hinge;

    [Header("Motor")]
    public float flipSpeed = 1500f;   // motor velocity when pressed
    public float returnSpeed = -200f; // motor velocity when released
    public float maxForce = 10000f;   // motor strength

    // Ensures hinge reference and enables motor.
    void Awake()
    {
        if (!hinge) hinge = GetComponent<HingeJoint>();
        if (!hinge)
        {
            Debug.LogWarning("[PinballFlipper] Missing HingeJoint.");
            return;
        }
        hinge.useMotor = true;
    }

    // Drives the hinge motor based on pressed state.
    public void PaddleMovement(bool isPressed)
    {
        if (!hinge) return;

        var motor = hinge.motor;
        motor.force = maxForce;
        motor.targetVelocity = isPressed ? flipSpeed : returnSpeed;
        hinge.motor = motor;
    }
}

```

## Assets/Scripts/PinballMusic.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class PinballMusic : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Plays once, then hands off to Loop.")]
    public AudioClip introClip;

    [Tooltip("Seamlessly loops forever after Intro finishes.")]
    public AudioClip loopClip;

    [Header("Routing (optional)")]
    [Tooltip("Optional mixer routing for both sources.")]
    public AudioMixerGroup outputMixerGroup;

    [Header("Behavior")]
    [Tooltip("Automatically start music on Start().")]
    public bool playOnStart = true;

    [Tooltip("Safety lead time before scheduled start, in seconds.")]
    [Min(0.01f)]
    public double scheduleLeadIn = 0.08; // small buffer for preload

    private AudioSource _introSource;
    private AudioSource _loopSource;
    private bool _started;



    void Awake()
    {
        _introSource = gameObject.AddComponent<AudioSource>();
        _loopSource = gameObject.AddComponent<AudioSource>();

        ConfigureSource(_introSource, false);
        ConfigureSource(_loopSource, true);
    }


    void Start()
    {
        if (playOnStart)
            StartMusic();
    }

    public void StartMusic()
    {
        if (_started)
            return;

        double dspStart = AudioSettings.dspTime + scheduleLeadIn;

        if (introClip != null)
        {
            _introSource.clip = introClip;
            _introSource.loop = false;
            _introSource.PlayScheduled(dspStart);

            double introDuration = (double)introClip.samples / introClip.frequency;

            if (loopClip != null)
            {
                _loopSource.clip = loopClip;
                _loopSource.loop = true;
                _loopSource.PlayScheduled(dspStart + introDuration);
            }
        }

        _started = true;

    }

    private void ConfigureSource(AudioSource source, bool loop)
    {
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = false;
        if(outputMixerGroup != null)
            source.outputAudioMixerGroup = outputMixerGroup;
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}

```

## Assets/Scripts/PinballUIM.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PinballUIM : MonoBehaviour
{
    [System.Serializable]
    public class RewardSlot
    {
        public Button button;
        public TMP_Text titleText;
        public TMP_Text descText;
    }

    [Header("UI Slots (6 buttons)")]
    [SerializeField] private List<RewardSlot> slots = new();

    [Header("Lives UI")]
    [SerializeField] private List<Image> lifeIcons = new(); // assign 5 placeholder Images in order (left->right)
    [SerializeField] private Color32 lifeOnColor = new Color32(75, 202, 107, 255);
    [SerializeField] private Color32 lifeOffColor = new Color32(75, 202, 107, 11);

    [Header("Panels")]
    public GameObject gamePanel;
    public GameObject paddleSelectPanel;
    public GameObject levelUpPanel;

    [Header("Charge/Readouts")]
    public Image ChargingSlider;
    public TMP_Text gameScore;
    public TMP_Text bc;   // score multiplier & timer
    public TMP_Text bcc;  // XP multiplier & timer
    public TMP_Text xpText;

    private Pinball pm;
    private List<RewardSO> currentRewards = new();

    // Sets initial panel visibility.
    void Start()
    {
        if (levelUpPanel) levelUpPanel.SetActive(false);
        if (paddleSelectPanel) paddleSelectPanel.SetActive(false);
        if (gamePanel) gamePanel.SetActive(true);
    }

    // Updates charge fill, multiplier text, and XP progress each frame.
    void Update()
    {
        if (pm == null) return;

        if (ChargingSlider) ChargingSlider.fillAmount = pm.chargePercentage;

        if (bc) bc.text = $"Score Mult: {pm.ScoreMultiplier:0.##}x | Timer: {pm.ScoreBonusTimeRemaining:0.0}s";
        if (bcc) bcc.text = $"XP Mult: {pm.XPMultiplier:0.##}x | Timer: {pm.XPBonusTimeRemaining:0.0}s";

        if (xpText)
        {
            int cur = Mathf.RoundToInt(pm.curXP);
            int max = Mathf.RoundToInt(pm.maxXP);
            if (cur >= max) cur = Mathf.Max(0, max - 1); // avoid flashing "max" just before level up
            xpText.text = $"{cur} / {max}";
        }
    }

    // Injects the Pinball manager instance.
    public void Init(Pinball manager)
    {
        pm = manager;
    }

    // Shows only the first maxLives life icons (hides extras).
    public void InitLives(int maxLives)
    {
        if (lifeIcons == null) return;
        for (int i = 0; i < lifeIcons.Count; i++)
        {
            if (!lifeIcons[i]) continue;
            lifeIcons[i].gameObject.SetActive(i < maxLives);
        }
    }

    // Colors life icons based on current lives.
    public void UpdateLives(int lives, int maxLives)
    {
        InitLives(maxLives);
        if (lifeIcons == null) return;

        for (int i = 0; i < lifeIcons.Count && i < maxLives; i++)
        {
            if (!lifeIcons[i]) continue;
            lifeIcons[i].color = i < lives ? lifeOnColor : lifeOffColor;
        }
    }

    // Shows the reward selection popup and binds button callbacks.
    public void ShowRewardPopup(List<RewardSO> rewards)
    {
        if (gamePanel) gamePanel.SetActive(false);
        if (levelUpPanel) levelUpPanel.SetActive(true);

        currentRewards = rewards ?? new List<RewardSO>();

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot?.button == null || slot.titleText == null || slot.descText == null)
                continue;

            slot.button.onClick.RemoveAllListeners();

            if (i < currentRewards.Count && currentRewards[i] != null)
            {
                var reward = currentRewards[i];

                slot.titleText.text = reward.Name;
                slot.descText.text = reward.Description;

                slot.button.interactable = true;
                slot.button.gameObject.SetActive(true);
                slot.button.onClick.AddListener(() => OnRewardClicked(reward));
            }
            else
            {
                slot.titleText.text = string.Empty;
                slot.descText.text = string.Empty;
                slot.button.gameObject.SetActive(false);
            }
        }
    }

    // Forwards the chosen reward to the manager.
    private void OnRewardClicked(RewardSO reward)
    {
        pm?.OnRewardChosen(reward);
    }

    // Restores the default in-game UI (hides level-up/paddle-select).
    public void DefaultUI()
    {
        if (levelUpPanel) levelUpPanel.SetActive(false);
        if (paddleSelectPanel) paddleSelectPanel.SetActive(false);
        if (gamePanel) gamePanel.SetActive(true);
    }

    // Updates the score label.
    public void UpdateScore(int score, int bumpCount, int bumpCountConsec)
    {
        if (gameScore) gameScore.text = score.ToString();
    }

    // Opens the paddle selection panel and disables reward buttons temporarily.
    public void PaddleSelect()
    {
        if (gamePanel) gamePanel.SetActive(false);

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot?.button == null) continue;
            slot.button.interactable = false;
        }

        if (paddleSelectPanel) paddleSelectPanel.SetActive(true);
    }

    // Closes paddle selection; optionally re-opens level-up if more levels are pending.
    public void ClosePaddleSelect(bool hasMoreLevels)
    {
        if (paddleSelectPanel) paddleSelectPanel.SetActive(false);

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot?.button == null) continue;
            slot.button.interactable = true;
        }

        if (hasMoreLevels && levelUpPanel)
            levelUpPanel.SetActive(true);
    }
}
```

## Assets/Scripts/PowerupPickup.cs

```csharp
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class PowerupPickup : MonoBehaviour
{
    [Tooltip("Powerup Id carried by this pickup (e.g., 'collect-all-xp').")]
    public string powerupId;

    private bool _collected; // prevents double-trigger

    [Header("Behaviour")]
    [Tooltip("Seconds before auto-despawn if not collected.")]
    public float lifetime = 10f;

    [Tooltip("Optional initial impulse to make the pickup pop out.")]
    public float spawnImpulse = 2.5f;

    [Header("Feedback")]
    public ParticleSystem pickupVfx;
    public AudioSource audioSource;
    public AudioClip pickupSfx;

    // Enforces trigger collider and ensures a kinematic rigidbody exists for reliable triggers.
    private void OnValidate()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (!rb)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // Ensures collider/rigidbody trigger setup (editor Reset hook).
    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // Arms lifetime despawn, applies small spawn push, and plays spawn tween if present.
    private void OnEnable()
    {
        if (lifetime > 0f)
            Destroy(gameObject, lifetime);

        var rb = GetComponent<Rigidbody>();
        if (rb && spawnImpulse > 0f)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y); // slight up bias
            rb.AddForce(dir.normalized * spawnImpulse, ForceMode.Impulse);
        }

        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlaySpawn();
    }

    // Handles collection by a Ball: triggers powerup, plays feedback, disables collider, and schedules destroy.
    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;

        var pm = Pinball.Instance;
        if (!pm) return;

        var ball = other.GetComponentInParent<Ball>();
        if (!ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (string.IsNullOrEmpty(powerupId))
        {
            Debug.LogWarning("[PowerupPickup] powerupId is empty. Ensure PowerupSystem assigned it at spawn.");
            return;
        }

        bool ok = PowerupSystem.TryTriggerById(pm, powerupId, transform.position);
        if (!ok) return;

        _collected = true;

        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlayCollect();
        if (pickupVfx) Instantiate(pickupVfx, transform.position, Quaternion.identity);

        var col = GetComponent<Collider>();
        if (col) col.enabled = false;

        float delay = 0.05f; // minimum so tween is visible
        if (tween) delay = Mathf.Max(delay, tween.GetCollectDuration());
        if (audioSource && pickupSfx)
        {
            audioSource.PlayOneShot(pickupSfx);
            delay = Mathf.Max(delay, pickupSfx.length);
        }

        Destroy(gameObject, delay);
    }
}
```

## Assets/Scripts/PowerupSystem.cs

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class PowerupSystem
{
    private static readonly List<IPowerup> _registry = new();
    private static bool _initialized;

    private const string PickupResourcePath = "Powerups/PowerupPickup"; // Resources/Powerups/PowerupPickup.prefab
    private static GameObject _pickupPrefab; // cached after first load

    // Scans assemblies once to auto-register IPowerup implementations with parameterless constructors.
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int ai = 0; ai < asms.Length; ai++)
            {
                var asm = asms[ai];
                if (asm == null || asm.IsDynamic) continue;

                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }

                if (types == null) continue;
                for (int ti = 0; ti < types.Length; ti++)
                {
                    var t = types[ti];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IPowerup).IsAssignableFrom(t)) continue;

                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;

                    try
                    {
                        var instance = (IPowerup)Activator.CreateInstance(t);
                        Register(instance);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PowerupSystem] Failed to instantiate {t?.FullName}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PowerupSystem] Reflection scan failed: {ex}");
        }

        Debug.Log($"[PowerupSystem] Registered {_registry.Count} powerups.");
    }

    // Adds a powerup to the registry if not already present by id.
    public static void Register(IPowerup powerup)
    {
        if (powerup == null) return;
        if (_registry.Exists(p => p.Id == powerup.Id)) return;
        _registry.Add(powerup);
    }

    // Rolls whether a pickup should drop using the context�s configured chance.
    public static bool TryRoll(IRunContext ctx)
    {
        float chance = GetDropChance(ctx);
        return UnityEngine.Random.value < chance;
    }

    // Returns the clamped drop chance from the Pinball singleton or a default.
    public static float GetDropChance(IRunContext ctx)
    {
        float baseChance = Pinball.Instance != null ? Pinball.Instance.PowerupDropChance : 0.03f;
        return Mathf.Clamp01(baseChance);
    }

    // Triggers a specific powerup by id if eligible for the given Pinball.
    public static bool TryTriggerById(Pinball pm, string id, Vector3 triggerPos)
    {
        EnsureInitialized();
        for (int i = 0; i < _registry.Count; i++)
        {
            var p = _registry[i];
            if (p == null) continue;
            if (p.Id == id && p.CanTrigger(pm))
            {
                Debug.Log($"[PowerupSystem] Triggering: {p.DebugLabel} @ {triggerPos}");
                p.Execute(pm, triggerPos);
                return true;
            }
        }
        return false;
    }

    // Rolls, picks a weighted eligible powerup, and spawns a pickup at the given position.
    public static bool TrySpawnPickupOnHit(Pinball pm, Vector3 pos, IRunContext ctx)
    {
        EnsureInitialized();

        if (!TryRoll(ctx))
            return false;

        var eligibles = ListPool<IPowerup>.Get();
        try
        {
            for (int i = 0; i < _registry.Count; i++)
            {
                var p = _registry[i];
                if (p != null && p.CanTrigger(pm))
                    eligibles.Add(p);
            }
            if (eligibles.Count == 0)
                return false;

            var picked = PickWeighted(eligibles);
            return SpawnPickup(picked.Id, pos);
        }
        finally
        {
            ListPool<IPowerup>.Release(eligibles);
        }
    }

    // Instantiates the pickup prefab from Resources and sets its powerup id.
    private static bool SpawnPickup(string powerupId, Vector3 pos)
    {
        if (_pickupPrefab == null)
        {
            _pickupPrefab = Resources.Load<GameObject>(PickupResourcePath);
            if (_pickupPrefab == null)
            {
                Debug.LogWarning($"[PowerupSystem] Failed to load pickup prefab at Resources/{PickupResourcePath}");
                return false;
            }
        }

        var go = UnityEngine.Object.Instantiate(_pickupPrefab, pos, Quaternion.identity);
        var pickup = go.GetComponent<PowerupPickup>();
        if (pickup == null)
        {
            Debug.LogWarning("[PowerupSystem] Instantiated pickup prefab is missing PowerupPickup component.");
            return false;
        }
        pickup.powerupId = powerupId;
        return true;
    }

    // Picks a powerup from a list using their Weight properties.
    private static IPowerup PickWeighted(List<IPowerup> items)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
            total += Mathf.Max(.0001f, items[i].Weight);

        float r = UnityEngine.Random.value * total;
        float accum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            accum += Mathf.Max(.0001f, items[i].Weight);
            if (r <= accum)
                return items[i];
        }
        return items[items.Count - 1];
    }

    // Small GC-free list pool for temporary allocations.
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new();

        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }
}
```

## Assets/Scripts/RandomFlingPowerup.cs

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RandomFlingPowerup : IPowerup
{
    public string Id => "random-fling";
    public float Weight => 0.8f;
    public string DebugLabel => "Random Fling";

    // Always eligible; pickup roll ensures pacing.
    public bool CanTrigger(IRunContext ctx) => true;

    // Applies a random horizontal impulse to each active ball for chaotic repositioning.
    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        var balls = Object.FindObjectsOfType<Ball>();
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;

            var rb = b.GetComponent<Rigidbody>();
            if (!rb) continue;

            Vector3 dir = Random.onUnitSphere;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            dir.Normalize();

            float strength = Random.Range(100f, 250f);
            rb.AddForce(dir * strength, ForceMode.Impulse);
        }

        pinball.ScreenShake();
    }
}
```

## Assets/Scripts/RewardCatalogSO.cs

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Reward Catalog")]
public class RewardCatalogSO : ScriptableObject
{
    [Tooltip("All reward assets available for this mode")]
    public List<RewardSO> allRewards = new List<RewardSO>();   // kept PUBLIC for compatibility

    // Cached maps (rebuilt on enable/validate)
    private Dictionary<string, RewardSO> _byId;
    private Dictionary<RewardRarity, List<RewardSO>> _byRarity;

    // -- One-line summary: Exposes a read-only snapshot (optional convenience).
    public ReadOnlyCollection<RewardSO> All => allRewards.AsReadOnly();

    // -- One-line summary: Returns a reward by ID or null if not found.
    public RewardSO GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureCaches();
        return _byId.TryGetValue(id.Trim(), out var r) ? r : null;
    }

    // -- One-line summary: Tries to get a reward by ID with a bool success result.
    public bool TryGetById(string id, out RewardSO reward)
    {
        reward = GetById(id);
        return reward != null;
    }

    // -- One-line summary: Returns the subset of rewards currently eligible in this run context.
    public List<RewardSO> GetEligible(IRunContext ctx)
    {
        EnsureCaches();
        return allRewards.Where(r => r && r.IsEligible(ctx)).ToList();
    }

    // -- One-line summary: Returns all rewards of a given rarity (no eligibility filter).
    public IReadOnlyList<RewardSO> GetByRarity(RewardRarity rarity)
    {
        EnsureCaches();
        return _byRarity.TryGetValue(rarity, out var list) ? (IReadOnlyList<RewardSO>)list : Array.Empty<RewardSO>();
    }

    // -- One-line summary: Picks one eligible reward from a rarity bucket at random.
    public RewardSO PickOneFromRarity(IRunContext ctx, RewardRarity rarity, System.Random rng = null)
    {
        rng ??= new System.Random();
        var pool = GetEligible(ctx).Where(r => r.Rarity == rarity).ToList();
        if (pool.Count == 0) return null;
        return pool[rng.Next(0, pool.Count)];
    }

    // -- One-line summary: Rolls a rarity using weights, then picks an eligible reward of that rarity.
    public RewardSO RollAndPick(IRunContext ctx, IReadOnlyDictionary<RewardRarity, float> weights, System.Random rng = null)
    {
        rng ??= new System.Random();
        var rarity = RollRarity(weights, rng);
        return PickOneFromRarity(ctx, rarity, rng);
    }

    // -- One-line summary: Builds N unique weighted choices; rerolls within a cap if buckets are empty.
    public List<RewardSO> GetWeightedChoices(IRunContext ctx, int count, IReadOnlyDictionary<RewardRarity, float> weights, int maxRerollsPerPick = 10, System.Random rng = null)
    {
        rng ??= new System.Random();
        EnsureCaches();

        var eligible = GetEligible(ctx);
        if (eligible.Count == 0) return new List<RewardSO>();

        var local = new List<RewardSO>(eligible);
        var picks = new List<RewardSO>(Mathf.Max(0, count));

        for (int i = 0; i < count && local.Count > 0; i++)
        {
            RewardSO chosen = null;
            for (int roll = 0; roll < maxRerollsPerPick && chosen == null; roll++)
            {
                var r = RollRarity(weights, rng);
                var bucket = local.Where(x => x.Rarity == r).ToList();
                if (bucket.Count > 0) chosen = bucket[rng.Next(0, bucket.Count)];
            }
            if (chosen == null) break;
            picks.Add(chosen);
            local.Remove(chosen);
        }
        return picks;
    }

    // -- One-line summary: Rolls a rarity given weights (falls back to Common if invalid).
    private static RewardRarity RollRarity(IReadOnlyDictionary<RewardRarity, float> weights, System.Random rng)
    {
        if (weights == null || weights.Count == 0) return RewardRarity.Common;

        float total = 0f;
        foreach (var kv in weights) total += Mathf.Max(0f, kv.Value);
        if (total <= 0f) return RewardRarity.Common;

        float pick = (float)(rng.NextDouble() * total);
        foreach (var kv in weights)
        {
            pick -= Mathf.Max(0f, kv.Value);
            if (pick <= 0f) return kv.Key;
        }
        return RewardRarity.Common;
    }

    // -- One-line summary: Rebuilds lookup caches from the current list (removes nulls/dupes).
    private void EnsureCaches()
    {
        if (_byId != null && _byRarity != null) return;

        // Scrub nulls/dupes in-place but KEEP public list semantics
        var seen = new HashSet<RewardSO>();
        for (int i = allRewards.Count - 1; i >= 0; i--)
        {
            var r = allRewards[i];
            if (!r || seen.Contains(r)) { allRewards.RemoveAt(i); continue; }
            seen.Add(r);
        }

        _byId = new Dictionary<string, RewardSO>(StringComparer.Ordinal);
        _byRarity = new Dictionary<RewardRarity, List<RewardSO>>();

        foreach (var r in allRewards)
        {
            var id = string.IsNullOrWhiteSpace(r.Id) ? null : r.Id.Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (!_byId.ContainsKey(id))
                _byId.Add(id, r);

            if (!_byRarity.TryGetValue(r.Rarity, out var list))
            {
                list = new List<RewardSO>();
                _byRarity.Add(r.Rarity, list);
            }
            list.Add(r);
        }
    }

    // -- One-line summary: Resets caches and light-cleans the list while editing.
    private void OnValidate()
    {
        _byId = null;
        _byRarity = null;

        // Remove nulls/dupes without changing public field semantics
        var seen = new HashSet<RewardSO>();
        for (int i = allRewards.Count - 1; i >= 0; i--)
        {
            var r = allRewards[i];
            if (!r || seen.Contains(r)) { allRewards.RemoveAt(i); continue; }
            seen.Add(r);
        }
    }

    // -- One-line summary: Ensures caches are built on load/enable.
    private void OnEnable()
    {
        _byId = null;
        _byRarity = null;
        EnsureCaches();
    }
}

```

## Assets/Scripts/RewardCategory.cs

```csharp
public enum RewardCategory
{
    ScoreMultiplier,
    XPMultiplier,
    PhysicsFX,
    BallFX,
    PaddleFX,
    BumperFX,
    LifeFX,
    Abilities
}

```

## Assets/Scripts/RewardRarity.cs

```csharp
public enum RewardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Artifact,
    Cursed
}

```

## Assets/Scripts/RewardSO.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base ScriptableObject for all pinball rewards. Encapsulates identity, classification,
/// stack/exclusivity rules, and provides an overridable Apply entry point.
/// </summary>
public abstract class RewardSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string rewardID;          // Unique, stable ID used by save/ownership systems
    [SerializeField] private string displayName;       // Player-facing name
    [TextArea, SerializeField] private string description;

    [Header("Classification")]
    [SerializeField] private RewardCategory category;
    [SerializeField] private RewardRarity rarity;

    // NOTE: Left as serialized public due to existing references in the project.
    [SerializeField] public bool isPaddleReward;

    [Header("Behavior")]
    [Tooltip("If true, this reward should not be offered again while an instance is currently active.")]
    [SerializeField] private bool blockWhenActive = true;

    [Tooltip("If false, a reward that is available but already owned cannot be offered again.")]
    [SerializeField] private bool canStack = true;

    [Header("Scaling / Replacement")]
    [Tooltip("If true, this entry is part of a progression path and may replace a lower tier.")]
    [SerializeField] private bool scalable = false;

    [Tooltip("Lower tier reward that this instance supersedes (for scalable paths).")]
    [SerializeField] private RewardSO replacesReward;

    [Header("Exclusivity")]
    [Tooltip("Exclusive group key. Rewards that share this key cannot co-exist as active.")]
    [SerializeField] private string exclusivityKey;

    [Tooltip("If any of these keys are active, this reward is ineligible.")]
    [SerializeField] private List<string> blockedKeys = new List<string>();

    // --------- Cached data for runtime checks (not serialized) ----------
    private HashSet<string> _blockedKeySet;            // Built on demand for O(1) lookups

    // ---------------- Public Read-Only API ----------------
    public string Id => rewardID;
    public string Name => displayName;
    public string Description => description;
    public RewardCategory Category => category;
    public RewardRarity Rarity => rarity;
    public bool BlockWhenActive => blockWhenActive;
    public bool CanStack => canStack;
    public bool Scalable => scalable;
    public RewardSO ReplacesReward => replacesReward;
    public string ExclusivityKey => exclusivityKey;

    // Expose as read-only view; keep List for Unity serialization friendliness.
    public IReadOnlyList<string> BlockedKeys => blockedKeys;

    // ---------------- Lifecycle / Helpers ----------------

    // Ensures internal caches are built (safe to call multiple times).
    private void EnsureCache()
    {
        // Build a set for quicker membership tests; tolerate null/empty gracefully.
        if (_blockedKeySet == null)
        {
            _blockedKeySet = new HashSet<string>(blockedKeys ?? System.Array.Empty<string>());
        }
    }

    // ---------------- Core Behavior ----------------

    // Determines if the reward can currently be offered to the player in the given run context.
    public virtual bool IsEligible(IRunContext ctx)
    {
        if (ctx == null) return false;

        EnsureCache();

        // If not part of a scalable chain, enforce "already active" + "available but can't stack" guards.
        if (!Scalable)
        {
            if (BlockWhenActive && ctx.IsActive(Id))
                return false;

            // If already available but this reward can't stack, block it
            if (ctx.IsAvailable(Id) && !CanStack)
                return false;
        }

        // For scalable chains, require ownership of the tier we replace
        if (Scalable && ReplacesReward != null && !ctx.Owns(ReplacesReward.Id))
            return false;

        // For scalable commons, don't re-offer if already owned (prevents infinite re-roll on base tier)
        if (Scalable && Rarity == RewardRarity.Common && ctx.Owns(Id))
            return false;

    }


    public abstract void Apply(IRunContext ctx);

    public virtual void ApplyToPaddle(PaddleElementalState state) { }

}


```

## Assets/Scripts/StatModifier.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum StatModType
{
    Flat = 100,
    PercentAdd = 200,
    PercentMult = 300,
}

public class StatModifier
{
    public readonly float Value;
    public readonly StatModType Type;
    public readonly int Order;
    public readonly object Source;

    public StatModifier(float value, StatModType type, int order, object source)
    {
        Value = value;
        Type = type;
        Order = order;
        Source = source;
    }

    public StatModifier(float value, StatModType type) : this(value, type, (int)type, null) { }

    public StatModifier(float value, StatModType type, int order) : this(value, type, order, null) { }

    public StatModifier(float value, StatModType type, object source) : this(value, type, (int)type, source) { }

}

```

## Assets/Scripts/Tank.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tank : BaseCharacter
{

    protected float hpLvlIncrease = 11.3f;
    protected float minAtkLvlIncrease = 1.7f;
    protected float maxAtkLvlIncrease = 2.04f;

    protected float endLVLinc = 4.94f;
    protected float strLVLinc = 2.68f;
    protected float agiLVLinc = 1.08f;
    protected float witLVLinc = 1.93f;
    protected float chaLVLinc = 3.27f;


    public Tank(string name, int level)
        : base(name, "Tank", level, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();

    }
    public Tank()
    : base("", "Tank", 5, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }

}

```

## Assets/Scripts/Test.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;

    protected Item sword;

    public CharacterUI ui;


    // Start is called before the first frame update
    void Start()
    {
        warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);

        ui.SetCharacterUI(warrior, true);
        ui.SetCharacterUI(mage, false);


        warrior.PrintStats();
        mage.PrintStats();
        warrior.Health.AddModifier(new StatModifier(5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(2.5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(1.9f, StatModType.PercentMult));
        warrior.Health.baseValue = warrior.Health.Value;

        Debug.Log("After Sword Equip Strength Value: " + warrior.Health.Value);
        Debug.Log("After Sword Equip Strength base Value: " + warrior.Health.baseValue);
        ui.SetCharacterUI(warrior, true);
        /*
        sword.Unequip(myCharacter.MaxAtk);
        Debug.Log("After Sword Unequip Strength Value: " + myCharacter.MaxAtk.Value);
        */
    }

    // Update is called once per frame
    void Update()
    {

    }
}

```

## Assets/Scripts/TweenDamageNumberSystem.cs

```csharp
using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.Pool;

[DisallowMultipleComponent]
public class TweenDamageNumberSystem : MonoBehaviour, IDamageNumberSystem
{
    [Header("Basics")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float duration = 0.9f;               // fallback if style is null
    [SerializeField] private float riseDistance = 1.25f;          // fallback if style is null
    [SerializeField] private float surfaceOffset = 0.08f;
    [SerializeField] private bool useUnscaledTime = false;        // fallback if style is null
    [SerializeField] private DamageNumberStyleSO style;
    [SerializeField] private float cameraForwardOffset = 0.02f;

    [Header("Typography (fallbacks if style is null)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float baseFontSize = 4f;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Rendering (fallbacks if style is null)")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 500;

    [Header("Damage → Size Mapping")]
    [SerializeField] private float minDamageForScale = 1f;
    [SerializeField] private float maxDamageForScale = 100f;
    [SerializeField] private float minScale = 0.75f;
    [SerializeField] private float maxScale = 1.75f;
    [SerializeField] private bool useLogScale = true;
    [SerializeField] private float logBase = 10f;
    [SerializeField] private AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Pooling")]
    [SerializeField] private int defaultCapacity = 32;
    [SerializeField] private int maxSize = 256;

    private IObjectPool<GameObject> pool;

    // -- One-line summary: Clamp authoring ranges for safer runtime values.
    private void OnValidate()
    {
        baseFontSize = Mathf.Max(0.1f, baseFontSize);
        duration = Mathf.Max(0.05f, duration);
        riseDistance = Mathf.Max(0f, riseDistance);
        minScale = Mathf.Max(0.01f, minScale);
        maxScale = Mathf.Max(minScale, maxScale);
        maxDamageForScale = Mathf.Max(minDamageForScale + 0.0001f, maxDamageForScale);
        logBase = Mathf.Clamp(logBase, 1.0001f, 100f);
    }

    // -- One-line summary: Register facade, build pool, and enlarge DOTween capacities.
    private void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        DamageNumbers.Register(this);

        pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroyPooled,
                                          collectionCheck: true, defaultCapacity, maxSize);

        Prewarm(48);
        DOTween.SetTweensCapacity(200, 50);
    }

    // -- One-line summary: Unregister facade on destroy to avoid stale references.
    private void OnDestroy()
    {
        if (DamageNumbers.System == this) DamageNumbers.Register(null);
    }

    // -- One-line summary: Pre-allocates a few instances to avoid runtime spikes.
    public void Prewarm(int count)
    {
        count = Mathf.Max(0, count);
        var temp = new GameObject[count];
        for (int i = 0; i < count; i++) temp[i] = pool.Get();
        for (int i = 0; i < count; i++) pool.Release(temp[i]);
    }

    // -- One-line summary: Compute scale multiplier from damage using log or linear mapping.
    private float ComputeScale(float damage)
    {
        float d = Mathf.Max(damage, 0f);
        if (useLogScale)
        {
            float minL = Mathf.Log(minDamageForScale + 1f, logBase);
            float maxL = Mathf.Log(maxDamageForScale + 1f, logBase);
            float valL = Mathf.Log(d + 1f, logBase);
            float tL = Mathf.InverseLerp(minL, maxL, valL);
            return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(tL));
        }
        float t = Mathf.InverseLerp(minDamageForScale, maxDamageForScale, d);
        return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(t));
    }

    // -- One-line summary: Spawn a pooled TMP label, position it, animate rise/fade, then release.
    public void Spawn(float amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        var tmp = go.GetComponent<TMP_Text>();
        var cam = targetCamera ? targetCamera.transform : null;
        var camFwd = cam ? cam.forward : Vector3.forward;

        // Place slightly above surface and offset toward camera to avoid z-fighting
        Vector3 basePos = position + Vector3.up * surfaceOffset + camFwd * cameraForwardOffset;
        go.transform.position = basePos;
        if (cam) go.transform.rotation = Quaternion.LookRotation(camFwd, Vector3.up);

        // Fresh content
        tmp.text = Mathf.RoundToInt(amount).ToString();

        // Font & color come from style if present, otherwise fallbacks assigned in Create()
        float baseSize = style ? style.BaseFontSize : baseFontSize;
        tmp.fontSize = baseSize * ComputeScale(amount);
        var c = overrideColor ?? (style ? style.DefaultColor : defaultColor);
        tmp.color = new Color(c.r, c.g, c.b, 0f);

        // Kill any lingering tweens on reuse
        go.transform.DOKill();
        tmp.DOKill();

        // Timings from style or fallback
        float dur = style ? style.Duration : duration;
        bool unscaled = style ? style.UseUnscaledTime : useUnscaledTime;
        float rise = style ? style.RiseDistance : riseDistance;

        float fadeIn, sustain, fadeOut;
        if (style) style.GetFadeTimings(out fadeIn, out sustain, out fadeOut);
        else { fadeIn = dur * 0.08f; fadeOut = dur * 0.25f; }

        // Animate rise + pop + fade, then return to pool
        DOTween.Sequence()
            .SetUpdate(unscaled)
            .SetRecyclable(true)
            .Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic))
            .Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic))
            .Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic))
            .Join(go.transform.DOScale(style ? style.PopToScale : 1.1f, fadeIn)
                               .From(style ? style.PopFromScale : 0.6f)
                               .SetEase(Ease.OutBack, 2f))
            .OnComplete(() => pool.Release(go));
    }

    // -- One-line summary: Create a pooled TextMeshPro object configured for world-space rendering.
    private GameObject Create()
    {
        var go = new GameObject("DamageNumber");
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;

        // Apply style or fallbacks
        if (style) style.ApplyToTMP(tmp);
        else
        {
            if (font) tmp.font = font;
            if (fontMaterial) tmp.fontSharedMaterial = fontMaterial;
            tmp.fontSize = baseFontSize;
            tmp.color = defaultColor;
        }

        // Sorting from style or fallbacks
        var mr = go.GetComponent<MeshRenderer>();
        if (mr)
        {
            mr.sortingLayerName = style ? style.SortingLayerName : sortingLayerName;
            mr.sortingOrder = style ? style.SortingOrder : sortingOrder;
        }

        go.SetActive(false);
        return go;
    }

    // -- One-line summary: Activate an instance when taken from the pool.
    private void OnGet(GameObject go) => go.SetActive(true);

    // -- One-line summary: Reset minimal state and disable before pooling.
    private void OnRelease(GameObject go)
    {
        var tmp = go.GetComponent<TMP_Text>();
        if (tmp) { tmp.alpha = 1f; tmp.text = string.Empty; }
        go.transform.localScale = Vector3.one;
        go.SetActive(false);
    }

    // -- One-line summary: Destroy pooled instance when the pool trims capacity.
    private void OnDestroyPooled(GameObject go)
    {
        if (go) Destroy(go);
    }
}

```

## Assets/Scripts/Warrior.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Warrior : BaseCharacter
{

    protected float hpLvlIncrease = 7.1f;
    protected float minAtkLvlIncrease = 2.1f;
    protected float maxAtkLvlIncrease = 2.2f;

    protected float endLVLinc = 4.57f;
    protected float strLVLinc = 3.93f;
    protected float agiLVLinc = 2.36f;
    protected float witLVLinc = 1.68f;
    protected float chaLVLinc = 2.07f;


    public Warrior(string name, int level)
        : base(name, "Warrior", level, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Warrior()
    : base("", "Warrior", 5, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }
}

```

## Assets/Scripts/XPCollectorRegistry.cs

```csharp
using System.Collections.Generic;
using System;
using UnityEngine;

// Registry of colliders that attract XP particles; raises OnChanged on mutations.
[DefaultExecutionOrder(-1000)] // Ensure this initializes before most systems.
public class XPCollectorRegistry : MonoBehaviour
{
    // Singleton instance.
    public static XPCollectorRegistry I { get; private set; }

    // Current set of active XP collector colliders.
    public readonly List<Collider> collectors = new();

    // Notifies listeners when the registry changes (add/remove/cleanup).
    public static event Action OnChanged;

    // Initialize singleton and prune stale entries early in the frame.
    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        PruneNulls();
    }

    // Clear singleton reference if this instance is destroyed.
    void OnDestroy()
    {
        if (I == this) I = null;
    }

    // Adds a collider to the registry (deduped); fires OnChanged.
    public void Register(Collider c)
    {
        if (!c) return;
        PruneNulls();
        if (collectors.Contains(c)) return;
        collectors.Add(c);
        OnChanged?.Invoke();
    }

    // Removes a collider from the registry; fires OnChanged when removed.
    public void Unregister(Collider c)
    {
        if (!c) return;
        if (collectors.Remove(c))
            OnChanged?.Invoke();
        PruneNulls();
    }

    // Removes null entries left by destroyed objects.
    private void PruneNulls()
    {
        for (int i = collectors.Count - 1; i >= 0; i--)
            if (!collectors[i]) collectors.RemoveAt(i);
    }
}
```

## Assets/Scripts/XPFormula.cs

```csharp
using System;
using UnityEngine;

/// XP progression helpers: per-level requirement, cumulative totals, and reverse lookup.
public static class XPFormula
{
    // Tunables (kept public/static for your existing usage)
    public static double A = 10.0;     // base scale; Lv1->2 baseline
    public static double p = 1.3;      // growth exponent
    public static double bumpEvery = 5.0;    // spacing between "walls"
    public static double bumpWidth = 0.8;    // narrow = steeper wall
    public static double bumpHeight = 0.20;  // 0.40 => +40% at the wall center

    // -- One-liner: Validate tunables once per domain load.
    static XPFormula()
    {
        ClampTunables();
    }

    // -- One-liner: Ensures no invalid values (e.g., zero/negative that cause math spikes).
    public static void ClampTunables()
    {
        A = Math.Max(0.0001, A);
        p = Math.Max(0.0001, p);
        bumpEvery = Math.Max(1.0, bumpEvery);
        bumpWidth = Math.Max(0.5, bumpWidth);     // matches your original safeguard
        bumpHeight = Math.Max(0.0, bumpHeight);
    }

    // -- One-liner: Logistic "hump" multiplier centered at `center`.
    static double Bump(double n, double center, double width, double height)
    {
        width = Math.Max(0.5, width);            // guard like your original
        double x = (n - center) / width;
        double s = 1.0 / (1.0 + Math.Exp(-x));   // sigmoid
        double bell = s * (1.0 - s) * 4.0;       // smooth hill, peak=1 at center
        return 1.0 + height * bell;              // baseline 1.0, peak 1+height
    }

    // -- One-liner: XP needed to go from level n -> n+1 (n >= 1).
    public static int XpReq(int n)
    {
        if (n < 1) n = 1;

        // base requirement
        double baseReq = A * Math.Pow(n, p);  // your original shape :contentReference[oaicite:0]{index=0}

        // multiply nearby humps (center and neighbors) for clean tails
        int center = (int)Math.Round(n / bumpEvery) * (int)bumpEvery; // original pattern :contentReference[oaicite:1]{index=1}

        double mul =
            Bump(n, center - bumpEvery, bumpWidth, bumpHeight) *
            Bump(n, center, bumpWidth, bumpHeight) *
            Bump(n, center + bumpEvery, bumpWidth, bumpHeight);       // original approach :contentReference[oaicite:2]{index=2}

        // clamp to at least 1 XP and return as int
        return Mathf.Max(1, (int)Math.Round(baseReq * mul));
    }

    // -- One-liner: Total XP required to *reach* level `level` (sum of per-level reqs).
    public static int TotalXpToReachLevel(int level)
    {
        if (level <= 1) return 0;
        long total = 0;
        for (int n = 1; n < level; n++)
        {
            total += XpReq(n);
            if (total > int.MaxValue) return int.MaxValue; // safety cap
        }
        return (int)total;
    }

    // -- One-liner: Finds the level you�re at given a total XP pool (binary search).
    public static int LevelForTotalXp(int totalXp, int maxLevel = 300)
    {
        totalXp = Math.Max(0, totalXp);
        maxLevel = Math.Max(2, maxLevel);

        int lo = 1, hi = maxLevel;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            int need = TotalXpToReachLevel(mid);
            if (need <= totalXp) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // -- One-liner: Predicts XP percent (0..1) toward next level at current total XP.
    public static float PercentToNextLevel(int totalXp, int maxLevel = 300)
    {
        int cur = LevelForTotalXp(totalXp, maxLevel);
        int curTotal = TotalXpToReachLevel(cur);
        int nextReq = XpReq(cur);
        if (nextReq <= 0) return 1f;
        return Mathf.Clamp01((totalXp - curTotal) / (float)nextReq);
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark01.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class Benchmark01 : MonoBehaviour
    {

        public int BenchmarkType = 0;

        public TMP_FontAsset TMProFont;
        public Font TextMeshFont;

        private TextMeshPro m_textMeshPro;
        private TextContainer m_textContainer;
        private TextMesh m_textMesh;

        private const string label01 = "The <#0050FF>count is: </color>{0}";
        private const string label02 = "The <color=#0050FF>count is: </color>";

        //private string m_string;
        //private int m_frame;

        private Material m_material01;
        private Material m_material02;



        IEnumerator Start()
        {



            if (BenchmarkType == 0) // TextMesh Pro Component
            {
                m_textMeshPro = gameObject.AddComponent<TextMeshPro>();
                m_textMeshPro.autoSizeTextContainer = true;

                //m_textMeshPro.anchorDampening = true;

                if (TMProFont != null)
                    m_textMeshPro.font = TMProFont;

                //m_textMeshPro.font = Resources.Load("Fonts & Materials/Anton SDF", typeof(TextMeshProFont)) as TextMeshProFont; // Make sure the Anton SDF exists before calling this...
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/Anton SDF", typeof(Material)) as Material; // Same as above make sure this material exists.

                m_textMeshPro.fontSize = 48;
                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                //m_textMeshPro.anchor = AnchorPositions.Center;
                m_textMeshPro.extraPadding = true;
                //m_textMeshPro.outlineWidth = 0.25f;
                //m_textMeshPro.fontSharedMaterial.SetFloat("_OutlineWidth", 0.2f);
                //m_textMeshPro.fontSharedMaterial.EnableKeyword("UNDERLAY_ON");
                //m_textMeshPro.lineJustification = LineJustificationTypes.Center;
                m_textMeshPro.enableWordWrapping = false;    
                //m_textMeshPro.lineLength = 60;          
                //m_textMeshPro.characterSpacing = 0.2f;
                //m_textMeshPro.fontColor = new Color32(255, 255, 255, 255);

                m_material01 = m_textMeshPro.font.material;
                m_material02 = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Drop Shadow"); // Make sure the LiberationSans SDF exists before calling this...  


            }
            else if (BenchmarkType == 1) // TextMesh
            {
                m_textMesh = gameObject.AddComponent<TextMesh>();

                if (TextMeshFont != null)
                {
                    m_textMesh.font = TextMeshFont;
                    m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                }
                else
                {
                    m_textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                }

                m_textMesh.fontSize = 48;
                m_textMesh.anchor = TextAnchor.MiddleCenter;

                //m_textMesh.color = new Color32(255, 255, 0, 255);
            }



            for (int i = 0; i <= 1000000; i++)
            {
                if (BenchmarkType == 0)
                {
                    m_textMeshPro.SetText(label01, i % 1000);
                    if (i % 1000 == 999)
                        m_textMeshPro.fontSharedMaterial = m_textMeshPro.fontSharedMaterial == m_material01 ? m_textMeshPro.fontSharedMaterial = m_material02 : m_textMeshPro.fontSharedMaterial = m_material01;



                }
                else if (BenchmarkType == 1)
                    m_textMesh.text = label02 + (i % 1000).ToString();

                yield return null;
            }


            yield return null;
        }


        /*
        void Update()
        {
            if (BenchmarkType == 0)
            {
                m_textMeshPro.text = (m_frame % 1000).ToString();
            }
            else if (BenchmarkType == 1)
            {
                m_textMesh.text = (m_frame % 1000).ToString();
            }

            m_frame += 1;
        }
        */
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark01_UGUI.cs

```csharp
using UnityEngine;
using System.Collections;
using UnityEngine.UI;


namespace TMPro.Examples
{
    
    public class Benchmark01_UGUI : MonoBehaviour
    {

        public int BenchmarkType = 0;

        public Canvas canvas;
        public TMP_FontAsset TMProFont;
        public Font TextMeshFont;

        private TextMeshProUGUI m_textMeshPro;
        //private TextContainer m_textContainer;
        private Text m_textMesh;

        private const string label01 = "The <#0050FF>count is: </color>";
        private const string label02 = "The <color=#0050FF>count is: </color>";

        //private const string label01 = "TextMesh <#0050FF>Pro!</color>  The count is: {0}";
        //private const string label02 = "Text Mesh<color=#0050FF>        The count is: </color>";

        //private string m_string;
        //private int m_frame;

        private Material m_material01;
        private Material m_material02;



        IEnumerator Start()
        {



            if (BenchmarkType == 0) // TextMesh Pro Component
            {
                m_textMeshPro = gameObject.AddComponent<TextMeshProUGUI>();
                //m_textContainer = GetComponent<TextContainer>();


                //m_textMeshPro.anchorDampening = true;

                if (TMProFont != null)
                    m_textMeshPro.font = TMProFont;

                //m_textMeshPro.font = Resources.Load("Fonts & Materials/Anton SDF", typeof(TextMeshProFont)) as TextMeshProFont; // Make sure the Anton SDF exists before calling this...           
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/Anton SDF", typeof(Material)) as Material; // Same as above make sure this material exists.

                m_textMeshPro.fontSize = 48;
                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                //m_textMeshPro.anchor = AnchorPositions.Center;
                m_textMeshPro.extraPadding = true;
                //m_textMeshPro.outlineWidth = 0.25f;
                //m_textMeshPro.fontSharedMaterial.SetFloat("_OutlineWidth", 0.2f);
                //m_textMeshPro.fontSharedMaterial.EnableKeyword("UNDERLAY_ON");
                //m_textMeshPro.lineJustification = LineJustificationTypes.Center;
                //m_textMeshPro.enableWordWrapping = true;    
                //m_textMeshPro.lineLength = 60;          
                //m_textMeshPro.characterSpacing = 0.2f;
                //m_textMeshPro.fontColor = new Color32(255, 255, 255, 255);

                m_material01 = m_textMeshPro.font.material;
                m_material02 = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - BEVEL"); // Make sure the LiberationSans SDF exists before calling this...  


            }
            else if (BenchmarkType == 1) // TextMesh
            {
                m_textMesh = gameObject.AddComponent<Text>();

                if (TextMeshFont != null)
                {
                    m_textMesh.font = TextMeshFont;
                    //m_textMesh.renderer.sharedMaterial = m_textMesh.font.material;
                }
                else
                {
                    //m_textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    //m_textMesh.renderer.sharedMaterial = m_textMesh.font.material;
                }

                m_textMesh.fontSize = 48;
                m_textMesh.alignment = TextAnchor.MiddleCenter;

                //m_textMesh.color = new Color32(255, 255, 0, 255);    
            }



            for (int i = 0; i <= 1000000; i++)
            {
                if (BenchmarkType == 0)
                {
                    m_textMeshPro.text = label01 + (i % 1000);
                    if (i % 1000 == 999)
                        m_textMeshPro.fontSharedMaterial = m_textMeshPro.fontSharedMaterial == m_material01 ? m_textMeshPro.fontSharedMaterial = m_material02 : m_textMeshPro.fontSharedMaterial = m_material01;



                }
                else if (BenchmarkType == 1)
                    m_textMesh.text = label02 + (i % 1000).ToString();

                yield return null;
            }


            yield return null;
        }


        /*
        void Update()
        {
            if (BenchmarkType == 0)
            {
                m_textMeshPro.text = (m_frame % 1000).ToString();            
            }
            else if (BenchmarkType == 1)
            {
                m_textMesh.text = (m_frame % 1000).ToString();
            }

            m_frame += 1;
        }
        */
    }

}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark02.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class Benchmark02 : MonoBehaviour
    {

        public int SpawnType = 0;
        public int NumberOfNPC = 12;

        public bool IsTextObjectScaleStatic;
        private TextMeshProFloatingText floatingText_Script;


        void Start()
        {

            for (int i = 0; i < NumberOfNPC; i++)
            {


                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation
                    GameObject go = new GameObject();
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.25f, Random.Range(-95f, 95f));

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();

                    textMeshPro.autoSizeTextContainer = true;
                    textMeshPro.rectTransform.pivot = new Vector2(0.5f, 0);

                    textMeshPro.alignment = TextAlignmentOptions.Bottom;
                    textMeshPro.fontSize = 96;
                    textMeshPro.enableKerning = false;

                    textMeshPro.color = new Color32(255, 255, 0, 255);
                    textMeshPro.text = "!";
                    textMeshPro.isTextObjectScaleStatic = IsTextObjectScaleStatic;

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                    floatingText_Script.IsTextObjectScaleStatic = IsTextObjectScaleStatic;
                }
                else if (SpawnType == 1)
                {
                    // TextMesh Implementation
                    GameObject go = new GameObject();
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.25f, Random.Range(-95f, 95f));

                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.font = Resources.Load<Font>("Fonts/ARIAL");
                    textMesh.GetComponent<Renderer>().sharedMaterial = textMesh.font.material;

                    textMesh.anchor = TextAnchor.LowerCenter;
                    textMesh.fontSize = 96;

                    textMesh.color = new Color32(255, 255, 0, 255);
                    textMesh.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 1;
                }
                else if (SpawnType == 2)
                {
                    // Canvas WorldSpace Camera
                    GameObject go = new GameObject();
                    Canvas canvas = go.AddComponent<Canvas>();
                    canvas.worldCamera = Camera.main;

                    go.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 5f, Random.Range(-95f, 95f));

                    TextMeshProUGUI textObject = new GameObject().AddComponent<TextMeshProUGUI>();
                    textObject.rectTransform.SetParent(go.transform, false);

                    textObject.color = new Color32(255, 255, 0, 255);
                    textObject.alignment = TextAlignmentOptions.Bottom;
                    textObject.fontSize = 96;
                    textObject.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                }



            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark03.cs

```csharp
using UnityEngine;
using System.Collections;
using UnityEngine.TextCore.LowLevel;


namespace TMPro.Examples
{

    public class Benchmark03 : MonoBehaviour
    {
        public enum BenchmarkType { TMP_SDF_MOBILE = 0, TMP_SDF__MOBILE_SSD = 1, TMP_SDF = 2, TMP_BITMAP_MOBILE = 3, TEXTMESH_BITMAP = 4 }

        public int NumberOfSamples = 100;
        public BenchmarkType Benchmark;

        public Font SourceFont;


        void Awake()
        {

        }


        void Start()
        {
            TMP_FontAsset fontAsset = null;

            // Create Dynamic Font Asset for the given font file.
            switch (Benchmark)
            {
                case BenchmarkType.TMP_SDF_MOBILE:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    break;
                case BenchmarkType.TMP_SDF__MOBILE_SSD:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    fontAsset.material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field SSD");
                    break;
                case BenchmarkType.TMP_SDF:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    fontAsset.material.shader = Shader.Find("TextMeshPro/Distance Field");
                    break;
                case BenchmarkType.TMP_BITMAP_MOBILE:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SMOOTH, 256, 256, AtlasPopulationMode.Dynamic);
                    break;
            }

            for (int i = 0; i < NumberOfSamples; i++)
            {
                switch (Benchmark)
                {
                    case BenchmarkType.TMP_SDF_MOBILE:
                    case BenchmarkType.TMP_SDF__MOBILE_SSD:
                    case BenchmarkType.TMP_SDF:
                    case BenchmarkType.TMP_BITMAP_MOBILE:
                        {
                            GameObject go = new GameObject();
                            go.transform.position = new Vector3(0, 1.2f, 0);

                            TextMeshPro textComponent = go.AddComponent<TextMeshPro>();
                            textComponent.font = fontAsset;
                            textComponent.fontSize = 128;
                            textComponent.text = "@";
                            textComponent.alignment = TextAlignmentOptions.Center;
                            textComponent.color = new Color32(255, 255, 0, 255);

                            if (Benchmark == BenchmarkType.TMP_BITMAP_MOBILE)
                                textComponent.fontSize = 132;

                        }
                        break;
                    case BenchmarkType.TEXTMESH_BITMAP:
                        {
                            GameObject go = new GameObject();
                            go.transform.position = new Vector3(0, 1.2f, 0);

                            TextMesh textMesh = go.AddComponent<TextMesh>();
                            textMesh.GetComponent<Renderer>().sharedMaterial = SourceFont.material;
                            textMesh.font = SourceFont;
                            textMesh.anchor = TextAnchor.MiddleCenter;
                            textMesh.fontSize = 130;

                            textMesh.color = new Color32(255, 255, 0, 255);
                            textMesh.text = "@";
                        }
                        break;
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark04.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class Benchmark04 : MonoBehaviour
    {

        public int SpawnType = 0;

        public int MinPointSize = 12;
        public int MaxPointSize = 64;
        public int Steps = 4;

        private Transform m_Transform;
        //private TextMeshProFloatingText floatingText_Script;
        //public Material material;


        void Start()
        {
            m_Transform = transform;

            float lineHeight = 0;
            float orthoSize = Camera.main.orthographicSize = Screen.height / 2;
            float ratio = (float)Screen.width / Screen.height;

            for (int i = MinPointSize; i <= MaxPointSize; i += Steps)
            {
                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation
                    GameObject go = new GameObject("Text - " + i + " Pts");

                    if (lineHeight > orthoSize * 2) return;

                    go.transform.position = m_Transform.position + new Vector3(ratio * -orthoSize * 0.975f, orthoSize * 0.975f - lineHeight, 0);

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();

                    //textMeshPro.fontSharedMaterial = material;
                    //textMeshPro.font = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TextMeshProFont)) as TextMeshProFont;
                    //textMeshPro.anchor = AnchorPositions.Left;
                    textMeshPro.rectTransform.pivot = new Vector2(0, 0.5f);

                    textMeshPro.enableWordWrapping = false;
                    textMeshPro.extraPadding = true;
                    textMeshPro.isOrthographic = true;
                    textMeshPro.fontSize = i;

                    textMeshPro.text = i + " pts - Lorem ipsum dolor sit...";
                    textMeshPro.color = new Color32(255, 255, 255, 255);

                    lineHeight += i;
                }
                else
                {
                    // TextMesh Implementation
                    // Causes crashes since atlas needed exceeds 4096 X 4096
                    /*
                    GameObject go = new GameObject("Arial " + i);

                    //if (lineHeight > orthoSize * 2 * 0.9f) return;

                    go.transform.position = m_Transform.position + new Vector3(ratio * -orthoSize * 0.975f, orthoSize * 0.975f - lineHeight, 1);
                                       
                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    textMesh.renderer.sharedMaterial = textMesh.font.material;
                    textMesh.anchor = TextAnchor.MiddleLeft;
                    textMesh.fontSize = i * 10;

                    textMesh.color = new Color32(255, 255, 255, 255);
                    textMesh.text = i + " pts - Lorem ipsum dolor sit...";

                    lineHeight += i;
                    */
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/CameraController.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class CameraController : MonoBehaviour
    {
        public enum CameraModes { Follow, Isometric, Free }

        private Transform cameraTransform;
        private Transform dummyTarget;

        public Transform CameraTarget;

        public float FollowDistance = 30.0f;
        public float MaxFollowDistance = 100.0f;
        public float MinFollowDistance = 2.0f;

        public float ElevationAngle = 30.0f;
        public float MaxElevationAngle = 85.0f;
        public float MinElevationAngle = 0f;

        public float OrbitalAngle = 0f;

        public CameraModes CameraMode = CameraModes.Follow;

        public bool MovementSmoothing = true;
        public bool RotationSmoothing = false;
        private bool previousSmoothing;

        public float MovementSmoothingValue = 25f;
        public float RotationSmoothingValue = 5.0f;

        public float MoveSensitivity = 2.0f;

        private Vector3 currentVelocity = Vector3.zero;
        private Vector3 desiredPosition;
        private float mouseX;
        private float mouseY;
        private Vector3 moveVector;
        private float mouseWheel;

        // Controls for Touches on Mobile devices
        //private float prev_ZoomDelta;


        private const string event_SmoothingValue = "Slider - Smoothing Value";
        private const string event_FollowDistance = "Slider - Camera Zoom";


        void Awake()
        {
            if (QualitySettings.vSyncCount > 0)
                Application.targetFrameRate = 60;
            else
                Application.targetFrameRate = -1;

            if (Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android)
                Input.simulateMouseWithTouches = false;

            cameraTransform = transform;
            previousSmoothing = MovementSmoothing;
        }


        // Use this for initialization
        void Start()
        {
            if (CameraTarget == null)
            {
                // If we don't have a target (assigned by the player, create a dummy in the center of the scene).
                dummyTarget = new GameObject("Camera Target").transform;
                CameraTarget = dummyTarget;
            }
        }

        // Update is called once per frame
        void LateUpdate()
        {
            GetPlayerInput();


            // Check if we still have a valid target
            if (CameraTarget != null)
            {
                if (CameraMode == CameraModes.Isometric)
                {
                    desiredPosition = CameraTarget.position + Quaternion.Euler(ElevationAngle, OrbitalAngle, 0f) * new Vector3(0, 0, -FollowDistance);
                }
                else if (CameraMode == CameraModes.Follow)
                {
                    desiredPosition = CameraTarget.position + CameraTarget.TransformDirection(Quaternion.Euler(ElevationAngle, OrbitalAngle, 0f) * (new Vector3(0, 0, -FollowDistance)));
                }
                else
                {
                    // Free Camera implementation
                }

                if (MovementSmoothing == true)
                {
                    // Using Smoothing
                    cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, desiredPosition, ref currentVelocity, MovementSmoothingValue * Time.fixedDeltaTime);
                    //cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, Time.deltaTime * 5.0f);
                }
                else
                {
                    // Not using Smoothing
                    cameraTransform.position = desiredPosition;
                }

                if (RotationSmoothing == true)
                    cameraTransform.rotation = Quaternion.Lerp(cameraTransform.rotation, Quaternion.LookRotation(CameraTarget.position - cameraTransform.position), RotationSmoothingValue * Time.deltaTime);
                else
                {
                    cameraTransform.LookAt(CameraTarget);
                }

            }

        }



        void GetPlayerInput()
        {
            moveVector = Vector3.zero;

            // Check Mouse Wheel Input prior to Shift Key so we can apply multiplier on Shift for Scrolling
            mouseWheel = Input.GetAxis("Mouse ScrollWheel");

            float touchCount = Input.touchCount;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || touchCount > 0)
            {
                mouseWheel *= 10;

                if (Input.GetKeyDown(KeyCode.I))
                    CameraMode = CameraModes.Isometric;

                if (Input.GetKeyDown(KeyCode.F))
                    CameraMode = CameraModes.Follow;

                if (Input.GetKeyDown(KeyCode.S))
                    MovementSmoothing = !MovementSmoothing;


                // Check for right mouse button to change camera follow and elevation angle
                if (Input.GetMouseButton(1))
                {
                    mouseY = Input.GetAxis("Mouse Y");
                    mouseX = Input.GetAxis("Mouse X");

                    if (mouseY > 0.01f || mouseY < -0.01f)
                    {
                        ElevationAngle -= mouseY * MoveSensitivity;
                        // Limit Elevation angle between min & max values.
                        ElevationAngle = Mathf.Clamp(ElevationAngle, MinElevationAngle, MaxElevationAngle);
                    }

                    if (mouseX > 0.01f || mouseX < -0.01f)
                    {
                        OrbitalAngle += mouseX * MoveSensitivity;
                        if (OrbitalAngle > 360)
                            OrbitalAngle -= 360;
                        if (OrbitalAngle < 0)
                            OrbitalAngle += 360;
                    }
                }

                // Get Input from Mobile Device
                if (touchCount == 1 && Input.GetTouch(0).phase == TouchPhase.Moved)
                {
                    Vector2 deltaPosition = Input.GetTouch(0).deltaPosition;

                    // Handle elevation changes
                    if (deltaPosition.y > 0.01f || deltaPosition.y < -0.01f)
                    {
                        ElevationAngle -= deltaPosition.y * 0.1f;
                        // Limit Elevation angle between min & max values.
                        ElevationAngle = Mathf.Clamp(ElevationAngle, MinElevationAngle, MaxElevationAngle);
                    }


                    // Handle left & right 
                    if (deltaPosition.x > 0.01f || deltaPosition.x < -0.01f)
                    {
                        OrbitalAngle += deltaPosition.x * 0.1f;
                        if (OrbitalAngle > 360)
                            OrbitalAngle -= 360;
                        if (OrbitalAngle < 0)
                            OrbitalAngle += 360;
                    }

                }

                // Check for left mouse button to select a new CameraTarget or to reset Follow position
                if (Input.GetMouseButton(0))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, 300, 1 << 10 | 1 << 11 | 1 << 12 | 1 << 14))
                    {
                        if (hit.transform == CameraTarget)
                        {
                            // Reset Follow Position
                            OrbitalAngle = 0;
                        }
                        else
                        {
                            CameraTarget = hit.transform;
                            OrbitalAngle = 0;
                            MovementSmoothing = previousSmoothing;
                        }

                    }
                }


                if (Input.GetMouseButton(2))
                {
                    if (dummyTarget == null)
                    {
                        // We need a Dummy Target to anchor the Camera
                        dummyTarget = new GameObject("Camera Target").transform;
                        dummyTarget.position = CameraTarget.position;
                        dummyTarget.rotation = CameraTarget.rotation;
                        CameraTarget = dummyTarget;
                        previousSmoothing = MovementSmoothing;
                        MovementSmoothing = false;
                    }
                    else if (dummyTarget != CameraTarget)
                    {
                        // Move DummyTarget to CameraTarget
                        dummyTarget.position = CameraTarget.position;
                        dummyTarget.rotation = CameraTarget.rotation;
                        CameraTarget = dummyTarget;
                        previousSmoothing = MovementSmoothing;
                        MovementSmoothing = false;
                    }


                    mouseY = Input.GetAxis("Mouse Y");
                    mouseX = Input.GetAxis("Mouse X");

                    moveVector = cameraTransform.TransformDirection(mouseX, mouseY, 0);

                    dummyTarget.Translate(-moveVector, Space.World);

                }

            }

            // Check Pinching to Zoom in - out on Mobile device
            if (touchCount == 2)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
                Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

                float prevTouchDelta = (touch0PrevPos - touch1PrevPos).magnitude;
                float touchDelta = (touch0.position - touch1.position).magnitude;

                float zoomDelta = prevTouchDelta - touchDelta;

                if (zoomDelta > 0.01f || zoomDelta < -0.01f)
                {
                    FollowDistance += zoomDelta * 0.25f;
                    // Limit FollowDistance between min & max values.
                    FollowDistance = Mathf.Clamp(FollowDistance, MinFollowDistance, MaxFollowDistance);
                }


            }

            // Check MouseWheel to Zoom in-out
            if (mouseWheel < -0.01f || mouseWheel > 0.01f)
            {

                FollowDistance -= mouseWheel * 5.0f;
                // Limit FollowDistance between min & max values.
                FollowDistance = Mathf.Clamp(FollowDistance, MinFollowDistance, MaxFollowDistance);
            }


        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ChatController.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChatController : MonoBehaviour {


    public TMP_InputField ChatInputField;

    public TMP_Text ChatDisplayOutput;

    public Scrollbar ChatScrollbar;

    void OnEnable()
    {
        ChatInputField.onSubmit.AddListener(AddToChatOutput);
    }

    void OnDisable()
    {
        ChatInputField.onSubmit.RemoveListener(AddToChatOutput);
    }


    void AddToChatOutput(string newText)
    {
        // Clear Input Field
        ChatInputField.text = string.Empty;

        var timeNow = System.DateTime.Now;

        string formattedInput = "[<#FFFF80>" + timeNow.Hour.ToString("d2") + ":" + timeNow.Minute.ToString("d2") + ":" + timeNow.Second.ToString("d2") + "</color>] " + newText;

        if (ChatDisplayOutput != null)
        {
            // No special formatting for first entry
            // Add line feed before each subsequent entries
            if (ChatDisplayOutput.text == string.Empty)
                ChatDisplayOutput.text = formattedInput;
            else
                ChatDisplayOutput.text += "\n" + formattedInput;
        }

        // Keep Chat input field active
        ChatInputField.ActivateInputField();

        // Set the scrollbar to the bottom when next text is submitted.
        ChatScrollbar.value = 0;
    }

}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/DropdownSample.cs

```csharp
using TMPro;
using UnityEngine;

public class DropdownSample: MonoBehaviour
{
	[SerializeField]
	private TextMeshProUGUI text = null;

	[SerializeField]
	private TMP_Dropdown dropdownWithoutPlaceholder = null;

	[SerializeField]
	private TMP_Dropdown dropdownWithPlaceholder = null;

	public void OnButtonClick()
	{
		text.text = dropdownWithPlaceholder.value > -1 ? "Selected values:\n" + dropdownWithoutPlaceholder.value + " - " + dropdownWithPlaceholder.value : "Error: Please make a selection";
	}
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/EnvMapAnimator.cs

```csharp
using UnityEngine;
using System.Collections;
using TMPro;

public class EnvMapAnimator : MonoBehaviour {

    //private Vector3 TranslationSpeeds;
    public Vector3 RotationSpeeds;
    private TMP_Text m_textMeshPro;
    private Material m_material;
    

    void Awake()
    {
        //Debug.Log("Awake() on Script called.");
        m_textMeshPro = GetComponent<TMP_Text>();
        m_material = m_textMeshPro.fontSharedMaterial;
    }

    // Use this for initialization
	IEnumerator Start ()
    {
        Matrix4x4 matrix = new Matrix4x4(); 
        
        while (true)
        {
            //matrix.SetTRS(new Vector3 (Time.time * TranslationSpeeds.x, Time.time * TranslationSpeeds.y, Time.time * TranslationSpeeds.z), Quaternion.Euler(Time.time * RotationSpeeds.x, Time.time * RotationSpeeds.y , Time.time * RotationSpeeds.z), Vector3.one);
             matrix.SetTRS(Vector3.zero, Quaternion.Euler(Time.time * RotationSpeeds.x, Time.time * RotationSpeeds.y , Time.time * RotationSpeeds.z), Vector3.one);

            m_material.SetMatrix("_EnvMatrix", matrix);

            yield return null;
        }
	}
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ObjectSpin.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class ObjectSpin : MonoBehaviour
    {

#pragma warning disable 0414

        public float SpinSpeed = 5;
        public int RotationRange = 15;
        private Transform m_transform;

        private float m_time;
        private Vector3 m_prevPOS;
        private Vector3 m_initial_Rotation;
        private Vector3 m_initial_Position;
        private Color32 m_lightColor;
        private int frames = 0;

        public enum MotionType { Rotation, BackAndForth, Translation };
        public MotionType Motion;

        void Awake()
        {
            m_transform = transform;
            m_initial_Rotation = m_transform.rotation.eulerAngles;
            m_initial_Position = m_transform.position;

            Light light = GetComponent<Light>();
            m_lightColor = light != null ? light.color : Color.black;
        }


        // Update is called once per frame
        void Update()
        {
            if (Motion == MotionType.Rotation)
            {
                m_transform.Rotate(0, SpinSpeed * Time.deltaTime, 0);
            }
            else if (Motion == MotionType.BackAndForth)
            {
                m_time += SpinSpeed * Time.deltaTime;
                m_transform.rotation = Quaternion.Euler(m_initial_Rotation.x, Mathf.Sin(m_time) * RotationRange + m_initial_Rotation.y, m_initial_Rotation.z);
            }
            else
            {
                m_time += SpinSpeed * Time.deltaTime;

                float x = 15 * Mathf.Cos(m_time * .95f);
                float y = 10; // *Mathf.Sin(m_time * 1f) * Mathf.Cos(m_time * 1f);
                float z = 0f; // *Mathf.Sin(m_time * .9f);    

                m_transform.position = m_initial_Position + new Vector3(x, z, y);

                // Drawing light patterns because they can be cool looking.
                //if (frames > 2)
                //    Debug.DrawLine(m_transform.position, m_prevPOS, m_lightColor, 100f);

                m_prevPOS = m_transform.position;
                frames += 1;
            }
        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ShaderPropAnimator.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class ShaderPropAnimator : MonoBehaviour
    {

        private Renderer m_Renderer;
        private Material m_Material;

        public AnimationCurve GlowCurve;

        public float m_frame;

        void Awake()
        {
            // Cache a reference to object's renderer
            m_Renderer = GetComponent<Renderer>();

            // Cache a reference to object's material and create an instance by doing so.
            m_Material = m_Renderer.material;
        }

        void Start()
        {
            StartCoroutine(AnimateProperties());
        }

        IEnumerator AnimateProperties()
        {
            //float lightAngle;
            float glowPower;
            m_frame = Random.Range(0f, 1f);

            while (true)
            {
                //lightAngle = (m_Material.GetFloat(ShaderPropertyIDs.ID_LightAngle) + Time.deltaTime) % 6.2831853f;
                //m_Material.SetFloat(ShaderPropertyIDs.ID_LightAngle, lightAngle);

                glowPower = GlowCurve.Evaluate(m_frame);
                m_Material.SetFloat(ShaderUtilities.ID_GlowPower, glowPower);

                m_frame += Time.deltaTime * Random.Range(0.2f, 0.3f);
                yield return new WaitForEndOfFrame();
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/SimpleScript.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class SimpleScript : MonoBehaviour
    {

        private TextMeshPro m_textMeshPro;
        //private TMP_FontAsset m_FontAsset;

        private const string label = "The <#0050FF>count is: </color>{0:2}";
        private float m_frame;


        void Start()
        {
            // Add new TextMesh Pro Component
            m_textMeshPro = gameObject.AddComponent<TextMeshPro>();

            m_textMeshPro.autoSizeTextContainer = true;

            // Load the Font Asset to be used.
            //m_FontAsset = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TMP_FontAsset)) as TMP_FontAsset;
            //m_textMeshPro.font = m_FontAsset;

            // Assign Material to TextMesh Pro Component
            //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/LiberationSans SDF - Bevel", typeof(Material)) as Material;
            //m_textMeshPro.fontSharedMaterial.EnableKeyword("BEVEL_ON");
            
            // Set various font settings.
            m_textMeshPro.fontSize = 48;

            m_textMeshPro.alignment = TextAlignmentOptions.Center;
            
            //m_textMeshPro.anchorDampening = true; // Has been deprecated but under consideration for re-implementation.
            //m_textMeshPro.enableAutoSizing = true;

            //m_textMeshPro.characterSpacing = 0.2f;
            //m_textMeshPro.wordSpacing = 0.1f;

            //m_textMeshPro.enableCulling = true;
            m_textMeshPro.enableWordWrapping = false;

            //textMeshPro.fontColor = new Color32(255, 255, 255, 255);
        }


        void Update()
        {
            m_textMeshPro.SetText(label, m_frame % 1000);
            m_frame += 1 * Time.deltaTime;
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/SkewTextExample.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class SkewTextExample : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        public AnimationCurve VertexCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.25f, 2.0f), new Keyframe(0.5f, 0), new Keyframe(0.75f, 2.0f), new Keyframe(1, 0f));
        //public float AngleMultiplier = 1.0f;
        //public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;
        public float ShearAmount = 1.0f;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(WarpText());
        }


        private AnimationCurve CopyAnimationCurve(AnimationCurve curve)
        {
            AnimationCurve newCurve = new AnimationCurve();

            newCurve.keys = curve.keys;

            return newCurve;
        }


        /// <summary>
        ///  Method to curve text along a Unity animation curve.
        /// </summary>
        /// <param name="textComponent"></param>
        /// <returns></returns>
        IEnumerator WarpText()
        {
            VertexCurve.preWrapMode = WrapMode.Clamp;
            VertexCurve.postWrapMode = WrapMode.Clamp;

            //Mesh mesh = m_TextComponent.textInfo.meshInfo[0].mesh;

            Vector3[] vertices;
            Matrix4x4 matrix;

            m_TextComponent.havePropertiesChanged = true; // Need to force the TextMeshPro Object to be updated.
            CurveScale *= 10;
            float old_CurveScale = CurveScale;
            float old_ShearValue = ShearAmount;
            AnimationCurve old_curve = CopyAnimationCurve(VertexCurve);

            while (true)
            {
                if (!m_TextComponent.havePropertiesChanged && old_CurveScale == CurveScale && old_curve.keys[1].value == VertexCurve.keys[1].value && old_ShearValue == ShearAmount)
                {
                    yield return null;
                    continue;
                }

                old_CurveScale = CurveScale;
                old_curve = CopyAnimationCurve(VertexCurve);
                old_ShearValue = ShearAmount;

                m_TextComponent.ForceMeshUpdate(); // Generate the mesh and populate the textInfo with data we can use and manipulate.

                TMP_TextInfo textInfo = m_TextComponent.textInfo;
                int characterCount = textInfo.characterCount;


                if (characterCount == 0) continue;

                //vertices = textInfo.meshInfo[0].vertices;
                //int lastVertexIndex = textInfo.characterInfo[characterCount - 1].vertexIndex;

                float boundsMinX = m_TextComponent.bounds.min.x;  //textInfo.meshInfo[0].mesh.bounds.min.x;
                float boundsMaxX = m_TextComponent.bounds.max.x;  //textInfo.meshInfo[0].mesh.bounds.max.x;



                for (int i = 0; i < characterCount; i++)
                {
                    if (!textInfo.characterInfo[i].isVisible)
                        continue;

                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the index of the mesh used by this character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    vertices = textInfo.meshInfo[materialIndex].vertices;

                    // Compute the baseline mid point for each character
                    Vector3 offsetToMidBaseline = new Vector2((vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) / 2, textInfo.characterInfo[i].baseLine);
                    //float offsetY = VertexCurve.Evaluate((float)i / characterCount + loopCount / 50f); // Random.Range(-0.25f, 0.25f);

                    // Apply offset to adjust our pivot point.
                    vertices[vertexIndex + 0] += -offsetToMidBaseline;
                    vertices[vertexIndex + 1] += -offsetToMidBaseline;
                    vertices[vertexIndex + 2] += -offsetToMidBaseline;
                    vertices[vertexIndex + 3] += -offsetToMidBaseline;

                    // Apply the Shearing FX
                    float shear_value = ShearAmount * 0.01f;
                    Vector3 topShear = new Vector3(shear_value * (textInfo.characterInfo[i].topRight.y - textInfo.characterInfo[i].baseLine), 0, 0);
                    Vector3 bottomShear = new Vector3(shear_value * (textInfo.characterInfo[i].baseLine - textInfo.characterInfo[i].bottomRight.y), 0, 0);

                    vertices[vertexIndex + 0] += -bottomShear;
                    vertices[vertexIndex + 1] += topShear;
                    vertices[vertexIndex + 2] += topShear;
                    vertices[vertexIndex + 3] += -bottomShear;


                    // Compute the angle of rotation for each character based on the animation curve
                    float x0 = (offsetToMidBaseline.x - boundsMinX) / (boundsMaxX - boundsMinX); // Character's position relative to the bounds of the mesh.
                    float x1 = x0 + 0.0001f;
                    float y0 = VertexCurve.Evaluate(x0) * CurveScale;
                    float y1 = VertexCurve.Evaluate(x1) * CurveScale;

                    Vector3 horizontal = new Vector3(1, 0, 0);
                    //Vector3 normal = new Vector3(-(y1 - y0), (x1 * (boundsMaxX - boundsMinX) + boundsMinX) - offsetToMidBaseline.x, 0);
                    Vector3 tangent = new Vector3(x1 * (boundsMaxX - boundsMinX) + boundsMinX, y1) - new Vector3(offsetToMidBaseline.x, y0);

                    float dot = Mathf.Acos(Vector3.Dot(horizontal, tangent.normalized)) * 57.2957795f;
                    Vector3 cross = Vector3.Cross(horizontal, tangent);
                    float angle = cross.z > 0 ? dot : 360 - dot;

                    matrix = Matrix4x4.TRS(new Vector3(0, y0, 0), Quaternion.Euler(0, 0, angle), Vector3.one);

                    vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    vertices[vertexIndex + 0] += offsetToMidBaseline;
                    vertices[vertexIndex + 1] += offsetToMidBaseline;
                    vertices[vertexIndex + 2] += offsetToMidBaseline;
                    vertices[vertexIndex + 3] += offsetToMidBaseline;
                }


                // Upload the mesh with the revised information
                m_TextComponent.UpdateVertexData();

                yield return null; // new WaitForSeconds(0.025f);
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TeleType.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TeleType : MonoBehaviour
    {


        //[Range(0, 100)]
        //public int RevealSpeed = 50;

        private string label01 = "Example <sprite=2> of using <sprite=7> <#ffa000>Graphics Inline</color> <sprite=5> with Text in <font=\"Bangers SDF\" material=\"Bangers SDF - Drop Shadow\">TextMesh<#40a0ff>Pro</color></font><sprite=0> and Unity<sprite=1>";
        private string label02 = "Example <sprite=2> of using <sprite=7> <#ffa000>Graphics Inline</color> <sprite=5> with Text in <font=\"Bangers SDF\" material=\"Bangers SDF - Drop Shadow\">TextMesh<#40a0ff>Pro</color></font><sprite=0> and Unity<sprite=2>";


        private TMP_Text m_textMeshPro;


        void Awake()
        {
            // Get Reference to TextMeshPro Component
            m_textMeshPro = GetComponent<TMP_Text>();
            m_textMeshPro.text = label01;
            m_textMeshPro.enableWordWrapping = true;
            m_textMeshPro.alignment = TextAlignmentOptions.Top;



            //if (GetComponentInParent(typeof(Canvas)) as Canvas == null)
            //{
            //    GameObject canvas = new GameObject("Canvas", typeof(Canvas));
            //    gameObject.transform.SetParent(canvas.transform);
            //    canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            //    // Set RectTransform Size
            //    gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 300);
            //    m_textMeshPro.fontSize = 48;
            //}


        }


        IEnumerator Start()
        {

            // Force and update of the mesh to get valid information.
            m_textMeshPro.ForceMeshUpdate();


            int totalVisibleCharacters = m_textMeshPro.textInfo.characterCount; // Get # of Visible Character in text object
            int counter = 0;
            int visibleCount = 0;

            while (true)
            {
                visibleCount = counter % (totalVisibleCharacters + 1);

                m_textMeshPro.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                // Once the last character has been revealed, wait 1.0 second and start over.
                if (visibleCount >= totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                    m_textMeshPro.text = label02;
                    yield return new WaitForSeconds(1.0f);
                    m_textMeshPro.text = label01;
                    yield return new WaitForSeconds(1.0f);
                }

                counter += 1;

                yield return new WaitForSeconds(0.05f);
            }

            //Debug.Log("Done revealing the text.");
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextConsoleSimulator.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    public class TextConsoleSimulator : MonoBehaviour
    {
        private TMP_Text m_TextComponent;
        private bool hasTextChanged;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(RevealCharacters(m_TextComponent));
            //StartCoroutine(RevealWords(m_TextComponent));
        }


        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        // Event received when the text object has changed.
        void ON_TEXT_CHANGED(Object obj)
        {
            hasTextChanged = true;
        }


        /// <summary>
        /// Method revealing the text one character at a time.
        /// </summary>
        /// <returns></returns>
        IEnumerator RevealCharacters(TMP_Text textComponent)
        {
            textComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = textComponent.textInfo;

            int totalVisibleCharacters = textInfo.characterCount; // Get # of Visible Character in text object
            int visibleCount = 0;

            while (true)
            {
                if (hasTextChanged)
                {
                    totalVisibleCharacters = textInfo.characterCount; // Update visible character count.
                    hasTextChanged = false; 
                }

                if (visibleCount > totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                    visibleCount = 0;
                }

                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                visibleCount += 1;

                yield return null;
            }
        }


        /// <summary>
        /// Method revealing the text one word at a time.
        /// </summary>
        /// <returns></returns>
        IEnumerator RevealWords(TMP_Text textComponent)
        {
            textComponent.ForceMeshUpdate();

            int totalWordCount = textComponent.textInfo.wordCount;
            int totalVisibleCharacters = textComponent.textInfo.characterCount; // Get # of Visible Character in text object
            int counter = 0;
            int currentWord = 0;
            int visibleCount = 0;

            while (true)
            {
                currentWord = counter % (totalWordCount + 1);

                // Get last character index for the current word.
                if (currentWord == 0) // Display no words.
                    visibleCount = 0;
                else if (currentWord < totalWordCount) // Display all other words with the exception of the last one.
                    visibleCount = textComponent.textInfo.wordInfo[currentWord - 1].lastCharacterIndex + 1;
                else if (currentWord == totalWordCount) // Display last word and all remaining characters.
                    visibleCount = totalVisibleCharacters;

                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                // Once the last character has been revealed, wait 1.0 second and start over.
                if (visibleCount >= totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                }

                counter += 1;

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextMeshProFloatingText.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class TextMeshProFloatingText : MonoBehaviour
    {
        public Font TheFont;

        private GameObject m_floatingText;
        private TextMeshPro m_textMeshPro;
        private TextMesh m_textMesh;

        private Transform m_transform;
        private Transform m_floatingText_Transform;
        private Transform m_cameraTransform;

        Vector3 lastPOS = Vector3.zero;
        Quaternion lastRotation = Quaternion.identity;

        public int SpawnType;
        public bool IsTextObjectScaleStatic;

        //private int m_frame = 0;

        static WaitForEndOfFrame k_WaitForEndOfFrame = new WaitForEndOfFrame();
        static WaitForSeconds[] k_WaitForSecondsRandom = new WaitForSeconds[]
        {
            new WaitForSeconds(0.05f), new WaitForSeconds(0.1f), new WaitForSeconds(0.15f), new WaitForSeconds(0.2f), new WaitForSeconds(0.25f),
            new WaitForSeconds(0.3f), new WaitForSeconds(0.35f), new WaitForSeconds(0.4f), new WaitForSeconds(0.45f), new WaitForSeconds(0.5f),
            new WaitForSeconds(0.55f), new WaitForSeconds(0.6f), new WaitForSeconds(0.65f), new WaitForSeconds(0.7f), new WaitForSeconds(0.75f),
            new WaitForSeconds(0.8f), new WaitForSeconds(0.85f), new WaitForSeconds(0.9f), new WaitForSeconds(0.95f), new WaitForSeconds(1.0f),
        };

        void Awake()
        {
            m_transform = transform;
            m_floatingText = new GameObject(this.name + " floating text");

            // Reference to Transform is lost when TMP component is added since it replaces it by a RectTransform.
            //m_floatingText_Transform = m_floatingText.transform;
            //m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

            m_cameraTransform = Camera.main.transform;
        }

        void Start()
        {
            if (SpawnType == 0)
            {
                // TextMesh Pro Implementation
                m_textMeshPro = m_floatingText.AddComponent<TextMeshPro>();
                m_textMeshPro.rectTransform.sizeDelta = new Vector2(3, 3);

                m_floatingText_Transform = m_floatingText.transform;
                m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

                //m_textMeshPro.fontAsset = Resources.Load("Fonts & Materials/JOKERMAN SDF", typeof(TextMeshProFont)) as TextMeshProFont; // User should only provide a string to the resource.
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(Material)) as Material;

                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                m_textMeshPro.color = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                m_textMeshPro.fontSize = 24;
                //m_textMeshPro.enableExtraPadding = true;
                //m_textMeshPro.enableShadows = false;
                m_textMeshPro.enableKerning = false;
                m_textMeshPro.text = string.Empty;
                m_textMeshPro.isTextObjectScaleStatic = IsTextObjectScaleStatic;

                StartCoroutine(DisplayTextMeshProFloatingText());
            }
            else if (SpawnType == 1)
            {
                //Debug.Log("Spawning TextMesh Objects.");

                m_floatingText_Transform = m_floatingText.transform;
                m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

                m_textMesh = m_floatingText.AddComponent<TextMesh>();
                m_textMesh.font = Resources.Load<Font>("Fonts/ARIAL");
                m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                m_textMesh.color = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                m_textMesh.anchor = TextAnchor.LowerCenter;
                m_textMesh.fontSize = 24;

                StartCoroutine(DisplayTextMeshFloatingText());
            }
            else if (SpawnType == 2)
            {

            }

        }


        //void Update()
        //{
        //    if (SpawnType == 0)
        //    {
        //        m_textMeshPro.SetText("{0}", m_frame);
        //    }
        //    else
        //    {
        //        m_textMesh.text = m_frame.ToString();
        //    }
        //    m_frame = (m_frame + 1) % 1000;

        //}


        public IEnumerator DisplayTextMeshProFloatingText()
        {
            float CountDuration = 2.0f; // How long is the countdown alive.
            float starting_Count = Random.Range(5f, 20f); // At what number is the counter starting at.
            float current_Count = starting_Count;

            Vector3 start_pos = m_floatingText_Transform.position;
            Color32 start_color = m_textMeshPro.color;
            float alpha = 255;
            int int_counter = 0;


            float fadeDuration = 3 / starting_Count * CountDuration;

            while (current_Count > 0)
            {
                current_Count -= (Time.deltaTime / CountDuration) * starting_Count;

                if (current_Count <= 3)
                {
                    //Debug.Log("Fading Counter ... " + current_Count.ToString("f2"));
                    alpha = Mathf.Clamp(alpha - (Time.deltaTime / fadeDuration) * 255, 0, 255);
                }

                int_counter = (int)current_Count;
                m_textMeshPro.text = int_counter.ToString();
                //m_textMeshPro.SetText("{0}", (int)current_Count);

                m_textMeshPro.color = new Color32(start_color.r, start_color.g, start_color.b, (byte)alpha);

                // Move the floating text upward each update
                m_floatingText_Transform.position += new Vector3(0, starting_Count * Time.deltaTime, 0);

                // Align floating text perpendicular to Camera.
                if (!lastPOS.Compare(m_cameraTransform.position, 1000) || !lastRotation.Compare(m_cameraTransform.rotation, 1000))
                {
                    lastPOS = m_cameraTransform.position;
                    lastRotation = m_cameraTransform.rotation;
                    m_floatingText_Transform.rotation = lastRotation;
                    Vector3 dir = m_transform.position - lastPOS;
                    m_transform.forward = new Vector3(dir.x, 0, dir.z);
                }

                yield return k_WaitForEndOfFrame;
            }

            //Debug.Log("Done Counting down.");

            yield return k_WaitForSecondsRandom[Random.Range(0, 19)];

            m_floatingText_Transform.position = start_pos;

            StartCoroutine(DisplayTextMeshProFloatingText());
        }


        public IEnumerator DisplayTextMeshFloatingText()
        {
            float CountDuration = 2.0f; // How long is the countdown alive.
            float starting_Count = Random.Range(5f, 20f); // At what number is the counter starting at.
            float current_Count = starting_Count;

            Vector3 start_pos = m_floatingText_Transform.position;
            Color32 start_color = m_textMesh.color;
            float alpha = 255;
            int int_counter = 0;

            float fadeDuration = 3 / starting_Count * CountDuration;

            while (current_Count > 0)
            {
                current_Count -= (Time.deltaTime / CountDuration) * starting_Count;

                if (current_Count <= 3)
                {
                    //Debug.Log("Fading Counter ... " + current_Count.ToString("f2"));
                    alpha = Mathf.Clamp(alpha - (Time.deltaTime / fadeDuration) * 255, 0, 255);
                }

                int_counter = (int)current_Count;
                m_textMesh.text = int_counter.ToString();
                //Debug.Log("Current Count:" + current_Count.ToString("f2"));

                m_textMesh.color = new Color32(start_color.r, start_color.g, start_color.b, (byte)alpha);

                // Move the floating text upward each update
                m_floatingText_Transform.position += new Vector3(0, starting_Count * Time.deltaTime, 0);

                // Align floating text perpendicular to Camera.
                if (!lastPOS.Compare(m_cameraTransform.position, 1000) || !lastRotation.Compare(m_cameraTransform.rotation, 1000))
                {
                    lastPOS = m_cameraTransform.position;
                    lastRotation = m_cameraTransform.rotation;
                    m_floatingText_Transform.rotation = lastRotation;
                    Vector3 dir = m_transform.position - lastPOS;
                    m_transform.forward = new Vector3(dir.x, 0, dir.z);
                }

                yield return k_WaitForEndOfFrame;
            }

            //Debug.Log("Done Counting down.");

            yield return k_WaitForSecondsRandom[Random.Range(0, 20)];

            m_floatingText_Transform.position = start_pos;

            StartCoroutine(DisplayTextMeshFloatingText());
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextMeshSpawner.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TextMeshSpawner : MonoBehaviour
    {

        public int SpawnType = 0;
        public int NumberOfNPC = 12;

        public Font TheFont;

        private TextMeshProFloatingText floatingText_Script;

        void Awake()
        {

        }

        void Start()
        {

            for (int i = 0; i < NumberOfNPC; i++)
            {
                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation     
                    //go.transform.localScale = new Vector3(2, 2, 2);
                    GameObject go = new GameObject(); //"NPC " + i);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.5f, Random.Range(-95f, 95f));

                    //go.transform.position = new Vector3(0, 1.01f, 0);
                    //go.renderer.castShadows = false;
                    //go.renderer.receiveShadows = false;
                    //go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();
                    //textMeshPro.FontAsset = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TextMeshProFont)) as TextMeshProFont;
                    //textMeshPro.anchor = AnchorPositions.Bottom;
                    textMeshPro.fontSize = 96;

                    textMeshPro.text = "!";
                    textMeshPro.color = new Color32(255, 255, 0, 255);
                    //textMeshPro.Text = "!";


                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                }
                else
                {
                    // TextMesh Implementation
                    GameObject go = new GameObject(); //"NPC " + i);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.5f, Random.Range(-95f, 95f));

                    //go.transform.position = new Vector3(0, 1.01f, 0);

                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.GetComponent<Renderer>().sharedMaterial = TheFont.material;
                    textMesh.font = TheFont;
                    textMesh.anchor = TextAnchor.LowerCenter;
                    textMesh.fontSize = 96;

                    textMesh.color = new Color32(255, 255, 0, 255);
                    textMesh.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 1;
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMPro_InstructionOverlay.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMPro_InstructionOverlay : MonoBehaviour
    {

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.BottomLeft;

        private const string instructions = "Camera Control - <#ffff00>Shift + RMB\n</color>Zoom - <#ffff00>Mouse wheel.";

        private TextMeshPro m_TextMeshPro;
        private TextContainer m_textContainer;
        private Transform m_frameCounter_transform;
        private Camera m_camera;

        //private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            m_camera = Camera.main;

            GameObject frameCounter = new GameObject("Frame Counter");
            m_frameCounter_transform = frameCounter.transform;
            m_frameCounter_transform.parent = m_camera.transform;
            m_frameCounter_transform.localRotation = Quaternion.identity;


            m_TextMeshPro = frameCounter.AddComponent<TextMeshPro>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");

            m_TextMeshPro.fontSize = 30;

            m_TextMeshPro.isOverlay = true;
            m_textContainer = frameCounter.GetComponent<TextContainer>();

            Set_FrameCounter_Position(AnchorPosition);
            //last_AnchorPosition = AnchorPosition;

            m_TextMeshPro.text = instructions;

        }




        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {

            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    //m_TextMeshPro.anchor = AnchorPositions.TopLeft;
                    m_textContainer.anchorPosition = TextContainerAnchors.TopLeft;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    //m_TextMeshPro.anchor = AnchorPositions.BottomLeft;
                    m_textContainer.anchorPosition = TextContainerAnchors.BottomLeft;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 0, 100.0f));
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    //m_TextMeshPro.anchor = AnchorPositions.TopRight;
                    m_textContainer.anchorPosition = TextContainerAnchors.TopRight;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    //m_TextMeshPro.anchor = AnchorPositions.BottomRight;
                    m_textContainer.anchorPosition = TextContainerAnchors.BottomRight;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 0, 100.0f));
                    break;
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_DigitValidator.cs

```csharp
using UnityEngine;
using System;


namespace TMPro
{
    /// <summary>
    /// EXample of a Custom Character Input Validator to only allow digits from 0 to 9.
    /// </summary>
    [Serializable]
    //[CreateAssetMenu(fileName = "InputValidator - Digits.asset", menuName = "TextMeshPro/Input Validators/Digits", order = 100)]
    public class TMP_DigitValidator : TMP_InputValidator
    {
        // Custom text input validation function
        public override char Validate(ref string text, ref int pos, char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                text += ch;
                pos += 1;
                return ch;
            }

            return (char)0;
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_ExampleScript_01.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;


namespace TMPro.Examples
{

    public class TMP_ExampleScript_01 : MonoBehaviour
    {
        public enum objectType { TextMeshPro = 0, TextMeshProUGUI = 1 };

        public objectType ObjectType;
        public bool isStatic;

        private TMP_Text m_text;

        //private TMP_InputField m_inputfield;


        private const string k_label = "The count is <#0080ff>{0}</color>";
        private int count;

        void Awake()
        {
            // Get a reference to the TMP text component if one already exists otherwise add one.
            // This example show the convenience of having both TMP components derive from TMP_Text. 
            if (ObjectType == 0)
                m_text = GetComponent<TextMeshPro>() ?? gameObject.AddComponent<TextMeshPro>();
            else
                m_text = GetComponent<TextMeshProUGUI>() ?? gameObject.AddComponent<TextMeshProUGUI>();

            // Load a new font asset and assign it to the text object.
            m_text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Anton SDF");

            // Load a new material preset which was created with the context menu duplicate.
            m_text.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/Anton SDF - Drop Shadow");

            // Set the size of the font.
            m_text.fontSize = 120;

            // Set the text
            m_text.text = "A <#0080ff>simple</color> line of text.";

            // Get the preferred width and height based on the supplied width and height as opposed to the actual size of the current text container.
            Vector2 size = m_text.GetPreferredValues(Mathf.Infinity, Mathf.Infinity);

            // Set the size of the RectTransform based on the new calculated values.
            m_text.rectTransform.sizeDelta = new Vector2(size.x, size.y);
        }


        void Update()
        {
            if (!isStatic)
            {
                m_text.SetText(k_label, count % 1000);
                count += 1;
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_FrameRateCounter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMP_FrameRateCounter : MonoBehaviour
    {
        public float UpdateInterval = 5.0f;
        private float m_LastInterval = 0;
        private int m_Frames = 0;

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.TopRight;

        private string htmlColorTag;
        private const string fpsLabel = "{0:2}</color> <#8080ff>FPS \n<#FF8000>{1:2} <#8080ff>MS";

        private TextMeshPro m_TextMeshPro;
        private Transform m_frameCounter_transform;
        private Camera m_camera;

        private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            m_camera = Camera.main;
            Application.targetFrameRate = 9999;

            GameObject frameCounter = new GameObject("Frame Counter");

            m_TextMeshPro = frameCounter.AddComponent<TextMeshPro>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");


            m_frameCounter_transform = frameCounter.transform;
            m_frameCounter_transform.SetParent(m_camera.transform);
            m_frameCounter_transform.localRotation = Quaternion.identity;

            m_TextMeshPro.enableWordWrapping = false;
            m_TextMeshPro.fontSize = 24;
            //m_TextMeshPro.FontColor = new Color32(255, 255, 255, 128);
            //m_TextMeshPro.edgeWidth = .15f;
            //m_TextMeshPro.isOverlay = true;

            //m_TextMeshPro.FaceColor = new Color32(255, 128, 0, 0);
            //m_TextMeshPro.EdgeColor = new Color32(0, 255, 0, 255);
            //m_TextMeshPro.FontMaterial.renderQueue = 4000;

            //m_TextMeshPro.CreateSoftShadowClone(new Vector2(1f, -1f));

            Set_FrameCounter_Position(AnchorPosition);
            last_AnchorPosition = AnchorPosition;


        }

        void Start()
        {
            m_LastInterval = Time.realtimeSinceStartup;
            m_Frames = 0;
        }

        void Update()
        {
            if (AnchorPosition != last_AnchorPosition)
                Set_FrameCounter_Position(AnchorPosition);

            last_AnchorPosition = AnchorPosition;

            m_Frames += 1;
            float timeNow = Time.realtimeSinceStartup;

            if (timeNow > m_LastInterval + UpdateInterval)
            {
                // display two fractional digits (f2 format)
                float fps = m_Frames / (timeNow - m_LastInterval);
                float ms = 1000.0f / Mathf.Max(fps, 0.00001f);

                if (fps < 30)
                    htmlColorTag = "<color=yellow>";
                else if (fps < 10)
                    htmlColorTag = "<color=red>";
                else
                    htmlColorTag = "<color=green>";

                //string format = System.String.Format(htmlColorTag + "{0:F2} </color>FPS \n{1:F2} <#8080ff>MS",fps, ms);
                //m_TextMeshPro.text = format;

                m_TextMeshPro.SetText(htmlColorTag + fpsLabel, fps, ms);

                m_Frames = 0;
                m_LastInterval = timeNow;
            }
        }


        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {
            //Debug.Log("Changing frame counter anchor position.");
            m_TextMeshPro.margin = new Vector4(1f, 1f, 1f, 1f);

            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopLeft;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(0, 1);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomLeft;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(0, 0);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 0, 100.0f));
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopRight;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(1, 1);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomRight;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(1, 0);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 0, 100.0f));
                    break;
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_PhoneNumberValidator.cs

```csharp
using UnityEngine;
using System.Collections;
using System;

namespace TMPro
{
    /// <summary>
    /// Example of a Custom Character Input Validator to only allow phone number in the (800) 555-1212 format.
    /// </summary>
    [Serializable]
    //[CreateAssetMenu(fileName = "InputValidator - Phone Numbers.asset", menuName = "TextMeshPro/Input Validators/Phone Numbers")]
    public class TMP_PhoneNumberValidator : TMP_InputValidator
    {
        // Custom text input validation function
        public override char Validate(ref string text, ref int pos, char ch)
        {
            Debug.Log("Trying to validate...");
            
            // Return unless the character is a valid digit
            if (ch < '0' && ch > '9') return (char)0;

            int length = text.Length;

            // Enforce Phone Number format for every character input.
            for (int i = 0; i < length + 1; i++)
            {
                switch (i)
                {
                    case 0:
                        if (i == length)
                            text = "(" + ch;
                        pos = 2;
                        break;
                    case 1:
                        if (i == length)
                            text += ch;
                        pos = 2;
                        break;
                    case 2:
                        if (i == length)
                            text += ch;
                        pos = 3;
                        break;
                    case 3:
                        if (i == length)
                            text += ch + ") ";
                        pos = 6;
                        break;
                    case 4:
                        if (i == length)
                            text += ") " + ch;
                        pos = 7;
                        break;
                    case 5:
                        if (i == length)
                            text += " " + ch;
                        pos = 7;
                        break;
                    case 6:
                        if (i == length)
                            text += ch;
                        pos = 7;
                        break;
                    case 7:
                        if (i == length)
                            text += ch;
                        pos = 8;
                        break;
                    case 8:
                        if (i == length)
                            text += ch + "-";
                        pos = 10;
                        break;
                    case 9:
                        if (i == length)
                            text += "-" + ch;
                        pos = 11;
                        break;
                    case 10:
                        if (i == length)
                            text += ch;
                        pos = 11;
                        break;
                    case 11:
                        if (i == length)
                            text += ch;
                        pos = 12;
                        break;
                    case 12:
                        if (i == length)
                            text += ch;
                        pos = 13;
                        break;
                    case 13:
                        if (i == length)
                            text += ch;
                        pos = 14;
                        break;
                }
            }

            return ch;
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextEventCheck.cs

```csharp
using UnityEngine;


namespace TMPro.Examples
{
    public class TMP_TextEventCheck : MonoBehaviour
    {

        public TMP_TextEventHandler TextEventHandler;

        private TMP_Text m_TextComponent;

        void OnEnable()
        {
            if (TextEventHandler != null)
            {
                // Get a reference to the text component
                m_TextComponent = TextEventHandler.GetComponent<TMP_Text>();
                
                TextEventHandler.onCharacterSelection.AddListener(OnCharacterSelection);
                TextEventHandler.onSpriteSelection.AddListener(OnSpriteSelection);
                TextEventHandler.onWordSelection.AddListener(OnWordSelection);
                TextEventHandler.onLineSelection.AddListener(OnLineSelection);
                TextEventHandler.onLinkSelection.AddListener(OnLinkSelection);
            }
        }


        void OnDisable()
        {
            if (TextEventHandler != null)
            {
                TextEventHandler.onCharacterSelection.RemoveListener(OnCharacterSelection);
                TextEventHandler.onSpriteSelection.RemoveListener(OnSpriteSelection);
                TextEventHandler.onWordSelection.RemoveListener(OnWordSelection);
                TextEventHandler.onLineSelection.RemoveListener(OnLineSelection);
                TextEventHandler.onLinkSelection.RemoveListener(OnLinkSelection);
            }
        }


        void OnCharacterSelection(char c, int index)
        {
            Debug.Log("Character [" + c + "] at Index: " + index + " has been selected.");
        }

        void OnSpriteSelection(char c, int index)
        {
            Debug.Log("Sprite [" + c + "] at Index: " + index + " has been selected.");
        }

        void OnWordSelection(string word, int firstCharacterIndex, int length)
        {
            Debug.Log("Word [" + word + "] with first character index of " + firstCharacterIndex + " and length of " + length + " has been selected.");
        }

        void OnLineSelection(string lineText, int firstCharacterIndex, int length)
        {
            Debug.Log("Line [" + lineText + "] with first character index of " + firstCharacterIndex + " and length of " + length + " has been selected.");
        }

        void OnLinkSelection(string linkID, string linkText, int linkIndex)
        {
            if (m_TextComponent != null)
            {
                TMP_LinkInfo linkInfo = m_TextComponent.textInfo.linkInfo[linkIndex];
            }
            
            Debug.Log("Link Index: " + linkIndex + " with ID [" + linkID + "] and Text \"" + linkText + "\" has been selected.");
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextEventHandler.cs

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System;


namespace TMPro
{

    public class TMP_TextEventHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Serializable]
        public class CharacterSelectionEvent : UnityEvent<char, int> { }

        [Serializable]
        public class SpriteSelectionEvent : UnityEvent<char, int> { }

        [Serializable]
        public class WordSelectionEvent : UnityEvent<string, int, int> { }

        [Serializable]
        public class LineSelectionEvent : UnityEvent<string, int, int> { }

        [Serializable]
        public class LinkSelectionEvent : UnityEvent<string, string, int> { }


        /// <summary>
        /// Event delegate triggered when pointer is over a character.
        /// </summary>
        public CharacterSelectionEvent onCharacterSelection
        {
            get { return m_OnCharacterSelection; }
            set { m_OnCharacterSelection = value; }
        }
        [SerializeField]
        private CharacterSelectionEvent m_OnCharacterSelection = new CharacterSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a sprite.
        /// </summary>
        public SpriteSelectionEvent onSpriteSelection
        {
            get { return m_OnSpriteSelection; }
            set { m_OnSpriteSelection = value; }
        }
        [SerializeField]
        private SpriteSelectionEvent m_OnSpriteSelection = new SpriteSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a word.
        /// </summary>
        public WordSelectionEvent onWordSelection
        {
            get { return m_OnWordSelection; }
            set { m_OnWordSelection = value; }
        }
        [SerializeField]
        private WordSelectionEvent m_OnWordSelection = new WordSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a line.
        /// </summary>
        public LineSelectionEvent onLineSelection
        {
            get { return m_OnLineSelection; }
            set { m_OnLineSelection = value; }
        }
        [SerializeField]
        private LineSelectionEvent m_OnLineSelection = new LineSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a link.
        /// </summary>
        public LinkSelectionEvent onLinkSelection
        {
            get { return m_OnLinkSelection; }
            set { m_OnLinkSelection = value; }
        }
        [SerializeField]
        private LinkSelectionEvent m_OnLinkSelection = new LinkSelectionEvent();



        private TMP_Text m_TextComponent;

        private Camera m_Camera;
        private Canvas m_Canvas;

        private int m_selectedLink = -1;
        private int m_lastCharIndex = -1;
        private int m_lastWordIndex = -1;
        private int m_lastLineIndex = -1;

        void Awake()
        {
            // Get a reference to the text component.
            m_TextComponent = gameObject.GetComponent<TMP_Text>();

            // Get a reference to the camera rendering the text taking into consideration the text component type.
            if (m_TextComponent.GetType() == typeof(TextMeshProUGUI))
            {
                m_Canvas = gameObject.GetComponentInParent<Canvas>();
                if (m_Canvas != null)
                {
                    if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                        m_Camera = null;
                    else
                        m_Camera = m_Canvas.worldCamera;
                }
            }
            else
            {
                m_Camera = Camera.main;
            }
        }


        void LateUpdate()
        {
            if (TMP_TextUtilities.IsIntersectingRectTransform(m_TextComponent.rectTransform, Input.mousePosition, m_Camera))
            {
                #region Example of Character or Sprite Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextComponent, Input.mousePosition, m_Camera, true);
                if (charIndex != -1 && charIndex != m_lastCharIndex)
                {
                    m_lastCharIndex = charIndex;

                    TMP_TextElementType elementType = m_TextComponent.textInfo.characterInfo[charIndex].elementType;

                    // Send event to any event listeners depending on whether it is a character or sprite.
                    if (elementType == TMP_TextElementType.Character)
                        SendOnCharacterSelection(m_TextComponent.textInfo.characterInfo[charIndex].character, charIndex);
                    else if (elementType == TMP_TextElementType.Sprite)
                        SendOnSpriteSelection(m_TextComponent.textInfo.characterInfo[charIndex].character, charIndex);
                }
                #endregion


                #region Example of Word Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextComponent, Input.mousePosition, m_Camera);
                if (wordIndex != -1 && wordIndex != m_lastWordIndex)
                {
                    m_lastWordIndex = wordIndex;

                    // Get the information about the selected word.
                    TMP_WordInfo wInfo = m_TextComponent.textInfo.wordInfo[wordIndex];

                    // Send the event to any listeners.
                    SendOnWordSelection(wInfo.GetWord(), wInfo.firstCharacterIndex, wInfo.characterCount);
                }
                #endregion


                #region Example of Line Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int lineIndex = TMP_TextUtilities.FindIntersectingLine(m_TextComponent, Input.mousePosition, m_Camera);
                if (lineIndex != -1 && lineIndex != m_lastLineIndex)
                {
                    m_lastLineIndex = lineIndex;

                    // Get the information about the selected word.
                    TMP_LineInfo lineInfo = m_TextComponent.textInfo.lineInfo[lineIndex];

                    // Send the event to any listeners.
                    char[] buffer = new char[lineInfo.characterCount];
                    for (int i = 0; i < lineInfo.characterCount && i < m_TextComponent.textInfo.characterInfo.Length; i++)
                    {
                        buffer[i] = m_TextComponent.textInfo.characterInfo[i + lineInfo.firstCharacterIndex].character;
                    }

                    string lineText = new string(buffer);
                    SendOnLineSelection(lineText, lineInfo.firstCharacterIndex, lineInfo.characterCount);
                }
                #endregion


                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextComponent, Input.mousePosition, m_Camera);

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    // Get information about the link.
                    TMP_LinkInfo linkInfo = m_TextComponent.textInfo.linkInfo[linkIndex];

                    // Send the event to any listeners.
                    SendOnLinkSelection(linkInfo.GetLinkID(), linkInfo.GetLinkText(), linkIndex);
                }
                #endregion
            }
            else
            {
                // Reset all selections given we are hovering outside the text container bounds.
                m_selectedLink = -1;
                m_lastCharIndex = -1;
                m_lastWordIndex = -1;
                m_lastLineIndex = -1;
            }
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            //Debug.Log("OnPointerEnter()");
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log("OnPointerExit()");
        }


        private void SendOnCharacterSelection(char character, int characterIndex)
        {
            if (onCharacterSelection != null)
                onCharacterSelection.Invoke(character, characterIndex);
        }

        private void SendOnSpriteSelection(char character, int characterIndex)
        {
            if (onSpriteSelection != null)
                onSpriteSelection.Invoke(character, characterIndex);
        }

        private void SendOnWordSelection(string word, int charIndex, int length)
        {
            if (onWordSelection != null)
                onWordSelection.Invoke(word, charIndex, length);
        }

        private void SendOnLineSelection(string line, int charIndex, int length)
        {
            if (onLineSelection != null)
                onLineSelection.Invoke(line, charIndex, length);
        }

        private void SendOnLinkSelection(string linkID, string linkText, int linkIndex)
        {
            if (onLinkSelection != null)
                onLinkSelection.Invoke(linkID, linkText, linkIndex);
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextInfoDebugTool.cs

```csharp
using System;
using UnityEngine;
using System.Collections;
using UnityEditor;


namespace TMPro.Examples
{

    public class TMP_TextInfoDebugTool : MonoBehaviour
    {
        // Since this script is used for debugging, we exclude it from builds.
        // TODO: Rework this script to make it into an editor utility.
        #if UNITY_EDITOR
        public bool ShowCharacters;
        public bool ShowWords;
        public bool ShowLinks;
        public bool ShowLines;
        public bool ShowMeshBounds;
        public bool ShowTextBounds;
        [Space(10)]
        [TextArea(2, 2)]
        public string ObjectStats;

        [SerializeField]
        private TMP_Text m_TextComponent;

        private Transform m_Transform;
        private TMP_TextInfo m_TextInfo;

        private float m_ScaleMultiplier;
        private float m_HandleSize;


        void OnDrawGizmos()
        {
            if (m_TextComponent == null)
            {
                m_TextComponent = GetComponent<TMP_Text>();

                if (m_TextComponent == null)
                    return;
            }

            m_Transform = m_TextComponent.transform;

            // Get a reference to the text object's textInfo
            m_TextInfo = m_TextComponent.textInfo;

            // Update Text Statistics
            ObjectStats = "Characters: " + m_TextInfo.characterCount + "   Words: " + m_TextInfo.wordCount + "   Spaces: " + m_TextInfo.spaceCount + "   Sprites: " + m_TextInfo.spriteCount + "   Links: " + m_TextInfo.linkCount
                          + "\nLines: " + m_TextInfo.lineCount + "   Pages: " + m_TextInfo.pageCount;

            // Get the handle size for drawing the various
            m_ScaleMultiplier = m_TextComponent.GetType() == typeof(TextMeshPro) ? 1 : 0.1f;
            m_HandleSize = HandleUtility.GetHandleSize(m_Transform.position) * m_ScaleMultiplier;

            // Draw line metrics
            #region Draw Lines
            if (ShowLines)
                DrawLineBounds();
            #endregion

            // Draw word metrics
            #region Draw Words
            if (ShowWords)
                DrawWordBounds();
            #endregion

            // Draw character metrics
            #region Draw Characters
            if (ShowCharacters)
                DrawCharactersBounds();
            #endregion

            // Draw Quads around each of the words
            #region Draw Links
            if (ShowLinks)
                DrawLinkBounds();
            #endregion

            // Draw Quad around the bounds of the text
            #region Draw Bounds
            if (ShowMeshBounds)
                DrawBounds();
            #endregion

            // Draw Quad around the rendered region of the text.
            #region Draw Text Bounds
            if (ShowTextBounds)
                DrawTextBounds();
            #endregion
        }


        /// <summary>
        /// Method to draw a rectangle around each character.
        /// </summary>
        /// <param name="text"></param>
        void DrawCharactersBounds()
        {
            int characterCount = m_TextInfo.characterCount;

            for (int i = 0; i < characterCount; i++)
            {
                // Draw visible as well as invisible characters
                TMP_CharacterInfo characterInfo = m_TextInfo.characterInfo[i];

                bool isCharacterVisible = i < m_TextComponent.maxVisibleCharacters &&
                                          characterInfo.lineNumber < m_TextComponent.maxVisibleLines &&
                                          i >= m_TextComponent.firstVisibleCharacter;

                if (m_TextComponent.overflowMode == TextOverflowModes.Page)
                    isCharacterVisible = isCharacterVisible && characterInfo.pageNumber + 1 == m_TextComponent.pageToDisplay;

                if (!isCharacterVisible)
                    continue;

                float dottedLineSize = 6;

                // Get Bottom Left and Top Right position of the current character
                Vector3 bottomLeft = m_Transform.TransformPoint(characterInfo.bottomLeft);
                Vector3 topLeft = m_Transform.TransformPoint(new Vector3(characterInfo.topLeft.x, characterInfo.topLeft.y, 0));
                Vector3 topRight = m_Transform.TransformPoint(characterInfo.topRight);
                Vector3 bottomRight = m_Transform.TransformPoint(new Vector3(characterInfo.bottomRight.x, characterInfo.bottomRight.y, 0));

                // Draw character bounds
                if (characterInfo.isVisible)
                {
                    Color color = Color.green;
                    DrawDottedRectangle(bottomLeft, topRight, color);
                }
                else
                {
                    Color color = Color.grey;

                    float whiteSpaceAdvance = Math.Abs(characterInfo.origin - characterInfo.xAdvance) > 0.01f ? characterInfo.xAdvance : characterInfo.origin + (characterInfo.ascender - characterInfo.descender) * 0.03f;
                    DrawDottedRectangle(m_Transform.TransformPoint(new Vector3(characterInfo.origin, characterInfo.descender, 0)), m_Transform.TransformPoint(new Vector3(whiteSpaceAdvance, characterInfo.ascender, 0)), color, 4);
                }

                float origin = characterInfo.origin;
                float advance = characterInfo.xAdvance;
                float ascentline = characterInfo.ascender;
                float baseline = characterInfo.baseLine;
                float descentline = characterInfo.descender;

                //Draw Ascent line
                Vector3 ascentlineStart = m_Transform.TransformPoint(new Vector3(origin, ascentline, 0));
                Vector3 ascentlineEnd = m_Transform.TransformPoint(new Vector3(advance, ascentline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(ascentlineStart, ascentlineEnd, dottedLineSize);

                // Draw Cap Height & Mean line
                float capline = characterInfo.fontAsset == null ? 0 : baseline + characterInfo.fontAsset.faceInfo.capLine * characterInfo.scale;
                Vector3 capHeightStart = new Vector3(topLeft.x, m_Transform.TransformPoint(new Vector3(0, capline, 0)).y, 0);
                Vector3 capHeightEnd = new Vector3(topRight.x, m_Transform.TransformPoint(new Vector3(0, capline, 0)).y, 0);

                float meanline = characterInfo.fontAsset == null ? 0 : baseline + characterInfo.fontAsset.faceInfo.meanLine * characterInfo.scale;
                Vector3 meanlineStart = new Vector3(topLeft.x, m_Transform.TransformPoint(new Vector3(0, meanline, 0)).y, 0);
                Vector3 meanlineEnd = new Vector3(topRight.x, m_Transform.TransformPoint(new Vector3(0, meanline, 0)).y, 0);

                if (characterInfo.isVisible)
                {
                    // Cap line
                    Handles.color = Color.cyan;
                    Handles.DrawDottedLine(capHeightStart, capHeightEnd, dottedLineSize);

                    // Mean line
                    Handles.color = Color.cyan;
                    Handles.DrawDottedLine(meanlineStart, meanlineEnd, dottedLineSize);
                }

                //Draw Base line
                Vector3 baselineStart = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                Vector3 baselineEnd = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(baselineStart, baselineEnd, dottedLineSize);

                //Draw Descent line
                Vector3 descentlineStart = m_Transform.TransformPoint(new Vector3(origin, descentline, 0));
                Vector3 descentlineEnd = m_Transform.TransformPoint(new Vector3(advance, descentline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(descentlineStart, descentlineEnd, dottedLineSize);

                // Draw Origin
                Vector3 originPosition = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                DrawCrosshair(originPosition, 0.05f / m_ScaleMultiplier, Color.cyan);

                // Draw Horizontal Advance
                Vector3 advancePosition = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));
                DrawSquare(advancePosition, 0.025f / m_ScaleMultiplier, Color.yellow);
                DrawCrosshair(advancePosition, 0.0125f / m_ScaleMultiplier, Color.yellow);

                // Draw text labels for metrics
               if (m_HandleSize < 0.5f)
               {
                   GUIStyle style = new GUIStyle(GUI.skin.GetStyle("Label"));
                   style.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1.0f);
                   style.fontSize = 12;
                   style.fixedWidth = 200;
                   style.fixedHeight = 20;

                   Vector3 labelPosition;
                   float center = (origin + advance) / 2;

                   //float baselineMetrics = 0;
                   //float ascentlineMetrics = ascentline - baseline;
                   //float caplineMetrics = capline - baseline;
                   //float meanlineMetrics = meanline - baseline;
                   //float descentlineMetrics = descentline - baseline;

                   // Ascent Line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, ascentline, 0));
                   style.alignment = TextAnchor.UpperCenter;
                   Handles.Label(labelPosition, "Ascent Line", style);
                   //Handles.Label(labelPosition, "Ascent Line (" + ascentlineMetrics.ToString("f3") + ")" , style);

                   // Base Line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, baseline, 0));
                   Handles.Label(labelPosition, "Base Line", style);
                   //Handles.Label(labelPosition, "Base Line (" + baselineMetrics.ToString("f3") + ")" , style);

                   // Descent line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, descentline, 0));
                   Handles.Label(labelPosition, "Descent Line", style);
                   //Handles.Label(labelPosition, "Descent Line (" + descentlineMetrics.ToString("f3") + ")" , style);

                   if (characterInfo.isVisible)
                   {
                       // Cap Line
                       labelPosition = m_Transform.TransformPoint(new Vector3(center, capline, 0));
                       style.alignment = TextAnchor.UpperCenter;
                       Handles.Label(labelPosition, "Cap Line", style);
                       //Handles.Label(labelPosition, "Cap Line (" + caplineMetrics.ToString("f3") + ")" , style);

                       // Mean Line
                       labelPosition = m_Transform.TransformPoint(new Vector3(center, meanline, 0));
                       style.alignment = TextAnchor.UpperCenter;
                       Handles.Label(labelPosition, "Mean Line", style);
                       //Handles.Label(labelPosition, "Mean Line (" + ascentlineMetrics.ToString("f3") + ")" , style);

                       // Origin
                       labelPosition = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                       style.alignment = TextAnchor.UpperRight;
                       Handles.Label(labelPosition, "Origin ", style);

                       // Advance
                       labelPosition = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));
                       style.alignment = TextAnchor.UpperLeft;
                       Handles.Label(labelPosition, "  Advance", style);
                   }
               }
            }
        }


        /// <summary>
        /// Method to draw rectangles around each word of the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawWordBounds()
        {
            for (int i = 0; i < m_TextInfo.wordCount; i++)
            {
                TMP_WordInfo wInfo = m_TextInfo.wordInfo[i];

                bool isBeginRegion = false;

                Vector3 bottomLeft = Vector3.zero;
                Vector3 topLeft = Vector3.zero;
                Vector3 bottomRight = Vector3.zero;
                Vector3 topRight = Vector3.zero;

                float maxAscender = -Mathf.Infinity;
                float minDescender = Mathf.Infinity;

                Color wordColor = Color.green;

                // Iterate through each character of the word
                for (int j = 0; j < wInfo.characterCount; j++)
                {
                    int characterIndex = wInfo.firstCharacterIndex + j;
                    TMP_CharacterInfo currentCharInfo = m_TextInfo.characterInfo[characterIndex];
                    int currentLine = currentCharInfo.lineNumber;

                    bool isCharacterVisible = characterIndex > m_TextComponent.maxVisibleCharacters ||
                                              currentCharInfo.lineNumber > m_TextComponent.maxVisibleLines ||
                                             (m_TextComponent.overflowMode == TextOverflowModes.Page && currentCharInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                    // Track Max Ascender and Min Descender
                    maxAscender = Mathf.Max(maxAscender, currentCharInfo.ascender);
                    minDescender = Mathf.Min(minDescender, currentCharInfo.descender);

                    if (isBeginRegion == false && isCharacterVisible)
                    {
                        isBeginRegion = true;

                        bottomLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.descender, 0);
                        topLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.ascender, 0);

                        //Debug.Log("Start Word Region at [" + currentCharInfo.character + "]");

                        // If Word is one character
                        if (wInfo.characterCount == 1)
                        {
                            isBeginRegion = false;

                            topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                            bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                            bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                            topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                            // Draw Region
                            DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);

                            //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        }
                    }

                    // Last Character of Word
                    if (isBeginRegion && j == wInfo.characterCount - 1)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);

                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                    // If Word is split on more than one line.
                    else if (isBeginRegion && currentLine != m_TextInfo.characterInfo[characterIndex + 1].lineNumber)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);
                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        maxAscender = -Mathf.Infinity;
                        minDescender = Mathf.Infinity;

                    }
                }

                //Debug.Log(wInfo.GetWord(m_TextMeshPro.textInfo.characterInfo));
            }


        }


        /// <summary>
        /// Draw rectangle around each of the links contained in the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawLinkBounds()
        {
            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            for (int i = 0; i < textInfo.linkCount; i++)
            {
                TMP_LinkInfo linkInfo = textInfo.linkInfo[i];

                bool isBeginRegion = false;

                Vector3 bottomLeft = Vector3.zero;
                Vector3 topLeft = Vector3.zero;
                Vector3 bottomRight = Vector3.zero;
                Vector3 topRight = Vector3.zero;

                float maxAscender = -Mathf.Infinity;
                float minDescender = Mathf.Infinity;

                Color32 linkColor = Color.cyan;

                // Iterate through each character of the link text
                for (int j = 0; j < linkInfo.linkTextLength; j++)
                {
                    int characterIndex = linkInfo.linkTextfirstCharacterIndex + j;
                    TMP_CharacterInfo currentCharInfo = textInfo.characterInfo[characterIndex];
                    int currentLine = currentCharInfo.lineNumber;

                    bool isCharacterVisible = characterIndex > m_TextComponent.maxVisibleCharacters ||
                                              currentCharInfo.lineNumber > m_TextComponent.maxVisibleLines ||
                                             (m_TextComponent.overflowMode == TextOverflowModes.Page && currentCharInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                    // Track Max Ascender and Min Descender
                    maxAscender = Mathf.Max(maxAscender, currentCharInfo.ascender);
                    minDescender = Mathf.Min(minDescender, currentCharInfo.descender);

                    if (isBeginRegion == false && isCharacterVisible)
                    {
                        isBeginRegion = true;

                        bottomLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.descender, 0);
                        topLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.ascender, 0);

                        //Debug.Log("Start Word Region at [" + currentCharInfo.character + "]");

                        // If Link is one character
                        if (linkInfo.linkTextLength == 1)
                        {
                            isBeginRegion = false;

                            topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                            bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                            bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                            topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                            // Draw Region
                            DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                            //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        }
                    }

                    // Last Character of Link
                    if (isBeginRegion && j == linkInfo.linkTextLength - 1)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                    // If Link is split on more than one line.
                    else if (isBeginRegion && currentLine != textInfo.characterInfo[characterIndex + 1].lineNumber)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                        maxAscender = -Mathf.Infinity;
                        minDescender = Mathf.Infinity;
                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                }

                //Debug.Log(wInfo.GetWord(m_TextMeshPro.textInfo.characterInfo));
            }
        }


        /// <summary>
        /// Draw Rectangles around each lines of the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawLineBounds()
        {
            int lineCount = m_TextInfo.lineCount;

            for (int i = 0; i < lineCount; i++)
            {
                TMP_LineInfo lineInfo = m_TextInfo.lineInfo[i];
                TMP_CharacterInfo firstCharacterInfo = m_TextInfo.characterInfo[lineInfo.firstCharacterIndex];
                TMP_CharacterInfo lastCharacterInfo = m_TextInfo.characterInfo[lineInfo.lastCharacterIndex];

                bool isLineVisible = (lineInfo.characterCount == 1 && (firstCharacterInfo.character == 10 || firstCharacterInfo.character == 11 || firstCharacterInfo.character == 0x2028 || firstCharacterInfo.character == 0x2029)) ||
                                      i > m_TextComponent.maxVisibleLines ||
                                     (m_TextComponent.overflowMode == TextOverflowModes.Page && firstCharacterInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                if (!isLineVisible) continue;

                float lineBottomLeft = firstCharacterInfo.bottomLeft.x;
                float lineTopRight = lastCharacterInfo.topRight.x;

                float ascentline = lineInfo.ascender;
                float baseline = lineInfo.baseline;
                float descentline = lineInfo.descender;

                float dottedLineSize = 12;

                // Draw line extents
                DrawDottedRectangle(m_Transform.TransformPoint(lineInfo.lineExtents.min), m_Transform.TransformPoint(lineInfo.lineExtents.max), Color.green, 4);

                // Draw Ascent line
                Vector3 ascentlineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, ascentline, 0));
                Vector3 ascentlineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, ascentline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(ascentlineStart, ascentlineEnd, dottedLineSize);

                // Draw Base line
                Vector3 baseLineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, baseline, 0));
                Vector3 baseLineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, baseline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(baseLineStart, baseLineEnd, dottedLineSize);

                // Draw Descent line
                Vector3 descentLineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, descentline, 0));
                Vector3 descentLineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, descentline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(descentLineStart, descentLineEnd, dottedLineSize);

                // Draw text labels for metrics
                if (m_HandleSize < 1.0f)
                {
                    GUIStyle style = new GUIStyle();
                    style.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
                    style.fontSize = 12;
                    style.fixedWidth = 200;
                    style.fixedHeight = 20;
                    Vector3 labelPosition;

                    // Ascent Line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, ascentline, 0));
                    style.padding = new RectOffset(0, 10, 0, 5);
                    style.alignment = TextAnchor.MiddleRight;
                    Handles.Label(labelPosition, "Ascent Line", style);

                    // Base Line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, baseline, 0));
                    Handles.Label(labelPosition, "Base Line", style);

                    // Descent line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, descentline, 0));
                    Handles.Label(labelPosition, "Descent Line", style);
                }
            }
        }


        /// <summary>
        /// Draw Rectangle around the bounds of the text object.
        /// </summary>
        void DrawBounds()
        {
            Bounds meshBounds = m_TextComponent.bounds;

            // Get Bottom Left and Top Right position of each word
            Vector3 bottomLeft = m_TextComponent.transform.position + meshBounds.min;
            Vector3 topRight = m_TextComponent.transform.position + meshBounds.max;

            DrawRectangle(bottomLeft, topRight, new Color(1, 0.5f, 0));
        }


        void DrawTextBounds()
        {
            Bounds textBounds = m_TextComponent.textBounds;

            Vector3 bottomLeft = m_TextComponent.transform.position + (textBounds.center - textBounds.extents);
            Vector3 topRight = m_TextComponent.transform.position + (textBounds.center + textBounds.extents);

            DrawRectangle(bottomLeft, topRight, new Color(0f, 0.5f, 0.5f));
        }


        // Draw Rectangles
        void DrawRectangle(Vector3 BL, Vector3 TR, Color color)
        {
            Gizmos.color = color;

            Gizmos.DrawLine(new Vector3(BL.x, BL.y, 0), new Vector3(BL.x, TR.y, 0));
            Gizmos.DrawLine(new Vector3(BL.x, TR.y, 0), new Vector3(TR.x, TR.y, 0));
            Gizmos.DrawLine(new Vector3(TR.x, TR.y, 0), new Vector3(TR.x, BL.y, 0));
            Gizmos.DrawLine(new Vector3(TR.x, BL.y, 0), new Vector3(BL.x, BL.y, 0));
        }

        void DrawDottedRectangle(Vector3 bottomLeft, Vector3 topRight, Color color, float size = 5.0f)
        {
            Handles.color = color;
            Handles.DrawDottedLine(bottomLeft, new Vector3(bottomLeft.x, topRight.y, bottomLeft.z), size);
            Handles.DrawDottedLine(new Vector3(bottomLeft.x, topRight.y, bottomLeft.z), topRight, size);
            Handles.DrawDottedLine(topRight, new Vector3(topRight.x, bottomLeft.y, bottomLeft.z), size);
            Handles.DrawDottedLine(new Vector3(topRight.x, bottomLeft.y, bottomLeft.z), bottomLeft, size);
        }

        void DrawSolidRectangle(Vector3 bottomLeft, Vector3 topRight, Color color, float size = 5.0f)
        {
            Handles.color = color;
            Rect rect = new Rect(bottomLeft, topRight - bottomLeft);
            Handles.DrawSolidRectangleWithOutline(rect, color, Color.black);
        }

        void DrawSquare(Vector3 position, float size, Color color)
        {
            Handles.color = color;
            Vector3 bottomLeft = new Vector3(position.x - size, position.y - size, position.z);
            Vector3 topLeft = new Vector3(position.x - size, position.y + size, position.z);
            Vector3 topRight = new Vector3(position.x + size, position.y + size, position.z);
            Vector3 bottomRight = new Vector3(position.x + size, position.y - size, position.z);

            Handles.DrawLine(bottomLeft, topLeft);
            Handles.DrawLine(topLeft, topRight);
            Handles.DrawLine(topRight, bottomRight);
            Handles.DrawLine(bottomRight, bottomLeft);
        }

        void DrawCrosshair(Vector3 position, float size, Color color)
        {
            Handles.color = color;

            Handles.DrawLine(new Vector3(position.x - size, position.y, position.z), new Vector3(position.x + size, position.y, position.z));
            Handles.DrawLine(new Vector3(position.x, position.y - size, position.z), new Vector3(position.x, position.y + size, position.z));
        }


        // Draw Rectangles
        void DrawRectangle(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Color color)
        {
            Gizmos.color = color;

            Gizmos.DrawLine(bl, tl);
            Gizmos.DrawLine(tl, tr);
            Gizmos.DrawLine(tr, br);
            Gizmos.DrawLine(br, bl);
        }


        // Draw Rectangles
        void DrawDottedRectangle(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Color color)
        {
            var cam = Camera.current;
            float dotSpacing = (cam.WorldToScreenPoint(br).x - cam.WorldToScreenPoint(bl).x) / 75f;
            UnityEditor.Handles.color = color;

            UnityEditor.Handles.DrawDottedLine(bl, tl, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(tl, tr, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(tr, br, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(br, bl, dotSpacing);
        }
        #endif
    }
}


```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextSelector_A.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;


namespace TMPro.Examples
{

    public class TMP_TextSelector_A : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TextMeshPro m_TextMeshPro;

        private Camera m_Camera;

        private bool m_isHoveringObject;
        private int m_selectedLink = -1;
        private int m_lastCharIndex = -1;
        private int m_lastWordIndex = -1;

        void Awake()
        {
            m_TextMeshPro = gameObject.GetComponent<TextMeshPro>();
            m_Camera = Camera.main;

            // Force generation of the text object so we have valid data to work with. This is needed since LateUpdate() will be called before the text object has a chance to generated when entering play mode.
            m_TextMeshPro.ForceMeshUpdate();
        }


        void LateUpdate()
        {
            m_isHoveringObject = false;

            if (TMP_TextUtilities.IsIntersectingRectTransform(m_TextMeshPro.rectTransform, Input.mousePosition, Camera.main))
            {
                m_isHoveringObject = true;
            }

            if (m_isHoveringObject)
            {
                #region Example of Character Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, Camera.main, true);
                if (charIndex != -1 && charIndex != m_lastCharIndex && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    //Debug.Log("[" + m_TextMeshPro.textInfo.characterInfo[charIndex].character + "] has been selected.");

                    m_lastCharIndex = charIndex;

                    int meshIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].materialReferenceIndex;

                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                    Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);

                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                    vertexColors[vertexIndex + 0] = c;
                    vertexColors[vertexIndex + 1] = c;
                    vertexColors[vertexIndex + 2] = c;
                    vertexColors[vertexIndex + 3] = c;

                    //m_TextMeshPro.mesh.colors32 = vertexColors;
                    m_TextMeshPro.textInfo.meshInfo[meshIndex].mesh.colors32 = vertexColors;
                }
                #endregion

                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous link selection if one existed.
                if ((linkIndex == -1 && m_selectedLink != -1) || linkIndex != m_selectedLink)
                {
                    //m_TextPopup_RectTransform.gameObject.SetActive(false);
                    m_selectedLink = -1;
                }

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];

                    // The following provides an example of how to access the link properties.
                    //Debug.Log("Link ID: \"" + linkInfo.GetLinkID() + "\"   Link Text: \"" + linkInfo.GetLinkText() + "\""); // Example of how to retrieve the Link ID and Link Text.

                    Vector3 worldPointInRectangle;

                    RectTransformUtility.ScreenPointToWorldPointInRectangle(m_TextMeshPro.rectTransform, Input.mousePosition, m_Camera, out worldPointInRectangle);

                    switch (linkInfo.GetLinkID())
                    {
                        case "id_01": // 100041637: // id_01
                                      //m_TextPopup_RectTransform.position = worldPointInRectangle;
                                      //m_TextPopup_RectTransform.gameObject.SetActive(true);
                                      //m_TextPopup_TMPComponent.text = k_LinkText + " ID 01";
                            break;
                        case "id_02": // 100041638: // id_02
                                      //m_TextPopup_RectTransform.position = worldPointInRectangle;
                                      //m_TextPopup_RectTransform.gameObject.SetActive(true);
                                      //m_TextPopup_TMPComponent.text = k_LinkText + " ID 02";
                            break;
                    }
                }
                #endregion


                #region Example of Word Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, Camera.main);
                if (wordIndex != -1 && wordIndex != m_lastWordIndex)
                {
                    m_lastWordIndex = wordIndex;

                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                    Vector3 wordPOS = m_TextMeshPro.transform.TransformPoint(m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex].bottomLeft);
                    wordPOS = Camera.main.WorldToScreenPoint(wordPOS);

                    //Debug.Log("Mouse Position: " + Input.mousePosition.ToString("f3") + "  Word Position: " + wordPOS.ToString("f3"));

                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[0].colors32;

                    Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    m_TextMeshPro.mesh.colors32 = vertexColors;
                }
                #endregion
            }
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            Debug.Log("OnPointerEnter()");
            m_isHoveringObject = true;
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            Debug.Log("OnPointerExit()");
            m_isHoveringObject = false;
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextSelector_B.cs

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;


#pragma warning disable 0618 // Disabled warning due to SetVertices being deprecated until new release with SetMesh() is available.

namespace TMPro.Examples
{

    public class TMP_TextSelector_B : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerUpHandler
    {
        public RectTransform TextPopup_Prefab_01;

        private RectTransform m_TextPopup_RectTransform;
        private TextMeshProUGUI m_TextPopup_TMPComponent;
        private const string k_LinkText = "You have selected link <#ffff00>";
        private const string k_WordText = "Word Index: <#ffff00>";


        private TextMeshProUGUI m_TextMeshPro;
        private Canvas m_Canvas;
        private Camera m_Camera;

        // Flags
        private bool isHoveringObject;
        private int m_selectedWord = -1;
        private int m_selectedLink = -1;
        private int m_lastIndex = -1;

        private Matrix4x4 m_matrix;

        private TMP_MeshInfo[] m_cachedMeshInfoVertexData;

        void Awake()
        {
            m_TextMeshPro = gameObject.GetComponent<TextMeshProUGUI>();


            m_Canvas = gameObject.GetComponentInParent<Canvas>();

            // Get a reference to the camera if Canvas Render Mode is not ScreenSpace Overlay.
            if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                m_Camera = null;
            else
                m_Camera = m_Canvas.worldCamera;

            // Create pop-up text object which is used to show the link information.
            m_TextPopup_RectTransform = Instantiate(TextPopup_Prefab_01) as RectTransform;
            m_TextPopup_RectTransform.SetParent(m_Canvas.transform, false);
            m_TextPopup_TMPComponent = m_TextPopup_RectTransform.GetComponentInChildren<TextMeshProUGUI>();
            m_TextPopup_RectTransform.gameObject.SetActive(false);
        }


        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            // UnSubscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextMeshPro)
            {
                // Update cached vertex data.
                m_cachedMeshInfoVertexData = m_TextMeshPro.textInfo.CopyMeshInfoVertexData();
            }
        }


        void LateUpdate()
        {
            if (isHoveringObject)
            {
                // Check if Mouse Intersects any of the characters. If so, assign a random color.
                #region Handle Character Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, m_Camera, true);

                // Undo Swap and Vertex Attribute changes.
                if (charIndex == -1 || charIndex != m_lastIndex)
                {
                    RestoreCachedVertexAttributes(m_lastIndex);
                    m_lastIndex = -1;
                }

                if (charIndex != -1 && charIndex != m_lastIndex && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    m_lastIndex = charIndex;

                    // Get the index of the material / sub text object used by this character.
                    int materialIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].materialReferenceIndex;

                    // Get the index of the first vertex of the selected character.
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                    // Get a reference to the vertices array.
                    Vector3[] vertices = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

                    // Determine the center point of the character.
                    Vector2 charMidBasline = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of the character to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    // Translate the character to the middle baseline.
                    vertices[vertexIndex + 0] = vertices[vertexIndex + 0] - offset;
                    vertices[vertexIndex + 1] = vertices[vertexIndex + 1] - offset;
                    vertices[vertexIndex + 2] = vertices[vertexIndex + 2] - offset;
                    vertices[vertexIndex + 3] = vertices[vertexIndex + 3] - offset;

                    float zoomFactor = 1.5f;

                    // Setup the Matrix for the scale change.
                    m_matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * zoomFactor);

                    // Apply Matrix operation on the given character.
                    vertices[vertexIndex + 0] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    // Translate the character back to its original position.
                    vertices[vertexIndex + 0] = vertices[vertexIndex + 0] + offset;
                    vertices[vertexIndex + 1] = vertices[vertexIndex + 1] + offset;
                    vertices[vertexIndex + 2] = vertices[vertexIndex + 2] + offset;
                    vertices[vertexIndex + 3] = vertices[vertexIndex + 3] + offset;

                    // Change Vertex Colors of the highlighted character
                    Color32 c = new Color32(255, 255, 192, 255);

                    // Get a reference to the vertex color
                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;

                    vertexColors[vertexIndex + 0] = c;
                    vertexColors[vertexIndex + 1] = c;
                    vertexColors[vertexIndex + 2] = c;
                    vertexColors[vertexIndex + 3] = c;


                    // Get a reference to the meshInfo of the selected character.
                    TMP_MeshInfo meshInfo = m_TextMeshPro.textInfo.meshInfo[materialIndex];

                    // Get the index of the last character's vertex attributes.
                    int lastVertexIndex = vertices.Length - 4;

                    // Swap the current character's vertex attributes with those of the last element in the vertex attribute arrays.
                    // We do this to make sure this character is rendered last and over other characters.
                    meshInfo.SwapVertexData(vertexIndex, lastVertexIndex);

                    // Need to update the appropriate 
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
                }
                #endregion


                #region Word Selection Handling
                //Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous word selection.
                if (m_TextPopup_RectTransform != null && m_selectedWord != -1 && (wordIndex == -1 || wordIndex != m_selectedWord))
                {
                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[m_selectedWord];

                    // Iterate through each of the characters of the word.
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int characterIndex = wInfo.firstCharacterIndex + i;

                        // Get the index of the material / sub text object used by this character.
                        int meshIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].materialReferenceIndex;

                        // Get the index of the first vertex of this character.
                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].vertexIndex;

                        // Get a reference to the vertex color
                        Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                        Color32 c = vertexColors[vertexIndex + 0].Tint(1.33333f);

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    // Update Geometry
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

                    m_selectedWord = -1;
                }


                // Word Selection Handling
                if (wordIndex != -1 && wordIndex != m_selectedWord && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    m_selectedWord = wordIndex;

                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                    // Iterate through each of the characters of the word.
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int characterIndex = wInfo.firstCharacterIndex + i;

                        // Get the index of the material / sub text object used by this character.
                        int meshIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].materialReferenceIndex;

                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].vertexIndex;

                        // Get a reference to the vertex color
                        Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                        Color32 c = vertexColors[vertexIndex + 0].Tint(0.75f);

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    // Update Geometry
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

                }
                #endregion


                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous link selection if one existed.
                if ((linkIndex == -1 && m_selectedLink != -1) || linkIndex != m_selectedLink)
                {
                    m_TextPopup_RectTransform.gameObject.SetActive(false);
                    m_selectedLink = -1;
                }

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];

                    // Debug.Log("Link ID: \"" + linkInfo.GetLinkID() + "\"   Link Text: \"" + linkInfo.GetLinkText() + "\""); // Example of how to retrieve the Link ID and Link Text.

                    Vector3 worldPointInRectangle;
                    RectTransformUtility.ScreenPointToWorldPointInRectangle(m_TextMeshPro.rectTransform, Input.mousePosition, m_Camera, out worldPointInRectangle);

                    switch (linkInfo.GetLinkID())
                    {
                        case "id_01": // 100041637: // id_01
                            m_TextPopup_RectTransform.position = worldPointInRectangle;
                            m_TextPopup_RectTransform.gameObject.SetActive(true);
                            m_TextPopup_TMPComponent.text = k_LinkText + " ID 01";
                            break;
                        case "id_02": // 100041638: // id_02
                            m_TextPopup_RectTransform.position = worldPointInRectangle;
                            m_TextPopup_RectTransform.gameObject.SetActive(true);
                            m_TextPopup_TMPComponent.text = k_LinkText + " ID 02";
                            break;
                    }
                }
                #endregion

            }
            else
            {
                // Restore any character that may have been modified
                if (m_lastIndex != -1)
                {
                    RestoreCachedVertexAttributes(m_lastIndex);
                    m_lastIndex = -1;
                }
            }
            
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            //Debug.Log("OnPointerEnter()");
            isHoveringObject = true;
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log("OnPointerExit()");
            isHoveringObject = false;
        }


        public void OnPointerClick(PointerEventData eventData)
        {
            //Debug.Log("Click at POS: " + eventData.position + "  World POS: " + eventData.worldPosition);

            // Check if Mouse Intersects any of the characters. If so, assign a random color.
            #region Character Selection Handling
            /*
            int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, m_Camera, true);
            if (charIndex != -1 && charIndex != m_lastIndex)
            {
                //Debug.Log("Character [" + m_TextMeshPro.textInfo.characterInfo[index].character + "] was selected at POS: " + eventData.position);
                m_lastIndex = charIndex;

                Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                uiVertices[vertexIndex + 0].color = c;
                uiVertices[vertexIndex + 1].color = c;
                uiVertices[vertexIndex + 2].color = c;
                uiVertices[vertexIndex + 3].color = c;

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
            }
            */
            #endregion


            #region Word Selection Handling
            //Check if Mouse intersects any words and if so assign a random color to that word.
            /*
            int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, m_Camera);

            // Clear previous word selection.
            if (m_TextPopup_RectTransform != null && m_selectedWord != -1 && (wordIndex == -1 || wordIndex != m_selectedWord))
            {
                TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[m_selectedWord];

                // Get a reference to the uiVertices array.
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                // Iterate through each of the characters of the word.
                for (int i = 0; i < wInfo.characterCount; i++)
                {
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                    Color32 c = uiVertices[vertexIndex + 0].color.Tint(1.33333f);

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);

                m_selectedWord = -1;
            }

            // Handle word selection
            if (wordIndex != -1 && wordIndex != m_selectedWord)
            {
                m_selectedWord = wordIndex;

                TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                // Get a reference to the uiVertices array.
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                // Iterate through each of the characters of the word.
                for (int i = 0; i < wInfo.characterCount; i++)
                {
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                    Color32 c = uiVertices[vertexIndex + 0].color.Tint(0.75f);

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
            }
            */
            #endregion


            #region Link Selection Handling
            /*
            // Check if Mouse intersects any words and if so assign a random color to that word.
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);
            if (linkIndex != -1)
            {
                TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];
                int linkHashCode = linkInfo.hashCode;

                //Debug.Log(TMP_TextUtilities.GetSimpleHashCode("id_02"));

                switch (linkHashCode)
                {
                    case 291445: // id_01
                        if (m_LinkObject01 == null)
                            m_LinkObject01 = Instantiate(Link_01_Prefab);
                        else
                        {
                            m_LinkObject01.gameObject.SetActive(true);
                        }

                        break;
                    case 291446: // id_02
                        break;

                }

                // Example of how to modify vertex attributes like colors
                #region Vertex Attribute Modification Example
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                for (int i = 0; i < linkInfo.characterCount; i++)
                {
                    TMP_CharacterInfo cInfo = m_TextMeshPro.textInfo.characterInfo[linkInfo.firstCharacterIndex + i];

                    if (!cInfo.isVisible) continue; // Skip invisible characters.

                    int vertexIndex = cInfo.vertexIndex;

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
                #endregion
            }
            */
            #endregion
        }


        public void OnPointerUp(PointerEventData eventData)
        {
            //Debug.Log("OnPointerUp()");
        }


        void RestoreCachedVertexAttributes(int index)
        {
            if (index == -1 || index > m_TextMeshPro.textInfo.characterCount - 1) return;

            // Get the index of the material / sub text object used by this character.
            int materialIndex = m_TextMeshPro.textInfo.characterInfo[index].materialReferenceIndex;

            // Get the index of the first vertex of the selected character.
            int vertexIndex = m_TextMeshPro.textInfo.characterInfo[index].vertexIndex;

            // Restore Vertices
            // Get a reference to the cached / original vertices.
            Vector3[] src_vertices = m_cachedMeshInfoVertexData[materialIndex].vertices;

            // Get a reference to the vertices that we need to replace.
            Vector3[] dst_vertices = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

            // Restore / Copy vertices from source to destination
            dst_vertices[vertexIndex + 0] = src_vertices[vertexIndex + 0];
            dst_vertices[vertexIndex + 1] = src_vertices[vertexIndex + 1];
            dst_vertices[vertexIndex + 2] = src_vertices[vertexIndex + 2];
            dst_vertices[vertexIndex + 3] = src_vertices[vertexIndex + 3];

            // Restore Vertex Colors
            // Get a reference to the vertex colors we need to replace.
            Color32[] dst_colors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;

            // Get a reference to the cached / original vertex colors.
            Color32[] src_colors = m_cachedMeshInfoVertexData[materialIndex].colors32;

            // Copy the vertex colors from source to destination.
            dst_colors[vertexIndex + 0] = src_colors[vertexIndex + 0];
            dst_colors[vertexIndex + 1] = src_colors[vertexIndex + 1];
            dst_colors[vertexIndex + 2] = src_colors[vertexIndex + 2];
            dst_colors[vertexIndex + 3] = src_colors[vertexIndex + 3];

            // Restore UV0S
            // UVS0
            Vector2[] src_uv0s = m_cachedMeshInfoVertexData[materialIndex].uvs0;
            Vector2[] dst_uv0s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs0;
            dst_uv0s[vertexIndex + 0] = src_uv0s[vertexIndex + 0];
            dst_uv0s[vertexIndex + 1] = src_uv0s[vertexIndex + 1];
            dst_uv0s[vertexIndex + 2] = src_uv0s[vertexIndex + 2];
            dst_uv0s[vertexIndex + 3] = src_uv0s[vertexIndex + 3];

            // UVS2
            Vector2[] src_uv2s = m_cachedMeshInfoVertexData[materialIndex].uvs2;
            Vector2[] dst_uv2s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs2;
            dst_uv2s[vertexIndex + 0] = src_uv2s[vertexIndex + 0];
            dst_uv2s[vertexIndex + 1] = src_uv2s[vertexIndex + 1];
            dst_uv2s[vertexIndex + 2] = src_uv2s[vertexIndex + 2];
            dst_uv2s[vertexIndex + 3] = src_uv2s[vertexIndex + 3];


            // Restore last vertex attribute as we swapped it as well
            int lastIndex = (src_vertices.Length / 4 - 1) * 4;

            // Vertices
            dst_vertices[lastIndex + 0] = src_vertices[lastIndex + 0];
            dst_vertices[lastIndex + 1] = src_vertices[lastIndex + 1];
            dst_vertices[lastIndex + 2] = src_vertices[lastIndex + 2];
            dst_vertices[lastIndex + 3] = src_vertices[lastIndex + 3];

            // Vertex Colors
            src_colors = m_cachedMeshInfoVertexData[materialIndex].colors32;
            dst_colors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;
            dst_colors[lastIndex + 0] = src_colors[lastIndex + 0];
            dst_colors[lastIndex + 1] = src_colors[lastIndex + 1];
            dst_colors[lastIndex + 2] = src_colors[lastIndex + 2];
            dst_colors[lastIndex + 3] = src_colors[lastIndex + 3];

            // UVS0
            src_uv0s = m_cachedMeshInfoVertexData[materialIndex].uvs0;
            dst_uv0s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs0;
            dst_uv0s[lastIndex + 0] = src_uv0s[lastIndex + 0];
            dst_uv0s[lastIndex + 1] = src_uv0s[lastIndex + 1];
            dst_uv0s[lastIndex + 2] = src_uv0s[lastIndex + 2];
            dst_uv0s[lastIndex + 3] = src_uv0s[lastIndex + 3];

            // UVS2
            src_uv2s = m_cachedMeshInfoVertexData[materialIndex].uvs2;
            dst_uv2s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs2;
            dst_uv2s[lastIndex + 0] = src_uv2s[lastIndex + 0];
            dst_uv2s[lastIndex + 1] = src_uv2s[lastIndex + 1];
            dst_uv2s[lastIndex + 2] = src_uv2s[lastIndex + 2];
            dst_uv2s[lastIndex + 3] = src_uv2s[lastIndex + 3];

            // Need to update the appropriate 
            m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_UiFrameRateCounter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMP_UiFrameRateCounter : MonoBehaviour
    {
        public float UpdateInterval = 5.0f;
        private float m_LastInterval = 0;
        private int m_Frames = 0;

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.TopRight;

        private string htmlColorTag;
        private const string fpsLabel = "{0:2}</color> <#8080ff>FPS \n<#FF8000>{1:2} <#8080ff>MS";

        private TextMeshProUGUI m_TextMeshPro;
        private RectTransform m_frameCounter_transform;

        private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            Application.targetFrameRate = 1000;

            GameObject frameCounter = new GameObject("Frame Counter");
            m_frameCounter_transform = frameCounter.AddComponent<RectTransform>();

            m_frameCounter_transform.SetParent(this.transform, false);

            m_TextMeshPro = frameCounter.AddComponent<TextMeshProUGUI>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");

            m_TextMeshPro.enableWordWrapping = false;
            m_TextMeshPro.fontSize = 36;

            m_TextMeshPro.isOverlay = true;

            Set_FrameCounter_Position(AnchorPosition);
            last_AnchorPosition = AnchorPosition;
        }


        void Start()
        {
            m_LastInterval = Time.realtimeSinceStartup;
            m_Frames = 0;
        }


        void Update()
        {
            if (AnchorPosition != last_AnchorPosition)
                Set_FrameCounter_Position(AnchorPosition);

            last_AnchorPosition = AnchorPosition;

            m_Frames += 1;
            float timeNow = Time.realtimeSinceStartup;

            if (timeNow > m_LastInterval + UpdateInterval)
            {
                // display two fractional digits (f2 format)
                float fps = m_Frames / (timeNow - m_LastInterval);
                float ms = 1000.0f / Mathf.Max(fps, 0.00001f);

                if (fps < 30)
                    htmlColorTag = "<color=yellow>";
                else if (fps < 10)
                    htmlColorTag = "<color=red>";
                else
                    htmlColorTag = "<color=green>";

                m_TextMeshPro.SetText(htmlColorTag + fpsLabel, fps, ms);

                m_Frames = 0;
                m_LastInterval = timeNow;
            }
        }


        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {
            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopLeft;
                    m_frameCounter_transform.pivot = new Vector2(0, 1);
                    m_frameCounter_transform.anchorMin = new Vector2(0.01f, 0.99f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.01f, 0.99f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(0, 1);
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomLeft;
                    m_frameCounter_transform.pivot = new Vector2(0, 0);
                    m_frameCounter_transform.anchorMin = new Vector2(0.01f, 0.01f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.01f, 0.01f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(0, 0);
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopRight;
                    m_frameCounter_transform.pivot = new Vector2(1, 1);
                    m_frameCounter_transform.anchorMin = new Vector2(0.99f, 0.99f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.99f, 0.99f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(1, 1);
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomRight;
                    m_frameCounter_transform.pivot = new Vector2(1, 0);
                    m_frameCounter_transform.anchorMin = new Vector2(0.99f, 0.01f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.99f, 0.01f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(1, 0);
                    break;
            }
        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexColorCycler.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexColorCycler : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {
            // Force the text object to update right away so we can have geometry to modify right from the start.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;
            int currentCharacter = 0;

            Color32[] newVertexColors;
            Color32 c0 = m_TextComponent.color;

            while (true)
            {
                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                // Get the index of the material used by the current character.
                int materialIndex = textInfo.characterInfo[currentCharacter].materialReferenceIndex;

                // Get the vertex colors of the mesh used by this text element (character or sprite).
                newVertexColors = textInfo.meshInfo[materialIndex].colors32;

                // Get the index of the first vertex used by this text element.
                int vertexIndex = textInfo.characterInfo[currentCharacter].vertexIndex;

                // Only change the vertex color if the text element is visible.
                if (textInfo.characterInfo[currentCharacter].isVisible)
                {
                    c0 = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);

                    newVertexColors[vertexIndex + 0] = c0;
                    newVertexColors[vertexIndex + 1] = c0;
                    newVertexColors[vertexIndex + 2] = c0;
                    newVertexColors[vertexIndex + 3] = c0;

                    // New function which pushes (all) updated vertex data to the appropriate meshes when using either the Mesh Renderer or CanvasRenderer.
                    m_TextComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

                    // This last process could be done to only update the vertex data that has changed as opposed to all of the vertex data but it would require extra steps and knowing what type of renderer is used.
                    // These extra steps would be a performance optimization but it is unlikely that such optimization will be necessary.
                }

                currentCharacter = (currentCharacter + 1) % characterCount;

                yield return new WaitForSeconds(0.05f);
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexJitter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexJitter : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;

        /// <summary>
        /// Structure to hold pre-computed animation data.
        /// </summary>
        private struct VertexAnim
        {
            public float angleRange;
            public float angle;
            public float speed;
        }

        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;

            int loopCount = 0;
            hasTextChanged = true;

            // Create an Array which contains pre-computed Angle Ranges and Speeds for a bunch of characters.
            VertexAnim[] vertexAnim = new VertexAnim[1024];
            for (int i = 0; i < 1024; i++)
            {
                vertexAnim[i].angleRange = Random.Range(10f, 25f);
                vertexAnim[i].speed = Random.Range(1f, 3f);
            }

            // Cache the vertex data of the text object as the Jitter FX is applied to the original position of the characters.
            TMP_MeshInfo[] cachedMeshInfo = textInfo.CopyMeshInfoVertexData();

            while (true)
            {
                // Get new copy of vertex data if the text has changed.
                if (hasTextChanged)
                {
                    // Update the copy of the vertex data for the text object.
                    cachedMeshInfo = textInfo.CopyMeshInfoVertexData();

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }


                for (int i = 0; i < characterCount; i++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[i];

                    // Skip characters that are not visible and thus have no geometry to manipulate.
                    if (!charInfo.isVisible)
                        continue;

                    // Retrieve the pre-computed animation data for the given character.
                    VertexAnim vertAnim = vertexAnim[i];

                    // Get the index of the material used by the current character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    // Get the index of the first vertex used by this text element.
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the cached vertices of the mesh used by this text element (character or sprite).
                    Vector3[] sourceVertices = cachedMeshInfo[materialIndex].vertices;

                    // Determine the center point of each character at the baseline.
                    //Vector2 charMidBasline = new Vector2((sourceVertices[vertexIndex + 0].x + sourceVertices[vertexIndex + 2].x) / 2, charInfo.baseLine);
                    // Determine the center point of each character.
                    Vector2 charMidBasline = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of each quad to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    Vector3[] destinationVertices = textInfo.meshInfo[materialIndex].vertices;

                    destinationVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] - offset;
                    destinationVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] - offset;
                    destinationVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] - offset;
                    destinationVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] - offset;

                    vertAnim.angle = Mathf.SmoothStep(-vertAnim.angleRange, vertAnim.angleRange, Mathf.PingPong(loopCount / 25f * vertAnim.speed, 1f));
                    Vector3 jitterOffset = new Vector3(Random.Range(-.25f, .25f), Random.Range(-.25f, .25f), 0);

                    matrix = Matrix4x4.TRS(jitterOffset * CurveScale, Quaternion.Euler(0, 0, Random.Range(-5f, 5f) * AngleMultiplier), Vector3.one);

                    destinationVertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 0]);
                    destinationVertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 1]);
                    destinationVertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 2]);
                    destinationVertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 3]);

                    destinationVertices[vertexIndex + 0] += offset;
                    destinationVertices[vertexIndex + 1] += offset;
                    destinationVertices[vertexIndex + 2] += offset;
                    destinationVertices[vertexIndex + 3] += offset;

                    vertexAnim[i] = vertAnim;
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                loopCount += 1;

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexShakeA.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexShakeA : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float ScaleMultiplier = 1.0f;
        public float RotationMultiplier = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj = m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            Vector3[][] copyOfVertices = new Vector3[0][];

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    if (copyOfVertices.Length < textInfo.meshInfo.Length)
                        copyOfVertices = new Vector3[textInfo.meshInfo.Length][];

                    for (int i = 0; i < textInfo.meshInfo.Length; i++)
                    {
                        int length = textInfo.meshInfo[i].vertices.Length;
                        copyOfVertices[i] = new Vector3[length];
                    }

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                int lineCount = textInfo.lineCount;

                // Iterate through each line of the text.
                for (int i = 0; i < lineCount; i++)
                {

                    int first = textInfo.lineInfo[i].firstCharacterIndex;
                    int last = textInfo.lineInfo[i].lastCharacterIndex;

                    // Determine the center of each line
                    Vector3 centerOfLine = (textInfo.characterInfo[first].bottomLeft + textInfo.characterInfo[last].topRight) / 2;
                    Quaternion rotation = Quaternion.Euler(0, 0, Random.Range(-0.25f, 0.25f) * RotationMultiplier);

                    // Iterate through each character of the line.
                    for (int j = first; j <= last; j++)
                    {
                        // Skip characters that are not visible and thus have no geometry to manipulate.
                        if (!textInfo.characterInfo[j].isVisible)
                            continue;

                        // Get the index of the material used by the current character.
                        int materialIndex = textInfo.characterInfo[j].materialReferenceIndex;

                        // Get the index of the first vertex used by this text element.
                        int vertexIndex = textInfo.characterInfo[j].vertexIndex;

                        // Get the vertices of the mesh used by this text element (character or sprite).
                        Vector3[] sourceVertices = textInfo.meshInfo[materialIndex].vertices;

                        // Need to translate all 4 vertices of each quad to aligned with center of character.
                        // This is needed so the matrix TRS is applied at the origin for each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = sourceVertices[vertexIndex + 0] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] = sourceVertices[vertexIndex + 1] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] = sourceVertices[vertexIndex + 2] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] = sourceVertices[vertexIndex + 3] - centerOfLine;

                        // Determine the random scale change for each character.
                        float randomScale = Random.Range(0.995f - 0.001f * ScaleMultiplier, 1.005f + 0.001f * ScaleMultiplier);

                        // Setup the matrix rotation.
                        matrix = Matrix4x4.TRS(Vector3.one, rotation, Vector3.one * randomScale);

                        // Apply the matrix TRS to the individual characters relative to the center of the current line.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] += centerOfLine;
                    }
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = copyOfVertices[i];
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexShakeB.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexShakeB : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj = m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            Vector3[][] copyOfVertices = new Vector3[0][];

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    if (copyOfVertices.Length < textInfo.meshInfo.Length)
                        copyOfVertices = new Vector3[textInfo.meshInfo.Length][];

                    for (int i = 0; i < textInfo.meshInfo.Length; i++)
                    {
                        int length = textInfo.meshInfo[i].vertices.Length;
                        copyOfVertices[i] = new Vector3[length];
                    }

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                int lineCount = textInfo.lineCount;

                // Iterate through each line of the text.
                for (int i = 0; i < lineCount; i++)
                {

                    int first = textInfo.lineInfo[i].firstCharacterIndex;
                    int last = textInfo.lineInfo[i].lastCharacterIndex;

                    // Determine the center of each line
                    Vector3 centerOfLine = (textInfo.characterInfo[first].bottomLeft + textInfo.characterInfo[last].topRight) / 2;
                    Quaternion rotation = Quaternion.Euler(0, 0, Random.Range(-0.25f, 0.25f));

                    // Iterate through each character of the line.
                    for (int j = first; j <= last; j++)
                    {
                        // Skip characters that are not visible and thus have no geometry to manipulate.
                        if (!textInfo.characterInfo[j].isVisible)
                            continue;

                        // Get the index of the material used by the current character.
                        int materialIndex = textInfo.characterInfo[j].materialReferenceIndex;

                        // Get the index of the first vertex used by this text element.
                        int vertexIndex = textInfo.characterInfo[j].vertexIndex;

                        // Get the vertices of the mesh used by this text element (character or sprite).
                        Vector3[] sourceVertices = textInfo.meshInfo[materialIndex].vertices;

                        // Determine the center point of each character at the baseline.
                        Vector3 charCenter = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                        // Need to translate all 4 vertices of each quad to aligned with center of character.
                        // This is needed so the matrix TRS is applied at the origin for each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = sourceVertices[vertexIndex + 0] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 1] = sourceVertices[vertexIndex + 1] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 2] = sourceVertices[vertexIndex + 2] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 3] = sourceVertices[vertexIndex + 3] - charCenter;

                        // Determine the random scale change for each character.
                        float randomScale = Random.Range(0.95f, 1.05f);

                        // Setup the matrix for the scale change.
                        matrix = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one * randomScale);

                        // Apply the scale change relative to the center of each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 1] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 2] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 3] += charCenter;

                        // Need to translate all 4 vertices of each quad to aligned with the center of the line.
                        // This is needed so the matrix TRS is applied from the center of the line.
                        copyOfVertices[materialIndex][vertexIndex + 0] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] -= centerOfLine;

                        // Setup the matrix rotation.
                        matrix = Matrix4x4.TRS(Vector3.one, rotation, Vector3.one);

                        // Apply the matrix TRS to the individual characters relative to the center of the current line.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] += centerOfLine;
                    }
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = copyOfVertices[i];
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexZoom.cs

```csharp
using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;


namespace TMPro.Examples
{

    public class VertexZoom : MonoBehaviour
    {
        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            // UnSubscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            TMP_MeshInfo[] cachedMeshInfoVertexData = textInfo.CopyMeshInfoVertexData();

            // Allocations for sorting of the modified scales
            List<float> modifiedCharScale = new List<float>();
            List<int> scaleSortingOrder = new List<int>();

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    // Get updated vertex data
                    cachedMeshInfoVertexData = textInfo.CopyMeshInfoVertexData();

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                // Clear list of character scales
                modifiedCharScale.Clear();
                scaleSortingOrder.Clear();

                for (int i = 0; i < characterCount; i++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[i];

                    // Skip characters that are not visible and thus have no geometry to manipulate.
                    if (!charInfo.isVisible)
                        continue;

                    // Get the index of the material used by the current character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    // Get the index of the first vertex used by this text element.
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the cached vertices of the mesh used by this text element (character or sprite).
                    Vector3[] sourceVertices = cachedMeshInfoVertexData[materialIndex].vertices;

                    // Determine the center point of each character at the baseline.
                    //Vector2 charMidBasline = new Vector2((sourceVertices[vertexIndex + 0].x + sourceVertices[vertexIndex + 2].x) / 2, charInfo.baseLine);
                    // Determine the center point of each character.
                    Vector2 charMidBasline = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of each quad to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    Vector3[] destinationVertices = textInfo.meshInfo[materialIndex].vertices;

                    destinationVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] - offset;
                    destinationVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] - offset;
                    destinationVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] - offset;
                    destinationVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] - offset;

                    //Vector3 jitterOffset = new Vector3(Random.Range(-.25f, .25f), Random.Range(-.25f, .25f), 0);

                    // Determine the random scale change for each character.
                    float randomScale = Random.Range(1f, 1.5f);
                    
                    // Add modified scale and index
                    modifiedCharScale.Add(randomScale);
                    scaleSortingOrder.Add(modifiedCharScale.Count - 1);

                    // Setup the matrix for the scale change.
                    //matrix = Matrix4x4.TRS(jitterOffset, Quaternion.Euler(0, 0, Random.Range(-5f, 5f)), Vector3.one * randomScale);
                    matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, Vector3.one * randomScale);

                    destinationVertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 0]);
                    destinationVertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 1]);
                    destinationVertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 2]);
                    destinationVertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 3]);

                    destinationVertices[vertexIndex + 0] += offset;
                    destinationVertices[vertexIndex + 1] += offset;
                    destinationVertices[vertexIndex + 2] += offset;
                    destinationVertices[vertexIndex + 3] += offset;

                    // Restore Source UVS which have been modified by the sorting
                    Vector2[] sourceUVs0 = cachedMeshInfoVertexData[materialIndex].uvs0;
                    Vector2[] destinationUVs0 = textInfo.meshInfo[materialIndex].uvs0;

                    destinationUVs0[vertexIndex + 0] = sourceUVs0[vertexIndex + 0];
                    destinationUVs0[vertexIndex + 1] = sourceUVs0[vertexIndex + 1];
                    destinationUVs0[vertexIndex + 2] = sourceUVs0[vertexIndex + 2];
                    destinationUVs0[vertexIndex + 3] = sourceUVs0[vertexIndex + 3];

                    // Restore Source Vertex Colors
                    Color32[] sourceColors32 = cachedMeshInfoVertexData[materialIndex].colors32;
                    Color32[] destinationColors32 = textInfo.meshInfo[materialIndex].colors32;

                    destinationColors32[vertexIndex + 0] = sourceColors32[vertexIndex + 0];
                    destinationColors32[vertexIndex + 1] = sourceColors32[vertexIndex + 1];
                    destinationColors32[vertexIndex + 2] = sourceColors32[vertexIndex + 2];
                    destinationColors32[vertexIndex + 3] = sourceColors32[vertexIndex + 3];
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    //// Sort Quads based modified scale
                    scaleSortingOrder.Sort((a, b) => modifiedCharScale[a].CompareTo(modifiedCharScale[b]));

                    textInfo.meshInfo[i].SortGeometry(scaleSortingOrder);

                    // Updated modified vertex attributes
                    textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                    textInfo.meshInfo[i].mesh.uv = textInfo.meshInfo[i].uvs0;
                    textInfo.meshInfo[i].mesh.colors32 = textInfo.meshInfo[i].colors32;

                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/WarpTextExample.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class WarpTextExample : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        public AnimationCurve VertexCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.25f, 2.0f), new Keyframe(0.5f, 0), new Keyframe(0.75f, 2.0f), new Keyframe(1, 0f));
        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(WarpText());
        }


        private AnimationCurve CopyAnimationCurve(AnimationCurve curve)
        {
            AnimationCurve newCurve = new AnimationCurve();

            newCurve.keys = curve.keys;

            return newCurve;
        }


        /// <summary>
        ///  Method to curve text along a Unity animation curve.
        /// </summary>
        /// <param name="textComponent"></param>
        /// <returns></returns>
        IEnumerator WarpText()
        {
            VertexCurve.preWrapMode = WrapMode.Clamp;
            VertexCurve.postWrapMode = WrapMode.Clamp;

            //Mesh mesh = m_TextComponent.textInfo.meshInfo[0].mesh;

            Vector3[] vertices;
            Matrix4x4 matrix;

            m_TextComponent.havePropertiesChanged = true; // Need to force the TextMeshPro Object to be updated.
            CurveScale *= 10;
            float old_CurveScale = CurveScale;
            AnimationCurve old_curve = CopyAnimationCurve(VertexCurve);

            while (true)
            {
                if (!m_TextComponent.havePropertiesChanged && old_CurveScale == CurveScale && old_curve.keys[1].value == VertexCurve.keys[1].value)
                {
                    yield return null;
                    continue;
                }

                old_CurveScale = CurveScale;
                old_curve = CopyAnimationCurve(VertexCurve);

                m_TextComponent.ForceMeshUpdate(); // Generate the mesh and populate the textInfo with data we can use and manipulate.

                TMP_TextInfo textInfo = m_TextComponent.textInfo;
                int characterCount = textInfo.characterCount;


                if (characterCount == 0) continue;

                //vertices = textInfo.meshInfo[0].vertices;
                //int lastVertexIndex = textInfo.characterInfo[characterCount - 1].vertexIndex;

                float boundsMinX = m_TextComponent.bounds.min.x;  //textInfo.meshInfo[0].mesh.bounds.min.x;
                float boundsMaxX = m_TextComponent.bounds.max.x;  //textInfo.meshInfo[0].mesh.bounds.max.x;



                for (int i = 0; i < characterCount; i++)
                {
                    if (!textInfo.characterInfo[i].isVisible)
                        continue;

                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the index of the mesh used by this character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    vertices = textInfo.meshInfo[materialIndex].vertices;

                    // Compute the baseline mid point for each character
                    Vector3 offsetToMidBaseline = new Vector2((vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) / 2, textInfo.characterInfo[i].baseLine);
                    //float offsetY = VertexCurve.Evaluate((float)i / characterCount + loopCount / 50f); // Random.Range(-0.25f, 0.25f);

                    // Apply offset to adjust our pivot point.
                    vertices[vertexIndex + 0] += -offsetToMidBaseline;
                    vertices[vertexIndex + 1] += -offsetToMidBaseline;
                    vertices[vertexIndex + 2] += -offsetToMidBaseline;
                    vertices[vertexIndex + 3] += -offsetToMidBaseline;

                    // Compute the angle of rotation for each character based on the animation curve
                    float x0 = (offsetToMidBaseline.x - boundsMinX) / (boundsMaxX - boundsMinX); // Character's position relative to the bounds of the mesh.
                    float x1 = x0 + 0.0001f;
                    float y0 = VertexCurve.Evaluate(x0) * CurveScale;
                    float y1 = VertexCurve.Evaluate(x1) * CurveScale;

                    Vector3 horizontal = new Vector3(1, 0, 0);
                    //Vector3 normal = new Vector3(-(y1 - y0), (x1 * (boundsMaxX - boundsMinX) + boundsMinX) - offsetToMidBaseline.x, 0);
                    Vector3 tangent = new Vector3(x1 * (boundsMaxX - boundsMinX) + boundsMinX, y1) - new Vector3(offsetToMidBaseline.x, y0);

                    float dot = Mathf.Acos(Vector3.Dot(horizontal, tangent.normalized)) * 57.2957795f;
                    Vector3 cross = Vector3.Cross(horizontal, tangent);
                    float angle = cross.z > 0 ? dot : 360 - dot;

                    matrix = Matrix4x4.TRS(new Vector3(0, y0, 0), Quaternion.Euler(0, 0, angle), Vector3.one);

                    vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    vertices[vertexIndex + 0] += offsetToMidBaseline;
                    vertices[vertexIndex + 1] += offsetToMidBaseline;
                    vertices[vertexIndex + 2] += offsetToMidBaseline;
                    vertices[vertexIndex + 3] += offsetToMidBaseline;
                }


                // Upload the mesh with the revised information
                m_TextComponent.UpdateVertexData();

                yield return new WaitForSeconds(0.025f);
            }
        }
    }
}

```

## Assets/UIRaycastInspector.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UIRaycastInspector : MonoBehaviour
{
    void Update()
    {
        if (!EventSystem.current) return;

        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder("UI Raycast stack:\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"{i,2}. {GetPath(r.gameObject)} " +
                          $"(Canvas:{r.module?.transform?.name}  SortingLayer:{r.sortingLayer}  Order:{r.sortingOrder})");
        }
        Debug.Log($"pen {sb.ToString()}");
    }

    string GetPath(GameObject go)
    {
        var t = go.transform; var path = go.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
```

