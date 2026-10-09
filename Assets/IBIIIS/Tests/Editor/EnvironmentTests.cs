using System.Collections.Generic;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class EnvironmentTests
    {
        private Scene scene;
        private GridMap map;
        private TileDefinition tile;
        private GridMapPlayer owner;
        private readonly List<string> paths = new List<string>();
        private Object previousSelection;
        [SetUp] public void SetUp()
        {
            previousSelection = Selection.activeObject;
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(2, 2);
            tile = ScriptableObject.CreateInstance<TileDefinition>(); tile.Initialize("environment-floor", "Floor", true, Color.green);
            map.AddTile(tile); map.SetTile(Vector2Int.zero, tile); map.SetTile(Vector2Int.right, tile); map.SetStart(Vector2Int.zero);
            var go = new GameObject("Environment test owner"); SceneManager.MoveGameObjectToScene(go, scene);
            owner = go.AddComponent<GridMapPlayer>(); owner.Configure(map, null, null);
        }
        [TearDown] public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var path in paths) AssetDatabase.DeleteAsset(path); paths.Clear();
            Object.DestroyImmediate(map); Object.DestroyImmediate(tile); Selection.activeObject = previousSelection;
        }
        private GameObject CreatePrefab()
        {
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/EnvironmentCheck.prefab"); paths.Add(path);
            return MapEnvironmentEditor.CreateEnvironment(map, path);
        }
        [Test] public void PreviewRebuildAndRuntimeReuseEnvironmentWithLocalChanges()
        {
            CreatePrefab(); Assert.True(MapEnvironmentEditor.Synchronize(owner, false));
            var instance = owner.EnvironmentInstance;
            var decoration = new GameObject("Outside grid"); decoration.transform.SetParent(instance.transform, false); decoration.transform.localPosition = new Vector3(-8, 3, 15);
            owner.RefreshPreview(); owner.RefreshPreview(); owner.Build(); owner.EnsureRuntimeEnvironment();
            Assert.AreSame(instance, owner.EnvironmentInstance); Assert.AreEqual(new Vector3(-8, 3, 15), decoration.transform.localPosition);
            Assert.AreEqual(2, owner.transform.childCount);
            owner.ClearGenerated(); Assert.AreSame(instance, owner.EnvironmentInstance); Assert.AreEqual(1, owner.transform.childCount);
        }
        [Test] public void ModifiedEnvironmentBlocksReplacementAndDisconnect()
        {
            var original = CreatePrefab(); MapEnvironmentEditor.Synchronize(owner, false); var instance = owner.EnvironmentInstance;
            var decoration = new GameObject("Unsaved decoration"); decoration.transform.SetParent(instance.transform, false);
            Assert.True(MapEnvironmentEditor.HasUnappliedChanges(instance));
            var replacement = CreatePrefab(); Assert.False(MapEnvironmentEditor.Synchronize(owner, false)); Assert.AreSame(instance, owner.EnvironmentInstance);
            map.SetEnvironmentPrefab(null); Assert.False(MapEnvironmentEditor.Synchronize(owner, false)); Assert.AreSame(instance, owner.EnvironmentInstance);
            map.SetEnvironmentPrefab(replacement); PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
            Assert.True(MapEnvironmentEditor.Synchronize(owner, false)); Assert.AreEqual(replacement, owner.EnvironmentSource); Assert.IsTrue(instance == null);
        }
        [Test] public void AppliedDecorationLoadsElsewhereWithoutChangingMovement()
        {
            var prefab = CreatePrefab(); MapEnvironmentEditor.Synchronize(owner, false);
            var decoration = GameObject.CreatePrimitive(PrimitiveType.Cube); decoration.name = "Tree";
            decoration.transform.SetParent(owner.EnvironmentInstance.transform, false); decoration.transform.localPosition = new Vector3(1, 0, 0); decoration.transform.localScale = Vector3.one * 20;
            PrefabUtility.ApplyPrefabInstance(owner.EnvironmentInstance, InteractionMode.AutomatedAction);
            var second = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Assert.AreEqual(1, second.transform.childCount); Assert.AreEqual(Vector3.one * 20, second.transform.GetChild(0).localScale);
            var session = new GridSession(map); Assert.True(session.TryMove(Vector2Int.right)); session.Advance(.25f); Assert.AreEqual(Vector2Int.right, session.Position);
        }
        [Test] public void EnvironmentOptionalAndRootTransformEditsProtected()
        {
            Assert.True(MapEnvironmentEditor.Synchronize(owner, false)); owner.Build(); Assert.IsNotNull(owner.Session); Assert.IsNull(owner.EnvironmentInstance);
            CreatePrefab(); MapEnvironmentEditor.Synchronize(owner, false); var instance = owner.EnvironmentInstance;
            instance.transform.localPosition = Vector3.right;
            map.SetEnvironmentPrefab(null); Assert.False(MapEnvironmentEditor.Synchronize(owner, false)); Assert.AreSame(instance, owner.EnvironmentInstance);
        }
        [Test] public void SlotChangeAndSceneReplacementUndoTogether()
        {
            var first = CreatePrefab(); Assert.True(MapEnvironmentEditor.Synchronize(owner, false));
            var second = CreatePrefab(); map.SetEnvironmentPrefab(first); // 만들기만 하면 슬롯이 새 프리팹으로 바뀌므로 되돌려 둔다
            MapEnvironmentEditor.ConnectPrefab(map, second, () => MapEnvironmentEditor.Synchronize(owner, false));
            Assert.AreEqual(second, map.EnvironmentPrefab); Assert.AreEqual(second, owner.EnvironmentSource);
            Undo.PerformUndo();
            Assert.AreEqual(first, map.EnvironmentPrefab, "map slot"); Assert.AreEqual(first, owner.EnvironmentSource, "scene source");
            Assert.IsNotNull(owner.EnvironmentInstance); Assert.AreEqual(first, PrefabUtility.GetCorrespondingObjectFromSource(owner.EnvironmentInstance));
            Undo.PerformRedo();
            Assert.AreEqual(second, map.EnvironmentPrefab); Assert.AreEqual(second, owner.EnvironmentSource);
            Assert.AreEqual(second, PrefabUtility.GetCorrespondingObjectFromSource(owner.EnvironmentInstance));
            Undo.ClearUndo(map); Undo.ClearUndo(owner);
        }
        [Test] public void InvalidRootIsRejectedWithReason()
        {
            var prefab = CreatePrefab(); var path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try { contents.transform.localScale = Vector3.one * 2; PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            StringAssert.Contains("크기", MapEnvironmentEditor.GetPrefabProblem(prefab));
            Assert.False(MapEnvironmentEditor.ValidPrefab(prefab));
            Assert.False(MapEnvironmentEditor.Synchronize(owner, false)); Assert.IsNull(owner.EnvironmentInstance);
            Assert.IsNull(MapEnvironmentEditor.GetPrefabProblem(null));
        }
        [Test] public void DeletedInstanceLeavesStaleLinkUntilButtonCleansIt()
        {
            CreatePrefab(); Assert.True(MapEnvironmentEditor.Synchronize(owner, false)); var source = owner.EnvironmentSource;
            Object.DestroyImmediate(owner.EnvironmentInstance); map.SetEnvironmentPrefab(null);
            Assert.True(MapEnvironmentEditor.Synchronize(owner, false)); Assert.AreEqual(source, owner.EnvironmentSource);
            Assert.True(MapEnvironmentEditor.Synchronize(owner, true)); Assert.IsNull(owner.EnvironmentSource);
        }
        private void PrepareRunnerScene()
        {
            var runnerScene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(runnerScene.path))
            {
                foreach (var root in runnerScene.GetRootGameObjects())
                    Assert.True(root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null,
                        "Only the test runner's default camera/light scene may be temporarily saved.");
                var runnerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/EmptyRunnerScene.unity"); paths.Add(runnerPath);
                Assert.True(EditorSceneManager.SaveScene(runnerScene, runnerPath));
            }
        }
        [Test] public void NewMapCreatesLinkedSceneAndAvoidsOverwritingEitherFile()
        {
            PrepareRunnerScene();
            var requested = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/NewMapPair.asset");
            var first = MapEditorSetup.CreateMapWithScene(requested, out var firstScene);
            paths.Add(AssetDatabase.GetAssetPath(first)); paths.Add(firstScene);
            var firstGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(first));
            var second = MapEditorSetup.CreateMapWithScene(requested, out var secondScene);
            paths.Add(AssetDatabase.GetAssetPath(second)); paths.Add(secondScene);
            Assert.AreNotEqual(firstScene, secondScene); Assert.AreNotEqual(first, second);
            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(first)));
            Assert.AreEqual(12, first.Width); Assert.AreEqual(12, first.Height); Assert.False(first.HasStart);
            Assert.IsNull(first.GetTile(Vector2Int.zero)); Assert.That(first.ValidateMap(false), Is.Empty);
            Assert.Throws<System.ArgumentException>(() => new GridSession(first));
            var loaded = EditorSceneManager.OpenPreviewScene(firstScene);
            try
            {
                var root = System.Array.Find(loaded.GetRootGameObjects(), g => g.GetComponent<GridMapPlayer>() != null);
                var player = root.GetComponent<GridMapPlayer>(); Assert.AreEqual(first, player.Map);
                Assert.NotNull(player.SharedPlayerSettings); Assert.NotNull(player.CameraSettings); Assert.NotNull(player.ViewCamera); Assert.NotNull(player.FallbackMaterial);
            }
            finally { EditorSceneManager.ClosePreviewScene(loaded); }
        }
        [Test] public void NewTestScenePersistsConnectedEnvironmentOnce()
        {
            PrepareRunnerScene();
            var prefab = CreatePrefab();
            var mapPath = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/EnvironmentMapCheck.asset"); paths.Add(mapPath);
            var copy = Object.Instantiate(map); AssetDatabase.CreateAsset(copy, mapPath);
            var tileCopy = Object.Instantiate(tile); AssetDatabase.AddObjectToAsset(tileCopy, copy);
            var serialized = new SerializedObject(copy); serialized.FindProperty("palette").GetArrayElementAtIndex(0).objectReferenceValue = tileCopy; serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(copy);
            var path = MapEditorSetup.CreateTestScene(copy, "Assets/IBIIIS/Tests/EnvironmentSceneCheck.unity"); paths.Add(path);
            var loaded = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var root = System.Array.Find(loaded.GetRootGameObjects(), g => g.GetComponent<GridMapPlayer>() != null);
                var player = root.GetComponent<GridMapPlayer>(); Assert.NotNull(player.EnvironmentInstance); Assert.AreEqual(prefab, player.EnvironmentSource);
                Assert.AreEqual(1, root.transform.childCount); player.EnsureRuntimeEnvironment(); Assert.AreEqual(1, root.transform.childCount);
                Assert.NotNull(player.SharedPlayerSettings);
            }
            finally { EditorSceneManager.ClosePreviewScene(loaded); }
        }
    }
}
