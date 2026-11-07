using System.Collections.Generic;
using System.Text;

namespace Scocoa.MatCapExtender.Editor
{
    // Compile-time shape of one slot. Everything here becomes constants or removed code in the generated
    // shader, so only these choices produce a distinct shader; plain values (colors, strengths) are uniforms.
    internal readonly struct SlotConfig
    {
        public readonly int Number; // 3..6
        public readonly bool HasTexture;
        public readonly bool HasMask;
        public readonly bool HasCustomNormal;
        public readonly int BlendMode;
        public readonly bool BackfaceMask;
        public readonly bool ApplyTransparency;
        public readonly bool ZRotCancel;
        public readonly bool FixPerspective;
        public readonly bool Animatable;
        public readonly bool ApplyInForwardAdd;

        public SlotConfig(int number, MatCapSlot s)
        {
            Number = number;
            HasTexture = s.texture != null;
            HasMask = s.blendMask != null;
            HasCustomNormal = s.customNormal && s.normalMap != null;
            BlendMode = (int)s.blendMode;
            BackfaceMask = s.backfaceMask;
            ApplyTransparency = s.applyTransparency;
            ZRotCancel = s.zRotCancel;
            FixPerspective = s.fixPerspective;
            Animatable = s.animatable;
            ApplyInForwardAdd = s.applyInForwardAdd;
        }

        public string Prefix => "_MCX" + Number + "_";

        public string Key =>
            $"{Number}:t{B(HasTexture)}m{B(HasMask)}n{B(HasCustomNormal)}b{BlendMode}f{B(BackfaceMask)}" +
            $"a{B(ApplyTransparency)}z{B(ZRotCancel)}p{B(FixPerspective)}v{B(Animatable)}d{B(ApplyInForwardAdd)}";

        // Texture samples per pixel when the slot runs (MatCap texture, mask, normal map).
        public int SamplesPerPixel => (HasTexture ? 1 : 0) + (HasMask ? 1 : 0) + (HasCustomNormal ? 1 : 0);

        static string B(bool b) => b ? "1" : "0";

        public static List<SlotConfig> FromTarget(MatCapTarget target)
        {
            var list = new List<SlotConfig>();
            if (target?.slots == null) return list;
            for (int i = 0; i < target.slots.Length && i < MatCapTarget.SlotCount; i++)
            {
                var s = target.slots[i];
                if (s != null && s.enabled) list.Add(new SlotConfig(MatCapTarget.FirstSlotNumber + i, s));
            }
            return list;
        }

        public static string KeyOf(List<SlotConfig> configs)
        {
            var sb = new StringBuilder();
            foreach (var c in configs) sb.Append(c.Key).Append(';');
            return sb.ToString();
        }
    }
}
