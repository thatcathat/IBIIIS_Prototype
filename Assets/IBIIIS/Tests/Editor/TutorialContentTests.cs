using System.IO;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IBIIIS.Tests
{
    /// <summary>실제 튜토리얼 콘텐츠(대본·맵·씬)를 화면 없이 처음부터 끝까지 진행해 본다.</summary>
    public sealed class TutorialContentTests
    {
        private Scene scene;
        private string savePath;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            savePath = Path.Combine(Path.GetTempPath(), $"IBIIIS_tutorial_test_{System.Guid.NewGuid():N}.json");
            ProgressStore.UseFile(savePath);
        }
        [TearDown] public void Cleanup()
        {
            var dialogue = Object.FindAnyObjectByType<DialogueController>(); if (dialogue != null) Object.DestroyImmediate(dialogue.gameObject);
            EditorSceneManager.ClosePreviewScene(scene);
            ProgressStore.UseFile(null); if (File.Exists(savePath)) File.Delete(savePath);
        }
        private (GridMapPlayer battle, TutorialDirector director, GameObject fly, GameObject scarecrow) Build(string mapPath, string scriptPath)
        {
            var go = new GameObject("Battle"); SceneManager.MoveGameObjectToScene(go, scene);
            var battle = go.AddComponent<GridMapPlayer>(); battle.Configure(AssetDatabase.LoadAssetAtPath<GridMap>(mapPath), null, null); battle.Build();
            var fly = new GameObject("Fly"); SceneManager.MoveGameObjectToScene(fly, scene); fly.SetActive(false);
            var scarecrow = new GameObject("Scarecrow"); SceneManager.MoveGameObjectToScene(scarecrow, scene); scarecrow.SetActive(false);
            var director = go.AddComponent<TutorialDirector>();
            director.Configure(battle, AssetDatabase.LoadAssetAtPath<TutorialScript>(scriptPath), new[]
            {
                new TutorialDirector.Actor { id = "fly", target = fly, height = .75f },
                new TutorialDirector.Actor { id = "scarecrow", target = scarecrow, height = 1.6f, blocksCell = true, cell = new Vector2Int(7, 2) },
            });
            return (battle, director, fly, scarecrow);
        }
        private static void Finish(GridMapPlayer battle) { for (int i = 0; i < 200 && (battle.Session.IsBusy || battle.IsPresenting); i++) battle.AdvanceMovement(.1f); }
        private static void Do(GridMapPlayer battle, PlayerAction action, Vector2Int direction)
        {
            Assert.IsTrue(battle.TryPlayerCommand(action, direction), $"대본이 허용한 행동 {action} {direction}"); Finish(battle);
        }
        // 대본 단계마다 플레이어가 할 법한 입력을 넣어 끝까지 진행한다. 진행한 단계 종류를 돌려준다.
        private static void Play(GridMapPlayer battle, TutorialDirector director)
        {
            director.Begin();
            for (int guard = 0; guard < 400 && director.IsRunning; guard++)
            {
                var step = director.Current;
                switch (step.type)
                {
                    case TutorialStepType.Line: director.Dialogue.Runner.Advance(); director.Dialogue.Runner.Advance(); break;
                    case TutorialStepType.NameInput: director.Dialogue.SubmitName("아무거나"); break;
                    case TutorialStepType.WaitAction:
                        Do(battle, (step.actions & TutorialActions.Dash) != 0 ? PlayerAction.Dash : PlayerAction.Move, step.direction); break;
                    case TutorialStepType.WaitFreeMove: Do(battle, PlayerAction.Move, Vector2Int.right); director.Tick(step.seconds + .01f); break;
                    case TutorialStepType.WaitReachCell:
                        var at = battle.Session.Position;
                        var dir = at.x != step.cell.x ? new Vector2Int(System.Math.Sign(step.cell.x - at.x), 0) : new Vector2Int(0, System.Math.Sign(step.cell.y - at.y));
                        Do(battle, PlayerAction.Move, dir); break;
                    case TutorialStepType.WaitClickCell: director.ClickCell(step.cell); director.Tick(step.seconds + .01f); break;
                    case TutorialStepType.FreePlay: Do(battle, PlayerAction.Wait, Vector2Int.zero); break;
                    case TutorialStepType.Wait: director.Tick(step.seconds + .01f); break;
                }
                director.Tick(0);
            }
            Assert.IsFalse(director.IsRunning, "대본이 끝까지 진행됨");
        }

        [Test] public void Stage1RunsToTheEndAndHeadsToStage2()
        {
            var (battle, director, fly, scarecrow) = Build(TutorialSetup.Stage1Map, TutorialSetup.Stage1Script);
            Assert.AreEqual(new Vector2Int(1, 2), battle.Session.Position, "기획서 시작 (1,2)");
            Play(battle, director);
            Assert.IsTrue(fly.activeSelf, "어시파리 등장"); Assert.IsTrue(scarecrow.activeSelf, "허수아비 등장");
            Assert.IsTrue(battle.Session.IsBlocked(new Vector2Int(7, 2)), "허수아비 칸은 막힘");
            Assert.AreEqual(new Vector2Int(6, 2), battle.Session.Position, "허수아비 앞 칸에서 공격");
            Assert.AreEqual(TutorialSetup.Stage2Scene, director.LoadedScene, "2단계로 넘어감");
            Assert.AreEqual("아무거나", director.EnteredName, "입력값은 기록만(대사는 어시 파리로 고정)");
        }
        [Test] public void Stage2RunsToTheEndClearsAndReturnsToTheOverworld()
        {
            var (battle, director, _, _) = Build(TutorialSetup.Stage2Map, TutorialSetup.Stage2Script);
            Assert.AreEqual(new Vector2Int(3, 0), battle.Session.Position, "기획서 시작 (3,6) → 맵 좌표 (3,0)");
            Assert.AreEqual(2, battle.Session.Enemies.Count);
            Play(battle, director);
            Assert.AreEqual(4, battle.Session.Enemies.Count, "1턴 뒤 2마리 합류");
            Assert.AreEqual(BattlePhase.Won, battle.Session.Phase, "피하기만 해서 몬스터끼리 부딪혀 전멸");
            Assert.IsTrue(ProgressStore.IsCleared(TutorialSetup.ClearId), "튜토리얼 클리어 기록");
            Assert.AreEqual(AssetPaths.OverworldScene, director.LoadedScene, "미니맵으로 돌아감");
        }
        [Test] public void ScenesAreWiredForTitleAndBothStages()
        {
            Assert.AreEqual(TutorialSetup.TitleScene, EditorBuildSettings.scenes[0].path, "게임 시작 씬은 타이틀");
            var opened = EditorSceneManager.OpenScene(TutorialSetup.TitleScene, OpenSceneMode.Additive);
            try
            {
                var title = Object.FindAnyObjectByType<TitleScreen>();
                Assert.IsNotNull(title); Assert.AreEqual(TutorialSetup.Stage1Scene, title.StartScenePath, "게임 시작 → 튜토리얼 1단계");
            }
            finally { EditorSceneManager.CloseScene(opened, true); }
            foreach (var path in new[] { TutorialSetup.Stage1Scene, TutorialSetup.Stage2Scene })
            {
                var stage = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    TutorialDirector director = null;
                    foreach (var root in stage.GetRootGameObjects()) if (director == null) director = root.GetComponentInChildren<TutorialDirector>(true);
                    Assert.IsNotNull(director, path);
                    var so = new SerializedObject(director);
                    Assert.IsNotNull(so.FindProperty("battle").objectReferenceValue, path); Assert.IsNotNull(so.FindProperty("script").objectReferenceValue, path);
                    Assert.Greater(so.FindProperty("actors").arraySize, 0, path);
                }
                finally { EditorSceneManager.CloseScene(stage, true); }
            }
        }
    }
}
