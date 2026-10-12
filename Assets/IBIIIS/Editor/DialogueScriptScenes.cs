using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor의 '씬에 연결': 열린 씬의 대본 진행기(TutorialDirector)에 대본을 꽂는 규칙(화면과 분리, 자동 테스트 대상).
    /// 연결은 Ctrl+Z로 되돌릴 수 있고 씬은 변경 표시만 한다(저장은 사용자가 한다).</summary>
    public static class DialogueScriptScenes
    {
        /// <summary>씬 하나에 있는 진행기(꺼진 오브젝트 포함).</summary>
        public static List<TutorialDirector> DirectorsIn(Scene scene)
        {
            var list = new List<TutorialDirector>();
            if (!scene.IsValid() || !scene.isLoaded) return list;
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<TutorialDirector>(true));
            return list;
        }
        /// <summary>지금 열린(Hierarchy에 있는) 모든 씬의 진행기.</summary>
        public static List<TutorialDirector> DirectorsInOpenScenes()
        {
            var list = new List<TutorialDirector>();
            for (int i = 0; i < SceneManager.sceneCount; i++) list.AddRange(DirectorsIn(SceneManager.GetSceneAt(i)));
            return list;
        }
        /// <summary>진행기에 연결된 대본.</summary>
        public static TutorialScript ScriptOf(TutorialDirector director)
            => director != null ? new SerializedObject(director).FindProperty("script").objectReferenceValue as TutorialScript : null;
        /// <summary>진행기에 대본을 연결한다(되돌리기 가능, 씬 변경 표시). 이미 같은 대본이면 아무것도 하지 않고 false.</summary>
        public static bool Connect(TutorialDirector director, TutorialScript script)
        {
            if (director == null || script == null) return false;
            var so = new SerializedObject(director); var property = so.FindProperty("script");
            if (property.objectReferenceValue == script) return false;
            property.objectReferenceValue = script;
            so.ApplyModifiedProperties(); // Undo 기록 포함
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            return true;
        }
        /// <summary>목록·안내에 쓰는 이름: 씬 이름 / 오브젝트 경로.</summary>
        public static string Describe(TutorialDirector director)
        {
            var path = director.name; var t = director.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return $"{director.gameObject.scene.name} / {path}";
        }
    }
}
