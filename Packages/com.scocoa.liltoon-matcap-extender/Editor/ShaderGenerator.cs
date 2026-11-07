using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Scocoa.MatCapExtender.Editor
{
    // Builds "<original shader> + MatCap 3rd..6th" from any lilToon-based shader.
    //
    // lilToon shaders are thin wrappers (Properties + UsePass "Hidden/.../ltspass_*/PASS"); the code lives in the
    // Hidden pass shaders. Both are read as source (.shader directly, .lilcontainer through lilToon's own
    // UnpackContainer), copied under new names, and the slot code is injected right before each
    // `#include ".../lil_pass_forward.hlsl"` through the BEFORE_RIMLIGHT hook (after MatCap 2nd).
    internal static class ShaderGenerator
    {
        // Single source of truth: package.json "version" (part of the cache hash).
        public static string ToolVersion => VersionCheck.OwnVersion ?? "unknown";
        public const string GeneratedRoot = "Assets/ScocoaGenerated/MatCapExtender";

        const string LilToonShaderFolderGuid = "ac0a8f602b5e72f458f4914bf08f0269"; // lilDirectoryManager.GetShaderFolderPath
        static readonly string[] HookCandidates = { "BEFORE_RIMLIGHT", "BEFORE_GLITTER" };

        static readonly Regex UsePassRx = new Regex("UsePass\\s+\"([^\"]+)/([^\"/]+)\"", RegexOptions.Compiled);
        static readonly Regex IncludeRx = new Regex("#include\\s+\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex ShaderNameRx = new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex ForwardIncludeRx = new Regex("^([ \\t]*)#include\\s+\"[^\"]*lil_pass_forward(_gem)?\\.hlsl\"",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Dictionary<string, Shader> Memo = new Dictionary<string, Shader>();
        static readonly Dictionary<Shader, string> SupportMemo = new Dictionary<Shader, string>();

        public sealed class Result
        {
            public Shader Shader;
            public string Error;
            public string Hash;
        }

        sealed class SourceSet
        {
            public string MainPath;
            public string MainName;
            public string MainText;
            public List<(string name, string path, string text)> Passes = new List<(string, string, string)>();
            public string Hook;
        }

        // Quick check for the inspector. Returns null when supported, otherwise the reason.
        public static string CheckSupport(Shader shader)
        {
            if (shader == null) return "No shader.";
            if (SupportMemo.TryGetValue(shader, out var cached)) return cached;
            string error = null;
            try { LoadSources(shader, out error); }
            catch (Exception e) { error = e.Message; }
            SupportMemo[shader] = error;
            return error;
        }

        public static void ClearMemo()
        {
            Memo.Clear();
            SupportMemo.Clear();
        }

        public static Result GetOrCreate(Shader source, List<SlotConfig> configs)
        {
            var result = new Result();
            if (configs.Count == 0) { result.Shader = source; return result; }

            SourceSet src;
            try { src = LoadSources(source, out result.Error); }
            catch (Exception e) { result.Error = e.Message; return result; }
            if (src == null) return result;

            string configKey = SlotConfig.KeyOf(configs);
            var hashInput = new StringBuilder();
            hashInput.Append(ToolVersion).Append('|').Append(configKey).Append('|').Append(src.Hook).Append('|').Append(src.MainText);
            foreach (var p in src.Passes) hashInput.Append('|').Append(p.text);
            string hash = Hash(hashInput.ToString());
            result.Hash = hash;

            if (Memo.TryGetValue(hash, out var memoShader) && memoShader != null)
            {
                result.Shader = memoShader;
                return result;
            }

            string dir = GeneratedRoot + "/" + hash;
            string mainOut = dir + "/main.shader";

            var existing = AssetDatabase.LoadAssetAtPath<Shader>(mainOut);
            if (existing == null)
            {
                Directory.CreateDirectory(dir);
                var renamed = new Dictionary<string, string>();
                var passOut = new List<string>();
                string injection = BuildInjection(configs, src.Hook);
                int injected = 0;

                for (int i = 0; i < src.Passes.Count; i++)
                {
                    var p = src.Passes[i];
                    string newName = "Hidden/ScocoaMCX/" + hash + "/" + p.name;
                    renamed[p.name] = newName;
                    string text = FixIncludes(p.text, Path.GetDirectoryName(p.path));
                    text = ShaderNameRx.Replace(text, "Shader \"" + newName + "\"", 1);
                    text = Inject(text, injection, ref injected);
                    string outPath = dir + "/pass" + i + ".shader";
                    WriteShader(outPath, Header(source, configKey) + text);
                    passOut.Add(outPath);
                }

                string main = FixIncludes(src.MainText, Path.GetDirectoryName(src.MainPath));
                main = ShaderNameRx.Replace(main, "Shader \"Hidden/ScocoaMCX/" + hash + "/" + src.MainName + "\"", 1);
                main = UsePassRx.Replace(main, m =>
                    renamed.TryGetValue(m.Groups[1].Value, out var n) ? "UsePass \"" + n + "/" + m.Groups[2].Value + "\"" : m.Value);
                main = Inject(main, injection, ref injected);
                main = InsertProperties(main, BuildProperties(configs), out bool propsOk);

                if (injected == 0 || !propsOk)
                {
                    TryDeleteDir(dir);
                    result.Error = injected == 0
                        ? "No lilToon forward pass found (Lite, Fur-only and FakeShadow shaders are not supported)."
                        : "Could not find the Properties block of the shader.";
                    return result;
                }

                WriteShader(mainOut, Header(source, configKey) + main);

                // Pass shaders first so UsePass in the main shader resolves.
                foreach (var p in passOut) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(mainOut, ImportAssetOptions.ForceSynchronousImport);
                existing = AssetDatabase.LoadAssetAtPath<Shader>(mainOut);

                // .lilcontainer shaders are imported without compiling variants, so some custom shaders carry
                // errors in variants nobody uses. Only errors that the unmodified source doesn't have count.
                var errors = new List<string>();
                var sources = src.Passes.Select(x => x.text).Append(src.MainText).ToList();
                var dirs = src.Passes.Select(x => Path.GetDirectoryName(x.path)).Append(Path.GetDirectoryName(src.MainPath)).ToList();
                var outs = passOut.Append(mainOut).ToList();
                for (int i = 0; i < outs.Count; i++)
                {
                    var s = AssetDatabase.LoadAssetAtPath<Shader>(outs[i]);
                    if (s == null) { errors.Add(outs[i] + ": not imported"); continue; }
                    var found = ErrorSet(s);
                    if (found.Count == 0) continue;
                    var baseline = BaselineErrors(sources[i], dirs[i], dir, i);
                    foreach (var e in found.Where(e => !baseline.Contains(e)).Take(3))
                        errors.Add(Path.GetFileName(outs[i]) + ": " + e);
                    if (found.Any(baseline.Contains))
                        Debug.LogWarning($"[MatCap Extender] {source.name}: the original shader already has compile errors in some variants ({baseline.First()}). They are unrelated to the added slots.");
                }
                if (errors.Count > 0)
                {
                    result.Error = "Generated shader has errors:\n" + string.Join("\n", errors);
                    result.Shader = null;
                    // Never leave a broken shader behind: the cache would hand it out next time.
                    AssetDatabase.DeleteAsset(dir);
                    return result;
                }
            }

            if (existing == null)
            {
                result.Error = "Generated shader could not be loaded: " + mainOut;
                return result;
            }

            Memo[hash] = existing;
            result.Shader = existing;
            return result;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Source collection

        static SourceSet LoadSources(Shader shader, out string error)
        {
            error = null;
            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path))
            {
                error = "Shader is not a project asset (built-in or generated in memory).";
                return null;
            }
            if (path.StartsWith(GeneratedRoot))
            {
                error = "Material already uses a MatCap Extender shader. Assign the original shader.";
                return null;
            }

            var set = new SourceSet { MainPath = path, MainName = shader.name };
            set.MainText = ReadSource(path, out error);
            if (set.MainText == null) return null;

            if (!set.MainText.Contains("_lilToonVersion"))
            {
                error = "Not a lilToon-based shader (no _lilToonVersion property).";
                return null;
            }

            foreach (var name in UsePassRx.Matches(set.MainText).Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
            {
                var passShader = Shader.Find(name);
                string passPath = passShader != null ? AssetDatabase.GetAssetPath(passShader) : null;
                if (string.IsNullOrEmpty(passPath))
                {
                    error = "Pass shader not found: " + name;
                    return null;
                }
                string text = ReadSource(passPath, out error);
                if (text == null) return null;
                set.Passes.Add((name, passPath, text));
            }

            var allTexts = new List<string> { set.MainText };
            allTexts.AddRange(set.Passes.Select(p => p.text));
            if (allTexts.Any(t => Regex.IsMatch(t, "#define\\s+LIL_LITE\\b")))
            {
                error = "lilToon Lite shaders are not supported (switch the material to the normal lilToon shader).";
                return null;
            }
            if (!allTexts.Any(t => ForwardIncludeRx.IsMatch(t)))
            {
                error = "No lilToon forward pass found (Fur-only and FakeShadow shaders are not supported).";
                return null;
            }

            // Custom shaders may already use the hook; pick the first one nobody defines.
            var custom = new StringBuilder();
            var dirs = new List<string> { Path.GetDirectoryName(path) };
            dirs.AddRange(set.Passes.Select(p => Path.GetDirectoryName(p.path)));
            foreach (var d in dirs.Distinct())
            {
                if (IsInsideLilToon(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.hlsl", SearchOption.AllDirectories)) custom.Append(File.ReadAllText(f));
                foreach (var f in Directory.GetFiles(d, "*.lilblock", SearchOption.AllDirectories)) custom.Append(File.ReadAllText(f));
            }
            foreach (var t in allTexts) custom.Append(t);
            string customText = custom.ToString();
            set.Hook = HookCandidates.FirstOrDefault(h => !Regex.IsMatch(customText, "#define\\s+" + h + "\\b"));
            if (set.Hook == null)
            {
                error = "Shader already uses every hook this tool can inject at (" + string.Join(", ", HookCandidates) + ").";
                return null;
            }
            return set;
        }

        static string ReadSource(string path, out string error)
        {
            error = null;
            if (path.EndsWith(".lilcontainer", StringComparison.OrdinalIgnoreCase))
                return lilToon.lilShaderContainer.UnpackContainer(path);
            if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllText(path);
            error = "Unsupported shader file type: " + path;
            return null;
        }

        static string LilToonShaderFolder => AssetDatabase.GUIDToAssetPath(LilToonShaderFolderGuid);

        static bool IsInsideLilToon(string dir)
        {
            string lil = LilToonShaderFolder;
            if (string.IsNullOrEmpty(lil)) return false;
            string root = Path.GetDirectoryName(lil)?.Replace('\\', '/');
            return root != null && dir.Replace('\\', '/').StartsWith(root);
        }

        // Generated files live in another folder, so relative includes must become project paths.
        static string FixIncludes(string text, string baseDir)
        {
            baseDir = baseDir.Replace('\\', '/');
            string lilDir = LilToonShaderFolder;
            return IncludeRx.Replace(text, m =>
            {
                string inc = m.Groups[1].Value;
                if (inc.StartsWith("Assets/") || inc.StartsWith("Packages/")) return m.Value;
                foreach (var cand in new[] { baseDir + "/" + inc, lilDir + "/" + inc })
                {
                    string norm = NormalizePath(cand);
                    if (Exists(norm)) return "#include \"" + norm + "\"";
                }
                return m.Value;
            });
        }

        static bool Exists(string projectPath)
        {
            return File.Exists(projectPath) ||
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(projectPath, AssetPathToGUIDOptions.OnlyExistingAssets));
        }

        static string NormalizePath(string p)
        {
            var parts = new List<string>();
            foreach (var part in p.Replace('\\', '/').Split('/'))
            {
                if (part == "." || part.Length == 0) continue;
                if (part == ".." && parts.Count > 0) { parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Text patching

        static string Inject(string text, string injection, ref int count)
        {
            int c = 0;
            string r = ForwardIncludeRx.Replace(text, m =>
            {
                c++;
                return Indent(injection, m.Groups[1].Value) + m.Value;
            });
            count += c;
            return r;
        }

        static string Indent(string block, string indent)
        {
            var sb = new StringBuilder();
            foreach (var line in block.Split('\n'))
                sb.Append(indent).Append(line.TrimEnd('\r')).Append('\n');
            return sb.ToString();
        }

        static string InsertProperties(string text, string props, out bool ok)
        {
            ok = false;
            var m = Regex.Match(text, "\\bProperties\\s*\\{");
            if (!m.Success) return text;
            int depth = 0;
            for (int i = m.Index + m.Length - 1; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        ok = true;
                        return text.Substring(0, i) + Indent(props, "        ") + "    " + text.Substring(i);
                    }
                }
            }
            return text;
        }

        static string Header(Shader source, string key) =>
            "// Generated by Scocoa lilToon MatCap Extender " + ToolVersion + ". Do not edit.\n" +
            "// Source: " + source.name + " (" + AssetDatabase.GetAssetPath(source) + ")\n" +
            "// Slots: " + key + "\n";

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return BitConverter.ToString(bytes, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }

        static void WriteShader(string path, string text) =>
            File.WriteAllText(path, text.Replace("\r\n", "\n").Replace('\r', '\n'));

        static HashSet<string> ErrorSet(Shader s)
        {
            var set = new HashSet<string>();
            if (!ShaderUtil.ShaderHasError(s)) return set;
            foreach (var msg in ShaderUtil.GetShaderMessages(s))
                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    set.Add($"{msg.message} ({Path.GetFileName(msg.file)}:{msg.line})");
            return set;
        }

        // Imports the unmodified source as a temporary shader and returns its errors.
        static HashSet<string> BaselineErrors(string sourceText, string sourceDir, string dir, int index)
        {
            string path = dir + "/baseline" + index + ".shader";
            string text = FixIncludes(sourceText, sourceDir);
            text = ShaderNameRx.Replace(text, "Shader \"Hidden/ScocoaMCX/baseline/" + Path.GetFileName(dir) + "_" + index + "\"", 1);
            // UsePass targets don't matter for the baseline; drop them so it never references the generated passes.
            text = UsePassRx.Replace(text, "");
            WriteShader(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
            var set = s != null ? ErrorSet(s) : new HashSet<string>();
            AssetDatabase.DeleteAsset(path);
            return set;
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* left for Clear Cache */ }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Generated code

        static string BuildProperties(List<SlotConfig> configs)
        {
            var sb = new StringBuilder();
            sb.Append("//---- Scocoa MatCap Extender\n");
            foreach (var c in configs)
            {
                string p = c.Prefix, n = "MatCap " + Ordinal(c.Number);
                sb.Append($"[HideInInspector] {p}Color (\"{n} Color\", Color) = (1,1,1,1)\n");
                if (c.HasTexture) sb.Append($"[HideInInspector] {p}Tex (\"{n} Texture\", 2D) = \"white\" {{}}\n");
                else sb.Append($"[HideInInspector] {p}Tex_ST (\"{n} Texture ST\", Vector) = (1,1,0,0)\n");
                sb.Append($"[HideInInspector] {p}BlendUV1 (\"{n} Blend UV1\", Vector) = (0,0,0,0)\n");
                sb.Append($"[HideInInspector] {p}MainStrength (\"{n} Main Color Power\", Range(0, 1)) = 0\n");
                sb.Append($"[HideInInspector] {p}VRParallax (\"{n} VR Parallax\", Range(0, 1)) = 1\n");
                sb.Append($"[HideInInspector] {p}Blend (\"{n} Blend\", Range(0, 1)) = 1\n");
                sb.Append($"[HideInInspector] {p}EnableLighting (\"{n} Enable Lighting\", Range(0, 1)) = 1\n");
                sb.Append($"[HideInInspector] {p}ShadowMask (\"{n} Shadow Mask\", Range(0, 1)) = 0\n");
                sb.Append($"[HideInInspector] {p}Lod (\"{n} Blur\", Range(0, 10)) = 0\n");
                sb.Append($"[HideInInspector] {p}NormalStrength (\"{n} Normal Strength\", Range(0, 1)) = 1\n");
                if (c.HasMask) sb.Append($"[HideInInspector] {p}Mask (\"{n} Mask\", 2D) = \"white\" {{}}\n");
                if (c.HasCustomNormal)
                {
                    sb.Append($"[HideInInspector] {p}BumpMap (\"{n} Normal Map\", 2D) = \"bump\" {{}}\n");
                    sb.Append($"[HideInInspector] {p}BumpScale (\"{n} Normal Scale\", Range(-10, 10)) = 1\n");
                }
            }
            return sb.ToString();
        }

        // Mirrors lilGetMatCap2nd (lil_common_frag.hlsl, lilToon 2.4.1) with the per-material switches turned
        // into constants or removed code.
        static string BuildInjection(List<SlotConfig> configs, string hook)
        {
            var sb = new StringBuilder();
            sb.Append("//---- Scocoa MatCap Extender (generated)\n");
            sb.Append("#if !defined(LIL_LITE) && !defined(LIL_OUTLINE) && !defined(SCOCOA_MCX_INCLUDED)\n");
            sb.Append("#define SCOCOA_MCX_INCLUDED\n");

            foreach (var c in configs)
            {
                string p = c.Prefix;
                bool guardFA = !c.ApplyInForwardAdd;
                if (guardFA) sb.Append("#if !defined(LIL_PASS_FORWARDADD)\n");

                sb.Append($"float4 {p}Color;\nfloat4 {p}Tex_ST;\nfloat4 {p}BlendUV1;\n");
                sb.Append($"float {p}MainStrength;\nfloat {p}VRParallax;\nfloat {p}Blend;\nfloat {p}EnableLighting;\n");
                sb.Append($"float {p}ShadowMask;\nfloat {p}Lod;\nfloat {p}NormalStrength;\n");
                if (c.HasTexture) sb.Append($"TEXTURE2D({p}Tex);\n");
                if (c.HasMask) sb.Append($"TEXTURE2D({p}Mask);\nfloat4 {p}Mask_ST;\n");
                if (c.HasCustomNormal) sb.Append($"TEXTURE2D({p}BumpMap);\nfloat4 {p}BumpMap_ST;\nfloat {p}BumpScale;\n");

                sb.Append($"void scocoaMcx{c.Number}(inout lilFragData fd LIL_SAMP_IN_FUNC(samp))\n{{\n");
                if (c.Animatable) sb.Append($"    if({p}Blend <= 0.0) return;\n");
                sb.Append("    float3 N = fd.N;\n");
                sb.Append("    #if defined(LIL_FEATURE_NORMAL_1ST) || defined(LIL_FEATURE_NORMAL_2ND)\n");
                sb.Append($"        N = lerp(fd.origN, fd.N, {p}NormalStrength);\n");
                sb.Append("    #endif\n");
                if (c.HasCustomNormal)
                {
                    sb.Append($"    float4 normalTex = LIL_SAMPLE_2D_ST({p}BumpMap, samp, fd.uvMain);\n");
                    sb.Append($"    float3 normalmap = lilUnpackNormalScale(normalTex, {p}BumpScale);\n");
                    sb.Append("    N = normalize(mul(normalmap, fd.TBN));\n");
                    sb.Append("    N = fd.facing < (_FlipNormal-1.0) ? -N : N;\n");
                }
                sb.Append($"    float2 matUV = lilCalcMatCapUV(fd.uv1, normalize(N), fd.V, fd.headV, {p}Tex_ST, {p}BlendUV1.xy, {L(c.ZRotCancel)}, {L(c.FixPerspective)}, {p}VRParallax);\n");
                sb.Append($"    float4 matCapColor = {p}Color;\n");
                if (c.HasTexture) sb.Append($"    matCapColor *= LIL_SAMPLE_2D_LOD({p}Tex, lil_sampler_linear_repeat, matUV, {p}Lod);\n");
                sb.Append("    #if !defined(LIL_PASS_FORWARDADD)\n");
                sb.Append($"        matCapColor.rgb = lerp(matCapColor.rgb, matCapColor.rgb * fd.lightColor, {p}EnableLighting);\n");
                sb.Append("    #else\n");
                if (c.BlendMode < 3) sb.Append($"        matCapColor.rgb *= fd.lightColor * {p}EnableLighting;\n");
                sb.Append("    #endif\n");
                sb.Append($"    matCapColor.a = lerp(matCapColor.a, matCapColor.a * fd.shadowmix, {p}ShadowMask);\n");
                if (c.ApplyTransparency)
                {
                    sb.Append("    #if LIL_RENDER == 2 && !defined(LIL_REFRACTION)\n");
                    sb.Append("        matCapColor.a *= fd.col.a;\n");
                    sb.Append("    #endif\n");
                }
                if (c.BackfaceMask) sb.Append("    matCapColor.a = fd.facing < 0.0 ? 0.0 : matCapColor.a;\n");
                if (c.HasMask) sb.Append($"    float3 matCapMask = LIL_SAMPLE_2D_ST({p}Mask, samp, fd.uvMain).rgb;\n");
                else sb.Append("    float3 matCapMask = 1.0;\n");
                sb.Append($"    matCapColor.rgb = lerp(matCapColor.rgb, matCapColor.rgb * fd.albedo, {p}MainStrength);\n");
                sb.Append($"    fd.col.rgb = lilBlendColor(fd.col.rgb, matCapColor.rgb, {p}Blend * matCapColor.a * matCapMask, {c.BlendMode});\n");
                sb.Append("}\n");

                if (guardFA) sb.Append("#endif\n");
            }

            string Calls(bool forwardAdd) => string.Concat(configs
                .Where(c => !forwardAdd || c.ApplyInForwardAdd)
                .Select(c => $" scocoaMcx{c.Number}(fd LIL_SAMP_IN(sampler_MainTex));"));

            sb.Append($"#undef {hook}\n");
            sb.Append("#if defined(LIL_PASS_FORWARDADD)\n");
            sb.Append($"    #define {hook}{Calls(true)}\n");
            sb.Append("#else\n");
            sb.Append($"    #define {hook}{Calls(false)}\n");
            sb.Append("#endif\n");
            sb.Append("#endif\n");
            sb.Append("//---- end Scocoa MatCap Extender\n");
            return sb.ToString();
        }

        static string L(bool b) => b ? "true" : "false";

        public static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => n + "th" };
    }
}
