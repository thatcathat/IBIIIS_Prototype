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
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                    var picked = EditorGUILayout.ObjectField(new GUIContent("Battle Scene", "필수. 들어갈 전투 씬(맵과 함께 만든 .unity). Grid Map Player가 있어야 합니다."),
                        property.objectReferenceValue as SceneAsset, typeof(SceneAsset), false);
                    EditorGUI.showMixedValue = false;
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.objectReferenceValue = picked;
                        serializedObject.FindProperty("battleScenePath").stringValue = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                    }
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
            if (!string.IsNullOrEmpty(path) && SceneUtilityIndex(path) < 0)
            {
                EditorGUILayout.HelpBox("이 전투 씬은 빌드 씬 목록(File > Build Profiles)에 없습니다. 에디터 Play에서는 들어갈 수 있지만, 빌드한 게임에서는 들어갈 수 없습니다.", MessageType.Info);
                if (GUILayout.Button("빌드 씬 목록에 추가")) AddToBuild(path);
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
        private static void AddToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes) { new EditorBuildSettingsScene(path, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[IBIIIS] 빌드 씬 목록에 추가: {path}");
        }
    }
}
