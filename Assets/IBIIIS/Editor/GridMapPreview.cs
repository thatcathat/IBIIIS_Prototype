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
        private static readonly Dictionary<int, string> signatures = new Dictionary<int, string>();
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
                var signature = EditorJsonUtility.ToJson(target) + target.transform.localToWorldMatrix.ToString();
                if (target.Map != null)
                {
                    signature += EditorJsonUtility.ToJson(target.Map);
                    foreach (var enemy in target.Map.Enemies) if (enemy != null && enemy.Prefab != null) signature += AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(enemy.Prefab)).ToString();
                    foreach (var tile in target.Map.Palette) if (tile != null)
                    {
                        signature += EditorJsonUtility.ToJson(tile);
                        if (tile.VisualPrefab != null) signature += AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(tile.VisualPrefab)).ToString();
                    }
                }
                if (target.ViewCamera != null) signature += target.ViewCamera.transform.rotation.ToString() + target.ViewCamera.aspect;
                if (target.PlayerVisualPrefab != null) signature += AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(target.PlayerVisualPrefab)).ToString();
                if (target.FallbackMaterial != null) signature += EditorJsonUtility.ToJson(target.FallbackMaterial);
                if (target.CameraSettings != null) signature += EditorJsonUtility.ToJson(target.CameraSettings);
                if (target.SharedPlayerSettings != null) signature += EditorJsonUtility.ToJson(target.SharedPlayerSettings);
                if (signatures.TryGetValue(id, out var previous) && previous == signature && (target.Generated != null || target.Map == null)) continue;
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
