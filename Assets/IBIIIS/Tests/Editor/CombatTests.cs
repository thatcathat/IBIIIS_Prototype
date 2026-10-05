using System;
using System.Collections.Generic;
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