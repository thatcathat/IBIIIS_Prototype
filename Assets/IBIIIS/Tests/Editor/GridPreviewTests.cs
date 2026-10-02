using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class GridPreviewTests
    {
        private Scene scene;
        private GridMap map;
        private TileDefinition tile;
        private GridMapPlayer player;
        private Camera camera;
        [SetUp] public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(2, 2);
            tile = ScriptableObject.CreateInstance<TileDefinition>(); tile.Initialize("preview-floor", "Floor", true, Color.green); map.AddTile(tile);
            map.SetTile(Vector2Int.zero, tile); map.SetTile(Vector2Int.right, tile); map.SetStart(Vector2Int.zero);
            var go = new GameObject("Preview test"); SceneManager.MoveGameObjectToScene(go, scene); player = go.AddComponent<GridMapPlayer>();
            var cameraObject = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene); camera = cameraObject.AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(55, 45, 0); camera.aspect = 16f / 9;
            player.Configure(map, null, camera);
        }
        [TearDown] public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(map); Object.DestroyImmediate(tile);
        }
        [Test] public void PreviewHasNoSessionAndRebuildDoesNotAccumulateObjects()
        {
            var original = EditorJsonUtility.ToJson(map);
            player.RefreshPreview(); var first = player.Generated;
            Assert.IsNull(player.Session); Assert.AreEqual(3, first.childCount);
            foreach (var child in first.GetComponentsInChildren<Transform>(true))
                Assert.That(child.gameObject.hideFlags & HideFlags.DontSaveInEditor, Is.Not.EqualTo(0));
            player.RefreshPreview(); Assert.IsTrue(first == null); Assert.AreEqual(1, player.transform.childCount);
            Assert.AreEqual(original, EditorJsonUtility.ToJson(map));
            player.enabled = false; Assert.IsTrue(player.Generated == null); Assert.AreEqual(0, player.transform.childCount);
        }
        [Test] public void PreviewAndRuntimeBuildSharePositionsAndCamera()
        {
            player.RefreshPreview(); var position = player.Generated.GetChild(2).position;
            var cameraPosition = camera.transform.position; var size = camera.orthographicSize;
            player.Build(); Assert.IsNotNull(player.Session); Assert.AreEqual(0, player.Session.Turn);
            Assert.AreEqual(position, player.Generated.GetChild(2).position); Assert.AreEqual(cameraPosition, camera.transform.position);
            Assert.AreEqual(size, camera.orthographicSize); Assert.AreEqual(1, player.transform.childCount);
        }
        [Test] public void ManualCameraIsPreservedInPreviewAndBuild()
        {
            var so = new SerializedObject(player); so.FindProperty("autoFitCamera").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            camera.transform.position = new Vector3(3, 8, -7); camera.orthographicSize = 4; camera.orthographic = false;
            player.RefreshPreview(); player.Build();
            Assert.AreEqual(new Vector3(3, 8, -7), camera.transform.position); Assert.AreEqual(4, camera.orthographicSize); Assert.IsFalse(camera.orthographic);
        }
        [Test] public void ChangedMapRebuildAndMissingMapRemoveStaleGeometry()
        {
            player.RefreshPreview(); map.SetTile(Vector2Int.up, tile); player.RefreshPreview(); Assert.AreEqual(4, player.Generated.childCount);
            player.Configure(null, null, camera); player.RefreshPreview(); Assert.IsTrue(player.Generated == null); Assert.IsNull(player.Session);
        }
        [Test] public void GlobalPerspectiveFramesMapForWideAndPortraitViews()
        {
            var settings = ScriptableObject.CreateInstance<MapCameraSettings>();
            try
            {
                var so = new SerializedObject(player); so.FindProperty("cameraSettings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                map.Resize(20, 12);
                foreach (float aspect in new[] { 16f / 9, 9f / 16 })
                {
                    camera.aspect = aspect; player.FitCamera();
                    Assert.IsFalse(camera.orthographic); Assert.That(camera.transform.eulerAngles.y, Is.EqualTo(0).Within(.01f));
                    for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) for (int y = 0; y < 2; y++)
                    {
                        var v = camera.WorldToViewportPoint(new Vector3(x == 0 ? -.5f : 19.5f, y, z == 0 ? -.5f : 11.5f));
                        Assert.That(v.x, Is.InRange(0f, 1f)); Assert.That(v.y, Is.InRange(0f, 1f)); Assert.That(v.z, Is.GreaterThan(camera.nearClipPlane));
                    }
                }
            }
            finally { Object.DestroyImmediate(settings); }
        }
        [Test] public void SharedPlayerSettingsKeepSessionsIndependentAndSnapshotCosts()
        {
            var settings = ScriptableObject.CreateInstance<IBIIIS.PlayerSettings>();
            var secondObject = new GameObject("Second player"); SceneManager.MoveGameObjectToScene(secondObject, scene);
            var second = secondObject.AddComponent<GridMapPlayer>(); second.Configure(map, null, camera);
            try
            {
                var values = new SerializedObject(settings); values.FindProperty("moveTurnCost").intValue = 3;
                values.FindProperty("blockedTurnCost").intValue = 2; values.ApplyModifiedPropertiesWithoutUndo();
                foreach (var target in new[] { player, second })
                {
                    var so = new SerializedObject(target); so.FindProperty("playerSettings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo(); target.Build();
                }
                string original = EditorJsonUtility.ToJson(settings);
                player.Session.TryMove(Vector2Int.right); player.Session.TryMove(Vector2Int.right);
                Assert.AreEqual(5, player.Session.Turn); Assert.AreEqual(0, second.Session.Turn); Assert.AreEqual(Vector2Int.zero, second.Session.Position);
                Assert.AreEqual(original, EditorJsonUtility.ToJson(settings));
                values.Update(); values.FindProperty("moveTurnCost").intValue = 7; values.ApplyModifiedPropertiesWithoutUndo();
                second.Session.TryMove(Vector2Int.right); Assert.AreEqual(3, second.Session.Turn);
                second.ClearGenerated(); second.Build(); second.Session.TryMove(Vector2Int.right); Assert.AreEqual(7, second.Session.Turn);
            }
            finally { Object.DestroyImmediate(settings); }
        }
        [Test] public void SharedVisualOverridesLegacyAndEmptySharedVisualUsesPlaceholder()
        {
            var settings = ScriptableObject.CreateInstance<IBIIIS.PlayerSettings>();
            var oldVisual = new GameObject("Legacy Visual"); SceneManager.MoveGameObjectToScene(oldVisual, scene);
            var newVisual = new GameObject("Shared Visual"); SceneManager.MoveGameObjectToScene(newVisual, scene);
            try
            {
                var so = new SerializedObject(player); so.FindProperty("playerVisualPrefab").objectReferenceValue = oldVisual;
                so.FindProperty("moveTurnCost").intValue = 4; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(oldVisual, player.PlayerVisualPrefab); Assert.AreEqual(4, player.MoveTurnCost);
                so.FindProperty("playerSettings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsNull(player.PlayerVisualPrefab); Assert.AreEqual(1, player.MoveTurnCost);
                var values = new SerializedObject(settings); values.FindProperty("visualPrefab").objectReferenceValue = newVisual; values.ApplyModifiedPropertiesWithoutUndo();
                player.RefreshPreview(); Assert.That(player.Generated.GetChild(2).GetChild(0).name, Does.StartWith("Shared Visual"));
                values.FindProperty("visualPrefab").objectReferenceValue = null; values.ApplyModifiedPropertiesWithoutUndo();
                player.RefreshPreview(); Assert.AreEqual("Temporary Player Visual", player.Generated.GetChild(2).GetChild(0).name);
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
