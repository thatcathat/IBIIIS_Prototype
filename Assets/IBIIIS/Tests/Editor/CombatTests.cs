using System;
using System.Collections.Generic;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IBIIIS.Tests
{
    public sealed class CombatTests
    {
        private GridMap map;
        private Scene scene;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7,7);
            for (int y=0;y<7;y++) for(int x=0;x<7;x++) map.SetWalkable(new Vector2Int(x,y),true);
            map.SetStart(new Vector2Int(3,0));
        }
        [TearDown] public void Cleanup() { Undo.ClearUndo(map); Object.DestroyImmediate(map); EditorSceneManager.ClosePreviewScene(scene); }
        private GameObject Enemy(int x, int y, Vector2Int facing, int speed=1, Vector2Int[] recognition=null, Vector2Int[] enhanced=null)
        {
            var go = new GameObject("Test Enemy"); SceneManager.MoveGameObjectToScene(go, scene);
            var definition = go.AddComponent<EnemyDefinition>(); var so = new SerializedObject(definition);
            so.FindProperty("moveCells").intValue = speed;
            SetOffsets(so.FindProperty("recognition"), recognition ?? Array.Empty<Vector2Int>());
            SetOffsets(so.FindProperty("recognizedAttack"), enhanced ?? new[]{Vector2Int.zero});
            so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x,y),go,facing); return go;
        }
        private static void SetActions(GameObject enemy, params EnemyActionStep[] steps)
        {
            var so=new SerializedObject(enemy.GetComponent<EnemyDefinition>()); var list=so.FindProperty("actions"); list.arraySize=steps.Length;
            for(int i=0;i<steps.Length;i++)
            {
                var element=list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("type").enumValueIndex=(int)steps[i].Type; element.FindPropertyRelative("cells").intValue=steps[i].Cells; element.FindPropertyRelative("turn").enumValueIndex=(int)steps[i].Turn;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetOffsets(SerializedProperty p, Vector2Int[] values)
        { p.arraySize=values.Length; for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).vector2IntValue=values[i]; }
        private static void WaitRound(GridSession s) { Assert.True(s.TryAct(PlayerAction.Wait,Vector2Int.zero)); s.Advance(2); }
        [Test] public void PlayerAndEnemiesMoveConcurrentlyAndWaitForSlowerSide()
        {
            Enemy(0,5,Vector2Int.right); var s=new GridSession(map);
            Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.right,.5f,1)); s.Advance(.25f);
            Assert.True(s.IsMoving); Assert.True(s.IsEnemiesMoving);
            Assert.That(s.Progress,Is.EqualTo(.5f)); Assert.That(s.EnemyProgress,Is.EqualTo(.25f));
            s.Advance(.25f); Assert.AreEqual(new Vector2Int(4,0),s.Position); Assert.False(s.IsMoving); Assert.True(s.IsEnemiesMoving);
            Assert.False(s.TryMove(Vector2Int.left)); s.Advance(.5f);
            Assert.AreEqual(BattlePhase.Waiting,s.Phase); Assert.AreEqual(new Vector2Int(1,5),s.Enemies[0].Position);
            s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.right,1,.25f)); s.Advance(.25f);
            Assert.True(s.IsMoving); Assert.False(s.IsEnemiesMoving); Assert.True(s.IsBusy); Assert.False(s.CanMove(Vector2Int.left));
            s.Advance(.75f); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
        }
        [Test] public void ConcurrentFramePartitionDoesNotChangeCombatResult()
        {
            Enemy(0,3,Vector2Int.right,2); Enemy(3,3,Vector2Int.left);
            var a=new GridSession(map); var b=new GridSession(map);
            a.TryAct(PlayerAction.Move,Vector2Int.up,.75f,.25f); b.TryAct(PlayerAction.Move,Vector2Int.up,.75f,.25f);
            a.Advance(1); for(int i=0;i<8;i++) b.Advance(.125f);
            Assert.AreEqual(BattlePhase.Won,a.Phase); Assert.AreEqual(a.Phase,b.Phase); Assert.AreEqual(a.Position,b.Position); Assert.AreEqual(a.AliveCount,b.AliveCount);
        }
        [Test] public void SameCellKillsAllButSwappingCellsDoesNot()
        {
            Enemy(1,3,Vector2Int.right); Enemy(3,3,Vector2Int.left); Enemy(2,4,Vector2Int.down);
            var s=new GridSession(map); WaitRound(s); Assert.AreEqual(0,s.AliveCount); Assert.AreEqual(BattlePhase.Won,s.Phase);
            map.RemoveEnemy(new Vector2Int(3,3)); map.RemoveEnemy(new Vector2Int(2,4)); Enemy(2,3,Vector2Int.left);
            s=new GridSession(map); WaitRound(s); Assert.AreEqual(2,s.AliveCount); Assert.AreEqual(new Vector2Int(2,3),s.Enemies[0].Position);
        }
        [Test] public void FastEnemyCollidesWithStationarySlowEnemyOnSecondSubstep()
        {
            Enemy(0,3,Vector2Int.right,2); Enemy(3,3,Vector2Int.left);
            var s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Wait,Vector2Int.zero)); s.Advance(.25f);
            Assert.AreEqual(2,s.AliveCount); Assert.True(s.IsEnemiesMoving);
            s.Advance(.25f); Assert.AreEqual(0,s.AliveCount); Assert.AreEqual(BattlePhase.Won,s.Phase);
        }
        [Test] public void SurvivingEnemyPathHitsButDeadEnemyPathDoesNot()
        {
            map.SetStart(new Vector2Int(1,3)); Enemy(0,3,Vector2Int.right,2);
            var s=new GridSession(map); WaitRound(s); Assert.AreEqual(BattlePhase.Lost,s.Phase); Assert.False(s.TryMove(Vector2Int.up));
            Enemy(2,2,Vector2Int.up); s=new GridSession(map); WaitRound(s); Assert.AreEqual(BattlePhase.Won,s.Phase);
        }
        [Test] public void WallReversesWithoutExtraActionAndTrappedEnemyStays()
        {
            Enemy(0,3,Vector2Int.left); var s=new GridSession(map); WaitRound(s);
            Assert.AreEqual(new Vector2Int(1,3),s.Enemies[0].Position); Assert.AreEqual(Vector2Int.right,s.Enemies[0].Direction);
            map.SetWalkable(new Vector2Int(1,3),false); s=new GridSession(map); WaitRound(s); Assert.AreEqual(new Vector2Int(0,3),s.Enemies[0].Position);
        }
        [Test] public void RecognitionBeforeMoveAimsAndAfterMoveOnlyExpandsAttack()
        {
            map.SetStart(new Vector2Int(1,4)); Enemy(1,3,Vector2Int.right,1,new[]{Vector2Int.left});
            var s=new GridSession(map); WaitRound(s); Assert.AreEqual(Vector2Int.up,s.Enemies[0].Direction); Assert.AreEqual(BattlePhase.Lost,s.Phase);
            map.RemoveEnemy(new Vector2Int(1,3)); map.SetStart(new Vector2Int(2,4));
            Enemy(1,3,Vector2Int.right,1,new[]{Vector2Int.left},new[]{Vector2Int.left});
            s=new GridSession(map); WaitRound(s); Assert.AreEqual(Vector2Int.right,s.Enemies[0].Direction); Assert.True(s.Enemies[0].Recognized); Assert.AreEqual(BattlePhase.Lost,s.Phase);
        }
        [Test] public void DashChecksIntermediateCellAndEvasionLocksForOneNormalAction()
        {
            Enemy(4,0,Vector2Int.up); var s=new GridSession(map); Assert.False(s.CanAct(PlayerAction.Dash,Vector2Int.right));
            Assert.False(s.CanMove(Vector2Int.right)); map.RemoveEnemy(new Vector2Int(4,0));
            s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Dash,Vector2Int.up)); s.Advance(1);
            Assert.AreEqual(new Vector2Int(3,2),s.Position); Assert.True(s.EvasionLocked); Assert.False(s.CanAct(PlayerAction.Roll,Vector2Int.one));
            WaitRound(s); Assert.False(s.EvasionLocked); Assert.True(s.TryAct(PlayerAction.Roll,Vector2Int.one)); s.Advance(1); Assert.AreEqual(new Vector2Int(4,3),s.Position);
        }
        [Test] public void UndoRestoresFullBattleStateAndAllowsAnotherChoice()
        {
            map.SetStart(new Vector2Int(1,3)); Enemy(0,3,Vector2Int.right,2); Enemy(5,5,Vector2Int.left);
            var s=new GridSession(map); Assert.False(s.CanUndo); Assert.False(s.TryUndo());
            WaitRound(s); Assert.AreEqual(BattlePhase.Lost,s.Phase); Assert.AreEqual(1,s.UndoCount); Assert.True(s.CanUndo);
            Assert.True(s.TryUndo()); Assert.AreEqual(0,s.UndoCount);
            var fresh=new GridSession(map);
            Assert.AreEqual(BattlePhase.Waiting,s.Phase); Assert.AreEqual(fresh.Position,s.Position); Assert.AreEqual(fresh.Destination,s.Destination); Assert.AreEqual(0,new List<Vector2Int>(s.AttackCells).Count);
            for(int i=0;i<2;i++){ Assert.AreEqual(fresh.Enemies[i].Position,s.Enemies[i].Position); Assert.AreEqual(fresh.Enemies[i].Direction,s.Enemies[i].Direction); Assert.True(s.Enemies[i].Alive); Assert.False(s.Enemies[i].Recognized); }
            Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.up)); s.Advance(2); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
        }
        [Test] public void UndoStepsBackThroughEvasionLockAndKilledEnemiesAndIsBlockedWhileMoving()
        {
            Enemy(1,3,Vector2Int.right); Enemy(3,3,Vector2Int.left);
            var s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Dash,Vector2Int.up)); Assert.False(s.CanUndo); Assert.False(s.TryUndo()); s.Advance(1);
            Assert.True(s.EvasionLocked); Assert.AreEqual(0,s.AliveCount); Assert.AreEqual(BattlePhase.Won,s.Phase);
            Assert.True(s.TryUndo()); Assert.False(s.EvasionLocked); Assert.AreEqual(2,s.AliveCount); Assert.AreEqual(new Vector2Int(3,0),s.Position); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
            Assert.AreEqual(0,s.UndoCount); WaitRound(s); Assert.AreEqual(BattlePhase.Won,s.Phase); Assert.AreEqual(1,s.UndoCount);
            Assert.True(s.TryUndo()); Assert.False(s.TryUndo()); Assert.True(s.CanAct(PlayerAction.Dash,Vector2Int.up));
        }
        [Test] public void RangeCellsUseLiveRecognitionAndSkipCellsOutsideTheMap()
        {
            map.SetStart(new Vector2Int(3,3)); Enemy(3,2,Vector2Int.up,1,new[]{Vector2Int.up},new[]{Vector2Int.zero,Vector2Int.up,Vector2Int.up*2});
            var s=new GridSession(map); var e=s.Enemies[0];
            Assert.That(new List<Vector2Int>(s.RangeCells(e,EnemyRange.Recognition)),Is.EqualTo(new[]{new Vector2Int(3,3)}));
            Assert.That(new List<Vector2Int>(s.RangeCells(e,EnemyRange.Attack)),Is.EqualTo(new[]{new Vector2Int(3,2),new Vector2Int(3,3),new Vector2Int(3,4)}));
            map.SetStart(new Vector2Int(0,0)); s=new GridSession(map);
            Assert.That(new List<Vector2Int>(s.RangeCells(s.Enemies[0],EnemyRange.Attack)),Is.EqualTo(new[]{new Vector2Int(3,2)}));
            map.RemoveEnemy(new Vector2Int(3,2)); map.SetStart(new Vector2Int(3,0)); Enemy(3,6,Vector2Int.up,1,new[]{Vector2Int.up},new[]{Vector2Int.up});
            s=new GridSession(map); Assert.That(new List<Vector2Int>(s.RangeCells(s.Enemies[0],EnemyRange.Recognition)),Is.Empty);
        }
        [Test] public void PlayerViewUndoRestoresAnchorEnemiesHintsAndRangeOverlay()
        {
            Enemy(3,3,Vector2Int.down,1,new[]{Vector2Int.up});
            var go=new GameObject("Player"); SceneManager.MoveGameObjectToScene(go,scene); var player=go.AddComponent<GridMapPlayer>(); player.Configure(map,null,null);
            try
            {
                player.Build(); var ranges=player.Generated.Find("Enemy Ranges"); Assert.NotNull(ranges); Assert.True(ranges.gameObject.activeSelf);
                int Active(string n){ int c=0; foreach(Transform t in ranges) if(t.gameObject.activeSelf&&t.name==n) c++; return c; }
                Assert.AreEqual(1,Active("Attack Mark")); Assert.AreEqual(1,Active("Recognition Mark"));
                Assert.True(player.TryBeginMove(Vector2Int.right)); Assert.False(ranges.gameObject.activeSelf); Assert.False(player.TryUndo());
                player.AdvanceMovement(2); Assert.AreEqual(new Vector2Int(4,0),player.Session.Position); Assert.True(ranges.gameObject.activeSelf);
                Assert.True(player.TryUndo()); Assert.AreEqual(new Vector2Int(3,0),player.Session.Position);
                Assert.That(player.Generated.Find("Player Logic Anchor").localPosition.x,Is.EqualTo(3).Within(.001f));
                Assert.That(player.Generated.Find("Enemies").GetChild(0).localPosition.z,Is.EqualTo(3).Within(.001f));
                Assert.True(player.Generated.Find("Movement Hints/Adjacent (1, 0)").gameObject.activeSelf);
                player.ToggleEnemyRanges(); Assert.False(ranges.gameObject.activeSelf); player.ToggleEnemyRanges(); Assert.True(ranges.gameObject.activeSelf);
                Assert.False(player.TryUndo());
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void ComposedActionsRunTurnBetweenMovesAndTurnOnlyEnemyStaysPut()
        {
            Assert.AreEqual(new Vector2Int(1,0),EnemyActionStep.Rotate(Vector2Int.up,EnemyTurn.Right)); Assert.AreEqual(new Vector2Int(-1,0),EnemyActionStep.Rotate(Vector2Int.up,EnemyTurn.Left)); Assert.AreEqual(Vector2Int.down,EnemyActionStep.Rotate(Vector2Int.up,EnemyTurn.Around));
            var go=Enemy(0,3,Vector2Int.up); SetActions(go,EnemyActionStep.Move(1),EnemyActionStep.TurnBy(EnemyTurn.Right),EnemyActionStep.Move(1));
            var s=new GridSession(map); Assert.AreEqual(2,s.Enemies[0].MoveCells); Assert.True(s.TryAct(PlayerAction.Wait,Vector2Int.zero)); s.Advance(.25f);
            Assert.AreEqual(new Vector2Int(0,4),s.Enemies[0].Position); Assert.AreEqual(Vector2Int.right,s.Enemies[0].Direction); Assert.True(s.IsEnemiesMoving);
            s.Advance(.25f); Assert.AreEqual(new Vector2Int(1,4),s.Enemies[0].Position); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
            SetActions(go,EnemyActionStep.TurnBy(EnemyTurn.Around)); s=new GridSession(map); WaitRound(s);
            Assert.AreEqual(new Vector2Int(0,3),s.Enemies[0].Position); Assert.AreEqual(Vector2Int.down,s.Enemies[0].Direction); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
        }
        [Test] public void AimActionUsesEnemyPositionAtItsOwnTurnInTheSequence()
        {
            map.SetStart(new Vector2Int(2,4)); var rec=new[]{Vector2Int.left}; var go=Enemy(1,3,Vector2Int.right,1,rec);
            SetActions(go,EnemyActionStep.Move(1),EnemyActionStep.Aim()); var s=new GridSession(map); WaitRound(s);
            Assert.AreEqual(new Vector2Int(2,3),s.Enemies[0].Position); Assert.AreEqual(Vector2Int.up,s.Enemies[0].Direction);
            SetActions(go,EnemyActionStep.Aim(),EnemyActionStep.Move(1)); s=new GridSession(map); WaitRound(s);
            Assert.AreEqual(new Vector2Int(2,3),s.Enemies[0].Position); Assert.AreEqual(Vector2Int.right,s.Enemies[0].Direction);
        }
        [Test] public void EnemiesAimOnlyAtThePlayerPositionBeforeTheAction()
        {
            // 인식 범위: 적의 위쪽 한 칸(3,4). 플레이어가 그 칸에서 떠나는 행동이면 인식하고, 그 칸으로 들어오는 행동이면 인식하지 못한다.
            var rec=new[]{Vector2Int.right}; map.SetStart(new Vector2Int(3,4)); Enemy(3,3,Vector2Int.left,1,rec);
            var s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.right)); s.Advance(2);
            Assert.AreEqual(Vector2Int.up,s.Enemies[0].Direction); Assert.AreEqual(new Vector2Int(3,4),s.Enemies[0].Position); Assert.AreEqual(BattlePhase.Waiting,s.Phase);
            map.RemoveEnemy(new Vector2Int(3,3)); map.SetStart(new Vector2Int(2,4)); Enemy(3,3,Vector2Int.left,1,rec);
            s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.right)); s.Advance(2);
            Assert.AreEqual(new Vector2Int(3,4),s.Position); Assert.AreEqual(Vector2Int.left,s.Enemies[0].Direction); Assert.AreEqual(new Vector2Int(2,3),s.Enemies[0].Position);
        }
        [Test] public void MidSequenceAimStillUsesTheOriginEvenIfThePlayerAlreadyArrived()
        {
            map.SetStart(new Vector2Int(3,4)); var go=Enemy(3,2,Vector2Int.up,1,new[]{Vector2Int.down}); SetActions(go,EnemyActionStep.Move(1),EnemyActionStep.TurnBy(EnemyTurn.Around),EnemyActionStep.Aim());
            var s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.right,.05f,.25f)); s.Advance(1);
            Assert.AreEqual(new Vector2Int(4,4),s.Position); Assert.AreEqual(Vector2Int.up,s.Enemies[0].Direction);
        }
        private static GridSession Replay(GridMap map, System.Collections.Generic.IEnumerable<SolverMove> path)
        {
            var s=new GridSession(map); foreach(var m in path){ Assert.True(s.TryAct(m.Action,m.Direction)); s.Advance(2); } return s;
        }
        // 분석기 상태 문자열: [플레이어 x+1, y+1, 회피기 잠금] + 적마다 [x+1, y+1, 방향x+1, 방향y+1, 생존]
        private static string Core(Vector2Int player, params (Vector2Int pos, Vector2Int dir, bool alive)[] enemies)
        {
            var data = new List<char> { (char)(player.x + 1), (char)(player.y + 1), (char)0 };
            foreach (var (pos, dir, alive) in enemies) data.AddRange(new[] { (char)(pos.x + 1), (char)(pos.y + 1), (char)(dir.x + 1), (char)(dir.y + 1), (char)(alive ? 1 : 0) });
            return new string(data.ToArray());
        }
        [Test] public void SolverStateIgnoresWhereDeadEnemiesFell()
        {
            Enemy(1,3,Vector2Int.right); Enemy(5,3,Vector2Int.left); Enemy(0,6,Vector2Int.right);
            var s = new GridSession(map);
            var alive = (new Vector2Int(0,6), Vector2Int.right, true);
            s.LoadCore(Core(new Vector2Int(3,0), (new Vector2Int(2,3), Vector2Int.right, false), (new Vector2Int(2,3), Vector2Int.left, false), alive)); var a = s.SaveCore();
            s.LoadCore(Core(new Vector2Int(3,0), (new Vector2Int(4,5), Vector2Int.up, false), (new Vector2Int(4,5), Vector2Int.down, false), alive)); var b = s.SaveCore();
            Assert.AreEqual(a, b, "죽은 적이 어디서 어느 방향으로 사라졌는지는 같은 상태");
            s.LoadCore(Core(new Vector2Int(3,0), (new Vector2Int(2,3), Vector2Int.right, false), (new Vector2Int(2,3), Vector2Int.left, false), (new Vector2Int(1,6), Vector2Int.right, true)));
            Assert.AreNotEqual(a, s.SaveCore(), "살아 있는 적의 위치는 구분");
            s.LoadCore(Core(new Vector2Int(3,0), (new Vector2Int(1,3), Vector2Int.right, true), (new Vector2Int(2,3), Vector2Int.left, false), alive));
            Assert.AreNotEqual(a, s.SaveCore(), "생존 여부는 구분");
            Assert.True(s.TryAct(PlayerAction.Wait, Vector2Int.zero)); s.Advance(2);
            Assert.AreNotEqual(BattlePhase.Lost, s.Phase, "죽은 적의 (-1,-1) 위치는 판정에 쓰이지 않음");
        }
        [Test] public void SolverFindsShortestWinAndItsPathReplaysToVictory()
        {
            Enemy(1,3,Vector2Int.right); Enemy(5,3,Vector2Int.left);
            var r=MapSolver.Analyze(map); Assert.True(r.Completed); Assert.True(r.Solvable); Assert.AreEqual(2,r.ShortestWin); Assert.AreEqual(2,r.WinPath.Count);
            Assert.AreEqual(BattlePhase.Won,Replay(map,r.WinPath).Phase); Assert.AreEqual(0,r.DeadStates); StringAssert.Contains("최단 2행동",r.ToReport());
        }
        [Test] public void ReplayRecordsEachStepOfTheSolverPathAndReportsAStalePath()
        {
            Enemy(1,3,Vector2Int.right); Enemy(5,3,Vector2Int.left);
            var r=MapSolver.Analyze(map); var replay=MapReplay.Build(map,"win",r.WinPath);
            Assert.Null(replay.Problem); Assert.AreEqual(r.WinPath.Count+1,replay.Frames.Count);
            Assert.AreEqual(map.Start,replay.Frames[0].Player); Assert.AreEqual(2,replay.Frames[0].AliveCount); Assert.AreEqual(BattlePhase.Waiting,replay.Frames[0].Phase);
            Assert.AreEqual(new Vector2Int(2,3),replay.Frames[1].EnemyPositions[0]); Assert.True(replay.Frames[1].EnemyAlive[0]);
            var last=replay.Frames[replay.Frames.Count-1]; Assert.AreEqual(BattlePhase.Won,last.Phase); Assert.False(last.EnemyAlive[0]); Assert.False(last.EnemyAlive[1]);
            replay.Index=99; Assert.AreSame(last,replay.Current);
            var blocked=MapReplay.Build(map,"stale",new[]{new SolverMove(PlayerAction.Move,Vector2Int.down)});
            Assert.NotNull(blocked.Problem); Assert.AreEqual(1,blocked.Frames.Count);
        }
        [Test] public void SolverReportsUnwinnableMapAndEveryStateAsDead()
        {
            Enemy(1,3,Vector2Int.right); var r=MapSolver.Analyze(map);
            Assert.True(r.Completed); Assert.False(r.Solvable); Assert.AreEqual(-1,r.ShortestWin); Assert.Null(r.WinPath); Assert.Greater(r.States,1); Assert.AreEqual(r.States,r.DeadStates);
            StringAssert.Contains("클리어: 불가능",r.ToReport());
        }
        [Test] public void SolverFindsEarliestLossAndItsPathReplaysToDefeat()
        {
            map.SetStart(new Vector2Int(1,3)); Enemy(0,3,Vector2Int.right,2);
            var r=MapSolver.Analyze(map); Assert.AreEqual(1,r.EarliestLoss); Assert.AreEqual(PlayerAction.Wait,r.LossPath[0].Action); Assert.AreEqual(BattlePhase.Lost,Replay(map,r.LossPath).Phase);
        }
        [Test] public void SolverStopsAtLimitsAndCancelAndRejectsMapsItCannotRun()
        {
            Enemy(1,3,Vector2Int.right);
            var limited=MapSolver.Analyze(map,new MapAnalysisOptions{MaxStates=5}); Assert.False(limited.Completed); Assert.AreEqual(-1,limited.DeadStates); StringAssert.Contains("불완전",limited.ToReport());
            var shallow=MapSolver.Analyze(map,new MapAnalysisOptions{MaxDepth=1}); Assert.False(shallow.Completed); Assert.LessOrEqual(shallow.MaxDepthReached,1);
            var cancelled=MapSolver.Analyze(map,new MapAnalysisOptions{Progress=(done,found)=>false}); Assert.True(cancelled.Cancelled); Assert.False(cancelled.Completed);
            map.RemoveEnemy(new Vector2Int(1,3)); var none=MapSolver.Analyze(map); Assert.AreEqual(0,none.States); Assert.That(none.Notes,Is.Not.Empty);
            var broken=ScriptableObject.CreateInstance<GridMap>();
            try { broken.Resize(3,3); var bad=MapSolver.Analyze(broken); Assert.That(bad.Errors,Is.Not.Empty); Assert.AreEqual(0,bad.States); } finally { Object.DestroyImmediate(broken); }
        }
        [Test] public void CollisionsAreRecordedWithCellStepAndMembersAndClearedOnUndo()
        {
            Enemy(1,3,Vector2Int.right); Enemy(3,3,Vector2Int.left); Enemy(2,4,Vector2Int.down);
            var s=new GridSession(map); WaitRound(s);
            Assert.AreEqual(1,s.Collisions.Count); var c=s.Collisions[0];
            Assert.AreEqual(new Vector2Int(2,3),c.Cell); Assert.AreEqual(1,c.Step); CollectionAssert.AreEquivalent(new[]{0,1,2},c.Enemies);
            Assert.True(s.TryUndo()); Assert.AreEqual(0,s.Collisions.Count);
            map.RemoveEnemy(new Vector2Int(3,3)); map.RemoveEnemy(new Vector2Int(2,4)); Enemy(2,3,Vector2Int.left);
            s=new GridSession(map); WaitRound(s); Assert.AreEqual(0,s.Collisions.Count,"자리 맞바꾸기는 충돌 아님");
        }
        [Test] public void StopAfterCollisionReturnsLeftoverTimeWithoutChangingTheResult()
        {
            Enemy(0,3,Vector2Int.right,2); Enemy(3,3,Vector2Int.left);
            var a=new GridSession(map); Assert.True(a.TryAct(PlayerAction.Wait,Vector2Int.zero,.25f,.25f));
            Assert.That(a.Advance(1f,true),Is.EqualTo(.5f).Within(1e-4f));
            Assert.AreEqual(1,a.Collisions.Count); Assert.AreEqual(2,a.Collisions[0].Step); Assert.AreEqual(new Vector2Int(2,3),a.Collisions[0].Cell);
            var b=new GridSession(map); Assert.True(b.TryAct(PlayerAction.Wait,Vector2Int.zero,.25f,.25f)); b.Advance(1f);
            Assert.AreEqual(BattlePhase.Won,a.Phase); Assert.AreEqual(b.Phase,a.Phase); Assert.AreEqual(b.AliveCount,a.AliveCount);
        }
        [Test] public void AttackersAreRecordedForAttackOrPathHitsAndClearedOnUndo()
        {
            // 공격으로 패배: 적이 (3,3)으로 내려온 뒤 앞 1칸(3,2)을 공격, 플레이어는 (3,2)로 이동
            map.SetStart(new Vector2Int(3,1)); var go=Enemy(3,4,Vector2Int.down); Enemy(6,6,Vector2Int.left);
            var so=new SerializedObject(go.GetComponent<EnemyDefinition>()); SetOffsets(so.FindProperty("attack"),new[]{Vector2Int.up}); so.ApplyModifiedPropertiesWithoutUndo();
            var s=new GridSession(map); Assert.True(s.TryAct(PlayerAction.Move,Vector2Int.up)); s.Advance(2);
            Assert.AreEqual(BattlePhase.Lost,s.Phase); CollectionAssert.AreEqual(new[]{0},s.Attackers);
            Assert.True(s.TryUndo()); Assert.IsEmpty(s.Attackers);
            // 경로로 패배: 두 칸 적이 플레이어 칸을 지나감. 패배가 아니면 비어 있다.
            map.RemoveEnemy(new Vector2Int(3,4)); map.RemoveEnemy(new Vector2Int(6,6)); map.SetStart(new Vector2Int(1,3)); Enemy(0,3,Vector2Int.right,2);
            s=new GridSession(map); WaitRound(s); Assert.AreEqual(BattlePhase.Lost,s.Phase); CollectionAssert.AreEqual(new[]{0},s.Attackers);
            map.SetStart(new Vector2Int(5,0)); s=new GridSession(map); WaitRound(s); Assert.AreEqual(BattlePhase.Waiting,s.Phase); Assert.IsEmpty(s.Attackers);
        }
        [Test] public void LegacyMoveCellsAndEquivalentActionsPlayIdentically()
        {
            var go=Enemy(0,3,Vector2Int.right,2,new[]{Vector2Int.up,Vector2Int.up*2,Vector2Int.left,Vector2Int.right}); Enemy(6,6,Vector2Int.down);
            var legacy=new GridSession(map); Assert.True(go.GetComponent<EnemyDefinition>().UsesLegacyActions);
            SetActions(go,EnemyActionStep.Aim(),EnemyActionStep.Move(2)); Assert.False(go.GetComponent<EnemyDefinition>().UsesLegacyActions);
            var explicitActions=new GridSession(map);
            var script=new[]{(PlayerAction.Wait,Vector2Int.zero),(PlayerAction.Move,Vector2Int.up),(PlayerAction.Move,Vector2Int.right),(PlayerAction.Wait,Vector2Int.zero),(PlayerAction.Dash,Vector2Int.up),(PlayerAction.Wait,Vector2Int.zero)};
            foreach(var (action,direction) in script)
            {
                Assert.AreEqual(legacy.TryAct(action,direction),explicitActions.TryAct(action,direction)); legacy.Advance(2); explicitActions.Advance(2);
                Assert.AreEqual(legacy.Phase,explicitActions.Phase); Assert.AreEqual(legacy.Position,explicitActions.Position);
                for(int i=0;i<2;i++){ Assert.AreEqual(legacy.Enemies[i].Position,explicitActions.Enemies[i].Position); Assert.AreEqual(legacy.Enemies[i].Direction,explicitActions.Enemies[i].Direction); Assert.AreEqual(legacy.Enemies[i].Alive,explicitActions.Enemies[i].Alive); }
            }
        }
        [Test] public void InvalidMoveActionIsRejectedAndLegacyConversionOnlyFillsAnEmptyList()
        {
            var go=Enemy(5,5,Vector2Int.up); var definition=go.GetComponent<EnemyDefinition>();
            SetActions(go,EnemyActionStep.Move(1)); var so=new SerializedObject(definition); so.FindProperty("actions").GetArrayElementAtIndex(0).FindPropertyRelative("cells").intValue=3; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.False(definition.IsValid); Assert.Throws<ArgumentException>(()=>map.PlaceEnemy(new Vector2Int(4,5),go,Vector2Int.up));
            var legacy=Enemy(2,5,Vector2Int.up,2).GetComponent<EnemyDefinition>(); Assert.True(legacy.UsesLegacyActions);
            Assert.True(EnemyDefinitionEditor.ConvertLegacy(new SerializedObject(legacy))); Assert.False(legacy.UsesLegacyActions);
            var converted=legacy.Actions; Assert.AreEqual(2,converted.Length); Assert.AreEqual(EnemyActionType.AimAtPlayer,converted[0].Type); Assert.AreEqual(2,converted[1].Cells);
            Assert.False(EnemyDefinitionEditor.ConvertLegacy(new SerializedObject(legacy))); Assert.AreEqual(2,legacy.Actions.Length);
        }
        [Test] public void PlacementUndoEraseResizeAndSnapshotKeepAssetIndependent()
        {
            var prefab=Enemy(1,3,Vector2Int.right); Assert.Throws<ArgumentException>(()=>map.PlaceEnemy(map.Start,prefab,Vector2Int.up));
            Assert.Throws<ArgumentException>(()=>map.SetStart(new Vector2Int(1,3)));
            Undo.RegisterCompleteObjectUndo(map,"Erase enemy"); map.SetWalkable(new Vector2Int(1,3),false);
            Undo.FlushUndoRecordObjects(); Assert.IsNull(map.EnemyAt(new Vector2Int(1,3))); Undo.PerformUndo(); Assert.NotNull(map.EnemyAt(new Vector2Int(1,3)));
            var before=EditorJsonUtility.ToJson(map); var first=new GridSession(map); var second=new GridSession(map); WaitRound(first);
            Assert.AreEqual(new Vector2Int(1,3),second.Enemies[0].Position); Assert.AreEqual(before,EditorJsonUtility.ToJson(map));
            map.Resize(2,2); Assert.That(map.Enemies,Is.Empty);
        }
        [Test] public void PrefabPlacementRoundTripAndScenePreviewUseSelectedAsset()
        {
            var source=Enemy(1,3,Vector2Int.right);
            string prefabPath=AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/CombatEnemy.prefab");
            string mapPath=AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/CombatMap.asset");
            GridMap saved=null;
            try
            {
                var prefab=PrefabUtility.SaveAsPrefabAsset(source,prefabPath); map.PlaceEnemy(new Vector2Int(1,3),prefab,Vector2Int.left);
                saved=Object.Instantiate(map); AssetDatabase.CreateAsset(saved,mapPath); AssetDatabase.SaveAssetIfDirty(saved); AssetDatabase.ImportAsset(mapPath,ImportAssetOptions.ForceUpdate);
                saved=AssetDatabase.LoadAssetAtPath<GridMap>(mapPath); Assert.AreSame(prefab,saved.Enemies[0].Prefab); Assert.AreEqual(Vector2Int.left,saved.Enemies[0].Direction);
                var go=new GameObject("Player"); SceneManager.MoveGameObjectToScene(go,scene); var player=go.AddComponent<GridMapPlayer>(); player.Configure(saved,null,null); player.RefreshPreview();
                Assert.AreEqual(1,player.Generated.Find("Enemies").childCount); Assert.IsNull(player.Session);
                player.Build(); Assert.True(player.TryBeginMove(Vector2Int.right)); player.AdvanceMovement(.125f); Assert.True(player.Session.IsEnemiesMoving); Assert.True(player.Session.IsMoving);
                foreach(Transform hint in player.Generated.Find("Movement Hints")) Assert.AreEqual(hint.name == "Destination",hint.gameObject.activeSelf);
                Assert.That(player.Generated.Find("Enemies").GetChild(0).localPosition.x,Is.EqualTo(.5f).Within(.001f));
                Assert.That(player.Generated.Find("Player Logic Anchor").localPosition.x,Is.EqualTo(3.5f).Within(.001f));
                player.AdvanceMovement(.25f); Assert.AreEqual(BattlePhase.Waiting,player.Session.Phase);
                Object.DestroyImmediate(go);
            }
            finally { AssetDatabase.DeleteAsset(mapPath); AssetDatabase.DeleteAsset(prefabPath); }
        }
    }
}