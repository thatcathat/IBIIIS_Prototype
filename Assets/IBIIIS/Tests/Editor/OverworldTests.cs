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
