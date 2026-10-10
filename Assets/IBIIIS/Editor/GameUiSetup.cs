using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>게임 화면 UI 기본 에셋을 만든다: Panel Settings(1920×1080 기준), 임시 회피기 아이콘, Game UI 설정 에셋.
    /// 화면 구성(UXML)·스타일(USS)·테마·글꼴은 Assets/IBIIIS/UI의 파일을 그대로 쓴다. 이미 있는 에셋·연결은 덮어쓰지 않고 빈 슬롯만 채운다.</summary>
    public static class GameUiSetup
    {
        public const string Folder = AssetPaths.UI;
        public const string PanelSettingsPath = Folder + "/GamePanelSettings.asset", ThemePath = Folder + "/GameRuntimeTheme.tss";
        public const string BattleHudPath = Folder + "/BattleHud.uxml", ResultPopupPath = Folder + "/ResultPopup.uxml", OverworldHudPath = Folder + "/OverworldHud.uxml";
        public const string EvasionIconPath = Folder + "/EvasionIcon.png";

        [MenuItem("IBIIIS/Create Default Game UI")]
        public static GameUiSettings EnsureDefault()
        {
            AssetPaths.EnsureFolder(Folder);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f;
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            var icon = EnsureEvasionIcon();
            var settings = AssetDatabase.LoadAssetAtPath<GameUiSettings>(AssetPaths.GameUiSettings);
            if (settings == null)
            {
                AssetPaths.EnsureFolder(AssetPaths.Settings);
                settings = ScriptableObject.CreateInstance<GameUiSettings>();
                AssetDatabase.CreateAsset(settings, AssetPaths.GameUiSettings);
            }
            var so = new SerializedObject(settings); bool filled = false;
            void Fill(string name, Object value) { var slot = so.FindProperty(name); if (slot.objectReferenceValue == null && value != null) { slot.objectReferenceValue = value; filled = true; } }
            Fill("panelSettings", panel);
            Fill("battleHud", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BattleHudPath));
            Fill("resultPopup", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ResultPopupPath));
            Fill("overworldHud", AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OverworldHudPath));
            Fill("evasionIcon", icon);
            if (filled) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
            AssetDatabase.SaveAssetIfDirty(settings);
            // 전투·미니맵 설정이 비어 있으면 같은 에셋을 연결한다.
            Connect(AssetDatabase.LoadAssetAtPath<IBIIIS.PlayerSettings>(AssetPaths.PlayerSettings), settings);
            Connect(AssetDatabase.LoadAssetAtPath<OverworldSettings>(AssetPaths.OverworldSettings), settings);
            Selection.activeObject = settings;
            return settings;
        }
        private static void Connect(Object owner, GameUiSettings settings)
        {
            if (owner == null) return;
            var so = new SerializedObject(owner); var slot = so.FindProperty("gameUi");
            if (slot == null || slot.objectReferenceValue != null) return;
            slot.objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner); AssetDatabase.SaveAssetIfDirty(owner);
        }
        // 임시 회피기 아이콘: 오른쪽을 향한 이중 화살표(»). 256×256, 흰색에 가까운 하늘색.
        private static Sprite EnsureEvasionIcon()
        {
            var fill = new Color(.85f, .95f, 1f);
            return GeneratedSprites.EnsureGeneratedSprite(EvasionIconPath, (u, v) =>
            {
                float first = GeneratedSprites.Triangle(new Vector2(u, v), new Vector2(-.36f, .2f), new Vector2(-.36f, .8f), new Vector2(.02f, .5f));
                float second = GeneratedSprites.Triangle(new Vector2(u, v), new Vector2(-.04f, .2f), new Vector2(-.04f, .8f), new Vector2(.34f, .5f));
                return GeneratedSprites.Shade(Mathf.Min(first, second), fill);
            });
        }
    }
}
