using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS.Tests
{
    public sealed class MotionFeedbackTests
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
            map.SetStart(new Vector2Int(3, 0));
            settings = ScriptableObject.CreateInstance<MotionFeedbackSettings>();
            var dust = Add(new GameObject("Dust Source")); dust.AddComponent<SpriteRenderer>();
            // 숨쉬기는 전용 테스트에서만 켠다(나머지는 "원래 자세로 복귀"를 정확히 확인하기 위해 끔).
            var so = new SerializedObject(settings); so.FindProperty("landingDust").objectReferenceValue = dust; so.FindProperty("breathAmount").floatValue = 0; so.ApplyModifiedPropertiesWithoutUndo();
            playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            so = new SerializedObject(playerSettings);
            so.FindProperty("motionFeedback").objectReferenceValue = settings; so.FindProperty("visualPrefab").objectReferenceValue = MakePlayerVisual();
            so.ApplyModifiedPropertiesWithoutUndo();
            camera = Add(new GameObject("Camera")).AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(55, 0, 0);
        }
        [TearDown] public void Cleanup()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(map); Object.DestroyImmediate(settings); Object.DestroyImmediate(playerSettings); Object.DestroyImmediate(testSprite);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        private Sprite testSprite;
        private GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }
        // 모든 슬롯에 스프라이트를 채운 테스트용 플레이어 외형
        private GameObject MakePlayerVisual()
        {
            var root = Add(new GameObject("PlayerVisual Source")); var child = new GameObject("Sprite"); child.transform.SetParent(root.transform, false);
            var visual = root.AddComponent<PlayerVisual>(); var so = new SerializedObject(visual);
            so.FindProperty("spriteRenderer").objectReferenceValue = child.AddComponent<SpriteRenderer>();
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero); testSprite = sprite;
            foreach (var group in new[] { "idle", "move", "dash" }) foreach (var face in new[] { "back", "right", "front", "left" }) so.FindProperty(group).FindPropertyRelative(face).objectReferenceValue = sprite;
            foreach (var roll in new[] { "rollBackLeft", "rollBackRight", "rollFrontLeft", "rollFrontRight" })
            { so.FindProperty(roll).FindPropertyRelative("first").objectReferenceValue = sprite; so.FindProperty(roll).FindPropertyRelative("second").objectReferenceValue = sprite; }
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }
        private void Build()
        {
            var go = Add(new GameObject("Player")); player = go.AddComponent<GridMapPlayer>(); player.Configure(map, null, camera);
            var so = new SerializedObject(player); so.FindProperty("playerSettings").objectReferenceValue = playerSettings; so.ApplyModifiedPropertiesWithoutUndo();
            player.Build();
        }
        private MotionPose Pose => player.Generated.GetComponentInChildren<PlayerVisual>().Pose;
        private Transform Anchor => player.Generated.Find("Player Logic Anchor");
        private int DustCount { get { int n = 0; foreach (Transform t in player.Generated) if (t.name == "Landing Dust") n++; return n; } }
        private static void AssertIdentity(MotionPose pose)
        {
            Assert.That(pose.Lift, Is.EqualTo(0).Within(1e-5f)); Assert.That(pose.Shift.magnitude, Is.EqualTo(0).Within(1e-5f));
            Assert.That(pose.Squash.x, Is.EqualTo(1).Within(1e-5f)); Assert.That(pose.Squash.y, Is.EqualTo(1).Within(1e-5f)); Assert.That(pose.Tilt, Is.EqualTo(0).Within(1e-4f));
        }
        private int Count(string name) { int n = 0; foreach (Transform t in player.Generated) if (t.name == name) n++; return n; }
        private void Run(float seconds, float step = .02f) { for (float t = 0; t < seconds; t += step) { player.AdvanceMovement(step); player.UpdateMotion(step); } }

        [Test] public void UndoRestoresTheFacingBeforeTheAction()
        {
            Build(); var visual = player.Generated.GetComponentInChildren<PlayerVisual>();
            Assert.AreEqual(PlayerFacing.Front, visual.Facing);
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.left)); Run(.5f);
            Assert.AreEqual(PlayerFacing.Left, visual.Facing);
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); Run(.5f);
            Assert.True(player.TryUndo()); Assert.AreEqual(PlayerFacing.Left, visual.Facing, "대기를 되돌리면 대기 전 방향");
            Assert.True(player.TryUndo()); Assert.AreEqual(PlayerFacing.Front, visual.Facing, "이동을 되돌리면 시작 방향");
        }
        [Test] public void HopPoseStartsAndEndsAtRestAndPeaksMidway()
        {
            var motion = new MotionFeedback(settings, null, c => Vector3.zero, 1, null);
            try
            {
                AssertIdentity(motion.HopPose(0)); AssertIdentity(motion.HopPose(1));
                var mid = motion.HopPose(.5f);
                Assert.That(mid.Lift, Is.EqualTo(settings.HopHeight).Within(1e-4f)); Assert.Greater(mid.Squash.y, 1, "공중에서 위아래로 늘어남");
                Assert.Greater(motion.HopPose(settings.TakeoffPortion / 2).Squash.x, 1, "출발 때 웅크림");
            }
            finally { motion.Dispose(); }
        }
        [Test] public void MoveHopsThenLandsWithSquashAndDustWithoutChangingLogicalPosition()
        {
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.up));
            player.AdvanceMovement(.125f); player.UpdateMotion(.125f);
            Assert.That(Pose.Lift, Is.EqualTo(settings.HopHeight).Within(1e-3f));
            Assert.That(Anchor.localPosition.z, Is.EqualTo(.5f).Within(1e-4f), "논리 위치 보간은 그대로"); Assert.AreEqual(0, Anchor.localPosition.y);
            player.AdvanceMovement(.2f); player.UpdateMotion(0);
            Assert.AreEqual(new Vector2Int(3, 1), player.Session.Position); Assert.AreEqual(2, DustCount, "착지 먼지 두 덩이");
            player.UpdateMotion(settings.LandingTime / 2);
            Assert.That(Pose.Squash.x, Is.EqualTo(1 + settings.LandingSquash).Within(1e-3f), "착지 납작함"); Assert.True(player.Motion.IsAnimating);
            player.UpdateMotion(1f);
            AssertIdentity(Pose); Assert.AreEqual(0, DustCount); Assert.False(player.Motion.IsAnimating);
        }
        [Test] public void BlockedInputBumpsTowardTheDirectionWithoutStartingAnAction()
        {
            Build();
            Assert.False(player.TryActionOrBump(PlayerAction.Move, Vector2Int.down), "맵 아래 경계");
            Assert.AreEqual(BattlePhase.Waiting, player.Session.Phase); Assert.AreEqual(0, player.Session.UndoCount); Assert.False(player.Session.IsBusy);
            player.UpdateMotion(settings.BumpTime / 2);
            Assert.That(Pose.Shift.z, Is.EqualTo(-settings.BumpDistance).Within(1e-3f)); Assert.That(Pose.Shift.x, Is.EqualTo(0).Within(1e-5f));
            player.UpdateMotion(1f); AssertIdentity(Pose);
            Assert.False(player.TryActionOrBump(PlayerAction.Wait, Vector2Int.right), "대기 방향 오류는 부딪힘 없음");
            player.UpdateMotion(.01f); AssertIdentity(Pose);
        }
        [Test] public void UndoClearsLandingAndDustImmediately()
        {
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.up)); player.AdvanceMovement(1f); player.UpdateMotion(0); player.UpdateMotion(settings.LandingTime / 2);
            Assert.True(player.Motion.IsAnimating);
            Assert.True(player.TryUndo());
            Assert.False(player.Motion.IsAnimating); Assert.AreEqual(0, DustCount); AssertIdentity(Pose);
            Assert.AreEqual(Vector3.zero + new Vector3(3, 0, 0), Anchor.localPosition);
        }
        [Test] public void DashLeansCrouchesLeavesAfterimagesAndSkidsToAStop()
        {
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Dash, Vector2Int.right));
            Assert.AreEqual(2, DustCount, "출발 먼지");
            player.AdvanceMovement(.25f); player.UpdateMotion(.25f); // 대시 0.5초의 중간
            Assert.That(Pose.Tilt, Is.EqualTo(-settings.DashLean).Within(1e-3f), "오른쪽 대시는 오른쪽(시계 방향)으로 기움");
            Assert.That(Pose.Squash.y, Is.EqualTo(1 - settings.DashSquash).Within(1e-3f)); Assert.AreEqual(0, Pose.Lift);
            Assert.AreEqual(2, Count("Dash Afterimage"), "잔상 3개 기준 25%·50% 지점을 지남");
            player.AdvanceMovement(.3f); player.UpdateMotion(0);
            Assert.AreEqual(new Vector2Int(5, 0), player.Session.Position);
            Assert.AreEqual(2, Count("Dash Afterimage"), "한 프레임에 도착까지 지나간 75% 지점의 잔상은 생략(문서와 같음)");
            player.UpdateMotion(settings.LandingTime / 2);
            Assert.That(Pose.Squash.x, Is.EqualTo(1 + settings.DashStopSquash).Within(1e-3f), "미끄러지며 멈춤"); Assert.AreEqual(0, Pose.Tilt);
            player.UpdateMotion(1f); AssertIdentity(Pose); Assert.AreEqual(0, Count("Dash Afterimage")); Assert.AreEqual(0, DustCount);
        }
        [Test] public void RollHopsLowerAndWaitNods()
        {
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Roll, new Vector2Int(1, 1))); player.AdvanceMovement(.125f); player.UpdateMotion(.125f);
            Assert.That(Pose.Lift, Is.EqualTo(settings.RollHopHeight).Within(1e-3f));
            player.AdvanceMovement(.2f); player.UpdateMotion(0); player.UpdateMotion(settings.LandingTime / 2);
            Assert.That(Pose.Squash.x, Is.EqualTo(1 + settings.RollLandingSquash).Within(1e-3f)); player.UpdateMotion(1f);
            Assert.True(player.TryBeginAction(PlayerAction.Wait, Vector2Int.zero)); player.AdvanceMovement(.125f); player.UpdateMotion(.125f);
            Assert.That(Pose.Squash.x, Is.EqualTo(1 + settings.WaitSquash).Within(1e-3f), "제자리 끄덕임"); Assert.AreEqual(0, Pose.Lift);
            player.AdvanceMovement(1f); player.UpdateMotion(0); AssertIdentity(Pose); Assert.AreEqual(0, DustCount, "대기는 착지 먼지 없음");
        }
        [Test] public void BreathingRunsOnlyWhileWaitingForInput()
        {
            var so = new SerializedObject(settings); so.FindProperty("breathAmount").floatValue = .05f; so.ApplyModifiedPropertiesWithoutUndo();
            Build();
            player.UpdateMotion(settings.BreathPeriod / 4);
            Assert.That(Pose.Squash.y, Is.EqualTo(1.05f).Within(1e-3f), "숨쉬기 최대로 늘어남");
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.up)); player.UpdateMotion(0);
            Assert.That(Pose.Squash.y, Is.EqualTo(1).Within(1e-4f), "행동이 시작되면 숨쉬기 대신 행동 자세(진행도 0)");
            Run(1f); player.UpdateMotion(.1f);
            Assert.AreEqual(BattlePhase.Waiting, player.Session.Phase); Assert.That(Mathf.Abs(Pose.Squash.y - 1), Is.GreaterThan(1e-3f), "입력 대기로 돌아오면 숨쉬기 재개");
        }
        [Test] public void UndoClearsAfterimages()
        {
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Dash, Vector2Int.up)); player.AdvanceMovement(.4f); player.UpdateMotion(.01f);
            Assert.AreEqual(3, Count("Dash Afterimage"), "긴 프레임이면 지난 지점만큼 한꺼번에 남김");
            player.AdvanceMovement(.2f); Assert.False(player.Session.IsBusy);
            Assert.True(player.TryUndo()); Assert.AreEqual(0, Count("Dash Afterimage")); Assert.AreEqual(0, DustCount); AssertIdentity(Pose);
        }
        [Test] public void DisabledMotionKeepsTheFlatSlide()
        {
            var so = new SerializedObject(settings); so.FindProperty("enabledFeedback").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            Build();
            Assert.True(player.TryBeginAction(PlayerAction.Move, Vector2Int.up)); player.AdvanceMovement(.125f); player.UpdateMotion(.125f);
            AssertIdentity(Pose);
            player.AdvanceMovement(1f); player.UpdateMotion(0); Assert.AreEqual(0, DustCount);
            Assert.False(player.TryActionOrBump(PlayerAction.Move, Vector2Int.right * 9)); player.UpdateMotion(.05f); AssertIdentity(Pose);
        }
    }
}
