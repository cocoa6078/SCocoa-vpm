using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Scocoa.MatCapExtender.Editor
{
    // NDMF preview: swaps materials on the proxy renderer only. Scene objects and material assets stay unchanged.
    internal sealed class MatCapPreviewFilter : IRenderFilter
    {
        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var comp in context.GetComponentsByType<MatCapExtender>())
            {
                if (comp == null) continue;
                if (!context.Observe(comp, c => c.enablePreview)) continue;
                var renderer = context.GetComponent<Renderer>(comp.gameObject);
                if (renderer == null) continue;
                groups.Add(RenderGroup.For(renderer).WithData(comp, (a, b) => a == b));
            }
            return groups.ToImmutable();
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var comp = group.GetData<MatCapExtender>();
            context.Observe(comp);
            var (_, proxy) = proxyPairs.First();
            var mats = proxy.sharedMaterials;

            var replacements = new Dictionary<int, Material>();
            foreach (var target in comp.targets)
            {
                var configs = SlotConfig.FromTarget(target);
                if (configs.Count == 0) continue;
                if (target.materialIndex < 0 || target.materialIndex >= mats.Length) continue;
                var src = mats[target.materialIndex];
                if (src == null || replacements.ContainsKey(target.materialIndex)) continue;
                context.Observe(src);

                var r = ShaderGenerator.GetOrCreate(src.shader, configs);
                if (r.Shader == null)
                {
                    WarnOnce(src, r.Error);
                    continue;
                }
                replacements[target.materialIndex] = MaterialBuilder.Build(src, r.Shader, target, configs);
            }

            if (replacements.Count == 0) return Task.FromResult<IRenderFilterNode>(null);
            return Task.FromResult<IRenderFilterNode>(new Node(replacements));
        }

        static readonly HashSet<string> Warned = new HashSet<string>();

        static void WarnOnce(Material m, string error)
        {
            string key = m.GetInstanceID() + error;
            if (Warned.Add(key)) Debug.LogWarning($"[MatCap Extender] Preview skipped for {m.name}: {error}", m);
        }

        sealed class Node : IRenderFilterNode
        {
            readonly Dictionary<int, Material> _replacements;

            public Node(Dictionary<int, Material> replacements) => _replacements = replacements;

            public RenderAspects WhatChanged => RenderAspects.Material;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                var mats = proxy.sharedMaterials;
                bool changed = false;
                foreach (var kv in _replacements)
                {
                    if (kv.Key >= mats.Length || mats[kv.Key] == kv.Value) continue;
                    mats[kv.Key] = kv.Value;
                    changed = true;
                }
                if (changed) proxy.sharedMaterials = mats;
            }

            public void Dispose()
            {
                foreach (var m in _replacements.Values)
                    if (m != null) Object.DestroyImmediate(m);
                _replacements.Clear();
            }
        }
    }
}
