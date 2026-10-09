using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    public sealed class GridMapTests
    {
        private GridMap map;
        private TileDefinition floor, wall;
        [SetUp] public void SetUp()
        {
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(3, 3);
            floor = ScriptableObject.CreateInstance<TileDefinition>(); floor.Initialize("floor", "Floor", true, Color.green);
            wall = ScriptableObject.CreateInstance<TileDefinition>(); wall.Initialize("wall", "Wall", false, Color.gray);
            map.AddTile(floor); map.AddTile(wall);
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) map.SetTile(new Vector2Int(x, y), floor);
            map.SetStart(Vector2Int.zero);
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(map); UnityEngine.Object.DestroyImmediate(floor); UnityEngine.Object.DestroyImmediate(wall); }
        [Test] public void TimedMovementRejectsExtraInputAndCommitsOnlyOnArrival()
        {
            var session = new GridSession(map);
            Assert.True(session.CanMove(Vector2Int.right)); Assert.True(session.TryMove(Vector2Int.right, .5f));
            Assert.AreEqual(Vector2Int.zero, session.Position); Assert.AreEqual(Vector2Int.right, session.Destination);
            Assert.False(session.TryMove(Vector2Int.up, .5f)); Assert.False(session.CanMove(Vector2Int.up));
            session.Advance(.2f); Assert.That(session.Progress, Is.EqualTo(.4f).Within(.00001f)); Assert.AreEqual(Vector2Int.zero, session.Position);
            session.Advance(.4f); Assert.False(session.IsMoving); Assert.AreEqual(Vector2Int.right, session.Position);
            session.Advance(1); Assert.AreEqual(Vector2Int.right, session.Position);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.TryMove(Vector2Int.up, 0));
            Assert.False(session.IsMoving);
        }
        [Test] public void WorldTimeRestoresPreviousScaleWhenOwnersAreReleased()
        {
            float previous = Time.timeScale; var first = new object(); var second = new object();
            try
            {
                Time.timeScale = .75f;
                MovementWorldTime.Register(first); Assert.AreEqual(0, Time.timeScale);
                MovementWorldTime.SetMoving(first, true); Assert.AreEqual(1, Time.timeScale);
                MovementWorldTime.Register(second); Assert.AreEqual(1, Time.timeScale);
                MovementWorldTime.SetMoving(first, false); Assert.AreEqual(0, Time.timeScale);
                MovementWorldTime.SetMoving(second, true); MovementWorldTime.Unregister(first); Assert.AreEqual(1, Time.timeScale);
                MovementWorldTime.Unregister(second); Assert.AreEqual(.75f, Time.timeScale);
            }
            finally { MovementWorldTime.Unregister(first); MovementWorldTime.Unregister(second); Time.timeScale = previous; }
        }
        [Test] public void FloorPaintingRejectsObstaclesAndDuplicateIdsAndPreservesStartOnRepaint()
        {
            var stone = ScriptableObject.CreateInstance<TileDefinition>(); stone.Initialize("stone", "Stone", true, Color.gray);
            try
            {
                Assert.Throws<ArgumentException>(() => map.AddFloor(wall));
                Assert.Throws<ArgumentException>(() => map.PaintFloor(Vector2Int.zero, wall));
                Assert.Throws<ArgumentException>(() => map.PaintFloor(Vector2Int.zero, stone));
                map.AddFloor(stone);
                Undo.RegisterCompleteObjectUndo(map, "Repaint floor"); map.PaintFloor(Vector2Int.zero, stone);
                Assert.True(map.HasStart); Assert.AreEqual(Vector2Int.zero, map.Start); Assert.AreSame(stone, map.GetTile(Vector2Int.zero));
                Assert.True(new GridSession(map).TryMove(Vector2Int.right));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.AreSame(floor, map.GetTile(Vector2Int.zero));
                Undo.PerformRedo(); Assert.AreSame(stone, map.GetTile(Vector2Int.zero));
                map.SetWalkable(Vector2Int.zero, false); Assert.False(map.HasStart); Assert.False(map.IsWalkable(Vector2Int.zero));
                stone.Initialize(floor.Id, "Duplicate", true, Color.white); Assert.Throws<ArgumentException>(() => map.AddFloor(stone));
            }
            finally { Undo.ClearUndo(map); UnityEngine.Object.DestroyImmediate(stone); }
        }
        [Test] public void NewMovementCellsNeedNoPaletteAndStartEraseUndoRestoresBoth()
        {
            var fresh = ScriptableObject.CreateInstance<GridMap>();
            try
            {
                fresh.SetWalkable(Vector2Int.zero, true); fresh.SetWalkable(Vector2Int.right, true); fresh.SetStart(Vector2Int.zero);
                Assert.That(fresh.Palette, Is.Empty); Assert.That(fresh.ValidateMap(), Is.Empty);
                Assert.True(new GridSession(fresh).TryMove(Vector2Int.right));
                Undo.RegisterCompleteObjectUndo(fresh, "Erase start"); fresh.SetWalkable(Vector2Int.zero, false);
                Assert.False(fresh.HasStart); Assert.Throws<ArgumentException>(() => new GridSession(fresh));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.True(fresh.HasStart); Assert.True(fresh.IsWalkable(Vector2Int.zero));
                Undo.PerformRedo(); Assert.False(fresh.HasStart); Assert.False(fresh.IsWalkable(Vector2Int.zero));
                fresh.SetWalkable(Vector2Int.zero, true); Assert.False(fresh.HasStart, "repainting must not silently restore a deleted start");
            }
            finally { Undo.ClearUndo(fresh); UnityEngine.Object.DestroyImmediate(fresh); }
        }
        [Test] public void LegacyAndNewCellsCoexistAndRoundTripWithoutChangingStart()
        {
            map.SetWalkable(Vector2Int.right, true); map.SetWalkable(Vector2Int.up, false);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/MixedCells.asset");
            var copy = UnityEngine.Object.Instantiate(map);
            try
            {
                AssetDatabase.CreateAsset(copy, path);
                var savedFloor = UnityEngine.Object.Instantiate(floor); var savedWall = UnityEngine.Object.Instantiate(wall);
                AssetDatabase.AddObjectToAsset(savedFloor, copy); AssetDatabase.AddObjectToAsset(savedWall, copy);
                var values = new SerializedObject(copy); var palette = values.FindProperty("palette");
                palette.GetArrayElementAtIndex(0).objectReferenceValue = savedFloor; palette.GetArrayElementAtIndex(1).objectReferenceValue = savedWall;
                values.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(copy); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                copy = AssetDatabase.LoadAssetAtPath<GridMap>(path);
                Assert.True(copy.IsWalkable(Vector2Int.zero)); Assert.True(copy.IsWalkable(Vector2Int.right)); Assert.False(copy.IsWalkable(Vector2Int.up));
                Assert.AreEqual(map.Start, copy.Start); Assert.That(copy.ValidateMap(), Is.Empty);
                copy.Resize(5, 5); Assert.True(copy.IsWalkable(Vector2Int.right)); Assert.False(copy.IsWalkable(new Vector2Int(4, 4)));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void MovementRejectsBoundaryEmptyWallAndDiagonalWithoutStartingTime()
        {
            map.SetTile(Vector2Int.right, wall); map.SetTile(Vector2Int.up, null);
            var session = new GridSession(map);
            Assert.False(session.TryMove(Vector2Int.left)); Assert.False(session.TryMove(Vector2Int.up));
            Assert.False(session.TryMove(Vector2Int.right)); Assert.False(session.TryMove(Vector2Int.one));
            Assert.False(session.IsMoving); Assert.AreEqual(Vector2Int.zero, session.Position);
        }
        [Test] public void SessionsAreIndependentAndDoNotMutateAsset()
        {
            var first = new GridSession(map); var second = new GridSession(map);
            Assert.True(first.TryMove(Vector2Int.right)); first.Advance(.25f); Assert.AreEqual(Vector2Int.right, first.Position);
            Assert.False(second.IsMoving); Assert.AreEqual(Vector2Int.zero, second.Position); Assert.AreEqual(Vector2Int.zero, map.Start);
            map.SetTile(Vector2Int.up, wall);
            Assert.True(second.TryMove(Vector2Int.up), "session uses a snapshot");
        }
        [Test] public void ResizePreservesCoordinatesAndClearsOutOfBoundsStart()
        {
            map.SetTile(new Vector2Int(2, 1), wall); map.SetStart(new Vector2Int(2, 2));
            map.Resize(5, 4); Assert.AreEqual(wall, map.GetTile(new Vector2Int(2, 1))); Assert.IsNull(map.GetTile(new Vector2Int(4, 3)));
            map.Resize(2, 2); Assert.False(map.HasStart); Assert.That(map.ValidateMap(), Is.Not.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Resize(0, 3)); Assert.AreEqual(2, map.Width);
        }
        [Test] public void InvalidStartAndDuplicateIdsBlockRuntime()
        {
            map.SetTile(Vector2Int.zero, wall); Assert.Throws<ArgumentException>(() => new GridSession(map));
            map.SetTile(Vector2Int.zero, floor); wall.Initialize("floor", "Duplicate", false, Color.red);
            Assert.That(map.ValidateMap(), Is.Not.Empty); Assert.Throws<ArgumentException>(() => new GridSession(map));
        }
        [Test] public void UnknownIdsAreReportedWithCoordinates()
        {
            map.SetTile(Vector2Int.right, wall); wall.Initialize("changed-id", "Wall", false, Color.gray);
            Assert.That(map.ValidateMap(), Has.Some.Contains("(1, 0)"));
        }
        [Test] public void PlayerVisualSizeAndFootOffsetDoNotChangeMovement()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var settings = ScriptableObject.CreateInstance<PlayerSettings>();
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            try
            {
                // 아주 크고 발 보정이 큰 외형과 기본(임시) 외형으로 같은 행동을 하고 결과를 비교한다.
                var big = new GameObject("Oversized visual"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(big, scene); big.transform.localScale = Vector3.one * 20;
                var child = new GameObject("Sprite"); child.transform.SetParent(big.transform, false);
                var visual = big.AddComponent<PlayerVisual>(); var so = new SerializedObject(visual);
                so.FindProperty("spriteRenderer").objectReferenceValue = child.AddComponent<SpriteRenderer>(); so.FindProperty("footOffset").floatValue = 3;
                foreach (var group in new[] { "idle", "move" }) foreach (var face in new[] { "back", "right", "front", "left" }) so.FindProperty(group).FindPropertyRelative(face).objectReferenceValue = sprite;
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(settings); so.FindProperty("visualPrefab").objectReferenceValue = big; so.ApplyModifiedPropertiesWithoutUndo();
                GridMapPlayer Make(PlayerSettings shared)
                {
                    var go = new GameObject("Player"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    var p = go.AddComponent<GridMapPlayer>(); p.Configure(map, null, null);
                    if (shared != null) { var s = new SerializedObject(p); s.FindProperty("playerSettings").objectReferenceValue = shared; s.ApplyModifiedPropertiesWithoutUndo(); }
                    p.Build(); return p;
                }
                var plain = Make(null); var dressed = Make(settings);
                foreach (var step in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.right })
                {
                    Assert.AreEqual(plain.TryBeginMove(step), dressed.TryBeginMove(step));
                    plain.AdvanceMovement(1); dressed.AdvanceMovement(1);
                    Assert.AreEqual(plain.Session.Position, dressed.Session.Position); Assert.AreEqual(plain.Session.Phase, dressed.Session.Phase);
                    Assert.AreEqual(plain.Generated.Find("Player Logic Anchor").localPosition, dressed.Generated.Find("Player Logic Anchor").localPosition, "논리 위치 기준점은 외형과 무관");
                }
                Assert.AreEqual(new Vector2Int(2, 1), dressed.Session.Position);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(settings); UnityEngine.Object.DestroyImmediate(sprite); }
        }
        [Test] public void PaintUndoRedoRestoresCell()
        {
            Undo.RegisterCompleteObjectUndo(map, "Paint test"); map.SetTile(Vector2Int.right, wall);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.AreEqual(floor, map.GetTile(Vector2Int.right));
            Undo.PerformRedo(); Assert.AreEqual(wall, map.GetTile(Vector2Int.right)); Undo.ClearUndo(map);
        }
        [Test] public void SavedMapRoundTripsIdsReferencesAndStart()
        {
            wall.Initialize("stone", "Stone", true, Color.gray); map.PaintFloor(Vector2Int.right, wall);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/RoundTrip.asset");
            try
            {
                var copy = UnityEngine.Object.Instantiate(map); AssetDatabase.CreateAsset(copy, path);
                var a = UnityEngine.Object.Instantiate(floor); var b = UnityEngine.Object.Instantiate(wall);
                AssetDatabase.AddObjectToAsset(a, copy); AssetDatabase.AddObjectToAsset(b, copy);
                var so = new SerializedObject(copy); var palette = so.FindProperty("palette");
                palette.GetArrayElementAtIndex(0).objectReferenceValue = a; palette.GetArrayElementAtIndex(1).objectReferenceValue = b; so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(copy); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<GridMap>(path);
                Assert.AreEqual("stone", loaded.GetId(Vector2Int.right)); Assert.True(loaded.IsWalkable(Vector2Int.right)); Assert.AreEqual("floor", loaded.GetId(Vector2Int.one)); Assert.AreEqual(Vector2Int.zero, loaded.Start); Assert.That(loaded.ValidateMap(), Is.Empty);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void DeletedFloorInPaletteBlocksPlayUntilRemovedWithoutTouchingCells()
        {
            var deleted = ScriptableObject.CreateInstance<TileDefinition>(); deleted.Initialize("deleted", "Deleted", true, Color.red);
            map.AddTile(deleted); UnityEngine.Object.DestroyImmediate(deleted); // 쓰는 칸이 없는 바닥 에셋이 삭제된 상황
            Assert.AreEqual(1, map.MissingTileCount);
            Assert.That(map.ValidateMap(), Has.Some.Contains("삭제된 바닥 참조"));
            Assert.Throws<ArgumentException>(() => new GridSession(map), "지금은 Play할 수 없음");
            Assert.AreEqual(1, map.RemoveMissingTiles());
            Assert.AreEqual(0, map.MissingTileCount); CollectionAssert.IsEmpty(map.ValidateMap());
            Assert.AreEqual(2, map.Palette.Count, "남은 바닥은 그대로");
            Assert.AreSame(floor, map.GetTile(new Vector2Int(2, 2))); Assert.AreEqual(Vector2Int.zero, map.Start);
            Assert.DoesNotThrow(() => new GridSession(map));
        }
    }
}
