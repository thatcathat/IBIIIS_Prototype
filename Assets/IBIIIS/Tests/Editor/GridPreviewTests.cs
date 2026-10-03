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
        [Test] public void DifferentFloorMaterialsAndFallbackColorsMatchInPreviewAndRuntime()
        {
            var stone = ScriptableObject.CreateInstance<TileDefinition>(); stone.Initialize("stone", "Stone", true, Color.gray);
            var shader = Shader.Find("Universal Render Pipeline/Unlit"); Assert.NotNull(shader);
            var material = new Material(shader) { color = Color.red };
            var fallback = new Material(shader);
            try
            {
                var so = new SerializedObject(stone); so.FindProperty("surfaceMaterial").objectReferenceValue = material; so.ApplyModifiedPropertiesWithoutUndo();
                map.AddFloor(stone); map.PaintFloor(Vector2Int.right, stone); player.Configure(map, fallback, camera);
                string original = EditorJsonUtility.ToJson(material);
                player.RefreshPreview();
                Assert.AreSame(material, player.Generated.Find("Cell 1,0").GetComponent<Renderer>().sharedMaterial);
                Assert.AreEqual(tile.Color, player.Generated.Find("Cell 0,0").GetComponent<Renderer>().sharedMaterial.color);
                player.Build(); Assert.True(player.Session.TryMove(Vector2Int.right));
                Assert.AreSame(material, player.Generated.Find("Cell 1,0").GetComponent<Renderer>().sharedMaterial);
                Assert.AreEqual(original, EditorJsonUtility.ToJson(material));
                Assert.That(player.Generated.Find("Cell 1,0").GetComponent<Renderer>().bounds.size.y, Is.LessThan(.0001f));
            }
            finally { player.ClearGenerated(); Object.DestroyImmediate(stone); Object.DestroyImmediate(material); Object.DestroyImmediate(fallback); }
        }
        [Test] public void FlatGroundAndCellsHaveSeparationAndMaterialOverrideDoesNotChangeCameraOrMovement()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit"); Assert.NotNull(shader);
            var material = new Material(shader);
            try
            {
                player.RefreshPreview(); var cameraPosition = camera.transform.position;
                var values = new SerializedObject(map); values.FindProperty("groundMaterial").objectReferenceValue = material;
                values.FindProperty("groundMargin").floatValue = 50; values.ApplyModifiedPropertiesWithoutUndo();
                player.RefreshPreview();
                var ground = player.Generated.Find("Background Ground"); var cell = player.Generated.Find("Cell 0,0");
                Assert.NotNull(ground); Assert.NotNull(cell); Assert.AreSame(material, ground.GetComponent<Renderer>().sharedMaterial);
                Assert.That(ground.GetComponent<Renderer>().bounds.size.y, Is.LessThan(.0001f));
                Assert.That(cell.GetComponent<Renderer>().bounds.size.y, Is.LessThan(.0001f));
                Assert.That(cell.localPosition.y - ground.localPosition.y, Is.EqualTo(.01f).Within(.00001f));
                Assert.That(ground.GetComponent<Renderer>().bounds.size.x, Is.EqualTo(102).Within(.001f));
                Assert.AreEqual(cameraPosition, camera.transform.position); Assert.True(new GridSession(map).TryMove(Vector2Int.right));
                map.SetWalkable(Vector2Int.right, false); player.RefreshPreview();
                Assert.IsNull(player.Generated.Find("Cell 1,0")); Assert.NotNull(player.Generated.Find("Background Ground"));
            }
            finally { Object.DestroyImmediate(material); }
        }
        [Test] public void PreviewHasNoSessionAndRebuildDoesNotAccumulateObjects()
        {
            var original = EditorJsonUtility.ToJson(map);
            player.RefreshPreview(); var first = player.Generated;
            Assert.IsNull(player.Session); Assert.AreEqual(5, first.childCount);
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
            player.Build(); Assert.IsNotNull(player.Session); Assert.False(player.Session.IsMoving);
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
            player.RefreshPreview(); map.SetTile(Vector2Int.up, tile); player.RefreshPreview(); Assert.AreEqual(6, player.Generated.childCount);
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
        [Test] public void SharedSettingsSnapshotDurationAndHintsFollowActualMovement()
        {
            var settings = ScriptableObject.CreateInstance<IBIIIS.PlayerSettings>();
            try
            {
                var values = new SerializedObject(settings); values.FindProperty("moveDuration").floatValue = .5f; values.ApplyModifiedPropertiesWithoutUndo();
                var so = new SerializedObject(player); so.FindProperty("playerSettings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                player.Build(); var hints = player.Generated.Find("Movement Hints");
                Assert.True(hints.GetChild(1).gameObject.activeSelf); Assert.False(hints.GetChild(0).gameObject.activeSelf); Assert.False(hints.GetChild(4).gameObject.activeSelf);
                Assert.False(player.TryBeginMove(Vector2Int.up)); Assert.False(player.Session.IsMoving);
                Assert.True(player.TryBeginMove(Vector2Int.right)); Assert.False(player.TryBeginMove(Vector2Int.right));
                Assert.False(hints.GetChild(1).gameObject.activeSelf); Assert.True(hints.GetChild(4).gameObject.activeSelf);
                values.Update(); values.FindProperty("moveDuration").floatValue = 1; values.ApplyModifiedPropertiesWithoutUndo();
                player.AdvanceMovement(.25f);
                Assert.That(player.Generated.Find("Player Logic Anchor").localPosition.x, Is.EqualTo(.5f).Within(.0001f));
                Assert.AreEqual(Vector2Int.zero, player.Session.Position); Assert.That(player.Session.ActiveDuration, Is.EqualTo(.5f));
                player.AdvanceMovement(.25f); Assert.False(player.Session.IsMoving); Assert.AreEqual(Vector2Int.right, player.Session.Position);
                Assert.False(hints.GetChild(4).gameObject.activeSelf); Assert.True(hints.GetChild(3).gameObject.activeSelf);
                Assert.True(player.TryBeginMove(Vector2Int.left)); Assert.AreEqual(1, player.Session.ActiveDuration);
                values.Update(); values.FindProperty("showMoveHints").boolValue = false; values.ApplyModifiedPropertiesWithoutUndo(); player.RefreshMoveHints();
                foreach (Transform hint in hints) Assert.False(hint.gameObject.activeSelf);
                Assert.AreEqual(1, settings.MoveDuration); Assert.AreEqual(Vector2Int.zero, map.Start);
                player.ClearGenerated(); Assert.IsNull(player.Session);
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
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(oldVisual, player.PlayerVisualPrefab); Assert.AreEqual(.25f, player.MoveDuration);
                so.FindProperty("playerSettings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsNull(player.PlayerVisualPrefab); Assert.AreEqual(.25f, player.MoveDuration);
                var values = new SerializedObject(settings); values.FindProperty("visualPrefab").objectReferenceValue = newVisual; values.ApplyModifiedPropertiesWithoutUndo();
                player.RefreshPreview(); Assert.That(player.Generated.GetChild(2).GetChild(0).name, Does.StartWith("Shared Visual"));
                values.FindProperty("visualPrefab").objectReferenceValue = null; values.ApplyModifiedPropertiesWithoutUndo();
                player.RefreshPreview(); Assert.AreEqual("Temporary Player Visual", player.Generated.GetChild(2).GetChild(0).name);
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
