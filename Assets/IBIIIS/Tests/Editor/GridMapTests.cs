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
        [Test] public void MovementRejectsBoundaryEmptyWallAndDiagonalWithoutTurn()
        {
            map.SetTile(Vector2Int.right, wall); map.SetTile(Vector2Int.up, null);
            var session = new GridSession(map);
            Assert.False(session.TryMove(Vector2Int.left)); Assert.False(session.TryMove(Vector2Int.up));
            Assert.False(session.TryMove(Vector2Int.right)); Assert.False(session.TryMove(Vector2Int.one));
            Assert.AreEqual(0, session.Turn); Assert.AreEqual(Vector2Int.zero, session.Position);
        }
        [Test] public void SessionsAreIndependentAndDoNotMutateAsset()
        {
            var first = new GridSession(map); var second = new GridSession(map);
            Assert.True(first.TryMove(Vector2Int.right)); Assert.AreEqual(1, first.Turn);
            Assert.AreEqual(0, second.Turn); Assert.AreEqual(Vector2Int.zero, second.Position); Assert.AreEqual(Vector2Int.zero, map.Start);
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
        [Test] public void VisualChangesDoNotChangeMovement()
        {
            var before = new GridSession(map);
            var visual = new GameObject("Oversized visual");
            try
            {
                visual.transform.localScale = Vector3.one * 20;
                var serialized = new SerializedObject(floor); serialized.FindProperty("visualPrefab").objectReferenceValue = visual; serialized.ApplyModifiedPropertiesWithoutUndo();
                var after = new GridSession(map);
                Assert.AreEqual(before.TryMove(Vector2Int.right), after.TryMove(Vector2Int.right)); Assert.AreEqual(before.Position, after.Position);
            }
            finally { UnityEngine.Object.DestroyImmediate(visual); }
        }
        [Test] public void PaintUndoRedoRestoresCell()
        {
            Undo.RegisterCompleteObjectUndo(map, "Paint test"); map.SetTile(Vector2Int.right, wall);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.AreEqual(floor, map.GetTile(Vector2Int.right));
            Undo.PerformRedo(); Assert.AreEqual(wall, map.GetTile(Vector2Int.right)); Undo.ClearUndo(map);
        }
        [Test] public void SavedMapRoundTripsIdsReferencesAndStart()
        {
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
                Assert.AreEqual("floor", loaded.GetId(Vector2Int.one)); Assert.AreEqual(Vector2Int.zero, loaded.Start); Assert.That(loaded.ValidateMap(), Is.Empty);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
