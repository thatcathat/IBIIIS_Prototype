using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class TutorialTests
    {
        private Scene scene;
        private GridMap map;
        private TutorialScript script;
        private DialogueCharacter fly;
        private GridMapPlayer battle;
        private TutorialDirector director;
        private GameObject scarecrow;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(9, 5);
            for (int y = 0; y < 5; y++) for (int x = 0; x < 9; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(1, 2));
            fly = ScriptableObject.CreateInstance<DialogueCharacter>(); fly.Set("fly", "어시 파리", DialogueSide.Right, null, Color.green);
            script = ScriptableObject.CreateInstance<TutorialScript>();
            script.Set("test", new[]
            {
                new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Narrate("주인공이 접속한다.") },
                new TutorialStep { type = TutorialStepType.Emote, target = "player", text = "?" },
                new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Say(fly, "안녕!") },
                new TutorialStep { type = TutorialStepType.NameInput, line = DialogueLine.Narrate("이름을 붙여주세요"), text = "파리" },
                new TutorialStep { type = TutorialStepType.WaitAction, line = DialogueLine.Narrate("A키로 왼쪽"), actions = TutorialActions.Move, direction = Vector2Int.left, keys = "A" },
                new TutorialStep { type = TutorialStepType.WaitFreeMove, actions = TutorialActions.Move, seconds = 3, keys = "W A S D" },
                new TutorialStep { type = TutorialStepType.WaitReachCell, actions = TutorialActions.Move, cell = new Vector2Int(3, 2) },
                new TutorialStep { type = TutorialStepType.SetActive, target = "scarecrow", active = true },
                new TutorialStep { type = TutorialStepType.WaitClickCell, cell = new Vector2Int(4, 2), seconds = 1 },
                new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Say(fly, "끝!") },
            });
            var go = new GameObject("Battle"); SceneManager.MoveGameObjectToScene(go, scene);
            battle = go.AddComponent<GridMapPlayer>(); battle.Configure(map, null, null); battle.Build();
            scarecrow = new GameObject("Scarecrow"); SceneManager.MoveGameObjectToScene(scarecrow, scene); scarecrow.SetActive(false);
            director = go.AddComponent<TutorialDirector>();
            director.Configure(battle, script, new[] { new TutorialDirector.Actor { id = "scarecrow", target = scarecrow, height = 1.5f } });
        }
        [TearDown] public void Cleanup()
        {
            var dialogue = Object.FindAnyObjectByType<DialogueController>(); if (dialogue != null) Object.DestroyImmediate(dialogue.gameObject);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(map); Object.DestroyImmediate(script); Object.DestroyImmediate(fly);
        }
        private void NextLine() { var r = director.Dialogue.Runner; r.Advance(); r.Advance(); }
        private void Act(PlayerAction action, Vector2Int direction, bool expect = true)
        {
            Assert.AreEqual(expect, battle.TryPlayerCommand(action, direction), $"{action} {direction}");
            if (expect) battle.AdvanceMovement(2);
            director.Tick(0);
        }

        [Test] public void ScriptRunsLinesInputsAndConditionsInOrder()
        {
            director.Begin();
            Assert.AreEqual(0, director.StepIndex); Assert.IsTrue(director.Dialogue.IsOpen);
            Assert.IsFalse(battle.AllowUndoRestart, "대본 중 되돌리기·다시 시작 막음"); Act(PlayerAction.Move, Vector2Int.right, false);
            NextLine();
            Assert.AreEqual(2, director.StepIndex, "감정 표시는 기다리지 않음"); CollectionAssert.Contains(director.ActiveEmotes, "?");
            Assert.AreEqual("어시 파리", director.Dialogue.Runner.SpeakerName);
            NextLine();
            CollectionAssert.DoesNotContain(director.ActiveEmotes, "?", "다음 대사가 끝나면 감정 표시 사라짐");
            Assert.IsTrue(director.Dialogue.AskingName); director.Dialogue.SubmitName("벌레");
            Assert.AreEqual("벌레", director.EnteredName); Assert.AreEqual(4, director.StepIndex);

            CollectionAssert.AreEqual(new[] { "A" }, director.Dialogue.Keys); Assert.AreEqual(1, battle.GuideCellCount, "왼쪽 칸 점멸");
            Act(PlayerAction.Move, Vector2Int.up, false); Act(PlayerAction.Dash, Vector2Int.left, false);
            Act(PlayerAction.Move, Vector2Int.left);
            Assert.AreEqual(5, director.StepIndex, "허용한 행동 한 번 뒤 다음");

            director.Tick(10); Assert.AreEqual(5, director.StepIndex, "처음 움직이기 전에는 시간이 흐르지 않음");
            Act(PlayerAction.Move, Vector2Int.right); director.Tick(1); Assert.AreEqual(5, director.StepIndex);
            director.Tick(2.1f); Assert.AreEqual(6, director.StepIndex, "첫 이동 뒤 3초");

            Act(PlayerAction.Move, Vector2Int.right); Assert.AreEqual(6, director.StepIndex);
            Act(PlayerAction.Move, Vector2Int.right);
            Assert.AreEqual(new Vector2Int(3, 2), battle.Session.Position);
            Assert.IsTrue(scarecrow.activeSelf, "도착하면 다음 단계(허수아비 등장)"); Assert.AreEqual(8, director.StepIndex);

            int clicks = 0; director.CellClicked += _ => clicks++;
            Act(PlayerAction.Move, Vector2Int.right, false);
            director.ClickCell(new Vector2Int(5, 2)); Assert.AreEqual(0, clicks, "다른 칸은 무시");
            director.ClickCell(new Vector2Int(4, 2)); Assert.AreEqual(1, clicks);
            director.Tick(.5f); Assert.AreEqual(8, director.StepIndex, "연출 시간 동안 기다림");
            director.Tick(.6f); Assert.AreEqual(9, director.StepIndex);
            NextLine();
            Assert.IsTrue(director.Finished); Assert.IsNull(battle.CommandFilter); Assert.IsTrue(battle.AllowUndoRestart, "끝나면 입력 제한을 풂");
        }
    }
}
