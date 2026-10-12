using UnityEditor;
namespace IBIIIS.Editor
{
    /// <summary>에디터 도구가 기본 에셋을 찾고 생성하는 프로젝트 경로. 폴더 구조를 바꾸면 이 파일만 수정한다.</summary>
    public static class AssetPaths
    {
        /// <summary>Assets 아래 폴더를 만든다. 이미 Unity에 등록된 폴더면 아무것도 하지 않는다(매번 전체 Refresh를 피함).</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            System.IO.Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(folder)) throw new System.IO.IOException($"폴더 '{folder}'를 만들지 못했습니다. Assets 아래 경로인지 확인하세요.");
        }
        public const string Root = "Assets/IBIIIS";
        // 데이터 에셋(설정·바닥·대화 인물·대본)은 ScriptableObjects/ 아래(Docs 개발 및 사용 가이드의 Assets 폴더 구조와 규칙).
        public const string Data = Root + "/ScriptableObjects";
        public const string Settings = Data + "/Settings";
        public const string Scenarios = Data + "/Scenarios";
        public const string PlayerSettings = Settings + "/GlobalPlayerSettings.asset";
        public const string CameraSettings = Settings + "/GlobalCameraSettings.asset";
        public const string InputActions = Settings + "/IBIIISInput.inputactions";
        public const string CollisionFeedback = Settings + "/CollisionFeedback.asset";
        public const string MotionFeedback = Settings + "/MotionFeedback.asset";
        public const string GameUiSettings = Settings + "/GameUiSettings.asset";
        public const string UI = Root + "/UI";
        public const string EnemyAlert = Settings + "/EnemyAlert.asset";
        public const string BattleDialogue = Settings + "/BattleDialogue.asset";
        // 그림은 Art/, 프리팹은 Prefabs/, 효과음은 Audio/SFX, 효과는 VFX/(Docs 개발 및 사용 가이드의 Assets 폴더 구조와 규칙).
        public const string Art = Root + "/Art";
        public const string Sprites = Art + "/Sprites";
        public const string Portraits = Art + "/Portraits";
        public const string Prefabs = Root + "/Prefabs";
        public const string CharacterPrefabs = Prefabs + "/Characters";
        public const string Audio = Root + "/Audio/SFX";
        public const string Effects = Root + "/VFX";
        public const string PlayerPrefabs = CharacterPrefabs + "/Player";
        public const string PlayerSprites = Sprites + "/Player";
        // 적 프리팹은 Prefabs/Characters/Enemies/<적ID>.prefab, 그림은 Art/Sprites/Enemies/<적ID>.png.
        public const string Enemies = CharacterPrefabs + "/Enemies";
        public const string EnemySprites = Sprites + "/Enemies";
        // 플레이어·NPC·적이 함께 쓰는 발밑 그림자·방향 화살표
        public const string SharedMarkerPrefabs = CharacterPrefabs + "/Shared";
        public const string SharedMarkerSprites = Sprites + "/Shared";
        public const string Tiles = Data + "/Tiles";
        public const string Environments = Prefabs + "/Environments";
        public const string Materials = Root + "/Materials";
        public const string PrototypeMaterial = Materials + "/PrototypeUnlit.mat";
        // 맵은 맵 이름 폴더에 GridMap(.asset)과 씬(.unity)을 함께 둔다.
        // 씬은 Scenes/ 아래. 전투 맵은 맵 데이터와 씬 한 쌍을 Scenes/Battle/<맵>/에 둔다(Docs 개발 및 사용 가이드의 Assets 폴더 구조와 규칙).
        public const string Scenes = Root + "/Scenes";
        public const string Maps = Scenes + "/Battle";
        public const string StarterMap = Maps + "/StarterMap/StarterMap.asset";
        public const string DefaultResources = Root + "/Resources/IBIIIS";
        // 코드와 편집기 화면 파일은 Scripts/ 아래(Docs 개발 및 사용 가이드의 Assets 폴더 구조와 규칙).
        public const string Scripts = Root + "/Scripts";
        public const string MapEditorLayout = Scripts + "/Editor/MapEditor/UI/MapEditor.uxml";
        // 미니맵(Overworld): 테스트 씬은 Scenes, 임시 재질은 Materials, 설정은 Settings, 플레이어 프리팹은 Prefabs/Characters/Player.
        public const string OverworldScene = Scenes + "/OverworldTest.unity";
        public const string OverworldSettings = Settings + "/OverworldSettings.asset";
        public const string OverworldCameraSettings = Settings + "/OverworldCameraSettings.asset";
        public const string OverworldInput = Settings + "/OverworldInput.inputactions";
        public const string OverworldPlayer = PlayerPrefabs + "/OverworldPlayer.prefab";
        // 미니맵 NPC: 공용 기본 프리팹과 임시 실루엣 그림
        public const string NpcPrefab = CharacterPrefabs + "/NPC/NPC.prefab";
        public const string NpcPlaceholderSprite = Sprites + "/NPC/NPC_Placeholder.png";
        public const string ClearedFlagPrefab = Prefabs + "/Overworld/ClearedFlag.prefab";
        public const string ClearedFlagSprite = Sprites + "/Overworld/ClearedFlag.png";
        public const string OverworldTestStage = Maps + "/ProtoTypeMap/ProtoTypeMap.unity";
    }
}
