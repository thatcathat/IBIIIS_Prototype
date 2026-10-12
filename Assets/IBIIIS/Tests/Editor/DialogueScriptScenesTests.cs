using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    /// <summary>IBIIIS Dialogue Editor의 '씬에 연결' 규칙. 열린 씬 목록에 넣지 않는 미리보기 씬을 쓴다(사용자의 씬 상태와 무관).</summary>
    public sealed class DialogueScriptScenesTests
    {
        private Scene scene;
        private TutorialScript first, second;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            first = ScriptableObject.CreateInstance<TutorialScript>(); first.name = "First";
            second = ScriptableObject.CreateInstance<TutorialScript>(); second.name = "Second";
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(first); Object.DestroyImmediate(second);
        }
        private TutorialDirector AddDirector(string name, Transform parent = null)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent);
            return go.AddComponent<TutorialDirector>();
        }

        [Test] public void FindsDirectorsInASceneIncludingInactiveOnes()
        {
            var battle = AddDirector("Battle");
            var hidden = AddDirector("Hidden", battle.transform); hidden.gameObject.SetActive(false);
            CollectionAssert.AreEquivalent(new[] { battle, hidden }, DialogueScriptScenes.DirectorsIn(scene));
            StringAssert.EndsWith("/ Battle/Hidden", DialogueScriptScenes.Describe(hidden));
        }
        [Test] public void ConnectsUndoablyAndSkipsTheSameScript()
        {
            var director = AddDirector("Battle");
            Assert.IsNull(DialogueScriptScenes.ScriptOf(director));
            Assert.IsTrue(DialogueScriptScenes.Connect(director, first));
            Assert.AreSame(first, DialogueScriptScenes.ScriptOf(director));
            Assert.IsFalse(DialogueScriptScenes.Connect(director, first), "같은 대본이면 바꾸지 않음");
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(DialogueScriptScenes.Connect(director, second));
            Undo.PerformUndo();
            Assert.AreSame(first, DialogueScriptScenes.ScriptOf(director), "Ctrl+Z로 이전 대본으로");
        }
    }
}
