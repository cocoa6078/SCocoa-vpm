using System.Collections.Generic;
using UnityEngine;

namespace Scocoa.MatCapExtender.Editor
{
    internal static class MaterialBuilder
    {
        // Returns a copy of `source` that uses `shader` and carries the slot values. `source` is never modified.
        public static Material Build(Material source, Shader shader, MatCapTarget target, List<SlotConfig> configs)
        {
            var m = new Material(source) { name = source.name + " (MatCap+)" };
            int queue = source.renderQueue;
            m.shader = shader;
            m.renderQueue = queue;
            Apply(m, target, configs);
            return m;
        }

        public static void Apply(Material m, MatCapTarget target, List<SlotConfig> configs)
        {
            foreach (var c in configs)
            {
                var s = target.slots[c.Number - MatCapTarget.FirstSlotNumber];
                string p = c.Prefix;
                m.SetColor(p + "Color", s.color);
                var st = new Vector4(s.textureScale.x, s.textureScale.y, s.textureOffset.x, s.textureOffset.y);
                if (c.HasTexture)
                {
                    m.SetTexture(p + "Tex", s.texture);
                    m.SetTextureScale(p + "Tex", s.textureScale);
                    m.SetTextureOffset(p + "Tex", s.textureOffset);
                }
                else
                {
                    m.SetVector(p + "Tex_ST", st);
                }
                m.SetVector(p + "BlendUV1", new Vector4(s.blendUV1.x, s.blendUV1.y, 0, 0));
                m.SetFloat(p + "MainStrength", s.mainColorPower);
                m.SetFloat(p + "VRParallax", s.vrParallaxStrength);
                m.SetFloat(p + "Blend", s.blend);
                m.SetFloat(p + "EnableLighting", s.enableLighting);
                m.SetFloat(p + "ShadowMask", s.shadowMask);
                m.SetFloat(p + "Lod", s.blur);
                m.SetFloat(p + "NormalStrength", s.normalStrength);
                if (c.HasMask) m.SetTexture(p + "Mask", s.blendMask);
                if (c.HasCustomNormal)
                {
                    m.SetTexture(p + "BumpMap", s.normalMap);
                    m.SetFloat(p + "BumpScale", s.normalScale);
                }
            }
        }
    }
}
