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
        private ScenarioScript first, second;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            first = ScriptableObject.CreateInstance<ScenarioScript>(); first.name = "First";
            second = ScriptableObject.CreateInstance<ScenarioScript>(); second.name = "Second";
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(first); Object.DestroyImmediate(second);
        }
        private ScenarioDirector AddDirector(string name, Transform parent = null)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent);
            return go.AddComponent<ScenarioDirector>();
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
        [Test] public void ReadsDirectorsFromSceneFilesWithoutOpeningThem()
        {
            var entries = DialogueScriptScenes.ReadSceneFile(TutorialSetup.Stage2Scene);
            Assert.AreEqual(1, entries.Count, "튜토리얼 2단계 씬의 진행기 하나");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(TutorialSetup.Stage2Script), entries[0].ScriptGuid, "연결된 대본");
            Assert.IsFalse(string.IsNullOrEmpty(entries[0].ObjectName)); Assert.IsFalse(entries[0].Open);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<ScenarioScript>(TutorialSetup.Stage2Script), entries[0].Script);
            Assert.IsEmpty(DialogueScriptScenes.ReadSceneFile(TutorialSetup.TitleScene), "진행기가 없는 씬");
            CollectionAssert.AreEqual(new[] { "fly" }, entries[0].ActorIds, "등장 오브젝트 ID를 파일에서 읽음");
            Assert.IsTrue(entries[0].PlayOnStart);
        }
        [Test] public void TargetIdsSkipThePlayerAndMissingActorsBecomeWarnings()
        {
            var steps = new[]
            {
                new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "!" },
                new ScenarioStep { type = ScenarioStepType.Emote, target = "fly", text = "?" },
                new ScenarioStep { type = ScenarioStepType.SetActive, target = "scarecrow", active = true },
                new ScenarioStep { type = ScenarioStepType.EndDisplay, display = ScenarioDisplay.Emote, target = "fly" },
                new ScenarioStep { type = ScenarioStepType.EndDisplay, display = ScenarioDisplay.Pose, target = "ignored" },
                new ScenarioStep { type = ScenarioStepType.Line, target = "not-a-target" },
            };
            var ids = DialogueScriptScenes.TargetIds(steps);
            CollectionAssert.AreEqual(new[] { ("fly", 1), ("scarecrow", 2) }, ids, "player·자세 끝·대사는 빼고 처음 나온 단계 번호");
            var entry = new DialogueScriptScenes.Entry { SceneName = "Test", ObjectName = "Director", FileActorIds = { "fly" } };
            var issues = DialogueScriptScenes.ActorIssues(steps, new[] { entry });
            Assert.AreEqual(1, issues.Count); Assert.AreEqual(2, issues[0].Index); StringAssert.Contains("scarecrow", issues[0].Message); Assert.IsFalse(issues[0].Error);
        }
        [Test] public void TutorialScriptsFindAllTheirActorsAndKeepThePreviousSettings()
        {
            foreach (var (scene, path) in new[] { (TutorialSetup.Stage1Scene, TutorialSetup.Stage1Script), (TutorialSetup.Stage2Scene, TutorialSetup.Stage2Script) })
            {
                var script = AssetDatabase.LoadAssetAtPath<ScenarioScript>(path);
                CollectionAssert.IsEmpty(DialogueScriptScenes.ActorIssues(script.Steps, DialogueScriptScenes.ReadSceneFile(scene)), path);
                // 진행기에서 대본으로 옮긴 설정: 두 튜토리얼 씬의 진행기 값(전투 준비 시 시작, 적 말풍선 숨김, 위험 그림은 대본이)과 같아야 한다.
                var o = script.Options;
                Assert.AreEqual(ScenarioStartTiming.BattleReady, o.startTiming, path); Assert.IsFalse(o.onlyOnce, path);
                Assert.IsTrue(o.hideEnemyAlerts, path); Assert.IsTrue(o.scriptControlsDanger, path);
                Assert.IsTrue(o.allowSkip, path); Assert.AreEqual(.6f, o.SkipHold, 1e-4f, path);
            }
            var fresh = ScriptableObject.CreateInstance<ScenarioScript>();
            try { Assert.IsTrue(fresh.Options.allowSkip); Assert.IsTrue(fresh.Options.hideEnemyAlerts); Assert.AreEqual(ScenarioStartTiming.BattleReady, fresh.Options.startTiming); }
            finally { Object.DestroyImmediate(fresh); }
        }
        [Test] public void ConnectsIntoAClosedSceneFileAndClosesIt()
        {
            string folder = "Assets/IBIIIS/Scripts/Tests/Temp_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            AssetDatabase.CreateFolder("Assets/IBIIIS/Scripts/Tests", folder.Substring(folder.LastIndexOf('/') + 1));
            try
            {
                string scenePath = folder + "/Copy.unity";
                Assert.IsTrue(AssetDatabase.CopyAsset(TutorialSetup.Stage2Scene, scenePath));
                var created = DialogueScriptAssets.Create(folder + "/Linked.asset", "test_linked_" + System.Guid.NewGuid().ToString("N").Substring(0, 6), DialogueScriptAssets.BlankSteps(), out _);
                int openBefore = SceneManager.sceneCount;
                var entry = DialogueScriptScenes.ReadSceneFile(scenePath)[0];
                Assert.IsTrue(DialogueScriptScenes.Connect(entry, created, out string problem), problem);
                Assert.AreEqual(openBefore, SceneManager.sceneCount, "연결 뒤 씬을 닫음");
                var after = DialogueScriptScenes.ReadSceneFile(scenePath)[0];
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(folder + "/Linked.asset"), after.ScriptGuid, "씬 파일에 저장됨");
                Assert.IsFalse(DialogueScriptScenes.Connect(after, created, out problem), "같은 대본이면 그대로"); Assert.IsNotNull(problem);
                var temp = ScriptableObject.CreateInstance<ScenarioScript>();
                Assert.IsFalse(DialogueScriptScenes.Connect(after, temp, out problem), "파일 아닌 대본은 닫힌 씬에 연결하지 않음");
                Object.DestroyImmediate(temp);
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(TutorialSetup.Stage2Script), DialogueScriptScenes.ReadSceneFile(TutorialSetup.Stage2Scene)[0].ScriptGuid, "원본 씬은 그대로");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
    }
}
