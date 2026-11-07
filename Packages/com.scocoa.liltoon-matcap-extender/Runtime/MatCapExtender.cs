using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scocoa.MatCapExtender
{
    // Same values and order as lilToon's _MatCapBlendMode.
    public enum MatCapBlendMode
    {
        Normal = 0,
        Add = 1,
        Screen = 2,
        Multiply = 3,
    }

    [Serializable]
    public class MatCapSlot
    {
        public bool enabled;

        [ColorUsage(true, true)] public Color color = Color.white;
        public Texture2D texture;
        public Vector2 textureScale = Vector2.one;
        public Vector2 textureOffset = Vector2.zero;
        [Range(0, 1)] public float mainColorPower;
        public Vector2 blendUV1 = Vector2.zero;
        public bool zRotCancel = true;
        public bool fixPerspective = true;
        [Range(0, 1)] public float vrParallaxStrength = 1f;

        [Range(0, 1)] public float blend = 1f;
        public Texture2D blendMask;
        [Range(0, 1)] public float enableLighting = 1f;
        [Range(0, 1)] public float shadowMask;
        public bool backfaceMask;
        [Range(0, 10)] public float blur;
        public MatCapBlendMode blendMode = MatCapBlendMode.Add;
        public bool applyTransparency = true;

        [Range(0, 1)] public float normalStrength = 1f;
        public bool customNormal;
        public Texture2D normalMap;
        [Range(-10, 10)] public float normalScale = 1f;

        // Keeps a runtime branch on Blend so animations can fade the slot out and skip its samples.
        public bool animatable;
        // Also applies the slot in the ForwardAdd (per-pixel point/spot light) pass, like lilToon's own MatCap.
        public bool applyInForwardAdd;
    }

    [Serializable]
    public class MatCapTarget
    {
        public const int SlotCount = 4; // 3rd..6th
        public const int FirstSlotNumber = 3;

        // Material slot index on the Renderer. Indices survive material replacement by other tools (e.g. PCSS4VRC).
        public int materialIndex;
        // Reference shown in the inspector to detect reordered material slots.
        public Material materialRef;
        public MatCapSlot[] slots = CreateSlots();

        public static MatCapSlot[] CreateSlots()
        {
            var s = new MatCapSlot[SlotCount];
            for (int i = 0; i < SlotCount; i++) s[i] = new MatCapSlot();
            return s;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Scocoa/lilToon MatCap Extender")]
    // IEditorOnly: the VRChat SDK build check and client skip this component (NDMF removes it at build anyway).
    public class MatCapExtender : MonoBehaviour
#if SCOCOA_MCX_VRCSDK
        , VRC.SDKBase.IEditorOnly
#endif
    {
        public bool enablePreview = true;
        public List<MatCapTarget> targets = new List<MatCapTarget>();
    }
}
