using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class EnemyMotionTests
    {
        private Scene scene;
        private GridMap map;
        private MotionFeedbackSettings settings;
        private PlayerSettings playerSettings;
        private Camera camera;
        private GridMapPlayer player;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7, 7);
            for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(6, 0));
            settings = ScriptableObject.CreateInstance<MotionFeedbackSettings>();
            var dust = Add(new GameObject("Dust Source")); dust.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(settings); so.FindProperty("landingDust").objectReferenceValue = dust; so.FindProperty("breathAmount").floatValue = 0; so.ApplyModifiedPropertiesWithoutUndo();
            playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            so = new SerializedObject(playerSettings); so.FindProperty("motionFeedback").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            camera = Add(new GameObject("Camera")).AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(55, 0, 0);
        }
        [TearDown] public void Cleanup()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(map); Object.DestroyImmediate(settings); Object.DestroyImmediate(playerSettings);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        private GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private void Enemy(int x, int y, Vector2Int facing, int speed = 1, Vector2Int[] recognition = null)
        {
            var go = Add(new GameObject("Enemy")); var definition = go.AddComponent<EnemyDefinition>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false); visual.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(definition);
            so.FindProperty("moveCells").intValue = speed; so.FindProperty("visual").objectReferenceValue = visual.transform;
            var rec = so.FindProperty("recognition"); var cells = recognition ?? new Vector2Int[0]; rec.arraySize = cells.Length;
            for (int i = 0; i < cells.Length; i++) rec.GetArrayElementAtIndex(i).vector2IntValue = cells[i];
            so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, y), go, facing);
        }
        private void Build()
        {
            var go = Add(new GameObject("Player")); player = go.AddComponent<GridMapPlayer>(); player.Configure(map, null, camera);
            var so = new SerializedObject(player); so.FindProperty("playerSettings").objectReferenceValue = playerSettings; so.ApplyModifiedPropertiesWithoutUndo();
            player.Build();
        }
        private Transform EnemyView(int i) => player.Generated.Find("Enemies").GetChild(i);
        private Transform EnemyVisual(int i) => EnemyView(i).Find("Visual");
        private int DustCount { get { int n = 0; foreach (Transform t in player.Generated) if (t.name == "Landing Dust") n++; return n; } }
        private void AssertRest(int i)
        {
            Assert.That(EnemyVisual(i).localPosition.magnitude, Is.EqualTo(0).Within(1e-4f));
            Assert.That((EnemyVisual(i).localScale - Vector3.one).magnitude, Is.EqualTo(0).Within(1e-4f));
        }

        [Test] public void EnemyHopsOnItsStepThenLandsWithSquashAndDust()
        {
            Enemy(1, 4, Vector2Int.right); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
            player.AdvanceMovement(.125f); player.UpdateMotion(.125f);
            Assert.That(EnemyVisual(0).localPosition.y, Is.EqualTo(settings.EnemyHopHeight).Within(1e-3f), "외형만 뜸");
            Assert.That(EnemyView(0).localPosition.x, Is.EqualTo(1.5f).Within(1e-4f)); Assert.AreEqual(0, EnemyView(0).localPosition.y, "논리 위치 보간은 그대로");
            player.AdvanceMovement(.2f); player.UpdateMotion(0);
            Assert.AreEqual(new Vector2Int(2, 4), player.Session.Enemies[0].Position); Assert.AreEqual(2, DustCount, "착지 먼지");
            player.UpdateMotion(settings.EnemyLandingTime / 2);
            Assert.That(EnemyVisual(0).localScale.x, Is.EqualTo(1 + settings.EnemyLandingSquash).Within(1e-3f));
            player.UpdateMotion(1f); AssertRest(0); Assert.AreEqual(0, DustCount);
        }
        [Test] public void TwoStepEnemyLandsAfterEachStep()
        {
            Enemy(0, 4, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
            player.AdvanceMovement(.1f); player.UpdateMotion(.01f);
            player.AdvanceMovement(.16f); player.UpdateMotion(.01f);
            Assert.AreEqual(2, DustCount, "첫 칸 착지"); Assert.True(player.Session.IsEnemiesMoving, "두 번째 칸 이동 중");
            player.AdvanceMovement(.25f); player.UpdateMotion(.01f);
            Assert.AreEqual(4, DustCount, "두 번째 칸 착지"); Assert.AreEqual(new Vector2Int(2, 4), player.Session.Enemies[0].Position);
        }
        [Test] public void WallBounceBumpsTowardTheWallBeforeTurningBack()
        {
            Enemy(0, 4, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
            Assert.AreEqual(Vector2Int.right, player.Session.Enemies[0].Direction, "벽 앞에서 돌아섬");
            player.UpdateMotion(0); player.UpdateMotion(settings.WallBumpTime / 2);
            Assert.That(EnemyVisual(0).position.x - EnemyView(0).position.x, Is.EqualTo(-settings.WallBumpDistance).Within(1e-3f), "벽(왼쪽) 쪽으로 부딪힘");
            Assert.Greater(EnemyVisual(0).localScale.x, 1, "돌아서며 눌림");
        }
        [Test] public void AimTurnSquashesWithoutWallBump()
        {
            map.SetStart(new Vector2Int(3, 4)); Enemy(3, 3, Vector2Int.right, 1, new[] { Vector2Int.left }); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
            Assert.AreEqual(Vector2Int.up, player.Session.Enemies[0].Direction, "플레이어 쪽으로 조준");
            player.UpdateMotion(0); player.UpdateMotion(settings.TurnTime / 2);
            Assert.That(EnemyVisual(0).localScale.x, Is.EqualTo(1 + settings.TurnSquash).Within(1e-3f));
            Assert.That((EnemyVisual(0).position - EnemyView(0).position).magnitude, Is.EqualTo(0).Within(1e-4f), "벽이 아니면 부딪힘 없음");
        }
        [Test] public void UndoRestoresEnemyLooksWithoutATurnReaction()
        {
            Enemy(0, 4, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.UpdateMotion(0);
            player.AdvanceMovement(1f); player.UpdateMotion(.05f);
            Assert.True(player.TryUndo()); AssertRest(0);
            Assert.AreEqual(Vector2Int.left, player.Session.Enemies[0].Direction);
            player.UpdateMotion(.05f); AssertRest(0);
        }
        [Test] public void DisabledMotionKeepsEnemiesFlat()
        {
            var so = new SerializedObject(settings); so.FindProperty("enabledFeedback").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            Enemy(0, 4, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.UpdateMotion(0);
            player.AdvanceMovement(.125f); player.UpdateMotion(.07f); AssertRest(0);
            player.AdvanceMovement(1f); player.UpdateMotion(0); Assert.AreEqual(0, DustCount);
        }
    }
}
