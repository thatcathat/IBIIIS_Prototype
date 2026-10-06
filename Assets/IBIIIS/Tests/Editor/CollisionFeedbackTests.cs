using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class CollisionFeedbackTests
    {
        private Scene scene;
        private GridMap map;
        private CollisionFeedbackSettings settings;
        private PlayerSettings playerSettings;
        private Camera camera;
        private GridMapPlayer player;
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(7, 7);
            for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++) map.SetWalkable(new Vector2Int(x, y), true);
            map.SetStart(new Vector2Int(3, 0));
            settings = ScriptableObject.CreateInstance<CollisionFeedbackSettings>();
            var impact = Add(new GameObject("Impact Source")); impact.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(settings); so.FindProperty("impactPrefab").objectReferenceValue = impact; so.ApplyModifiedPropertiesWithoutUndo();
            playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            so = new SerializedObject(playerSettings); so.FindProperty("collisionFeedback").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            camera = Add(new GameObject("Camera")).AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(55, 0, 0);
        }
        [TearDown] public void Cleanup()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(map); Object.DestroyImmediate(settings); Object.DestroyImmediate(playerSettings);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        private GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private void Enemy(int x, int y, Vector2Int facing, int speed = 1)
        {
            var go = Add(new GameObject("Enemy")); var definition = go.AddComponent<EnemyDefinition>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false); visual.AddComponent<SpriteRenderer>();
            var marker = new GameObject("Marker"); marker.transform.SetParent(go.transform, false); marker.AddComponent<SpriteRenderer>();
            var so = new SerializedObject(definition);
            so.FindProperty("moveCells").intValue = speed; so.FindProperty("recognition").arraySize = 0; so.FindProperty("visual").objectReferenceValue = visual.transform;
            so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, y), go, facing);
        }
        private void Build()
        {
            var go = Add(new GameObject("Player")); player = go.AddComponent<GridMapPlayer>(); player.Configure(map, null, camera);
            var so = new SerializedObject(player); so.FindProperty("playerSettings").objectReferenceValue = playerSettings; so.ApplyModifiedPropertiesWithoutUndo();
            player.Build();
        }
        private Transform EnemyView(int i) => player.Generated.Find("Enemies").GetChild(i);
        private Transform Impact => player.Generated.Find("Collision Impact");

        [Test] public void CollisionPlaysThenHidesEnemiesAndRestoresCameraAndLooks()
        {
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Build();
            var cameraBase = camera.transform.position;
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            Assert.AreEqual(BattlePhase.Won, player.Session.Phase); Assert.True(player.IsPresenting, "판정은 끝났지만 연출 중에는 입력을 받지 않는다");
            Assert.True(EnemyView(0).gameObject.activeSelf); Assert.True(EnemyView(1).gameObject.activeSelf);
            Assert.That(EnemyView(0).localPosition.x, Is.EqualTo(2).Within(1e-4f)); Assert.NotNull(Impact);
            player.AdvanceMovement(.3f);
            Assert.That(Mathf.Abs(EnemyView(0).localPosition.x - 2), Is.GreaterThan(1e-3f), "튕겨 날아가는 중");
            Assert.False(EnemyView(0).Find("Marker").GetComponent<SpriteRenderer>().enabled, "날아가는 동안 발밑 표시는 숨김");
            player.AdvanceMovement(1f);
            Assert.False(player.IsPresenting); Assert.False(EnemyView(0).gameObject.activeSelf); Assert.False(EnemyView(1).gameObject.activeSelf);
            Assert.Null(Impact); Assert.AreEqual(cameraBase, camera.transform.position);
            var visual = EnemyView(0).Find("Visual");
            Assert.AreEqual(Vector3.one, visual.localScale); Assert.AreEqual(Color.white, visual.GetComponent<SpriteRenderer>().color);
            Assert.True(EnemyView(0).Find("Marker").GetComponent<SpriteRenderer>().enabled);
        }
        [Test] public void FloatRangeSamplesBetweenMinAndMaxEvenWhenReversed()
        {
            var range = new FloatRange(2, 1);
            Assert.AreEqual(1, range.Min); Assert.AreEqual(2, range.Max);
            Assert.AreEqual(1, range.Sample(0)); Assert.AreEqual(2, range.Sample(1)); Assert.AreEqual(1.5f, range.Sample(.5f));
            for (int i = 0; i < 50; i++) { float v = range.Sample(); Assert.That(v, Is.InRange(1f, 2f)); }
        }
        [Test] public void EachEnemyFliesWithDistanceHeightAndSpinPickedInsideTheRanges()
        {
            var so = new SerializedObject(settings);
            void Set(string name, float min, float max) { var p = so.FindProperty(name); p.FindPropertyRelative("min").floatValue = min; p.FindPropertyRelative("max").floatValue = max; }
            Set("flyDistance", 1, 2); Set("flyHeight", .5f, .9f); Set("spinTurns", 1, 3); so.ApplyModifiedPropertiesWithoutUndo();
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            player.AdvanceMovement(settings.HoldTime + settings.FlyTime * .5f); // 퇴장 중간(u=0.5): 수평 = 거리/2, 높이 = 최고 높이
            for (int i = 0; i < 2; i++)
            {
                var offset = EnemyView(i).localPosition - new Vector3(2, 0, 3);
                Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.InRange(.5f - 1e-3f, 1f + 1e-3f), "수평 거리");
                Assert.That(offset.y, Is.InRange(.5f - 1e-3f, .9f + 1e-3f), "높이");
            }
        }
        [Test] public void HitStopFreezesOtherMovementThenResumes()
        {
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Enemy(0, 6, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            Assert.True(player.Session.IsEnemiesMoving); Assert.AreEqual(0, player.Session.EnemyProgress);
            player.AdvanceMovement(.1f); Assert.AreEqual(0, player.Session.EnemyProgress, "멈춤 시간에는 다른 적도 멈춘다");
            player.AdvanceMovement(.1f); Assert.That(player.Session.EnemyProgress, Is.EqualTo(.04f / .25f).Within(1e-3f));
            player.AdvanceMovement(2f); Assert.AreEqual(new Vector2Int(2, 6), player.Session.Enemies[2].Position);
        }
        [Test] public void UndoDuringPresentationRestoresEnemies()
        {
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f); player.AdvanceMovement(.3f);
            Assert.True(player.TryUndo()); Assert.False(player.IsPresenting); Assert.Null(Impact);
            Assert.True(EnemyView(0).gameObject.activeSelf); Assert.That(EnemyView(0).localPosition.x, Is.EqualTo(1).Within(1e-4f));
            Assert.AreEqual(Vector3.one, EnemyView(0).Find("Visual").localScale); Assert.AreEqual(Color.white, EnemyView(0).Find("Visual").GetComponent<SpriteRenderer>().color);
        }
        [Test] public void DisabledFeedbackOrMissingSettingsStillHidesEnemies()
        {
            var so = new SerializedObject(settings); so.FindProperty("enabledFeedback").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            Assert.False(player.IsPresenting); Assert.False(EnemyView(0).gameObject.activeSelf);
            Object.DestroyImmediate(player.gameObject);
            so = new SerializedObject(playerSettings); so.FindProperty("collisionFeedback").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo();
            Build(); Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            Assert.True(player.IsPresenting, "설정이 없으면 기본 수치로 연출"); Assert.Null(Impact);
            player.AdvanceMovement(1f); Assert.False(player.IsPresenting); Assert.False(EnemyView(0).gameObject.activeSelf);
        }
    }
}
