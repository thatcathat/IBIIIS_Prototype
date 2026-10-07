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
            foreach (var path in new[] { savePath, savePath + ".corrupt", savePath + ".tmp" }) if (File.Exists(path)) File.Delete(path);
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
        [Test] public void AssetWithoutOverworldMapFallsBackToDefaults()
        {
            var battleOnly = BattleInput.CreateDefaultAsset();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Overworld"));
            using (var fallback = new OverworldInput(battleOnly)) Assert.AreEqual("F", fallback.Key(OverworldInput.Interact));
            Object.DestroyImmediate(battleOnly);
        }
    }
}
