namespace IBIIIS.Editor
{
    /// <summary>에디터 도구가 기본 에셋을 찾고 생성하는 프로젝트 경로. 폴더 구조를 바꾸면 이 파일만 수정한다.</summary>
    public static class AssetPaths
    {
        public const string Root = "Assets/IBIIIS";
        public const string Settings = Root + "/Settings";
        public const string PlayerSettings = Settings + "/GlobalPlayerSettings.asset";
        public const string CameraSettings = Settings + "/GlobalCameraSettings.asset";
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
    }
}
