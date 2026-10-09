using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace IBIIIS.Tests
{
    public sealed class OverworldTests
    {
        private string savePath;
        private GameObject[] spawned = new GameObject[0];
        [SetUp] public void SetUp()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"IBIIIS_progress_test_{System.Guid.NewGuid():N}.json");
            ProgressStore.UseFile(savePath);
        }
        [TearDown] public void TearDown()
        {
            ProgressStore.UseFile(null);
            foreach (var path in new[] { savePath, savePath + ".corrupt", savePath + ".tmp" })
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path); // 저장 실패를 만들려고 같은 이름으로 만든 폴더
            }
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            StageFlow.Begin(null);
        }
        [Test] public void MoveDirectionShowsSideForDiagonalsAndBackOrFrontOnlyForPureVertical()
        {
            PlayerFacing Facing(float x, float y) => PlayerVisual.FacingOf(OverworldPlayer.DirectionOf(new Vector2(x, y)));
            float d = Mathf.Sqrt(.5f);
            Assert.AreEqual(PlayerFacing.Back, Facing(0, 1), "W");
            Assert.AreEqual(PlayerFacing.Front, Facing(0, -1), "S");
            Assert.AreEqual(PlayerFacing.Left, Facing(-1, 0), "A");
            Assert.AreEqual(PlayerFacing.Right, Facing(1, 0), "D");
            Assert.AreEqual(PlayerFacing.Left, Facing(-d, d), "WA");
            Assert.AreEqual(PlayerFacing.Left, Facing(-d, -d), "AS");
            Assert.AreEqual(PlayerFacing.Right, Facing(d, d), "WD");
            Assert.AreEqual(PlayerFacing.Right, Facing(d, -d), "SD");
            Assert.AreEqual(Vector2Int.zero, OverworldPlayer.DirectionOf(Vector2.zero));
            Assert.AreEqual(Vector2Int.zero, OverworldPlayer.DirectionOf(new Vector2(.05f, -.05f)), "데드존 안의 입력은 정지");
        }
        [Test] public void ClearRecordsSurviveReloadWithoutDuplicates()
        {
            Assert.IsFalse(ProgressStore.IsCleared("stage-a"));
            ProgressStore.MarkCleared("stage-a"); ProgressStore.MarkCleared("stage-a");
            ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsCleared("stage-a"));
            Assert.IsFalse(ProgressStore.IsCleared("stage-b"));
            Assert.AreEqual(1, ProgressStore.ClearedStages.Count);
            Assert.Throws<System.ArgumentException>(() => ProgressStore.MarkCleared(""));
            ProgressStore.ResetAll();
            Assert.IsFalse(File.Exists(savePath)); Assert.IsFalse(ProgressStore.IsCleared("stage-a"));
        }
        [Test] public void UnreadableSaveIsBackedUpBeforeBeingReplaced()
        {
            File.WriteAllText(savePath, "{ not json");
            ProgressStore.Reload();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("클리어 기록"));
            Assert.IsFalse(ProgressStore.IsCleared("stage-a"));
            Assert.AreEqual("{ not json", File.ReadAllText(savePath + ".corrupt"));
            ProgressStore.MarkCleared("stage-a"); ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsCleared("stage-a"));
        }
        [Test] public void FailedSaveDoesNotMarkStageClearedAndCanBeRetried()
        {
            Directory.CreateDirectory(savePath + ".tmp"); // 임시 파일 자리를 폴더로 막아 저장을 실패시킨다
            Assert.Throws<System.UnauthorizedAccessException>(() => ProgressStore.MarkCleared("stage-a"));
            Assert.IsFalse(ProgressStore.IsCleared("stage-a"), "저장에 실패하면 깃발도 켜지지 않는다");
            Directory.Delete(savePath + ".tmp");
            ProgressStore.MarkCleared("stage-a"); ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsCleared("stage-a"));
        }
        [Test] public void UnreadableSaveBlocksSavingInsteadOfOverwriting()
        {
            File.WriteAllText(savePath, "{\"version\":1,\"clearedStages\":[\"stage-old\"]}");
            var original = File.ReadAllText(savePath);
            using (new FileStream(savePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // 다른 프로그램이 잠근 상황
            {
                ProgressStore.Reload();
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("읽지 못해 저장하지 않습니다"));
                Assert.IsFalse(ProgressStore.IsCleared("stage-old"));
                Assert.IsNotNull(ProgressStore.SaveBlockedReason);
                Assert.Throws<System.InvalidOperationException>(() => ProgressStore.MarkCleared("stage-a"));
            }
            Assert.AreEqual(original, File.ReadAllText(savePath));
            Assert.IsFalse(File.Exists(savePath + ".corrupt"), "읽기 실패는 형식 오류가 아니므로 사본을 만들지 않는다");
            ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsCleared("stage-old")); Assert.IsNull(ProgressStore.SaveBlockedReason);
        }
        [Test] public void CorruptSaveIsKeptWhenBackupCannotBeMade()
        {
            File.WriteAllText(savePath, "{ not json");
            Directory.CreateDirectory(savePath + ".corrupt"); // 사본 자리를 폴더로 막아 복사를 실패시킨다
            ProgressStore.Reload();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("사본도 만들지 못해"));
            Assert.IsFalse(ProgressStore.IsCleared("stage-a"));
            Assert.Throws<System.InvalidOperationException>(() => ProgressStore.MarkCleared("stage-a"));
            Assert.AreEqual("{ not json", File.ReadAllText(savePath));
        }
        [Test] public void ReturnPointIsGivenOnceAndOnlyToTheReturnScene()
        {
            var run = new StageRun("stage-a", "A", "Assets/Battle.unity", "Assets/Overworld.unity", new Vector3(1, 0, 2), PlayerFacing.Left);
            StageFlow.Begin(run);
            Assert.IsFalse(StageFlow.TakeReturnPoint("Assets/Overworld.unity", out _, out _), "전투 중(돌아가기 전)에는 복원하지 않음");
            Assert.IsTrue(StageFlow.BeginReturn());
            Assert.IsFalse(StageFlow.TakeReturnPoint("Assets/Other.unity", out _, out _), "다른 씬에는 복원하지 않음");
            Assert.IsTrue(StageFlow.TakeReturnPoint("Assets/Overworld.unity", out var position, out var facing));
            Assert.AreEqual(new Vector3(1, 0, 2), position); Assert.AreEqual(PlayerFacing.Left, facing);
            Assert.IsNull(StageFlow.Current);
            Assert.IsFalse(StageFlow.TakeReturnPoint("Assets/Overworld.unity", out _, out _), "한 번만 복원");
        }
        private NpcSpeaker Npc(Vector3 position, params string[] lines)
        {
            var go = new GameObject("NPC"); go.transform.position = position;
            System.Array.Resize(ref spawned, spawned.Length + 1); spawned[spawned.Length - 1] = go;
            var npc = go.AddComponent<NpcSpeaker>();
            var so = new SerializedObject(npc); var array = so.FindProperty("lines"); array.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++) array.GetArrayElementAtIndex(i).stringValue = lines[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return npc;
        }
        [Test] public void NearestTargetIgnoresFarAndSilentCandidates()
        {
            var near = Npc(new Vector3(1, 0, 0), "a");
            var farther = Npc(new Vector3(0, 0, 1.2f), "b");
            var outOfRange = Npc(new Vector3(.2f, 5, 3), "c");
            var silent = Npc(new Vector3(.5f, 0, 0));
            var all = new OverworldInteractable[] { farther, outOfRange, silent, near };
            Assert.AreSame(near, OverworldInteractable.FindNearest(Vector3.zero, all), "높이는 무시하고 바닥 평면 거리로 판정");
            Assert.IsNull(OverworldInteractable.FindNearest(new Vector3(10, 0, 10), all));
            Assert.IsNull(silent.PromptVerb, "대사가 없으면 상호작용 대상이 아님");
        }
        private GameObject Track(GameObject go) { System.Array.Resize(ref spawned, spawned.Length + 1); spawned[spawned.Length - 1] = go; return go; }
        [Test] public void CameraFacingSpriteStandsParallelToTheCamera()
        {
            var camera = Track(new GameObject("Camera", typeof(Camera))).GetComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(40, 0, 0);
            var sprite = Track(new GameObject("Sprite")).transform; sprite.rotation = Quaternion.Euler(0, 90, 0);
            CameraFacingSprite.Face(sprite, camera);
            Assert.That(Quaternion.Angle(camera.transform.rotation, sprite.rotation), Is.LessThan(.01f));
            Assert.AreEqual(1, Vector3.Dot(sprite.forward, camera.transform.forward), 1e-4f, "그림 평면이 화면과 나란함");
            CameraFacingSprite.Face(sprite, null); // 카메라가 없으면 그대로
            Assert.That(Quaternion.Angle(camera.transform.rotation, sprite.rotation), Is.LessThan(.01f));
        }
        [Test] public void NpcPrefabLookChangesDoNotAffectBlockingOrTalkRange()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IBIIIS.Editor.AssetPaths.NpcPrefab);
            Assert.IsNotNull(prefab, "NPC 기본 프리팹이 있어야 함");
            var npc = Track((GameObject)PrefabUtility.InstantiatePrefab(prefab));
            Assert.IsNull(npc.GetComponent<Renderer>(), "루트는 판정만 담당하고 그림은 자식에 둠");
            var visual = npc.transform.Find("Visual");
            Assert.IsNotNull(visual.GetComponent<SpriteRenderer>().sprite); Assert.IsNotNull(visual.GetComponent<CameraFacingSprite>());
            Assert.IsNotNull(visual.GetComponent<BreathingSprite>(), "NPC 숨쉬기");
            Assert.IsNotNull(npc.transform.Find(IBIIIS.Editor.GroundMarkerSetup.ShadowName), "발밑 그림자");
            var speaker = npc.GetComponent<NpcSpeaker>(); var collider = npc.GetComponent<CapsuleCollider>();
            var probes = new[] { new Vector3(1.4f, 0, 0), new Vector3(1.6f, 0, 0), new Vector3(0, 0, -1.2f) };
            bool[] Ranges() { var r = new bool[probes.Length]; for (int i = 0; i < probes.Length; i++) r[i] = speaker.InRange(probes[i]); return r; }
            var before = Ranges(); var center = collider.center; var height = collider.height; var radius = collider.radius;
            visual.localScale = Vector3.one * 4; visual.localPosition = new Vector3(.5f, 1, 0); visual.GetComponent<SpriteRenderer>().sprite = null;
            CollectionAssert.AreEqual(before, Ranges());
            Assert.AreEqual(center, collider.center); Assert.AreEqual(height, collider.height); Assert.AreEqual(radius, collider.radius);
        }
        [Test] public void ClearedFlagSpriteShowsOnlyAfterTheStageIsCleared()
        {
            var flagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(IBIIIS.Editor.AssetPaths.ClearedFlagPrefab);
            Assert.IsNotNull(flagPrefab, "클리어 깃발 프리팹이 있어야 함");
            var stage = Track(new GameObject("Stage"));
            var flag = (GameObject)PrefabUtility.InstantiatePrefab(flagPrefab, stage.transform);
            Assert.IsNotNull(flag.GetComponent<SpriteRenderer>().sprite); Assert.IsNotNull(flag.GetComponent<CameraFacingSprite>());
            var entrance = stage.AddComponent<StageEntrance>();
            var so = new SerializedObject(entrance);
            so.FindProperty("stageId").stringValue = "stage-flag"; so.FindProperty("clearedIndicator").objectReferenceValue = flag;
            so.ApplyModifiedPropertiesWithoutUndo();
            entrance.RefreshIndicator(); Assert.IsFalse(flag.activeSelf);
            ProgressStore.MarkCleared("stage-flag");
            entrance.RefreshIndicator(); Assert.IsTrue(flag.activeSelf);
            ProgressStore.ResetAll();
            entrance.RefreshIndicator(); Assert.IsFalse(flag.activeSelf);
        }
        private readonly System.Collections.Generic.List<Object> assets = new System.Collections.Generic.List<Object>();
        [TearDown] public void DestroyAssets() { foreach (var a in assets) if (a != null) Object.DestroyImmediate(a); assets.Clear(); }
        private T Asset<T>() where T : ScriptableObject { var a = ScriptableObject.CreateInstance<T>(); assets.Add(a); return a; }
        private static void Set(Object target, string property, object value)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(property);
            if (value is bool b) p.boolValue = b; else if (value is float f) p.floatValue = f; else if (value is int i) p.intValue = i;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // 오른쪽으로 distance만큼 frames번에 나눠 막힘 없이 걷는다.
        private static MotionPose Walk(OverworldMotion motion, float distance, int frames)
        {
            var pose = MotionPose.Identity; float step = distance / frames;
            for (int i = 0; i < frames; i++) { var d = new Vector3(step, 0, 0); pose = motion.Tick(step / 3.5f, Vector3.zero, d, d); }
            return pose;
        }
        [Test] public void WalkCycleCountsDistanceNotFrames()
        {
            var shared = Asset<MotionFeedbackSettings>(); var overworld = Asset<OverworldSettings>();
            using (var oneFrame = new OverworldMotion(shared, overworld, null, null))
            using (var manyFrames = new OverworldMotion(shared, overworld, null, null))
            {
                var a = Walk(oneFrame, 3.1f, 1); var b = Walk(manyFrames, 3.1f, 97);
                Assert.AreEqual(3, oneFrame.Steps, "0.9칸 걸음 × 3 = 2.7칸"); Assert.AreEqual(oneFrame.Steps, manyFrames.Steps);
                Assert.AreEqual(oneFrame.StrideProgress, manyFrames.StrideProgress, 1e-4f);
                Assert.AreEqual(a.Lift, b.Lift, 1e-4f); Assert.AreEqual(a.Squash.y, b.Squash.y, 1e-4f);
                Assert.Greater(a.Lift, 0, "걸음 중간에는 떠 있음");
                Assert.Less(a.Lift, shared.HopHeight, "전투 1칸 뜀보다 낮음(배율 0.5)");
            }
        }
        [Test] public void StoppingSquashesThenBreathesAndWallBumpFiresOncePerBlock()
        {
            var shared = Asset<MotionFeedbackSettings>(); var overworld = Asset<OverworldSettings>();
            using (var motion = new OverworldMotion(shared, overworld, null, null))
            {
                Walk(motion, 1.3f, 20);
                var stop = motion.Tick(.03f, Vector3.zero, Vector3.zero, Vector3.zero);
                Assert.IsFalse(motion.IsWalking); Assert.Greater(stop.Squash.x, 1, "멈출 때 납작해짐");
                var idle = MotionPose.Identity; for (int i = 0; i < 30; i++) idle = motion.Tick(.05f, Vector3.zero, Vector3.zero, Vector3.zero);
                Assert.AreEqual(0, idle.Lift); Assert.That(Mathf.Abs(idle.Squash.y - 1), Is.LessThanOrEqualTo(shared.BreathAmount + 1e-4f), "멈춰 있으면 숨쉬기 정도만");
                var push = new Vector3(0, 0, .1f);
                var bump = motion.Tick(.03f, Vector3.zero, push, Vector3.zero);
                Assert.Greater(bump.Shift.z, 0, "막힌 쪽으로 부딪힘");
                var later = MotionPose.Identity; for (int i = 0; i < 20; i++) later = motion.Tick(.05f, Vector3.zero, push, Vector3.zero);
                Assert.AreEqual(Vector3.zero, later.Shift, "계속 밀어도 한 번만");
                motion.Tick(.03f, Vector3.zero, Vector3.zero, Vector3.zero);
                Assert.Greater(motion.Tick(.03f, Vector3.zero, push, Vector3.zero).Shift.z, 0, "손을 뗐다 다시 밀면 다시 부딪힘");
            }
        }
        [Test] public void WalkMotionSwitchesTurnPartsOff()
        {
            var shared = Asset<MotionFeedbackSettings>(); var overworld = Asset<OverworldSettings>();
            Set(overworld, "walkHopScale", 0f); Set(overworld, "stopSquashScale", 0f); Set(overworld, "breathing", false); Set(overworld, "wallBump", false);
            using (var motion = new OverworldMotion(shared, overworld, null, null))
            {
                Assert.AreEqual(0, Walk(motion, .4f, 5).Lift, "걸음 뜀 끔");
                Assert.IsTrue(motion.Tick(.03f, Vector3.zero, Vector3.zero, Vector3.zero).IsIdentity, "멈춤 반응·숨쉬기 끔");
                Assert.IsTrue(motion.Tick(.03f, Vector3.zero, new Vector3(.1f, 0, 0), Vector3.zero).IsIdentity, "벽 부딪힘 끔");
            }
            Set(shared, "enabledFeedback", false);
            using (var off = new OverworldMotion(shared, Asset<OverworldSettings>(), null, null))
                Assert.IsTrue(Walk(off, 1, 3).IsIdentity, "공용 손맛을 끄면 미니맵도 꺼짐");
        }
        [Test] public void NpcBreathingOnlyScalesTheVisualAroundItsBase()
        {
            var shared = Asset<MotionFeedbackSettings>();
            var visual = Track(new GameObject("Visual")); visual.transform.localScale = Vector3.one * 1.5f; visual.transform.localPosition = new Vector3(0, 0, 0);
            var breath = visual.AddComponent<BreathingSprite>(); breath.Configure(shared);
            breath.Advance(shared.BreathPeriod / 4);
            Assert.AreNotEqual(1.5f, visual.transform.localScale.y, "숨쉬기로 크기가 변함");
            Assert.AreEqual(1.5f, visual.transform.localScale.y, 1.5f * shared.BreathAmount + 1e-4f);
            Assert.AreEqual(Vector3.zero, visual.transform.localPosition, "위치는 그대로(발밑 기준점)");
            Set(shared, "enabledFeedback", false); breath.Advance(.1f);
            Assert.AreEqual(Vector3.one * 1.5f, visual.transform.localScale, "공용 손맛을 끄면 원래 크기");
        }
        [Test] public void CameraKeepsTheSameViewHeightWhenFieldOfViewChanges()
        {
            var overworld = Asset<OverworldSettings>(); Set(overworld, "cameraViewHeight", 14f);
            var player = Track(new GameObject("Player")).transform; player.position = new Vector3(3, 0, -2);
            var cameraGo = Track(new GameObject("Camera", typeof(Camera), typeof(OverworldCamera)));
            var camera = cameraGo.GetComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(40, 0, 0);
            var follow = cameraGo.GetComponent<OverworldCamera>(); follow.Configure(player, overworld);
            var focus = player.position + Vector3.up * .5f;
            foreach (var fov in new[] { 75f, 30f })
            {
                camera.fieldOfView = fov; follow.Snap();
                Assert.AreEqual(.5f, camera.WorldToViewportPoint(focus).y, 1e-3f, $"화각 {fov}: 플레이어가 화면 가운데");
                Assert.AreEqual(1f, camera.WorldToViewportPoint(focus + camera.transform.up * 7).y, 1e-3f, $"화각 {fov}: 세로 14칸이 화면 높이");
            }
            Assert.AreEqual(7 / Mathf.Tan(15 * Mathf.Deg2Rad), follow.Distance, 1e-3f, "화각 30이면 약 26칸 떨어짐");
            camera.orthographic = true; camera.orthographicSize = 3;
            Assert.AreEqual(20, follow.Distance, 1e-4f); Assert.AreEqual(3, camera.orthographicSize, "거리를 읽기만 해서는 카메라를 바꾸지 않음");
            follow.Snap(); Assert.AreEqual(7, camera.orthographicSize, 1e-4f, "Snap에서 세로 14칸에 맞춤");
        }
        private static Vector3 Travel(OverworldEvade evade, int frames, float total)
        {
            var sum = Vector3.zero; for (int i = 0; i < frames && evade.Active; i++) { sum += evade.Advance(total / frames); if (evade.ReachedEnd) evade.Finish(); }
            return sum;
        }
        [Test] public void RollTravelsItsDistanceRegardlessOfFramesThenCoolsDown()
        {
            var a = new OverworldEvade(); var b = new OverworldEvade();
            Assert.IsTrue(a.TryStart(new Vector3(1, 0, 1), 1.5f, .35f, .3f));
            Assert.IsTrue(b.TryStart(new Vector3(1, 0, 1), 1.5f, .35f, .3f));
            var oneFrame = Travel(a, 1, .35f); var manyFrames = Travel(b, 37, .35f);
            Assert.AreEqual(1.5f, oneFrame.magnitude, 1e-4f); Assert.AreEqual(oneFrame.x, manyFrames.x, 1e-4f); Assert.AreEqual(oneFrame.z, manyFrames.z, 1e-4f);
            Assert.AreEqual(oneFrame.x, oneFrame.z, 1e-4f, "대각선 방향 그대로");
            Assert.IsFalse(a.Active); Assert.IsFalse(a.CanStart, "끝나면 쿨다운");
            Assert.IsFalse(a.TryStart(Vector3.right, 1.5f, .35f, .3f), "쿨다운 중에는 다시 구를 수 없음");
            a.TickCooldown(.2f); Assert.IsFalse(a.CanStart); a.TickCooldown(.11f); Assert.IsTrue(a.CanStart);
            Assert.IsTrue(a.TryStart(Vector3.right, 1.5f, .35f, .3f));
            a.Advance(.1f); a.Finish(); Assert.IsFalse(a.Active, "막히면 일찍 끝냄"); Assert.Greater(a.CooldownLeft, 0);
            Assert.IsFalse(new OverworldEvade().TryStart(Vector3.zero, 1, 1, 0), "방향이 없으면 시작하지 않음");
        }
        [Test] public void BlockedCheckWorksAtAnyFrameRate()
        {
            float minMove = .001f * 1.5f; // 미니맵 플레이어 프리팹의 CharacterController.minMoveDistance 기준
            foreach (var dt in new[] { .001f, .02f })
            {
                var start = new Vector3(8.57f * dt, 0, 0); // 구르기 첫 프레임(1.5칸·0.35초·감속 1의 시작 속도)
                Assert.IsTrue(OverworldMotion.IsBlocked(start, Vector3.zero, minMove), $"dt {dt}: 벽 앞이면 막힘");
                Assert.IsFalse(OverworldMotion.IsBlocked(start, start, minMove), $"dt {dt}: 빈 공간은 막힘 아님");
                Assert.IsFalse(OverworldMotion.IsBlocked(start, start * .5f, minMove), $"dt {dt}: 벽을 따라 미끄러지면 막힘 아님");
            }
            Assert.IsFalse(OverworldMotion.IsBlocked(new Vector3(.0005f, 0, 0), Vector3.zero, minMove), "CharacterController가 무시하는 최소 이동 미만은 막힘으로 보지 않음");
            Assert.IsFalse(OverworldMotion.IsBlocked(new Vector3(0, .5f, 0), Vector3.zero, minMove), "수평 성분만 봄");
        }
        [Test] public void RollBurstsOutThenStopsAndRecoversBeforeCooldown()
        {
            var a = new OverworldEvade(); var b = new OverworldEvade();
            Assert.IsTrue(a.TryStart(Vector3.right, 1.5f, .35f, .25f, 1, .2f));
            Assert.IsTrue(b.TryStart(Vector3.right, 1.5f, .35f, .25f, 1, .2f));
            var firstHalf = a.Advance(.175f).x;
            Assert.AreEqual(1.5f * .75f, firstHalf, 1e-4f, "시간 절반에 거리 3/4(처음이 빠름)");
            var first = b.Advance(.035f).x; b.Advance(.28f); var last = b.Advance(.035f).x;
            Assert.Greater(first, .035f / .35f * 1.5f * 1.8f, "첫 구간 속도는 평균의 약 2배"); Assert.Less(last, first * .15f, "끝 구간은 거의 멈춤");
            a.Advance(.175f); Assert.IsTrue(a.ReachedEnd); a.Finish();
            Assert.IsTrue(a.Recovering); Assert.IsFalse(a.CanStart);
            a.TickCooldown(.15f); Assert.IsTrue(a.Recovering); Assert.AreEqual(.25f, a.CooldownLeft, 1e-5f, "회복 중에는 쿨다운이 줄지 않음");
            a.TickCooldown(.1f); Assert.IsFalse(a.Recovering); Assert.AreEqual(.2f, a.CooldownLeft, 1e-4f, "회복 뒤 남은 시간만큼 쿨다운 감소");
            a.TickCooldown(.2f); Assert.IsTrue(a.CanStart);
            var total = 0f; var c = new OverworldEvade(); c.TryStart(Vector3.right, 1.5f, .35f, 0, 1, .2f);
            for (int i = 0; i < 23 && c.Active; i++) { total += c.Advance(.35f / 23).x; if (c.ReachedEnd) c.Finish(); }
            Assert.AreEqual(1.5f, total, 1e-4f, "감속해도 총 거리는 같음");
            Assert.AreEqual(.5f, OverworldEvade.Covered(.5f, 0), 1e-6f, "감속 0이면 일정한 속도(대시)");
        }
        [Test] public void EvadeDirectionFollowsInputElseFacingAndRollUsesDiagonalFrames()
        {
            float d = Mathf.Sqrt(.5f);
            var diagonal = OverworldPlayer.EvadeDirection(new Vector2(-d, d), PlayerFacing.Front);
            Assert.AreEqual(-d, diagonal.x, 1e-4f); Assert.AreEqual(d, diagonal.z, 1e-4f);
            Assert.AreEqual(Vector3.left, OverworldPlayer.EvadeDirection(Vector2.zero, PlayerFacing.Left), "멈춰 있으면 바라보는 방향");
            Assert.AreEqual(Vector3.forward, OverworldPlayer.EvadeDirection(Vector2.zero, PlayerFacing.Back));
            Assert.AreEqual(new Vector2Int(-1, 1), OverworldPlayer.RollSpriteDirection(new Vector3(-d, 0, d), PlayerFacing.Front));
            Assert.AreEqual(new Vector2Int(-1, -1), OverworldPlayer.RollSpriteDirection(Vector3.left, PlayerFacing.Left), "옆으로 구르면 앞쪽 대각 그림");
            Assert.AreEqual(new Vector2Int(1, 1), OverworldPlayer.RollSpriteDirection(Vector3.forward, PlayerFacing.Back), "위로 구르면 오른쪽 뒤 그림");
            Assert.AreEqual(new Vector2Int(-1, 1), OverworldPlayer.RollSpriteDirection(Vector3.forward, PlayerFacing.Left), "바라보던 쪽 유지");
        }
        [Test] public void RollHopsAndLandsWithSquash()
        {
            var shared = Asset<MotionFeedbackSettings>();
            using (var motion = new OverworldMotion(shared, Asset<OverworldSettings>(), null, null))
            {
                motion.BeginRoll();
                Assert.Greater(motion.TickRoll(.01f, .5f).Lift, 0, "구르기는 낮게 뜀");
                motion.EndRoll(Vector3.zero);
                Assert.Greater(motion.Tick(.02f, Vector3.zero, Vector3.zero, Vector3.zero).Squash.x, 1, "구르기 착지 납작함");
            }
        }
        [Test] public void RunningTakesLongerStridesThanWalking()
        {
            var shared = Asset<MotionFeedbackSettings>(); var overworld = Asset<OverworldSettings>();
            Assert.Greater(overworld.RunSpeed, overworld.MoveSpeed, "달리기가 걷기보다 빠름");
            using (var walk = new OverworldMotion(shared, overworld, null, null))
            using (var run = new OverworldMotion(shared, overworld, null, null))
            {
                var d = new Vector3(.1f, 0, 0);
                for (int i = 0; i < 40; i++) { walk.Tick(.02f, Vector3.zero, d, d); run.Tick(.02f, Vector3.zero, d, d, true); }
                Assert.AreEqual(4, walk.Steps, "4칸 ÷ 걷기 보폭 0.9");
                Assert.AreEqual(3, run.Steps, "4칸 ÷ 달리기 보폭 1.3");
                var small = new Vector3(.001f, 0, 0);
                float before = run.StrideProgress; run.Tick(.0003f, Vector3.zero, small, small);
                Assert.AreEqual(before + .001f / overworld.StrideLength, run.StrideProgress, 1e-4f, "걷기로 바뀌어도 걸음 진행 비율은 이어짐");
            }
        }
        [Test] public void PopupOffersReturnWhenBattleCannotStart()
        {
            var map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(3, 3); // 시작 위치가 없어 전투를 시작할 수 없는 맵
            var battle = Track(new GameObject("Battle")).AddComponent<GridMapPlayer>(); battle.Configure(map, null, null);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("시작 위치"));
            battle.Build();
            var popup = StageResultPopup.Create(new StageRun("stage-a", "A", "Assets/Battle.unity", "Assets/Overworld.unity", Vector3.zero, PlayerFacing.Front));
            Track(popup.gameObject);
            Assert.IsNull(popup.Problem, "지켜볼 전투가 없으면 문제 없음");
            popup.Watch(battle);
            Assert.IsNull(battle.Session); Assert.IsFalse(battle.enabled);
            StringAssert.Contains("전투를 시작하지 못했습니다", popup.Problem);
            Object.DestroyImmediate(map);
        }
                [Test] public void SpeechBubbleAdvancesLineByLineThenCloses()
        {
            var npc = Npc(Vector3.zero, "첫째", "둘째");
            Assert.IsFalse(npc.IsTalking); Assert.AreEqual("말 걸기", npc.PromptVerb);
            npc.Interact(null); Assert.AreEqual("첫째", npc.CurrentLine); Assert.AreEqual("다음", npc.PromptVerb);
            npc.Interact(null); Assert.AreEqual("둘째", npc.CurrentLine); Assert.AreEqual("닫기", npc.PromptVerb);
            npc.Interact(null); Assert.IsFalse(npc.IsTalking);
            npc.Interact(null); npc.OnLeft(); Assert.IsFalse(npc.IsTalking, "범위를 벗어나면 닫힘");
        }
    }
    public sealed class OverworldInputTests : InputTestFixture
    {
        private Keyboard keyboard;
        private OverworldInput input;
        public override void Setup() { base.Setup(); keyboard = InputSystem.AddDevice<Keyboard>(); input = new OverworldInput(); input.Enable(); }
        public override void TearDown() { input.Dispose(); base.TearDown(); }
        [Test] public void DefaultKeysMoveFreelyAndFInteracts()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.A)); InputSystem.Update();
            var move = input.ReadMove();
            Assert.AreEqual(-Mathf.Sqrt(.5f), move.x, 1e-3f); Assert.AreEqual(Mathf.Sqrt(.5f), move.y, 1e-3f);
            Assert.IsFalse(input.InteractPressed());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); InputSystem.Update();
            Assert.IsTrue(input.InteractPressed()); Assert.AreEqual(Vector2.zero, input.ReadMove());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow)); InputSystem.Update();
            Assert.AreEqual(Vector2.down, input.ReadMove());
            Assert.AreEqual("F", input.Key(OverworldInput.Interact));
        }
        [Test] public void HoldingShiftRunsAndSpaceRolls()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.D)); InputSystem.Update();
            Assert.IsTrue(input.DashHeld()); Assert.IsFalse(input.RollPressed());
            InputSystem.Update();
            Assert.IsTrue(input.DashHeld(), "누르고 있는 동안 계속 달림");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); InputSystem.Update();
            Assert.IsFalse(input.DashHeld(), "떼면 걷기");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
            Assert.IsTrue(input.RollPressed());
        }
        [Test] public void OlderAssetWithoutDashAndRollGetsDefaultKeysWithoutWarning()
        {
            var old = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = old.AddActionMap(OverworldInput.MapName);
            map.AddAction(OverworldInput.Move, InputActionType.Value, "<Keyboard>/w", expectedControlLayout: "Vector2");
            map.AddAction(OverworldInput.Interact, InputActionType.Button, "<Keyboard>/g");
            using (var upgraded = new OverworldInput(old))
            {
                upgraded.Enable();
                Assert.AreEqual("G", upgraded.Key(OverworldInput.Interact), "기존 바인딩 유지");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
                Assert.IsTrue(upgraded.RollPressed(), "구르기 기본 키 추가");
            }
            Assert.IsNull(map.FindAction(OverworldInput.Dash), "원본 에셋은 바꾸지 않음(사본 사용)");
            LogAssert.NoUnexpectedReceived();
            Object.DestroyImmediate(old);
        }
        [Test] public void MoveBoundToASingleKeyIsIgnoredWithOneWarning()
        {
            var wrong = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = wrong.AddActionMap(OverworldInput.MapName);
            map.AddAction(OverworldInput.Move, InputActionType.Value, "<Keyboard>/w"); // Vector2가 아닌 버튼 값
            map.AddAction(OverworldInput.Interact, InputActionType.Button, "<Keyboard>/f");
            using (var wrongInput = new OverworldInput(wrong))
            {
                wrongInput.Enable();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("2D Vector"));
                Assert.AreEqual(Vector2.zero, wrongInput.ReadMove(), "예외 없이 무시");
                Assert.AreEqual(Vector2.zero, wrongInput.ReadMove()); // 경고는 한 번만
                LogAssert.NoUnexpectedReceived();
            }
            Object.DestroyImmediate(wrong);
        }
        [Test] public void AssetWithoutOverworldMapFallsBackToDefaults()
        {
            var battleOnly = BattleInput.CreateDefaultAsset();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Overworld"));
            using (var fallback = new OverworldInput(battleOnly)) Assert.AreEqual("F", fallback.Key(OverworldInput.Interact));
            Object.DestroyImmediate(battleOnly);
        }
    }
}
