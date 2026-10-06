using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    public static class MapEnvironmentEditor
    {
        public static bool HasUnappliedChanges(GameObject instance)
        {
            if (instance == null) return false;
            var t = instance.transform;
            return !PrefabUtility.IsPartOfPrefabInstance(instance) ||
                PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false) ||
                t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one;
        }
        public static bool Synchronize(GridMapPlayer owner, bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || owner == null) return false;
            var desired = owner.Map != null ? owner.Map.EnvironmentPrefab : null;
            var existing = owner.EnvironmentInstance;
            if (existing != null && owner.EnvironmentSource == desired) return true;
            if (existing == null && desired == null) return true;
            if (desired != null && !ValidPrefab(desired)) return false;
            if (HasUnappliedChanges(existing))
            {
                if (!interactive) return false;
                int decision = EditorUtility.DisplayDialogComplex("환경 배치 변경 보호",
                    "기존 환경에 프리팹으로 적용하지 않은 변경이 있습니다. 기존 프리팹에 적용한 뒤 교체하거나, 변경을 버리고 교체할 수 있습니다.",
                    "적용 후 교체", "취소", "버리고 교체");
                if (decision == 1) return false;
                if (decision == 0 && !Apply(existing)) return false;
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Update map environment");
            Undo.RecordObject(owner, "Update map environment");
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            GameObject next = null;
            if (desired != null)
            {
                next = (GameObject)PrefabUtility.InstantiatePrefab(desired, owner.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(next, "Create map environment");
                next.transform.SetParent(owner.transform, false);
                next.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); next.transform.localScale = Vector3.one;
            }
            owner.SetEnvironmentInstance(next, desired);
            PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            EditorUtility.SetDirty(owner); EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
            Undo.CollapseUndoOperations(group); return true;
        }
        public static bool ValidPrefab(GameObject prefab)
        {
            return PrefabUtility.IsPartOfPrefabAsset(prefab) && AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)) == prefab &&
                prefab.transform.localPosition == Vector3.zero && prefab.transform.localRotation == Quaternion.identity && prefab.transform.localScale == Vector3.one;
        }
        public static bool Apply(GameObject instance)
        {
            if (instance == null || !PrefabUtility.IsPartOfPrefabInstance(instance))
            { EditorUtility.DisplayDialog("환경 적용 불가", "프리팹 연결이 끊겼습니다. Unity의 Prefab 저장 기능으로 배치를 보존한 뒤 새 환경으로 연결하세요.", "확인"); return false; }
            if (instance.transform.localPosition != Vector3.zero || instance.transform.localRotation != Quaternion.identity || instance.transform.localScale != Vector3.one)
            { EditorUtility.DisplayDialog("환경 루트 확인", "루트는 위치·회전 0, 크기 1로 유지하세요. 위치 보정은 자식 오브젝트에서 편집한 뒤 적용하세요.", "확인"); return false; }
            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.UserAction);
            return true;
        }
        public static GameObject CreateEnvironment(GridMap map, string path)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject prefab;
            try
            {
                var root = new GameObject("Environment"); SceneManager.MoveGameObjectToScene(root, scene);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (prefab == null) throw new InvalidOperationException("환경 프리팹을 저장하지 못했습니다.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Undo.RecordObject(map, "Connect environment prefab"); map.SetEnvironmentPrefab(prefab); EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            return prefab;
        }
        public static VisualElement CreateMapControls(GridMap map)
        {
            var root = new VisualElement();
            var field = new ObjectField("Environment Prefab") { objectType = typeof(GameObject), allowSceneObjects = false };
            field.SetValueWithoutNotify(map.EnvironmentPrefab); root.Add(field);
            field.RegisterValueChangedCallback(e =>
            {
                var prefab = e.newValue as GameObject;
                if (prefab != null && !ValidPrefab(prefab))
                { field.SetValueWithoutNotify(map.EnvironmentPrefab); EditorUtility.DisplayDialog("환경 프리팹 확인", "프리팹의 루트를 지정하고 루트 위치·회전 0, 크기 1로 설정하세요.", "확인"); return; }
                Undo.RecordObject(map, "Connect environment prefab"); map.SetEnvironmentPrefab(prefab); EditorUtility.SetDirty(map); GridMapPreview.RefreshAll();
            });
            var serialized = new SerializedObject(map);
            root.TrackPropertyValue(serialized.FindProperty("environmentPrefab"), _ => field.SetValueWithoutNotify(map.EnvironmentPrefab));
            root.Add(new Button(() =>
            {
                var path = EditorUtility.SaveFilePanelInProject("환경 프리팹 생성", map.name + "_Environment", "prefab", "환경 배치 저장 위치", AssetPaths.Environments);
                if (string.IsNullOrEmpty(path)) return;
                CreateEnvironment(map, path); field.SetValueWithoutNotify(map.EnvironmentPrefab); GridMapPreview.RefreshAll();
            }) { text = "새 환경 프리팹 만들고 연결" });
            root.Add(new HelpBox("Scene의 Grid Map Player 아래 Environment에 장식을 배치하세요. 씬 저장과 별도로 프리팹에 적용해야 다른 씬에서도 같은 배치를 불러옵니다. 교체가 보류되면 Grid Map Player Inspector에서 해결하세요.", HelpBoxMessageType.Info));
            root.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode); return root;
        }
        public static VisualElement CreateSceneControls(GridMapPlayer owner)
        {
            var root = new VisualElement(); var status = new HelpBox("", HelpBoxMessageType.Info); root.Add(status);
            root.schedule.Execute(() =>
            {
                if (owner == null) return;
                var desired = owner.Map != null ? owner.Map.EnvironmentPrefab : null;
                bool pending = owner.EnvironmentSource != desired || (owner.EnvironmentInstance == null && desired != null);
                status.text = pending ? "환경 연결 변경 대기. 아래 동기화 버튼으로 미적용 변경을 확인하세요." :
                    owner.EnvironmentInstance == null ? "환경 없음. 맵의 환경 설정에서 프리팹을 연결하세요." :
                    HasUnappliedChanges(owner.EnvironmentInstance) ? "환경에 미적용 변경이 있습니다. 프리팹 적용 후 씬도 저장하세요." : "환경 프리팹과 연결되어 있습니다.";
            }).Every(400);
            root.Add(new Button(() => { if (owner.Map != null) Selection.activeObject = owner.Map; }) { text = "맵 환경 설정 열기" });
            root.Add(new Button(() => { if (owner.EnvironmentInstance != null) Selection.activeGameObject = owner.EnvironmentInstance; }) { text = "Scene 환경 선택" });
            root.Add(new Button(() => Synchronize(owner, true)) { text = "환경 연결 동기화 / 변경 해결" });
            root.Add(new Button(() => Apply(owner.EnvironmentInstance)) { text = "환경 배치를 프리팹에 적용" });
            root.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode); return root;
        }
    }
}
