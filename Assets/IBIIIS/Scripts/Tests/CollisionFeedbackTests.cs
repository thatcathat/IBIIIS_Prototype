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
        // 상황별 그림을 채운 임시 플레이어 외형(프리팹 대신 장면 오브젝트를 복제해 쓴다).
        private Sprite idle, danger, cleared, cleared2, gone, defeated;
        private void UseReactionVisual()
        {
            Sprite Make(string n) { var s = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero); s.name = n; return s; }
            idle = Make("idle"); danger = Make("danger"); cleared = Make("cleared"); cleared2 = Make("cleared2"); gone = Make("gone"); defeated = Make("defeated");
            var root = Add(new GameObject("Visual Source")); var child = new GameObject("Sprite"); child.transform.SetParent(root.transform, false);
            var visual = root.AddComponent<PlayerVisual>(); var so = new SerializedObject(visual);
            so.FindProperty("spriteRenderer").objectReferenceValue = child.AddComponent<SpriteRenderer>();
            foreach (var face in new[] { "back", "right", "front", "left" }) so.FindProperty("idle").FindPropertyRelative(face).objectReferenceValue = idle;
            so.FindProperty("danger").objectReferenceValue = danger; so.FindProperty("escaped").objectReferenceValue = gone; so.FindProperty("defeated").objectReferenceValue = defeated;
            var evaded = so.FindProperty("evaded"); evaded.arraySize = 2;
            evaded.GetArrayElementAtIndex(0).objectReferenceValue = cleared; evaded.GetArrayElementAtIndex(1).objectReferenceValue = cleared2;
            so.FindProperty("reactionFrameSeconds").floatValue = .4f;
            so.ApplyModifiedPropertiesWithoutUndo();
            so = new SerializedObject(playerSettings); so.FindProperty("visualPrefab").objectReferenceValue = root; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private Sprite Shown => player.Generated.GetComponentInChildren<PlayerVisual>(true).Renderer.sprite;
        [TearDown] public void DestroyReactionSprites() { foreach (var s in new[] { idle, danger, cleared, cleared2, gone, defeated }) if (s != null) Object.DestroyImmediate(s); }
        [Test] public void DefeatShowsTheDefeatedSpriteInPlaceWithoutFlyingUntilUndo()
        {
            UseReactionVisual();
            map.SetStart(new Vector2Int(1, 3)); Enemy(0, 3, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.5f);
            Assert.AreEqual(BattlePhase.Lost, player.Session.Phase);
            Assert.False(player.IsPresenting, "플레이어는 날아가지 않음"); Assert.Null(Impact);
            Assert.AreSame(defeated, Shown, "패배 그림");
            player.UpdateMotion(5); Assert.AreSame(defeated, Shown, "되돌리기 전까지 유지");
            Assert.True(PlayerAnchor.gameObject.activeSelf); Assert.That(PlayerAnchor.localPosition.x, Is.EqualTo(1).Within(1e-4f)); Assert.That(PlayerAnchor.localPosition.z, Is.EqualTo(3).Within(1e-4f));
            Assert.True(player.TryUndo()); Assert.IsNull(player.ReactionSprite); Assert.AreSame(idle, Shown, "되돌리면 원래 그림");
        }
        [Test] public void SkillEvasionShowsEvadedSpritesInOrderThenReturns()
        {
            UseReactionVisual();
            map.SetStart(new Vector2Int(1, 3)); Enemy(0, 3, Vector2Int.right, 2); Build();
            Assert.True(player.TryBeginAction(PlayerAction.Dash, Vector2Int.up)); player.AdvanceMovement(2);
            Assert.AreEqual(BattlePhase.Waiting, player.Session.Phase, "대시로 피함");
            Assert.AreSame(cleared, Shown, "첫 회피 그림");
            player.UpdateMotion(.45f); Assert.AreSame(cleared2, Shown, "다음 회피 그림");
            player.UpdateMotion(.45f); Assert.IsNull(player.ReactionSprite); Assert.AreSame(idle, Shown, "끝나면 원래 그림");
        }
        [Test] public void DangerIdleAndWalkingOutOfRecognitionShowEscapedSprite()
        {
            UseReactionVisual();
            map.SetStart(new Vector2Int(4, 2)); Enemy(3, 4, Vector2Int.down);
            var so = new SerializedObject(map.EnemyAt(new Vector2Int(3, 4)).Prefab.GetComponent<EnemyDefinition>()); var recognition = so.FindProperty("recognition"); recognition.arraySize = 6;
            int n = 0; for (int x = -1; x <= 1; x++) for (int y = 1; y <= 2; y++) recognition.GetArrayElementAtIndex(n++).vector2IntValue = new Vector2Int(x, y);
            so.ApplyModifiedPropertiesWithoutUndo();
            Build();
            Assert.AreSame(danger, Shown, "인식 범위 안에서 대기하면 위험 그림");
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.right)); player.AdvanceMovement(2);
            Assert.AreEqual(BattlePhase.Waiting, player.Session.Phase);
            Assert.AreSame(gone, Shown, "이동만으로 인식 범위를 벗어남");
            player.UpdateMotion(.45f); Assert.IsNull(player.ReactionSprite); Assert.AreSame(idle, Shown, "벗어났으므로 일반 대기 그림");
            Assert.True(player.TryUndo()); Assert.AreSame(danger, Shown, "되돌리면 다시 인식 범위 안");
        }
        [Test] public void SkillWhileRecognizedShowsEvadedSpritesEvenWithoutAnAttackToDodge()
        {
            UseReactionVisual();
            map.SetStart(new Vector2Int(4, 2)); Enemy(3, 4, Vector2Int.down);
            var so = new SerializedObject(map.EnemyAt(new Vector2Int(3, 4)).Prefab.GetComponent<EnemyDefinition>()); var recognition = so.FindProperty("recognition"); recognition.arraySize = 6;
            int n = 0; for (int x = -1; x <= 1; x++) for (int y = 1; y <= 2; y++) recognition.GetArrayElementAtIndex(n++).vector2IntValue = new Vector2Int(x, y);
            so.ApplyModifiedPropertiesWithoutUndo();
            Build();
            Assert.AreSame(danger, Shown, "인식된 상태(위험 그림)");
            Assert.AreNotEqual(BattlePhase.Lost, player.Session.Predict(PlayerAction.Wait, Vector2Int.zero), "제자리에 있어도 맞지 않는 상황");
            Assert.True(player.TryBeginAction(PlayerAction.Dash, Vector2Int.right)); player.AdvanceMovement(2);
            Assert.AreEqual(BattlePhase.Waiting, player.Session.Phase);
            Assert.AreSame(cleared, Shown, "위험 그림이 보이던 상태에서 회피기를 쓰면 회피 그림");
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
