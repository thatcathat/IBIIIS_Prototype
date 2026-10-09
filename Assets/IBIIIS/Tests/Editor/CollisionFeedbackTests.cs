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
            Set("flyDistance", 1, 2); Set("flyHeight", .5f, .9f); Set("spinTurns", .25f, .25f); so.ApplyModifiedPropertiesWithoutUndo();
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            player.AdvanceMovement(settings.HoldTime + settings.FlyTime * .5f); // 퇴장 중간(u=0.5): 수평 = 거리/2, 높이 = 최고 높이
            for (int i = 0; i < 2; i++)
            {
                var offset = EnemyView(i).localPosition - new Vector3(2, 0, 3);
                Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.InRange(.5f - 1e-3f, 1f + 1e-3f), "수평 거리");
                Assert.That(offset.y, Is.InRange(.5f - 1e-3f, .9f + 1e-3f), "높이");
            }
            // 0.25바퀴의 절반 지점: 카메라를 바라본 채 화면 안에서 ±45도, 두 적은 반대로 돈다.
            float Spin(int i) { var rel = Quaternion.Inverse(camera.transform.rotation) * EnemyView(i).Find("Visual").rotation; return Mathf.DeltaAngle(0, rel.eulerAngles.z); }
            Assert.AreEqual(45, Mathf.Abs(Spin(0)), .1f); Assert.AreEqual(45, Mathf.Abs(Spin(1)), .1f);
            Assert.AreEqual(-Mathf.Sign(Spin(0)), Mathf.Sign(Spin(1)), "도는 방향이 번갈아 바뀜");
        }
        [Test] public void HitStopFreezesOtherMovementThenResumes()
        {
            var so = new SerializedObject(settings); so.FindProperty("squashTime").floatValue = .08f; so.FindProperty("hitStopTime").floatValue = .08f; so.ApplyModifiedPropertiesWithoutUndo();
            so = new SerializedObject(playerSettings); so.FindProperty("enemyStepDuration").floatValue = .25f; so.ApplyModifiedPropertiesWithoutUndo();
            Enemy(1, 3, Vector2Int.right); Enemy(3, 3, Vector2Int.left); Enemy(0, 6, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.25f);
            Assert.True(player.Session.IsEnemiesMoving); Assert.AreEqual(0, player.Session.EnemyProgress);
            player.AdvanceMovement(.1f); Assert.AreEqual(0, player.Session.EnemyProgress, "멈춤 시간에는 다른 적도 멈춘다");
            player.AdvanceMovement(.1f); Assert.That(player.Session.EnemyProgress, Is.EqualTo((.2f - settings.HoldTime) / playerSettings.EnemyStepDuration).Within(1e-3f));
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
        private Transform PlayerAnchor => player.Generated.Find("Player Logic Anchor");
        [Test] public void DefeatSendsOnlyThePlayerFlyingAwayFromTheAttackerUntilUndo()
        {
            map.SetStart(new Vector2Int(1, 3)); Enemy(0, 3, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.5f);
            Assert.AreEqual(BattlePhase.Lost, player.Session.Phase); Assert.True(player.IsPresenting); Assert.True(player.Feedback.OwnsPlayer);
            Assert.False(player.Feedback.Owns(0), "공격한 적은 날아가지 않는다"); Assert.NotNull(Impact);
            player.AdvanceMovement(settings.HoldTime + settings.FlyTime * .5f);
            Assert.Greater(PlayerAnchor.localPosition.x, 1 + 1e-3f, "적이 바라보는 오른쪽으로 날아감");
            Assert.That(PlayerAnchor.localPosition.z, Is.EqualTo(3).Within(1e-3f));
            Assert.That(EnemyView(0).localPosition.x, Is.EqualTo(2).Within(1e-4f)); Assert.True(EnemyView(0).gameObject.activeSelf);
            player.AdvanceMovement(1f);
            Assert.False(player.IsPresenting); Assert.False(PlayerAnchor.gameObject.activeSelf, "날아가 사라진 채 유지"); Assert.True(player.Feedback.OwnsPlayer);
            Assert.True(player.TryUndo());
            Assert.True(PlayerAnchor.gameObject.activeSelf); Assert.False(player.Feedback.OwnsPlayer);
            Assert.That(PlayerAnchor.localPosition.x, Is.EqualTo(1).Within(1e-4f)); Assert.That(PlayerAnchor.localPosition.y, Is.EqualTo(0).Within(1e-4f));
        }
        [Test] public void DefeatedPlayerFliesAlongTheAttackerFacingNotAwayFromItsPosition()
        {
            // 위를 보는 적이 앞오른쪽 칸(대각선)을 공격: 위치 기준이면 대각선이지만 바라보는 방향(위, +Z)으로 날아가야 한다.
            map.SetStart(new Vector2Int(4, 4)); Enemy(3, 2, Vector2Int.up);
            var definition = map.EnemyAt(new Vector2Int(3, 2)).Prefab.GetComponent<EnemyDefinition>();
            var so = new SerializedObject(definition); var attack = so.FindProperty("attack"); attack.arraySize = 1; attack.GetArrayElementAtIndex(0).vector2IntValue = Vector2Int.one; so.ApplyModifiedPropertiesWithoutUndo();
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.5f);
            Assert.AreEqual(BattlePhase.Lost, player.Session.Phase); CollectionAssert.AreEqual(new[] { 0 }, player.Session.Attackers);
            player.AdvanceMovement(settings.HoldTime + settings.FlyTime * .5f);
            Assert.Greater(PlayerAnchor.localPosition.z, 4 + 1e-3f); Assert.That(PlayerAnchor.localPosition.x, Is.EqualTo(4).Within(1e-3f));
        }
        [Test] public void DisabledFeedbackLeavesTheDefeatedPlayerInPlace()
        {
            var so = new SerializedObject(settings); so.FindProperty("enabledFeedback").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            map.SetStart(new Vector2Int(1, 3)); Enemy(0, 3, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.5f);
            Assert.AreEqual(BattlePhase.Lost, player.Session.Phase); Assert.False(player.IsPresenting); Assert.True(PlayerAnchor.gameObject.activeSelf);
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
