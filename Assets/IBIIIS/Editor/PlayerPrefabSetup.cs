using System.IO;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>Characters/Player/Sprites의 캐릭터 스프라이트로 기본 플레이어 외형 프리팹을 만든다. 이미 있는 프리팹은 덮어쓰지 않는다.</summary>
    public static class PlayerPrefabSetup
    {
        public const string Folder = AssetPaths.Player;
        public const string SpriteFolder = Folder + "/Sprites";
        public const string PrefabPath = Folder + "/PlayerVisual.prefab";
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
            var settings = AssetDatabase.LoadAssetAtPath<PlayerSettings>(SettingsPath);
            if (settings == null) return;
            var so = new SerializedObject(settings); var property = so.FindProperty("visualPrefab");
            if (property.objectReferenceValue != null) return;
            property.objectReferenceValue = prefab; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
        }
        private static void ImportSprites()
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
