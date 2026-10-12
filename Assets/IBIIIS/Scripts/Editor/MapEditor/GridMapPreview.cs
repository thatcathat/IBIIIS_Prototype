using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    [InitializeOnLoad]
    public static class GridMapPreview
    {
        // 미리보기를 다시 만들지 판단하는 입력 해시. 0.4초마다 계산하므로 큰 문자열을 만들지 않고 해시에 바로 누적한다.
        private static readonly Dictionary<int, Hash128> signatures = new Dictionary<int, Hash128>();
        private static readonly HashSet<GameObject> hashedPrefabs = new HashSet<GameObject>();
        private static double nextUpdate;
        static GridMapPreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Clear();
                if (state == PlayModeStateChange.EnteredEditMode) { signatures.Clear(); nextUpdate = 0; }
                if (state == PlayModeStateChange.EnteredPlayMode)
                    foreach (var target in Targets()) if (target.isActiveAndEnabled) target.Build();
            };
        }
        private static GridMapPlayer[] Targets() => Object.FindObjectsByType<GridMapPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static void Clear()
        {
            foreach (var target in Targets()) target.ClearGenerated();
            signatures.Clear();
        }
        public static void RefreshAll() { signatures.Clear(); nextUpdate = 0; Update(); }
        private static void AppendDependency(ref Hash128 signature, Object asset)
        {
            var hash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset));
            signature.Append(ref hash);
        }
        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextUpdate) return;
            nextUpdate = EditorApplication.timeSinceStartup + .4;
            var alive = new HashSet<int>();
            foreach (var target in Targets())
            {
                if (EditorUtility.IsPersistent(target) || EditorSceneManager.IsPreviewScene(target.gameObject.scene) || PrefabStageUtility.GetPrefabStage(target.gameObject) != null) continue;
                int id = target.GetInstanceID(); alive.Add(id);
                if (!target.isActiveAndEnabled) { signatures.Remove(id); continue; }
                MapEnvironmentEditor.Synchronize(target, false);
                var signature = new Hash128();
                signature.Append(EditorJsonUtility.ToJson(target)); signature.Append(target.transform.localToWorldMatrix.ToString());
                if (target.Map != null)
                {
                    signature.Append(EditorJsonUtility.ToJson(target.Map));
                    hashedPrefabs.Clear();
                    foreach (var enemy in target.Map.Enemies) if (enemy != null && enemy.Prefab != null && hashedPrefabs.Add(enemy.Prefab)) AppendDependency(ref signature, enemy.Prefab);
                    foreach (var tile in target.Map.Palette) if (tile != null)
                    {
                        signature.Append(EditorJsonUtility.ToJson(tile));
                        if (tile.VisualPrefab != null) AppendDependency(ref signature, tile.VisualPrefab);
                    }
                }
                if (target.ViewCamera != null) { signature.Append(target.ViewCamera.transform.rotation.ToString()); signature.Append(target.ViewCamera.aspect); }
                if (target.PlayerVisualPrefab != null) AppendDependency(ref signature, target.PlayerVisualPrefab);
                if (target.FallbackMaterial != null) signature.Append(EditorJsonUtility.ToJson(target.FallbackMaterial));
                if (target.CameraSettings != null) signature.Append(EditorJsonUtility.ToJson(target.CameraSettings));
                if (target.SharedPlayerSettings != null) signature.Append(EditorJsonUtility.ToJson(target.SharedPlayerSettings));
                // 설정이 잘못된 맵은 미리보기를 만들 수 없으므로 입력이 바뀔 때만 다시 시도한다(원인은 Grid Map Player Inspector에 표시).
                bool unbuildable = target.Map != null && target.Map.ValidateMap(false).Count > 0;
                if (signatures.TryGetValue(id, out var previous) && previous == signature && (target.Generated != null || target.Map == null || unbuildable)) continue;
                target.RefreshPreview(); signatures[id] = signature;
                SceneView.RepaintAll(); EditorApplication.QueuePlayerLoopUpdate();
            }
            var stale = new List<int>(); foreach (var id in signatures.Keys) if (!alive.Contains(id)) stale.Add(id);
            foreach (var id in stale) signatures.Remove(id);
        }
    }
    [CustomEditor(typeof(GridMapPlayer))]
    public sealed class GridMapPlayerInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.Add(MapEnvironmentEditor.CreateSceneControls((GridMapPlayer)target));
            root.Add(new Button(() =>
            {
                var settings = ((GridMapPlayer)target).SharedPlayerSettings;
                if (settings != null) { Selection.activeObject = settings; EditorGUIUtility.PingObject(settings); }
            }) { text = "연결된 공용 플레이어 설정 열기" });
            var mapErrors = new HelpBox("", HelpBoxMessageType.Error); root.Add(mapErrors);
            mapErrors.schedule.Execute(() =>
            {
                var map = target != null ? ((GridMapPlayer)target).Map : null;
                var errors = map != null ? map.ValidateMap() : null;
                bool show = errors != null && errors.Count > 0;
                mapErrors.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) mapErrors.text = "맵 설정 오류로 미리보기·Play를 할 수 없습니다: " + string.Join(" / ", errors);
            }).Every(400);
            var missingSettings = new HelpBox("Player Settings가 없습니다. 이전 씬의 숨겨진 기존 값을 사용 중입니다. 공용 설정을 연결하세요.", HelpBoxMessageType.Warning);
            root.Add(missingSettings);
            void UpdateSettingsWarning() { missingSettings.SetEnabled(true); missingSettings.visible = ((GridMapPlayer)target).SharedPlayerSettings == null; }
            root.TrackPropertyValue(serializedObject.FindProperty("playerSettings"), _ => UpdateSettingsWarning()); UpdateSettingsWarning();
            root.Add(new HelpBox("편집 중 맵·시작 위치가 자동 표시됩니다. Auto Fit Camera를 끄면 직접 맞춘 카메라 구도를 Play에서도 유지합니다. 미리보기는 씬에 저장되지 않습니다.", HelpBoxMessageType.Info));
            var buttons = new VisualElement(); root.Add(buttons);
            buttons.Add(new Button(GridMapPreview.RefreshAll) { text = "Scene 미리보기 새로고침" });
            buttons.Add(new Button(() =>
            {
                var player = (GridMapPlayer)target; var camera = player.ViewCamera;
                if (camera == null) return;
                Undo.RecordObjects(new Object[] { camera, camera.transform }, "Fit map camera"); player.FitCamera();
                EditorSceneManager.MarkSceneDirty(camera.gameObject.scene); GridMapPreview.RefreshAll();
            }) { text = "카메라를 맵 전체에 맞추기" });
            buttons.Add(new Button(() =>
            {
                var player = (GridMapPlayer)target; var camera = player.ViewCamera; var view = SceneView.lastActiveSceneView;
                if (camera == null || view == null) return;
                view.orthographic = camera.orthographic;
                view.in2DMode = false;
                view.LookAtDirect(player.CameraFocus, camera.transform.rotation, camera.orthographicSize);
            }) { text = "Scene 뷰를 게임 카메라 방향으로 보기" });
            buttons.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            return root;
        }
    }
}
