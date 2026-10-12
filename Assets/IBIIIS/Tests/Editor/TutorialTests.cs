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

        private const string Enemy003 = "Assets/IBIIIS/Characters/Enemies/Enemy003/Enemy003.prefab";
        [Test] public void SpawnedEnemiesJoinAndUndoRemovesThem()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Enemy003);
            Assert.IsFalse(battle.SpawnEnemy(prefab, new Vector2Int(1, 2), Vector2Int.left), "플레이어 칸에는 합류 불가");
            Assert.IsFalse(battle.SpawnEnemy(prefab, new Vector2Int(6, 2), Vector2Int.one), "방향은 상하좌우");
            Assert.IsTrue(battle.TryPlayerCommand(PlayerAction.Wait, Vector2Int.zero)); battle.AdvanceMovement(2);
            Assert.IsTrue(battle.SpawnEnemy(prefab, new Vector2Int(6, 2), Vector2Int.left));
            Assert.IsFalse(battle.SpawnEnemy(prefab, new Vector2Int(6, 2), Vector2Int.left), "같은 칸에 두 번 불가");
            Assert.AreEqual(1, battle.Session.Enemies.Count); Assert.AreEqual(1, battle.Generated.Find("Enemies").childCount);
            Assert.IsTrue(battle.TryPlayerCommand(PlayerAction.Wait, Vector2Int.zero)); battle.AdvanceMovement(2);
            Assert.AreEqual(new Vector2Int(5, 2), battle.Session.Enemies[0].Position, "합류한 다음 행동부터 움직임");
            Assert.IsTrue(battle.TryUndo()); Assert.AreEqual(1, battle.Session.Enemies.Count, "합류 뒤 행동을 되돌리면 그대로 있음");
            Assert.IsTrue(battle.TryUndo()); Assert.AreEqual(0, battle.Session.Enemies.Count, "합류 전으로 되돌리면 빠짐");
            Assert.AreEqual(0, battle.Generated.Find("Enemies").childCount, "외형도 지움");
        }
        [Test] public void BlockingActorStopsMovementAndPredictionDoesNotChangeTheBattle()
        {
            script.Set("block", new[]
            {
                new TutorialStep { type = TutorialStepType.SetActive, target = "scarecrow", active = true },
                new TutorialStep { type = TutorialStepType.FreePlay, actions = TutorialActions.Move | TutorialActions.Wait },
            });
            director.Configure(battle, script, new[] { new TutorialDirector.Actor { id = "scarecrow", target = scarecrow, blocksCell = true, cell = new Vector2Int(2, 2) } });
            director.Begin();
            Assert.IsTrue(scarecrow.activeSelf); Assert.IsTrue(battle.Session.IsBlocked(new Vector2Int(2, 2)));
            Assert.AreEqual(battle.CellWorldPosition(new Vector2Int(2, 2)), scarecrow.transform.position, "그 칸으로 옮김");
            Assert.IsFalse(battle.TryPlayerCommand(PlayerAction.Move, Vector2Int.right), "막힌 칸으로는 못 감");
            var before = battle.Session.Position; int actions = battle.Session.UndoCount;
            Assert.AreEqual(BattlePhase.Waiting, battle.Session.Predict(PlayerAction.Move, Vector2Int.up));
            Assert.AreEqual(before, battle.Session.Position); Assert.AreEqual(actions, battle.Session.UndoCount, "미리 계산은 상태를 바꾸지 않음");
        }
        [Test] public void ClickThrowsTheSpriteThenContinues()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            try
            {
                script.Set("throw", new[]
                {
                    new TutorialStep { type = TutorialStepType.WaitClickCell, cell = new Vector2Int(4, 2), seconds = 1, sprite = sprite },
                    new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Narrate("...") },
                });
                director.Begin(); director.ClickCell(new Vector2Int(4, 2));
                Assert.AreEqual(1, director.ThrownCount, "그림이 날아감");
                director.Tick(1.01f); Assert.AreEqual(0, director.ThrownCount, "연출이 끝나면 지움"); Assert.AreEqual(1, director.StepIndex);
            }
            finally { Object.DestroyImmediate(sprite); }
        }
        [Test] public void EvasionTutorialPathClearsAndDangerousMovesAreBlocked()
        {
            // 기획서 2단계(7x7, 행 0이 맨 위)를 맵 좌표(행 0이 맨 아래)로 옮긴다: 맵 행 = 6 - 기획서 행, 아래는 (0,-1).
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Enemy003);
            var evade = ScriptableObject.CreateInstance<GridMap>(); evade.Resize(7, 7);
            var steps = ScriptableObject.CreateInstance<TutorialScript>();
            try
            {
                for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) evade.SetWalkable(new Vector2Int(x, y), true);
                evade.SetStart(new Vector2Int(3, 0));
                evade.PlaceEnemy(new Vector2Int(1, 5), prefab, Vector2Int.down); evade.PlaceEnemy(new Vector2Int(4, 1), prefab, Vector2Int.down);
                var go = new GameObject("Evade Battle"); SceneManager.MoveGameObjectToScene(go, scene);
                var fight = go.AddComponent<GridMapPlayer>(); fight.Configure(evade, null, null); fight.Build();
                var run = go.AddComponent<TutorialDirector>();
                steps.Set("evade", new[]
                {
                    new TutorialStep { type = TutorialStepType.WaitAction, actions = TutorialActions.Move, direction = Vector2Int.left, keys = "A" },
                    new TutorialStep { type = TutorialStepType.SpawnEnemy, prefab = prefab, cell = new Vector2Int(6, 2), direction = Vector2Int.left },
                    new TutorialStep { type = TutorialStepType.SpawnEnemy, prefab = prefab, cell = new Vector2Int(0, 1), direction = Vector2Int.right },
                    new TutorialStep { type = TutorialStepType.WaitAction, actions = TutorialActions.Dash, direction = Vector2Int.up, keys = "Shift W" },
                    new TutorialStep { type = TutorialStepType.FreePlay, actions = TutorialActions.Move | TutorialActions.Wait | TutorialActions.Dash, line = DialogueLine.Say(fly, "거기로 가면 위험해!"), seconds = 1.5f },
                    new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Say(fly, "끝") },
                });
                run.Configure(fight, steps); run.Begin();
                Assert.IsFalse(fight.TryPlayerCommand(PlayerAction.Move, Vector2Int.up), "대본 입력 외에는 무시");
                Assert.IsTrue(fight.TryPlayerCommand(PlayerAction.Move, Vector2Int.left)); fight.AdvanceMovement(2); run.Tick(0);
                Assert.AreEqual(4, fight.Session.Enemies.Count, "1턴 뒤 2마리 합류"); Assert.AreEqual(3, run.StepIndex);
                Assert.IsTrue(fight.TryPlayerCommand(PlayerAction.Dash, Vector2Int.up)); fight.AdvanceMovement(3); run.Tick(0);
                Assert.AreEqual(4, run.StepIndex, "자유 진행"); Assert.AreEqual(new Vector2Int(2, 2), fight.Session.Position);
                foreach (var dir in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                    if (fight.Session.Predict(PlayerAction.Move, dir) == BattlePhase.Lost)
                    {
                        int blocked = run.BlockedDangerousMoves;
                        Assert.IsFalse(fight.TryPlayerCommand(PlayerAction.Move, dir), "패배할 이동은 막음"); Assert.AreEqual(blocked + 1, run.BlockedDangerousMoves);
                        Assert.AreEqual("거기로 가면 위험해!", run.Dialogue.Runner.Text);
                    }
                for (int turn = 0; turn < 5 && run.StepIndex == 4; turn++)
                {
                    Assert.IsTrue(fight.TryPlayerCommand(PlayerAction.Wait, Vector2Int.zero), "제자리 대기는 안전");
                    for (int i = 0; i < 100 && (fight.Session.IsBusy || fight.IsPresenting); i++) fight.AdvanceMovement(.1f); // 충돌 연출까지 끝낸다
                    run.Tick(0);
                }
                Assert.AreEqual(BattlePhase.Won, fight.Session.Phase, "기획서 경로대로 대기만 해도 몬스터끼리 부딪혀 전멸");
                Assert.AreEqual(5, run.StepIndex, "적이 모두 사라지면 다음 대사");
            }
            finally { Object.DestroyImmediate(evade); Object.DestroyImmediate(steps); }
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
