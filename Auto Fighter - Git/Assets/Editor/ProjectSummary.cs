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