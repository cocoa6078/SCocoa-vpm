# lilToon MatCap Extender

Adds MatCap 3rd-6th slots to lilToon materials at NDMF build time. Works with lilToon itself and with
lilToon-based custom shaders built from `.lilcontainer` (PCSS4lilToon, lilSSRT, liltoon-vat, ...).

## Usage

1. Add `Scocoa/lilToon MatCap Extender` to the GameObject that has the MeshRenderer / SkinnedMeshRenderer.
2. **Add Material**, pick the material slot, enable MatCap 3rd-6th and set them up like lilToon's MatCap.
3. NDMF preview shows the result in the editor. The material asset is never modified; a copy with the extended
   shader is made on build / play.

## How it works

- The material's shader source is read (`.shader`, or `.lilcontainer` through `lilShaderContainer.UnpackContainer`),
  copied with its `Hidden/.../ltspass_*` pass shaders under new names, and the slot code is inserted at the
  `BEFORE_RIMLIGHT` hook (right after MatCap 2nd). If a custom shader already uses that hook, `BEFORE_GLITTER` is used.
- Runs in NDMF `FirstChance`, after PCSS4VRC (so PCSS-converted materials get the slots) and before
  Modular Avatar / AAO.
- Generated shaders are cached in `Assets/ScocoaGenerated/MatCapExtender/<hash>` (hash of source + slot setup).
  `Tools > Scocoa > MatCap Extender > Clear Generated Shaders` deletes them; they are rebuilt on demand.

## Cost

- Materials without enabled slots keep their original shader (no change).
- Only enabled slots are compiled. No texture / mask / custom normal = no sample for it.
  Blend mode, backface mask, transparency, Z-rotation cancel and perspective are compile-time constants.
- Slots run in ForwardBase only unless "Apply in ForwardAdd" is on.
- No extra pass, draw call, material slot or sampler (shares lilToon's samplers).
- Each distinct slot setup is one more shader in the avatar bundle.
- "Animatable" keeps a runtime branch on Blend so `material._MCX<n>_Blend` can be animated; without it, animate
  nothing but values (Color, Blend, ...), which are plain uniforms anyway.

## Not supported

- lilToon Lite, Fur-only, FakeShadow, non-lilToon shaders (error in the inspector / build log).
- Materials swapped in by animations or other tools after this pass (add those materials to the component too only
  if they sit in the same slot at build time).
- Android/Quest (custom shaders are not allowed there).

## Versions

Tested versions are listed in `package.json` → `testedVersions`. A warning is shown in the inspector and the NDMF
console when an installed lilToon / NDMF version is not in the list. Check the result, then add the version.

Tested: lilToon 2.4.1, NDMF 1.14.8, Unity 2022.3.22f1, PCSS4lilToon (PCSS4VRC), lilSSRT.
