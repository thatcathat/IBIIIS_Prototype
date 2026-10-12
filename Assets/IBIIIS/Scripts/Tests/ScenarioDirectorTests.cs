using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class ScenarioDirectorTests
    {
        private Scene scene;
        private GridMap map;
        private ScenarioScript script;
        private DialogueCharacter fly;
        private GridMapPlayer battle;
        private ScenarioDirector director;
        private GameObject scarecrow;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(9, 5);
            for (int y = 0; y < 5; y++) for (int x = 0; x < 9; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(1, 2));
            fly = ScriptableObject.CreateInstance<DialogueCharacter>(); fly.Set("fly", "어시 파리", DialogueSide.Right, null, Color.green);
            script = ScriptableObject.CreateInstance<ScenarioScript>();
            script.Set("test", new[]
            {
                new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate("주인공이 접속한다.") },
                new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "?" },
                new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "안녕!") },
                new ScenarioStep { type = ScenarioStepType.NameInput, line = DialogueLine.Narrate("이름을 붙여주세요"), text = "파리" },
                new ScenarioStep { type = ScenarioStepType.WaitAction, line = DialogueLine.Narrate("A키로 왼쪽"), actions = ScenarioActions.Move, direction = Vector2Int.left, keys = "A" },
                new ScenarioStep { type = ScenarioStepType.WaitFreeMove, actions = ScenarioActions.Move, seconds = 3, keys = "W A S D" },
                new ScenarioStep { type = ScenarioStepType.WaitReachCell, actions = ScenarioActions.Move, cell = new Vector2Int(3, 2) },
                new ScenarioStep { type = ScenarioStepType.SetActive, target = "scarecrow", active = true },
                new ScenarioStep { type = ScenarioStepType.WaitClickCell, cell = new Vector2Int(4, 2), seconds = 1 },
                new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "끝!") },
            });
            var go = new GameObject("Battle"); SceneManager.MoveGameObjectToScene(go, scene);
            battle = go.AddComponent<GridMapPlayer>(); battle.Configure(map, null, null); battle.Build();
            scarecrow = new GameObject("Scarecrow"); SceneManager.MoveGameObjectToScene(scarecrow, scene); scarecrow.SetActive(false);
            director = go.AddComponent<ScenarioDirector>();
            director.Configure(battle, script, new[] { new ScenarioDirector.Actor { id = "scarecrow", target = scarecrow, height = 1.5f } });
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

        private const string Enemy003 = "Assets/IBIIIS/Prefabs/Characters/Enemies/Enemy003.prefab";
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
                new ScenarioStep { type = ScenarioStepType.SetActive, target = "scarecrow", active = true },
                new ScenarioStep { type = ScenarioStepType.FreePlay, actions = ScenarioActions.Move | ScenarioActions.Wait },
            });
            director.Configure(battle, script, new[] { new ScenarioDirector.Actor { id = "scarecrow", target = scarecrow, blocksCell = true, cell = new Vector2Int(2, 2) } });
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
                    new ScenarioStep { type = ScenarioStepType.WaitClickCell, cell = new Vector2Int(4, 2), seconds = 1, sprite = sprite },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate("...") },
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
            var steps = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) evade.SetWalkable(new Vector2Int(x, y), true);
                evade.SetStart(new Vector2Int(3, 0));
                evade.PlaceEnemy(new Vector2Int(1, 5), prefab, Vector2Int.down); evade.PlaceEnemy(new Vector2Int(4, 1), prefab, Vector2Int.down);
                var go = new GameObject("Evade Battle"); SceneManager.MoveGameObjectToScene(go, scene);
                var fight = go.AddComponent<GridMapPlayer>(); fight.Configure(evade, null, null); fight.Build();
                var run = go.AddComponent<ScenarioDirector>();
                steps.Set("evade", new[]
                {
                    new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Move, direction = Vector2Int.left, keys = "A" },
                    new ScenarioStep { type = ScenarioStepType.SpawnEnemy, prefab = prefab, cell = new Vector2Int(6, 2), direction = Vector2Int.left },
                    new ScenarioStep { type = ScenarioStepType.SpawnEnemy, prefab = prefab, cell = new Vector2Int(0, 1), direction = Vector2Int.right },
                    new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Dash, direction = Vector2Int.up, keys = "Shift W" },
                    new ScenarioStep { type = ScenarioStepType.FreePlay, actions = ScenarioActions.Move | ScenarioActions.Wait | ScenarioActions.Dash, line = DialogueLine.Say(fly, "거기로 가면 위험해!") },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "끝") },
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
                        int blocked = run.BlockedDangerousMoves; var before = fight.Session.Position;
                        Assert.IsFalse(fight.TryPlayerCommand(PlayerAction.Move, dir), "패배할 이동은 막음"); Assert.AreEqual(blocked + 1, run.BlockedDangerousMoves);
                        Assert.AreEqual("거기로 가면 위험해!", run.Dialogue.Runner.Text);
                        // 경고가 떠 있는 동안에는 안전한 대기도 받지 않는다(경고를 넘기는 Space가 대기로 읽혀 턴이 넘어가던 문제).
                        Assert.IsFalse(fight.TryPlayerCommand(PlayerAction.Wait, Vector2Int.zero), "경고 중에는 턴이 넘어가지 않음");
                        Assert.IsFalse(fight.Session.IsBusy); Assert.AreEqual(before, fight.Session.Position);
                        run.Dialogue.Runner.Advance(); run.Dialogue.Runner.Advance(); run.Tick(0);
                        Assert.IsFalse(run.Dialogue.IsOpen, "경고는 넘기면 닫힘"); Assert.AreEqual(4, run.StepIndex, "경고를 닫아도 자유 진행 유지");
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
        [Test] public void AllDirectionsMustBeTriedAndPoseAndIconEmotesShow()
        {
            var pose = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0));
            var steps = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                steps.Set("dirs", new[]
                {
                    new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = ";;", sprite = pose },
                    new ScenarioStep { type = ScenarioStepType.Pose, sprite = pose, motion = PoseMotion.Hop, endMotion = PoseMotion.Sigh },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "놀랐지?") },
                    new ScenarioStep { type = ScenarioStepType.WaitAllDirections, keys = "W A S D" },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "끝") },
                });
                director.Configure(battle, steps); director.Begin();
                Assert.AreEqual(2, director.StepIndex, "감정 표시·자세는 기다리지 않음");
                Assert.AreSame(pose, battle.PoseSprite, "자세 그림으로 바뀜"); CollectionAssert.Contains(director.ActiveEmotes, ";;");
                NextLine();
                Assert.IsNull(battle.PoseSprite, "다음 대사가 끝나면 원래 그림");
                Assert.AreEqual(PoseMotion.Sigh, battle.PoseMotionPlaying, "기본 그림으로 돌아올 때 정한 움직임을 줌");
                CollectionAssert.AreEqual(new[] { "W", "A", "S", "D" }, director.Dialogue.Keys);
                Act(PlayerAction.Move, Vector2Int.right); Act(PlayerAction.Move, Vector2Int.right);
                Act(PlayerAction.Dash, Vector2Int.up, false);
                CollectionAssert.AreEqual(new[] { "W", "A", "S" }, director.Dialogue.Keys, "누른 키는 표시에서 빠짐");
                Act(PlayerAction.Move, Vector2Int.up); Act(PlayerAction.Move, Vector2Int.left);
                Assert.AreEqual(3, director.StepIndex, "한 방향이라도 남으면 기다림"); CollectionAssert.AreEqual(new[] { "S" }, director.Dialogue.Keys);
                Act(PlayerAction.Move, Vector2Int.down);
                Assert.AreEqual(4, director.StepIndex, "네 방향을 모두 눌러야 다음");
            }
            finally { Object.DestroyImmediate(steps); Object.DestroyImmediate(pose); }
        }
        [Test] public void PoseCanHoldUntilTheNextPlayerAction()
        {
            var pose = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0));
            var steps = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                steps.Set("hold", new[]
                {
                    new ScenarioStep { type = ScenarioStepType.Pose, sprite = pose, motion = PoseMotion.Shake, untilAction = true },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "피해!") },
                    new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Move, direction = Vector2Int.left, keys = "A" },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "끝") },
                });
                director.Configure(battle, steps); director.Begin();
                Assert.IsFalse(battle.DangerIdleEnabled, "대본 중에는 위험 그림을 대본이 정함");
                Assert.AreSame(pose, battle.PoseSprite);
                NextLine(); battle.UpdateMotion(10); Assert.AreSame(pose, battle.PoseSprite, "대사가 끝나고 시간이 지나도 유지");
                Act(PlayerAction.Move, Vector2Int.left);
                Assert.IsNull(battle.PoseSprite, "다음 행동을 시작하면 끝남");
                NextLine(); Assert.IsTrue(battle.DangerIdleEnabled, "대본이 끝나면 자동 위험 그림 복원");
            }
            finally { Object.DestroyImmediate(steps); Object.DestroyImmediate(pose); }
        }
        [Test] public void EndDisplayStepEndsHeldEmoteAndPoseAndMatchesTheTimeline()
        {
            var pose = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0));
            var steps = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                var list = new[]
                {
                    new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "!", untilEnd = true },
                    new ScenarioStep { type = ScenarioStepType.Pose, sprite = pose, untilEnd = true },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "하나") },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "둘") },
                    new ScenarioStep { type = ScenarioStepType.EndDisplay, display = ScenarioDisplay.Emote, target = "player" },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "셋") },
                    new ScenarioStep { type = ScenarioStepType.EndDisplay, display = ScenarioDisplay.Pose },
                    new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Say(fly, "넷") },
                };
                steps.Set("end", list);
                var emote = ScenarioTimeline.SpanOf(list, 0).Value; var held = ScenarioTimeline.SpanOf(list, 1).Value;
                Assert.AreEqual((3, 4, ScenarioSpanEnd.EndStep), (emote.End, emote.Marker, emote.EndKind), "감정 표시는 4번 표시 끝 앞의 3번 대사까지");
                Assert.AreEqual((5, 6), (held.End, held.Marker), "자세는 6번 표시 끝 앞의 5번 대사까지");
                director.Configure(battle, steps); director.Begin();
                foreach (int line in new[] { 2, 3, 5, 7 })
                {
                    Assert.AreEqual(line, director.StepIndex);
                    Assert.AreEqual(emote.Covers(line), director.ActiveEmotes.Count > 0, $"{line}번의 감정 표시");
                    Assert.AreEqual(held.Covers(line), battle.PoseSprite == pose, $"{line}번의 자세");
                    battle.UpdateMotion(30); director.Tick(30); // 시간이 지나도 표시 끝 단계 전에는 유지
                    NextLine();
                }
                Assert.IsTrue(director.Finished);
            }
            finally { Object.DestroyImmediate(steps); Object.DestroyImmediate(pose); }
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
