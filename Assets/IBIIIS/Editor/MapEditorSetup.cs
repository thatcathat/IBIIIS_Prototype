using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    public static class MapEditorSetup
    {
        public const string Root = "Assets/IBIIIS/Content";
        public static IBIIIS.PlayerSettings EnsurePlayerSettings(GridMapPlayer legacySource = null)
        {
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var path = Root + "/GlobalPlayerSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<IBIIIS.PlayerSettings>(path);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<IBIIIS.PlayerSettings>();
            if (legacySource != null)
            {
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("visualPrefab").objectReferenceValue = legacySource.PlayerVisualPrefab;
                serialized.FindProperty("moveTurnCost").intValue = legacySource.MoveTurnCost;
                serialized.FindProperty("blockedTurnCost").intValue = legacySource.BlockedTurnCost;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }
        [MenuItem("IBIIIS/Player Settings")]
        public static void OpenPlayerSettings()
        {
            Selection.activeObject = EnsurePlayerSettings(); EditorGUIUtility.PingObject(Selection.activeObject);
        }
        public static MapCameraSettings EnsureCameraSettings()
        {
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var path = Root + "/GlobalCameraSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<MapCameraSettings>(path);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<MapCameraSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }
        public static TileDefinition[] EnsureDefaultTiles()
        {
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            return new[] { Tile("Floor", "기본 바닥", true, new Color(.28f, .55f, .46f)), Tile("Wall", "장애물", false, new Color(.48f, .51f, .6f)) };
        }
        private static TileDefinition Tile(string name, string label, bool walkable, Color color)
        {
            var path = Root + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TileDefinition>(path);
            if (existing != null) return existing;
            var tile = ScriptableObject.CreateInstance<TileDefinition>(); tile.Initialize(Guid.NewGuid().ToString("N"), label, walkable, color);
            AssetDatabase.CreateAsset(tile, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(tile); return tile;
        }
        [MenuItem("IBIIIS/Create Starter Map")]
        public static void CreateStarter()
        {
            var tiles = EnsureDefaultTiles();
            var map = AssetDatabase.LoadAssetAtPath<GridMap>(Root + "/StarterMap.asset");
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<GridMap>(); foreach (var tile in tiles) map.AddTile(tile);
                map.Resize(12, 10);
                for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
                    map.SetTile(new Vector2Int(x, y), x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1 ? tiles[1] : tiles[0]);
                map.SetTile(new Vector2Int(5, 4), tiles[1]); map.SetStart(new Vector2Int(2, 2));
                AssetDatabase.CreateAsset(map, AssetDatabase.GenerateUniqueAssetPath(Root + "/StarterMap.asset")); AssetDatabase.SaveAssetIfDirty(map);
            }
            MapEditorWindow.OpenMap(map);
        }
        public static GridMap CreateMapWithScene(string requestedPath, out string scenePath)
        {
            EnsureCanCreateScene();
            if (string.IsNullOrEmpty(requestedPath) || !requestedPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                Path.GetExtension(requestedPath) != ".asset" || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(requestedPath).Replace('\\', '/')))
                throw new ArgumentException("Assets 폴더 안의 .asset 저장 경로를 지정하세요.");
            string stem = requestedPath.Substring(0, requestedPath.Length - ".asset".Length);
            string candidate = stem;
            int suffix = 1;
            while (File.Exists(candidate + ".asset") || File.Exists(candidate + ".unity")) candidate = stem + " " + suffix++;
            string mapPath = candidate + ".asset";
            scenePath = candidate + ".unity";
            var map = ScriptableObject.CreateInstance<GridMap>();
            try
            {
                foreach (var tile in EnsureDefaultTiles()) map.AddTile(tile);
                AssetDatabase.CreateAsset(map, mapPath); AssetDatabase.SaveAssetIfDirty(map);
                var createdScene = CreateTestScene(map, scenePath, true);
                if (string.IsNullOrEmpty(createdScene)) throw new IOException("새 맵의 씬을 생성하지 못했습니다.");
                scenePath = createdScene;
                return map;
            }
            catch
            {
                if (File.Exists(scenePath)) AssetDatabase.DeleteAsset(scenePath);
                if (AssetDatabase.GetAssetPath(map) == mapPath) AssetDatabase.DeleteAsset(mapPath);
                else if (map != null) UnityEngine.Object.DestroyImmediate(map);
                throw;
            }
        }
        private static void EnsureCanCreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play 모드를 종료한 뒤 테스트 씬을 만드세요.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("이름 없는 씬이 열려 있습니다. 현재 씬을 먼저 저장한 뒤 테스트 씬을 생성하세요. 기존 작업은 변경하지 않았습니다.");
        }
        public static string CreateTestScene(GridMap map, string requestedPath = null, bool allowIncompleteMap = false)
        {
            EnsureCanCreateScene();
            var errors = map.ValidateMap(!allowIncompleteMap);
            if (errors.Count > 0) { EditorUtility.DisplayDialog("맵 확인 필요", string.Join("\n", errors), "확인"); return null; }
            var path = requestedPath ?? EditorUtility.SaveFilePanelInProject("테스트 씬 저장", map.name + "_Test", "unity", "새 테스트 씬 저장 위치");
            if (string.IsNullOrEmpty(path)) return null;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.SaveAssetIfDirty(map);
            var materialPath = Root + "/PrototypeUnlit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Directory.CreateDirectory(Root); AssetDatabase.Refresh();
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP/Unlit 셰이더가 없습니다.");
                material = new Material(shader); AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(materialPath)); AssetDatabase.SaveAssetIfDirty(material);
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var cameraObject = new GameObject("Map Camera", typeof(Camera), typeof(AudioListener));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.GetComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(55, 45, 0);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f); camera.farClipPlane = 500;
                var game = new GameObject("Grid Map Player", typeof(GridMapPlayer)); game.GetComponent<GridMapPlayer>().Configure(map, material, camera);
                SceneManager.MoveGameObjectToScene(game, scene);
                var playerSettings = new SerializedObject(game.GetComponent<GridMapPlayer>());
                playerSettings.FindProperty("playerSettings").objectReferenceValue = EnsurePlayerSettings();
                playerSettings.FindProperty("cameraSettings").objectReferenceValue = EnsureCameraSettings(); playerSettings.ApplyModifiedPropertiesWithoutUndo();
                game.GetComponent<GridMapPlayer>().FitCamera();
                MapEnvironmentEditor.Synchronize(game.GetComponent<GridMapPlayer>(), false);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("테스트 씬 저장에 실패했습니다.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path); EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log(allowIncompleteMap ? $"[IBIIIS] 편집용 씬 저장: {path}. 타일과 시작 위치를 지정한 뒤 Play하세요." : $"[IBIIIS] 테스트 씬 저장: {path}. 이 씬을 열고 Play 후 WASD로 확인하세요.");
            return path;
        }
    }
}
