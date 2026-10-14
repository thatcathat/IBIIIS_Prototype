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
            var high = Npc(new Vector3(.8f, 3, 0), "d"); // 바닥 거리 0.8로 가장 가깝지만 3D 거리는 범위 밖
            var silent = Npc(new Vector3(.5f, 0, 0));
            var all = new OverworldInteractable[] { farther, outOfRange, silent, near, high };
            Assert.AreSame(high, OverworldInteractable.FindNearest(Vector3.zero, all), "높이는 무시하고 바닥 평면 거리로 판정");
            all = new OverworldInteractable[] { farther, outOfRange, silent, near };
            Assert.AreSame(near, OverworldInteractable.FindNearest(Vector3.zero, all));
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
        [Test] public void WinningTheWatchedBattleSavesTheClearOnceAndUndoKeepsIt()
        {
            var map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(5, 1);
            for (int x = 0; x < 5; x++) map.SetWalkable(new Vector2Int(x, 0), true);
            map.SetStart(new Vector2Int(4, 0));
            foreach (var (x, facing) in new[] { (0, Vector2Int.right), (2, Vector2Int.left) }) // 첫 행동에 (1,0)에서 충돌
            {
                var enemy = Track(new GameObject("Enemy")); var so = new SerializedObject(enemy.AddComponent<EnemyDefinition>());
                so.FindProperty("recognition").arraySize = 0; so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, 0), enemy, facing);
            }
            var battle = Track(new GameObject("Battle")).AddComponent<GridMapPlayer>(); battle.Configure(map, null, null); battle.Build();
            var popup = StageResultPopup.Create(new StageRun("stage-win", "W", "Assets/Battle.unity", "Assets/Overworld.unity", Vector3.zero, PlayerFacing.Front));
            Track(popup.gameObject); popup.Watch(battle);
            popup.CheckResult(); Assert.IsFalse(ProgressStore.IsCleared("stage-win"), "승리 전에는 저장하지 않음");
            Assert.IsFalse(popup.TryGetView(out _, out _, out _), "결과 전에는 팝업 없음"); popup.Confirm(); Assert.IsNull(StageFlow.Current, "보이지 않을 때 확인은 무시");
            Assert.True(battle.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
            for (int i = 0; i < 200 && (battle.Session.IsBusy || battle.IsPresenting); i++) { battle.AdvanceMovement(.05f); popup.CheckResult(); }
            Assert.AreEqual(BattlePhase.Won, popup.Result);
            popup.CheckResult(); Assert.IsTrue(ProgressStore.IsCleared("stage-win")); Assert.AreEqual(1, ProgressStore.ClearedStages.Count);
            Assert.IsNull(popup.Problem);
            Assert.IsTrue(popup.TryGetView(out var heading, out var message, out _)); Assert.AreEqual("클리어!", heading); StringAssert.Contains("W", message);
            Assert.IsFalse(popup.UsesGameUi, "Game UI 설정 없이 만들면 임시 표시");
            Assert.True(battle.TryUndo()); Assert.AreEqual(BattlePhase.Waiting, popup.Result, "되돌리면 팝업이 사라짐");
            ProgressStore.Reload(); Assert.IsTrue(ProgressStore.IsCleared("stage-win"), "저장한 기록은 유지");
            Object.DestroyImmediate(map);
        }
        // 첫 대기에서 두 적이 (1,0)에서 부딪혀 이기는 1x5 맵.
        private GridMapPlayer WinnableBattle(out GridMap map, int start = 4, bool single = false)
        {
            map = ScriptableObject.CreateInstance<GridMap>(); map.Resize(5, 1);
            for (int x = 0; x < 5; x++) map.SetWalkable(new Vector2Int(x, 0), true);
            map.SetStart(new Vector2Int(start, 0));
            foreach (var (x, facing) in single ? new[] { (0, Vector2Int.right) } : new[] { (0, Vector2Int.right), (2, Vector2Int.left) })
            {
                var enemy = Track(new GameObject("Enemy")); var so = new SerializedObject(enemy.AddComponent<EnemyDefinition>());
                so.FindProperty("recognition").arraySize = 0; so.ApplyModifiedPropertiesWithoutUndo(); map.PlaceEnemy(new Vector2Int(x, 0), enemy, facing);
            }
            var battle = Track(new GameObject("Battle")).AddComponent<GridMapPlayer>(); battle.Configure(map, null, null); battle.Build();
            return battle;
        }
        private static ScenarioDirector ClearTalk(GridMapPlayer battle, ScenarioScript script, bool onlyOnce)
        {
            var options = script.Options; options.startTiming = ScenarioStartTiming.BattleWon; options.onlyOnce = onlyOnce; script.SetOptions(options);
            var director = battle.gameObject.AddComponent<ScenarioDirector>(); director.Configure(battle, script);
            return director;
        }
        private static void FinishLine(ScenarioDirector director) { var r = director.Dialogue.Runner; r.Advance(); r.Advance(); }
        // Play 중 매 프레임 호출되는 진행기 Update를 한 번 실행한다.
        private static void Frame(ScenarioDirector director) => typeof(ScenarioDirector).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(director, null);
        private static ScenarioStep Say(string text) => new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate(text) };
        private static ScenarioStep Marker(ScenarioStepType type) => new ScenarioStep { type = type };
        // 세 구간 대본: [입장 전 "닿았다"] [입장 후 "가자!"] [클리어 후 "해냈다!"]
        private static ScenarioScript ThreePart(string id, bool onlyOnce)
        {
            var script = ScriptableObject.CreateInstance<ScenarioScript>();
            script.Set(id, new[] { Say("닿았다"), Marker(ScenarioStepType.BattleSection), Say("가자!"), Marker(ScenarioStepType.ClearSection), Say("해냈다!") });
            var options = script.Options; options.onlyOnce = onlyOnce; script.SetOptions(options);
            return script;
        }
        private static void CloseDialogue() { var dialogue = Object.FindAnyObjectByType<DialogueController>(); if (dialogue != null) Object.DestroyImmediate(dialogue.gameObject); }
        [Test] public void BattleDirectorRunsOnlyTheEntryPartAndDoesNotHoldTheWin()
        {
            var battle = WinnableBattle(out var map);
            var script = ThreePart("three", true);
            try
            {
                var director = battle.gameObject.AddComponent<ScenarioDirector>(); director.Configure(battle, script);
                var popup = StageResultPopup.Create(new StageRun("stage-three", "T", "Assets/Battle.unity", "Assets/Overworld.unity", Vector3.zero, PlayerFacing.Front));
                Track(popup.gameObject); popup.Watch(battle);
                Frame(director);
                Assert.IsTrue(director.IsRunning); Assert.AreEqual(2, director.StepIndex, "입장 후 구간 첫 단계(입장 전 구간은 전투씬에서 실행하지 않음)");
                Assert.IsFalse(battle.AllowUndoRestart);
                FinishLine(director);
                Assert.IsFalse(director.IsRunning); Assert.IsTrue(director.Finished, "입장 후 구간에서 끝(클리어 후 구간은 어드벤처씬)");
                Assert.IsTrue(battle.AllowUndoRestart, "끝나면 일반 전투"); Assert.IsTrue(ProgressStore.IsSeen("three"));
                battle.Restart(); battle = director.Battle;
                for (int i = 0; i < 3; i++) Frame(director);
                Assert.IsFalse(director.IsRunning, "재시작해도 다시 나오지 않음");
                Assert.True(battle.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
                for (int i = 0; i < 200 && (battle.Session.IsBusy || battle.IsPresenting); i++) battle.AdvanceMovement(.05f);
                for (int i = 0; i < 3; i++) Frame(director);
                Assert.IsFalse(director.IsRunning, "이겨도 전투씬에서는 클리어 후 구간을 실행하지 않음");
                Assert.IsTrue(popup.TryGetView(out var heading, out _, out _), "결과 팝업이 바로 뜸"); Assert.AreEqual("클리어!", heading);
            }
            finally { CloseDialogue(); Object.DestroyImmediate(map); Object.DestroyImmediate(script); }
        }
        [Test] public void AdventureDialoguePlaysTheBeforeAndClearPartsOnceEach()
        {
            var script = ThreePart("adv", true);
            try
            {
                bool done = false;
                Assert.IsFalse(AdventureDialogue.Play(script, ScenarioSection.Battle, null, null), "입장 후 구간은 어드벤처씬에서 실행하지 않음");
                Assert.IsTrue(AdventureDialogue.Play(script, ScenarioSection.Before, null, () => done = true));
                var dialogue = Object.FindAnyObjectByType<DialogueController>(); Assert.IsTrue(dialogue.IsOpen);
                Assert.AreEqual("닿았다", dialogue.Runner.Current.text, "입장 전 구간의 대사");
                dialogue.Runner.Advance(); dialogue.Runner.Advance();
                Assert.IsTrue(done, "끝나면 done"); Assert.IsTrue(ProgressStore.IsSeen("adv:before"));
                Assert.IsFalse(AdventureDialogue.Play(script, ScenarioSection.Before, null, null), "'한 번만': 다시 보이지 않음");
                Assert.IsTrue(AdventureDialogue.Play(script, ScenarioSection.Clear, null, null));
                Assert.AreEqual("해냈다!", dialogue.Runner.Current.text, "클리어 후 구간의 대사");
                dialogue.Runner.Advance(); dialogue.Runner.Advance();
                Assert.IsTrue(ProgressStore.IsSeen("adv:clear"));
                var record = ScriptableObject.CreateInstance<ScenarioScript>();
                record.Set("rec", new[] { Marker(ScenarioStepType.BattleSection), Marker(ScenarioStepType.ClearSection), new ScenarioStep { type = ScenarioStepType.RecordClear, text = "story-1" } });
                try { Assert.IsFalse(AdventureDialogue.Play(record, ScenarioSection.Clear, null, null), "대사가 없으면 대화창 없이"); Assert.IsTrue(ProgressStore.IsCleared("story-1"), "클리어 기록은 실행"); }
                finally { Object.DestroyImmediate(record); }
            }
            finally { CloseDialogue(); Object.DestroyImmediate(script); }
        }
        private StageEnemy Enemy(string id, Vector3 position, ScenarioScript script, string scene = "Assets/IBIIIS/Scripts/Tests/NoSuchBattle.unity")
        {
            var enemy = Track(new GameObject("Stage Enemy " + id)).AddComponent<StageEnemy>(); enemy.transform.position = position;
            var so = new UnityEditor.SerializedObject(enemy);
            so.FindProperty("stageId").stringValue = id; so.FindProperty("script").objectReferenceValue = script;
            so.ApplyModifiedPropertiesWithoutUndo();
            // 경로는 OnValidate가 씬 에셋에서 다시 쓰므로 직접 넣는다(시험용 없는 씬: 불러오기가 실패해야 Play 없이 확인할 수 있음).
            typeof(StageEnemy).GetField("battleScenePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(enemy, scene);
            Enable(enemy);
            return enemy;
        }
        // EditMode에서 붙인 컴포넌트는 OnEnable이 불리지 않으므로(Play 중에는 자동) 목록 등록을 직접 부른다.
        private static void Enable(MonoBehaviour component) => component.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(component, null);
        private static void Disable(MonoBehaviour component)
        {
            component.GetType().GetMethod("OnDisable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(component, null);
        }
        [Test] public void StageEnemyContactRulesAndReturnPoint()
        {
            var enemy = Enemy("stage-e", new Vector3(5, 0, 5), null);
            Assert.AreSame(enemy, StageEnemy.FindTouching(new Vector3(5.5f, 0, 5)), "닿음 거리(0.8칸) 안");
            Assert.IsNull(StageEnemy.FindTouching(new Vector3(6, 0, 5)), "닿음 거리 밖");
            Assert.AreSame(enemy, StageEnemy.FindTouching(new Vector3(5.5f, 3, 5)), "높이는 보지 않음(바닥 평면 거리)");
            StageEnemy.IgnoreContactFor(10);
            Assert.IsNull(StageEnemy.FindTouching(new Vector3(5, 0, 5)), "돌아온 직후 접촉 무시 시간");
            StageEnemy.IgnoreContactFor(0);
            ProgressStore.MarkCleared("stage-e");
            Assert.IsNull(StageEnemy.FindTouching(new Vector3(5, 0, 5)), "클리어한 적은 닿지 않음");
            var back = StageEnemy.ReturnPoint(new Vector3(5, 0, 5), new Vector3(5.3f, .2f, 5), .8f, .6f);
            Assert.That(OverworldInteractable.FlatDistance(back, new Vector3(5, 0, 5)), Is.EqualTo(1.4f).Within(1e-4f), "닿음 거리 + 여유만큼 떨어진 자리");
            Assert.That(back.x, Is.GreaterThan(5), "플레이어가 있던 쪽"); Assert.AreEqual(.2f, back.y, 1e-4f, "높이는 그대로");
            Assert.That(StageEnemy.ReturnPoint(Vector3.zero, Vector3.zero, 1, 0).z, Is.EqualTo(-1).Within(1e-4f), "같은 자리면 앞(-Z) 쪽");
            Disable(enemy);
        }
        [Test] public void TouchingAStageEnemyPlaysTheHitHoldsThenDialogueThenEntersBattle()
        {
            var script = ThreePart("touch", false);
            var settings = ScriptableObject.CreateInstance<OverworldSettings>();
            var burst = Track(new GameObject("Burst Source")); burst.AddComponent<SpriteRenderer>();
            var so = new UnityEditor.SerializedObject(settings);
            so.FindProperty("contactImpactPrefab").objectReferenceValue = burst;
            so.FindProperty("contactHoldSeconds").floatValue = .5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                var enemy = Enemy("stage-touch", Vector3.zero, script);
                var player = Track(new GameObject("Player")).AddComponent<OverworldPlayer>();
                so = new UnityEditor.SerializedObject(player); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
                player.transform.position = new Vector3(-.6f, 0, 0);
                enemy.Engage(player);
                Assert.IsTrue(enemy.Engaging); Assert.IsTrue(player.InputLocked, "부딪힌 뒤 멈춰 있음");
                var impact = Object.FindAnyObjectByType<ImpactPop>(); Assert.IsNotNull(impact, "충격 이펙트");
                Track(impact.gameObject);
                Assert.That(impact.transform.position.x, Is.EqualTo(-.3f).Within(1e-4f), "플레이어와 적 사이");
                Assert.IsNull(Object.FindAnyObjectByType<DialogueController>(), "멈춤 동안은 대화 없음"); Assert.AreEqual(1, enemy.EngagePhase);
                Assert.IsNull(StageEnemy.FindTouching(Vector3.zero), "진입 중에는 다시 닿지 않음");
                enemy.Advance(.3f); Assert.AreEqual(1, enemy.EngagePhase, "아직 멈춤");
                enemy.Advance(.25f); Assert.AreEqual(2, enemy.EngagePhase, "멈춤 뒤 입장 전 대화");
                var dialogue = Object.FindAnyObjectByType<DialogueController>(); Assert.AreEqual("닿았다", dialogue.Runner.Current.text);
                Assert.IsTrue(player.InputLocked, "대화 동안 이동을 막음");
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("찾을 수 없습니다")); // 시험용 없는 전투 씬
                dialogue.Runner.Advance(); dialogue.Runner.Advance();
                Assert.AreEqual(0, enemy.EngagePhase, "대화가 끝나면 전투 씬으로 들어가려 함");
                Assert.IsFalse(enemy.Engaging, "전투 씬을 불러오지 못하면 다시 닿을 수 있게 됨"); Assert.IsNull(StageFlow.Current);
                Assert.IsFalse(SceneTransition.Busy, "전환도 시작하지 않음"); Assert.IsFalse(player.InputLocked, "이동 다시 가능");
                Disable(enemy);
            }
            finally { CloseDialogue(); Object.DestroyImmediate(script); Object.DestroyImmediate(settings); }
        }
        [Test] public void SceneTransitionCoversLoadsThenUncovers()
        {
            string loaded = null;
            var settings = ScriptableObject.CreateInstance<SceneTransitionSettings>();
            var so = new UnityEditor.SerializedObject(settings); so.FindProperty("outSeconds").floatValue = .4f; so.FindProperty("inSeconds").floatValue = .2f; so.ApplyModifiedPropertiesWithoutUndo();
            SceneTransition.Loader = path => loaded = path;
            try
            {
                Assert.IsTrue(SceneTransition.Begin("Assets/Next.unity", settings));
                Assert.IsTrue(SceneTransition.Busy); Assert.IsFalse(SceneTransition.Begin("Assets/Other.unity", settings), "전환 중에는 다른 전환을 받지 않음");
                var run = SceneTransition.Current; var view = (FadeTransitionView)run.View;
                Assert.AreEqual(SceneTransitionPhase.Out, run.Phase); Assert.AreEqual(0, view.Cover);
                for (int i = 0; i < 6; i++) run.Tick(SceneTransition.MaxStep);
                Assert.That(view.Cover, Is.EqualTo(6 * SceneTransition.MaxStep / .4f).Within(1e-4f), "가리는 중"); Assert.IsNull(loaded, "다 가리기 전에는 불러오지 않음");
                for (int i = 0; i < 20 && run.Phase == SceneTransitionPhase.Out; i++) run.Tick(SceneTransition.MaxStep);
                Assert.AreEqual(SceneTransitionPhase.Loading, run.Phase); Assert.AreEqual("Assets/Next.unity", loaded, "다 가리면 불러옴"); Assert.AreEqual(1, view.Cover);
                run.Tick(5); Assert.AreEqual(SceneTransitionPhase.Loading, run.Phase, "불러오기가 끝날 때까지 가린 채 기다림");
                run.Loaded(); Assert.AreEqual(SceneTransitionPhase.In, run.Phase); Assert.AreEqual(1, view.Cover);
                for (int i = 0; i < SceneTransition.SettleFrames; i++) run.Tick(.5f); // 불러온 직후의 긴 프레임
                Assert.AreEqual(1, view.Cover, "불러온 직후 프레임은 건너뜀(한 번에 밝아지지 않음)");
                run.Tick(.5f); Assert.That(view.Cover, Is.EqualTo(1 - SceneTransition.MaxStep / .2f).Within(1e-4f), "긴 프레임도 최대 한 걸음만 진행");
                for (int i = 0; i < 10 && SceneTransition.Busy; i++) run.Tick(SceneTransition.MaxStep);
                Assert.IsFalse(SceneTransition.Busy, "다 드러내면 사라짐");
                Assert.IsTrue(SceneTransition.Begin("Assets/Again.unity", settings), "끝난 뒤에는 다시 전환 가능");
            }
            finally { SceneTransition.Cancel(); SceneTransition.Loader = null; Object.DestroyImmediate(settings); }
        }
        [Test] public void StageRunCarriesTheScriptAndResultBackToTheAdventureScene()
        {
            var script = ThreePart("back", false);
            try
            {
                var run = new StageRun("stage-back", "B", "Assets/Battle.unity", "Assets/Overworld.unity", new Vector3(1, 0, 2), PlayerFacing.Left, script);
                StageFlow.Begin(run); run.Result = BattlePhase.Won; StageFlow.BeginReturn();
                Assert.IsTrue(StageFlow.TakeReturn("Assets/Overworld.unity", out var back));
                Assert.AreSame(script, back.Script); Assert.AreEqual(BattlePhase.Won, back.Result); Assert.AreEqual(new Vector3(1, 0, 2), back.ReturnPosition);
                Assert.IsNull(StageFlow.Current, "한 번만 가져감");
            }
            finally { Object.DestroyImmediate(script); }
        }
        [Test] public void DialogueZoneStartsOnEnteringAndOnlyOnce()
        {
            var lines = ScriptableObject.CreateInstance<DialogueScript>();
            try
            {
                var so = new UnityEditor.SerializedObject(lines); var list = so.FindProperty("lines"); list.arraySize = 1;
                list.GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue = "여기는 숲이다"; so.ApplyModifiedPropertiesWithoutUndo();
                var zone = Track(new GameObject("Zone")).AddComponent<DialogueZone>(); zone.transform.position = new Vector3(10, 0, 0);
                so = new UnityEditor.SerializedObject(zone); so.FindProperty("zoneId").stringValue = "forest"; so.FindProperty("dialogue").objectReferenceValue = lines; so.ApplyModifiedPropertiesWithoutUndo();
                Enable(zone);
                Assert.IsNull(DialogueZone.UpdateInside(Vector3.zero), "구역 밖");
                Assert.AreSame(zone, DialogueZone.UpdateInside(new Vector3(11, 0, 1)), "구역에 들어온 순간");
                Assert.IsNull(DialogueZone.UpdateInside(new Vector3(10.5f, 0, 0)), "안에 계속 있으면 다시 시작하지 않음");
                Assert.IsTrue(zone.Play(null));
                var dialogue = Object.FindAnyObjectByType<DialogueController>(); Assert.AreEqual("여기는 숲이다", dialogue.Runner.Current.text);
                dialogue.Runner.Advance(); dialogue.Runner.Advance();
                Assert.IsTrue(ProgressStore.IsSeen("zone:forest"));
                DialogueZone.UpdateInside(Vector3.zero);
                Assert.IsNull(DialogueZone.UpdateInside(new Vector3(10, 0, 0)), "'한 번만': 본 뒤로는 다시 들어와도 시작하지 않음");
            }
            finally { foreach (var z in Object.FindObjectsByType<DialogueZone>(FindObjectsSortMode.None)) Disable(z); CloseDialogue(); Object.DestroyImmediate(lines); }
        }
        [Test] public void LostBattleIsNotHeldByAClearDialogue()
        {
            var battle = WinnableBattle(out var map, 1, true); // 적 하나가 플레이어 칸 (1,0)으로 들어온다
            var script = ScriptableObject.CreateInstance<ScenarioScript>(); script.Set("clear-talk", new[] { new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate("해냈다!") } });
            try
            {
                var director = ClearTalk(battle, script, false);
                Assert.True(battle.TryBeginAction(PlayerAction.Wait, Vector2Int.zero));
                for (int i = 0; i < 200 && (battle.Session.IsBusy || battle.IsPresenting); i++) battle.AdvanceMovement(.05f);
                Assert.AreEqual(BattlePhase.Lost, battle.Session.Phase);
                Assert.IsFalse(director.TryAutoStart(), "패배하면 시작하지 않음"); Assert.IsFalse(ScenarioDirector.HoldsWonResultOf(battle));
            }
            finally { Object.DestroyImmediate(map); Object.DestroyImmediate(script); }
        }
        [Test] public void OldSaveWithoutSeenListLoadsAndSeenRecordsPersist()
        {
            File.WriteAllText(savePath, "{\"version\":1,\"clearedStages\":[\"stage-a\"]}");
            ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsCleared("stage-a")); Assert.IsFalse(ProgressStore.IsSeen("talk"));
            ProgressStore.MarkSeen("talk"); ProgressStore.MarkSeen("talk");
            ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsSeen("talk")); Assert.IsTrue(ProgressStore.IsCleared("stage-a"), "클리어 기록 유지");
            Assert.IsFalse(ProgressStore.IsCleared("talk"), "본 기록은 클리어 기록과 따로");
        }
        [Test] public void NpcSwitchesToClearedLinesAfterTheStageIsCleared()
        {
            var npc = Npc(Vector3.zero, "저 앞 밭이 위험해");
            var so = new SerializedObject(npc);
            so.FindProperty("clearedStageId").stringValue = "stage-n";
            var cleared = so.FindProperty("clearedLines"); cleared.arraySize = 1; cleared.GetArrayElementAtIndex(0).stringValue = "벌레를 쫓아냈구나!";
            so.ApplyModifiedPropertiesWithoutUndo();
            npc.Interact(null); Assert.AreEqual("저 앞 밭이 위험해", npc.CurrentLine); Assert.IsFalse(npc.UsesCleared);
            npc.OnLeft(); ProgressStore.MarkCleared("stage-n");
            npc.Interact(null); Assert.AreEqual("벌레를 쫓아냈구나!", npc.CurrentLine); Assert.IsTrue(npc.UsesCleared);
            npc.Interact(null); Assert.IsFalse(npc.IsTalking, "클리어 후 대사도 마지막 줄 다음 닫힘");
        }
        [Test] public void EnteringAMissingBattleSceneDoesNothing()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("찾을 수 없습니다"));
            Assert.IsFalse(StageFlow.Enter(new StageRun("stage-x", "X", "Assets/IBIIIS/Scripts/Tests/NoSuchBattle.unity", "Assets/Overworld.unity", Vector3.zero, PlayerFacing.Front)));
            Assert.IsNull(StageFlow.Current, "진행 정보를 남기지 않음");
            Assert.IsNull(Object.FindAnyObjectByType<StageResultPopup>(), "결과 팝업을 만들지 않음");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("전투 씬이 지정되지"));
            Assert.IsFalse(StageFlow.Enter(new StageRun("stage-x", "X", "", "Assets/Overworld.unity", Vector3.zero, PlayerFacing.Front)));
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
