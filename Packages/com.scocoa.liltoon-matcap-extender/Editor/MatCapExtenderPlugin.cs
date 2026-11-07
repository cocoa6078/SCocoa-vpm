using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(Scocoa.MatCapExtender.Editor.MatCapExtenderPlugin))]

namespace Scocoa.MatCapExtender.Editor
{
    public sealed class MatCapExtenderPlugin : Plugin<MatCapExtenderPlugin>
    {
        public override string QualifiedName => VersionCheck.PackageName;
        public override string DisplayName => "lilToon MatCap Extender";

        protected override void Configure()
        {
            // After PCSS4VRC swaps lilToon materials to PCSS4lilToon (so the extra slots are added to that shader),
            // before Modular Avatar / AAO / atlasing tools see the materials.
            InPhase(BuildPhase.FirstChance)
                .AfterPlugin("nharuka.pcss4vrc")
                .BeforePlugin("nadena.dev.modular-avatar")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Add MatCap slots", Apply)
                .PreviewingWith(new MatCapPreviewFilter());
        }

        static void Apply(BuildContext context)
        {
            var comps = context.AvatarRootObject.GetComponentsInChildren<MatCapExtender>(true);
            if (comps.Length == 0) return;

            foreach (var w in VersionCheck.Warnings) McxError.Report(ErrorSeverity.Information, w, null);

            // Same source material + same values -> one copy, even across renderers.
            var built = new Dictionary<(Material, string), Material>();

            foreach (var comp in comps)
            {
                var renderer = comp.GetComponent<Renderer>();
                if (renderer == null)
                {
                    McxError.Report(ErrorSeverity.NonFatal, "MatCap Extender needs a Renderer on the same GameObject.", comp);
                    Object.DestroyImmediate(comp);
                    continue;
                }

                var mats = renderer.sharedMaterials;
                bool changed = false;
                foreach (var target in comp.targets)
                {
                    var configs = SlotConfig.FromTarget(target);
                    if (configs.Count == 0) continue;
                    if (target.materialIndex < 0 || target.materialIndex >= mats.Length || mats[target.materialIndex] == null)
                    {
                        McxError.Report(ErrorSeverity.NonFatal, $"Material slot {target.materialIndex} does not exist on {renderer.name}.", comp);
                        continue;
                    }

                    var src = mats[target.materialIndex];
                    string valueKey = JsonUtility.ToJson(target);
                    if (!built.TryGetValue((src, valueKey), out var mat))
                    {
                        var r = ShaderGenerator.GetOrCreate(src.shader, configs);
                        if (r.Shader == null)
                        {
                            McxError.Report(ErrorSeverity.Error,
                                $"{renderer.name} / {src.name} ({src.shader.name}): {r.Error}", comp);
                            continue;
                        }
                        mat = MaterialBuilder.Build(src, r.Shader, target, configs);
                        context.AssetSaver.SaveAsset(mat);
                        built[(src, valueKey)] = mat;
                    }
                    mats[target.materialIndex] = mat;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
                Object.DestroyImmediate(comp);
            }
        }
    }

    internal sealed class McxError : SimpleError
    {
        static readonly Localizer Loc = new Localizer("en-US", () =>
            new List<(string, Func<string, string>)> { ("en-US", key => key) });

        readonly ErrorSeverity _severity;
        readonly string _message;

        McxError(ErrorSeverity severity, string message, Object context)
        {
            _severity = severity;
            _message = message;
            if (context != null) AddReference(ObjectRegistry.GetReference(context));
        }

        public override Localizer Localizer => Loc;
        public override ErrorSeverity Severity => _severity;
        public override string TitleKey => "lilToon MatCap Extender";
        public override string DetailsKey => _message;
        public override string HintKey => null;

        public static void Report(ErrorSeverity severity, string message, Object context)
        {
            ErrorReport.ReportError(new McxError(severity, message, context));
            if (severity >= ErrorSeverity.Error) Debug.LogError("[MatCap Extender] " + message, context);
            else Debug.LogWarning("[MatCap Extender] " + message, context);
        }
    }
}
