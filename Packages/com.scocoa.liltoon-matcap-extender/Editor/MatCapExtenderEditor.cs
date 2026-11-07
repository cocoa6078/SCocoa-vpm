using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Scocoa.MatCapExtender.Editor
{
    [CustomEditor(typeof(MatCapExtender))]
    internal sealed class MatCapExtenderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var comp = (MatCapExtender)target;
            var renderer = comp.GetComponent<Renderer>();
            serializedObject.Update();

            DrawVersionInfo();

            if (renderer == null)
            {
                EditorGUILayout.HelpBox("Add this component to a GameObject with a MeshRenderer or SkinnedMeshRenderer.", MessageType.Error);
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MatCapExtender.enablePreview)),
                new GUIContent("NDMF Preview"));
            EditorGUILayout.Space();

            var mats = renderer.sharedMaterials;
            var targets = serializedObject.FindProperty(nameof(MatCapExtender.targets));
            int removeAt = -1;

            for (int t = 0; t < targets.arraySize; t++)
            {
                var tp = targets.GetArrayElementAtIndex(t);
                var indexProp = tp.FindPropertyRelative(nameof(MatCapTarget.materialIndex));
                var refProp = tp.FindPropertyRelative(nameof(MatCapTarget.materialRef));

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var options = mats.Select((m, i) => $"{i}: {(m != null ? m.name : "(none)")}").ToArray();
                        int idx = Mathf.Clamp(indexProp.intValue, 0, Mathf.Max(0, mats.Length - 1));
                        int newIdx = EditorGUILayout.Popup("Material", idx, options);
                        if (newIdx != indexProp.intValue || (mats.Length > 0 && refProp.objectReferenceValue == null))
                        {
                            indexProp.intValue = newIdx;
                            refProp.objectReferenceValue = mats.Length > newIdx ? mats[newIdx] : null;
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(64))) removeAt = t;
                    }

                    var mat = indexProp.intValue >= 0 && indexProp.intValue < mats.Length ? mats[indexProp.intValue] : null;
                    if (mat == null)
                    {
                        EditorGUILayout.HelpBox("This material slot is empty or out of range.", MessageType.Error);
                    }
                    else
                    {
                        if (refProp.objectReferenceValue != null && refProp.objectReferenceValue != mat)
                        {
                            EditorGUILayout.HelpBox(
                                $"Slot {indexProp.intValue} now holds '{mat.name}' (was '{refProp.objectReferenceValue.name}'). " +
                                "The slots are applied by index; reselect the material if the order changed.", MessageType.Warning);
                            if (GUILayout.Button("Accept current material")) refProp.objectReferenceValue = mat;
                        }
                        string support = ShaderGenerator.CheckSupport(mat.shader);
                        if (support != null)
                            EditorGUILayout.HelpBox($"{mat.shader.name}: {support}", MessageType.Error);
                    }

                    var slots = tp.FindPropertyRelative(nameof(MatCapTarget.slots));
                    if (slots.arraySize != MatCapTarget.SlotCount) slots.arraySize = MatCapTarget.SlotCount;
                    for (int s = 0; s < MatCapTarget.SlotCount; s++)
                        DrawSlot(slots.GetArrayElementAtIndex(s), MatCapTarget.FirstSlotNumber + s, t * 16 + s);

                    DrawCost(comp, t);
                }
            }

            if (removeAt >= 0) targets.DeleteArrayElementAtIndex(removeAt);

            if (GUILayout.Button("Add Material"))
            {
                int next = Enumerable.Range(0, mats.Length)
                    .FirstOrDefault(i => !comp.targets.Any(x => x.materialIndex == i));
                targets.arraySize++;
                var tp = targets.GetArrayElementAtIndex(targets.arraySize - 1);
                tp.FindPropertyRelative(nameof(MatCapTarget.materialIndex)).intValue = next;
                tp.FindPropertyRelative(nameof(MatCapTarget.materialRef)).objectReferenceValue = mats.Length > next ? mats[next] : null;
                var slots = tp.FindPropertyRelative(nameof(MatCapTarget.slots));
                slots.arraySize = MatCapTarget.SlotCount;
                for (int s = 0; s < MatCapTarget.SlotCount; s++)
                    ResetSlot(slots.GetArrayElementAtIndex(s));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Generated Shaders")) ClearCache();
                if (GUILayout.Button("Recheck Versions")) VersionCheck.Reset();
            }
        }

        static System.Collections.Generic.Dictionary<int, bool> _foldouts = new System.Collections.Generic.Dictionary<int, bool>();

        void DrawSlot(SerializedProperty sp, int number, int foldKey)
        {
            var enabled = sp.FindPropertyRelative(nameof(MatCapSlot.enabled));
            _foldouts.TryGetValue(foldKey, out bool open);
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled.boolValue = EditorGUILayout.ToggleLeft("MatCap " + ShaderGenerator.Ordinal(number), enabled.boolValue,
                    EditorStyles.boldLabel, GUILayout.Width(120));
                if (enabled.boolValue) open = EditorGUILayout.Foldout(open, open ? "" : "settings", true);
            }
            _foldouts[foldKey] = open;
            if (!enabled.boolValue || !open) return;

            EditorGUI.indentLevel++;
            P(sp, nameof(MatCapSlot.color), "Color");
            P(sp, nameof(MatCapSlot.texture), "Texture");
            if (sp.FindPropertyRelative(nameof(MatCapSlot.texture)).objectReferenceValue == null)
                EditorGUILayout.HelpBox("No texture: the slot uses Color only (no texture sample).", MessageType.None);
            P(sp, nameof(MatCapSlot.textureScale), "Tiling");
            P(sp, nameof(MatCapSlot.textureOffset), "Offset");
            P(sp, nameof(MatCapSlot.mainColorPower), "Main Color Power");
            P(sp, nameof(MatCapSlot.blendUV1), "Blend UV1");
            P(sp, nameof(MatCapSlot.zRotCancel), "Z-axis Rotation Cancel");
            P(sp, nameof(MatCapSlot.fixPerspective), "Fix Perspective");
            P(sp, nameof(MatCapSlot.vrParallaxStrength), "VR Parallax Strength");
            EditorGUILayout.Space(2);
            P(sp, nameof(MatCapSlot.blend), "Blend");
            P(sp, nameof(MatCapSlot.blendMask), "Mask");
            P(sp, nameof(MatCapSlot.enableLighting), "Enable Lighting");
            P(sp, nameof(MatCapSlot.shadowMask), "Shadow Mask");
            P(sp, nameof(MatCapSlot.backfaceMask), "Backface Mask");
            P(sp, nameof(MatCapSlot.blur), "Blur");
            P(sp, nameof(MatCapSlot.blendMode), "Blend Mode");
            P(sp, nameof(MatCapSlot.applyTransparency), "Apply Transparency");
            EditorGUILayout.Space(2);
            P(sp, nameof(MatCapSlot.normalStrength), "Normal Map Strength");
            P(sp, nameof(MatCapSlot.customNormal), "Custom Normal Map");
            if (sp.FindPropertyRelative(nameof(MatCapSlot.customNormal)).boolValue)
            {
                P(sp, nameof(MatCapSlot.normalMap), "Normal Map");
                P(sp, nameof(MatCapSlot.normalScale), "Normal Scale");
            }
            EditorGUILayout.Space(2);
            P(sp, nameof(MatCapSlot.animatable), "Animatable (keeps Blend branch)");
            P(sp, nameof(MatCapSlot.applyInForwardAdd), "Apply in ForwardAdd (point lights)");
            EditorGUI.indentLevel--;
        }

        static void P(SerializedProperty sp, string name, string label) =>
            EditorGUILayout.PropertyField(sp.FindPropertyRelative(name), new GUIContent(label));

        static void ResetSlot(SerializedProperty sp)
        {
            var d = new MatCapSlot();
            sp.FindPropertyRelative(nameof(MatCapSlot.enabled)).boolValue = false;
            sp.FindPropertyRelative(nameof(MatCapSlot.color)).colorValue = d.color;
            sp.FindPropertyRelative(nameof(MatCapSlot.texture)).objectReferenceValue = null;
            sp.FindPropertyRelative(nameof(MatCapSlot.textureScale)).vector2Value = d.textureScale;
            sp.FindPropertyRelative(nameof(MatCapSlot.textureOffset)).vector2Value = d.textureOffset;
            sp.FindPropertyRelative(nameof(MatCapSlot.mainColorPower)).floatValue = d.mainColorPower;
            sp.FindPropertyRelative(nameof(MatCapSlot.blendUV1)).vector2Value = d.blendUV1;
            sp.FindPropertyRelative(nameof(MatCapSlot.zRotCancel)).boolValue = d.zRotCancel;
            sp.FindPropertyRelative(nameof(MatCapSlot.fixPerspective)).boolValue = d.fixPerspective;
            sp.FindPropertyRelative(nameof(MatCapSlot.vrParallaxStrength)).floatValue = d.vrParallaxStrength;
            sp.FindPropertyRelative(nameof(MatCapSlot.blend)).floatValue = d.blend;
            sp.FindPropertyRelative(nameof(MatCapSlot.blendMask)).objectReferenceValue = null;
            sp.FindPropertyRelative(nameof(MatCapSlot.enableLighting)).floatValue = d.enableLighting;
            sp.FindPropertyRelative(nameof(MatCapSlot.shadowMask)).floatValue = d.shadowMask;
            sp.FindPropertyRelative(nameof(MatCapSlot.backfaceMask)).boolValue = d.backfaceMask;
            sp.FindPropertyRelative(nameof(MatCapSlot.blur)).floatValue = d.blur;
            sp.FindPropertyRelative(nameof(MatCapSlot.blendMode)).enumValueIndex = (int)d.blendMode;
            sp.FindPropertyRelative(nameof(MatCapSlot.applyTransparency)).boolValue = d.applyTransparency;
            sp.FindPropertyRelative(nameof(MatCapSlot.normalStrength)).floatValue = d.normalStrength;
            sp.FindPropertyRelative(nameof(MatCapSlot.customNormal)).boolValue = d.customNormal;
            sp.FindPropertyRelative(nameof(MatCapSlot.normalMap)).objectReferenceValue = null;
            sp.FindPropertyRelative(nameof(MatCapSlot.normalScale)).floatValue = d.normalScale;
            sp.FindPropertyRelative(nameof(MatCapSlot.animatable)).boolValue = d.animatable;
            sp.FindPropertyRelative(nameof(MatCapSlot.applyInForwardAdd)).boolValue = d.applyInForwardAdd;
        }

        static void DrawCost(MatCapExtender comp, int t)
        {
            if (t >= comp.targets.Count) return;
            var configs = SlotConfig.FromTarget(comp.targets[t]);
            if (configs.Count == 0)
            {
                EditorGUILayout.LabelField("No slot enabled: material is left unchanged (no cost).", EditorStyles.miniLabel);
                return;
            }
            int samples = configs.Sum(c => c.SamplesPerPixel);
            int fa = configs.Count(c => c.ApplyInForwardAdd);
            EditorGUILayout.LabelField(
                $"Cost: {configs.Count} slot(s), {samples} texture sample(s)/pixel in ForwardBase" +
                (fa > 0 ? $", {fa} slot(s) also per point light" : ", none in ForwardAdd") +
                ". No extra pass, draw call or material slot.", EditorStyles.wordWrappedMiniLabel);
        }

        static void DrawVersionInfo()
        {
            var warnings = VersionCheck.Warnings;
            if (warnings.Count == 0) return;
            EditorGUILayout.HelpBox("MatCap Extender " + VersionCheck.OwnVersion + "\n" + string.Join("\n", warnings), MessageType.Warning);
            if (!string.IsNullOrEmpty(VersionCheck.Url) && GUILayout.Button("Open release page"))
                Application.OpenURL(VersionCheck.Url);
        }

        [MenuItem("Tools/Scocoa/MatCap Extender/Clear Generated Shaders")]
        static void ClearCache()
        {
            ShaderGenerator.ClearMemo();
            if (AssetDatabase.IsValidFolder(ShaderGenerator.GeneratedRoot))
                AssetDatabase.DeleteAsset(ShaderGenerator.GeneratedRoot);
            else if (Directory.Exists(ShaderGenerator.GeneratedRoot))
                Directory.Delete(ShaderGenerator.GeneratedRoot, true);
        }
    }
}
