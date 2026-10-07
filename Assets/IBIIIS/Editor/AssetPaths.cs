namespace IBIIIS.Editor
{
    /// <summary>에디터 도구가 기본 에셋을 찾고 생성하는 프로젝트 경로. 폴더 구조를 바꾸면 이 파일만 수정한다.</summary>
    public static class AssetPaths
    {
        public const string Root = "Assets/IBIIIS";
        public const string Settings = Root + "/Settings";
        public const string PlayerSettings = Settings + "/GlobalPlayerSettings.asset";
        public const string CameraSettings = Settings + "/GlobalCameraSettings.asset";
        public const string InputActions = Settings + "/IBIIISInput.inputactions";
        public const string CollisionFeedback = Settings + "/CollisionFeedback.asset";
        public const string MotionFeedback = Settings + "/MotionFeedback.asset";
        public const string EnemyAlert = Settings + "/EnemyAlert.asset";
        public const string Audio = Root + "/Audio";
        public const string Effects = Root + "/Effects";
        public const string Player = Root + "/Characters/Player";
        // 적은 개체별 폴더(Enemies/EnemyNNN/)에 프리팹·재질·스프라이트를 함께 둔다.
        public const string Enemies = Root + "/Characters/Enemies";
        public const string Tiles = Root + "/Tiles";
        public const string Environments = Root + "/Environments";
        public const string Materials = Root + "/Materials";
        public const string PrototypeMaterial = Materials + "/PrototypeUnlit.mat";
        // 맵은 맵 이름 폴더에 GridMap(.asset)과 씬(.unity)을 함께 둔다.
        public const string Maps = Root + "/Maps";
        public const string StarterMap = Maps + "/StarterMap/StarterMap.asset";
        public const string DefaultResources = Root + "/Resources/IBIIIS";
        public const string MapEditorLayout = Root + "/Editor/UI/MapEditor.uxml";
        // 미니맵(Overworld): 테스트 씬과 임시 재질은 Overworld 폴더, 설정은 Settings, 플레이어 프리팹은 Characters/Player.
        public const string Overworld = Root + "/Overworld";
        public const string OverworldScene = Overworld + "/OverworldTest.unity";
        public const string OverworldSettings = Settings + "/OverworldSettings.asset";
        public const string OverworldCameraSettings = Settings + "/OverworldCameraSettings.asset";
        public const string OverworldInput = Settings + "/OverworldInput.inputactions";
        public const string OverworldPlayer = Player + "/OverworldPlayer.prefab";
        // 미니맵 NPC: 공용 기본 프리팹과 임시 실루엣 그림
        public const string Npc = Root + "/Characters/NPC";
        public const string NpcPrefab = Npc + "/NPC.prefab";
        public const string NpcPlaceholderSprite = Npc + "/NPC_Placeholder.png";
        public const string ClearedFlagPrefab = Overworld + "/ClearedFlag.prefab";
        public const string ClearedFlagSprite = Overworld + "/ClearedFlag.png";
        public const string OverworldTestStage = Maps + "/ProtoTypeMap/ProtoTypeMap.unity";
    }
}
