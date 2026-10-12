using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>Art/Sprites/Player의 캐릭터 스프라이트로 기본 플레이어 외형 프리팹을 만든다. 이미 있는 프리팹은 덮어쓰지 않는다.</summary>
    public static class PlayerPrefabSetup
    {
        public const string SpriteFolder = AssetPaths.PlayerSprites;
        public const string PrefabPath = AssetPaths.PlayerPrefabs + "/PlayerVisual.prefab";
        public const string SettingsPath = AssetPaths.PlayerSettings;
        // 500x500 캔버스 = 월드 1 단위(한 칸 너비), 기준점은 캔버스 아래 중앙(발밑).
        public const float PixelsPerUnit = 500;
        // 임시 표시 배율. 프리팹의 Sprite 자식 Scale로 조정하며 발밑 기준점은 유지된다.
        private const float DisplayScale = 1.5f;
        // 임시: 제작되지 않은 정면 대기·앞뒤 대시는 c_basic_right로 대체한다. 제작되면 이 이름만 교체한다.
        private const string TemporaryFallback = "c_basic_right";
        [MenuItem("IBIIIS/Create Default Player Visual")]
        public static void EnsureDefault()
        {
            ImportSprites();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { prefab = CreatePrefab(); GroundMarkerSetup.AttachTo(PrefabPath, false); }
            FillReactionSlots(PrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<PlayerSettings>(SettingsPath);
            if (settings == null) return;
            var so = new SerializedObject(settings); var property = so.FindProperty("visualPrefab");
            if (property.objectReferenceValue != null) return;
            property.objectReferenceValue = prefab; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
        }
        /// <summary>플레이어 스프라이트 폴더의 그림을 스프라이트(PPU 500, 기준점 아래 중앙)로 맞춘다. 이미 맞으면 건드리지 않는다.</summary>
        public static void ImportSprites()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool changed = importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != PixelsPerUnit || importer.mipmapEnabled;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                if (settings.spriteAlignment != (int)SpriteAlignment.BottomCenter) changed = true;
                if (!changed) continue;
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.ReadTextureSettings(settings); settings.spriteAlignment = (int)SpriteAlignment.BottomCenter; settings.spritePivot = new Vector2(.5f, 0);
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }
        /// <summary>프리팹 PlayerVisual의 상황별 그림 슬롯 중 비어 있는 것만 기본 그림으로 채운다(사용자가 바꾼 슬롯은 그대로). 그림 파일이 없으면 비워 둔다.</summary>
        public static void FillReactionSlots(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var visual = root.GetComponentInChildren<PlayerVisual>(true); if (visual == null) return;
                var so = new SerializedObject(visual); bool changed = false;
                changed |= FillSlot(so.FindProperty("danger"), "c_danger");
                changed |= FillSlot(so.FindProperty("escaped"), "c_danger_gone");
                changed |= FillSlot(so.FindProperty("defeated"), "c_defeated");
                var evaded = so.FindProperty("evaded");
                if (evaded.arraySize == 0)
                {
                    var first = TryLoad("c_danger_cleared"); var second = TryLoad("c_danger_cleared_2");
                    if (first != null && second != null)
                    {
                        evaded.arraySize = 2; evaded.GetArrayElementAtIndex(0).objectReferenceValue = first; evaded.GetArrayElementAtIndex(1).objectReferenceValue = second; changed = true;
                    }
                }
                if (!changed) return;
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static bool FillSlot(SerializedProperty property, string name)
        {
            if (property.objectReferenceValue != null) return false;
            var sprite = TryLoad(name); if (sprite == null) return false;
            property.objectReferenceValue = sprite; return true;
        }
        private static Sprite TryLoad(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFolder}/{name}.png");
        private static Sprite Load(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFolder}/{name}.png");
            if (sprite == null) throw new FileNotFoundException($"플레이어 스프라이트가 없습니다: {SpriteFolder}/{name}.png");
            return sprite;
        }
        private static GameObject CreatePrefab()
        {
            var root = new GameObject("PlayerVisual");
            try
            {
                var visual = new GameObject("Sprite"); visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = new Vector3(DisplayScale, DisplayScale, 1);
                var renderer = visual.AddComponent<SpriteRenderer>();
                var component = root.AddComponent<PlayerVisual>(); var so = new SerializedObject(component);
                so.FindProperty("spriteRenderer").objectReferenceValue = renderer;
                SetFacing(so.FindProperty("idle"), "c_basic_back", "c_basic_right", TemporaryFallback, "c_basic_left");
                SetFacing(so.FindProperty("move"), "c_move_back", "c_move_right", "c_move_front", "c_move_left");
                SetFacing(so.FindProperty("dash"), TemporaryFallback, "c_dash_right", TemporaryFallback, "c_dash_left");
                SetRoll(so.FindProperty("rollBackLeft"), "c_roll_bl"); SetRoll(so.FindProperty("rollBackRight"), "c_roll_br");
                SetRoll(so.FindProperty("rollFrontLeft"), "c_roll_fl"); SetRoll(so.FindProperty("rollFrontRight"), "c_roll_fr");
                so.ApplyModifiedPropertiesWithoutUndo();
                renderer.sprite = Load("c_basic_right");
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void SetFacing(SerializedProperty property, string back, string right, string front, string left)
        {
            property.FindPropertyRelative("back").objectReferenceValue = Load(back); property.FindPropertyRelative("right").objectReferenceValue = Load(right);
            property.FindPropertyRelative("front").objectReferenceValue = Load(front); property.FindPropertyRelative("left").objectReferenceValue = Load(left);
        }
        private static void SetRoll(SerializedProperty property, string prefix)
        {
            property.FindPropertyRelative("first").objectReferenceValue = Load(prefix + "_1"); property.FindPropertyRelative("second").objectReferenceValue = Load(prefix + "_2");
        }
    }
}
