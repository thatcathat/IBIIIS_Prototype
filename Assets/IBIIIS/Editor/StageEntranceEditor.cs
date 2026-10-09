using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>스테이지 입구 Inspector. 전투 씬을 씬 에셋으로 고르게 하고, ID 누락·중복과 빌드 씬 목록 누락을 알려 준다.</summary>
    [CustomEditor(typeof(StageEntrance)), CanEditMultipleObjects]
    public sealed class StageEntranceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (property.name == "m_Script" || property.name == "battleScenePath") continue;
                if (property.name == "battleSceneAsset")
                {
                    // SerializedProperty를 받는 필드라 여러 선택·프리팹 오버라이드 표시·우클릭 메뉴가 기본과 같다.
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.ObjectField(property, typeof(SceneAsset), new GUIContent("Battle Scene", "필수. 들어갈 전투 씬(맵과 함께 만든 .unity). Grid Map Player가 있어야 합니다."));
                    if (EditorGUI.EndChangeCheck())
                        serializedObject.FindProperty("battleScenePath").stringValue = property.objectReferenceValue != null ? AssetDatabase.GetAssetPath(property.objectReferenceValue) : "";
                    continue;
                }
                EditorGUILayout.PropertyField(property, true);
            }
            serializedObject.ApplyModifiedProperties();
            if (targets.Length != 1) return;
            var entrance = (StageEntrance)target;
            if (entrance.Problem != null) EditorGUILayout.HelpBox(entrance.Problem + " 이 입구는 상호작용하지 않습니다.", MessageType.Error);
            var duplicate = FindDuplicate(entrance);
            if (duplicate != null) EditorGUILayout.HelpBox($"Stage Id '{entrance.StageId}'를 '{duplicate.name}'도 쓰고 있습니다. 클리어 기록이 섞이므로 ID를 다르게 하세요.", MessageType.Error);
            var path = entrance.BattleScenePath;
            int index = string.IsNullOrEmpty(path) ? -2 : SceneUtilityIndex(path);
            if (index == -1)
            {
                EditorGUILayout.HelpBox("이 전투 씬은 빌드 씬 목록(File > Build Profiles)에 없습니다. 에디터 Play에서는 들어갈 수 있지만, 빌드한 게임에서는 들어갈 수 없습니다.", MessageType.Info);
                if (GUILayout.Button("빌드 씬 목록에 추가")) AddToBuild(path);
            }
            else if (index >= 0 && !EditorBuildSettings.scenes[index].enabled)
            {
                EditorGUILayout.HelpBox("이 전투 씬은 빌드 씬 목록에 있지만 꺼져 있습니다. 빌드한 게임에서는 들어갈 수 없습니다.", MessageType.Info);
                if (GUILayout.Button("빌드 씬 목록에서 켜기")) EnableInBuild(index);
            }
            if (!string.IsNullOrEmpty(entrance.StageId))
                EditorGUILayout.LabelField("클리어 기록", entrance.IsCleared ? "클리어함" : "클리어 안 함");
        }
        private static StageEntrance FindDuplicate(StageEntrance entrance)
        {
            if (string.IsNullOrEmpty(entrance.StageId)) return null;
            foreach (var other in Object.FindObjectsByType<StageEntrance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other != entrance && other.StageId == entrance.StageId) return other;
            return null;
        }
        private static int SceneUtilityIndex(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].path == path) return i;
            return -1;
        }
        private static void EnableInBuild(int index)
        {
            var scenes = EditorBuildSettings.scenes; scenes[index].enabled = true; EditorBuildSettings.scenes = scenes;
            Debug.Log($"[IBIIIS] 빌드 씬 목록에서 켬: {scenes[index].path}");
        }
        private static void AddToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes) { new EditorBuildSettingsScene(path, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[IBIIIS] 빌드 씬 목록에 추가: {path}");
        }
    }
}
