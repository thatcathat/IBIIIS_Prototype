using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>대화 진행 규칙(기획서 7.6)과 NPC·근접 대화 구역·적의 그래프 연결.</summary>
    public sealed class DialogueProgressTests
    {
        private string savePath;
        private readonly List<Object> made = new List<Object>();
        [SetUp] public void SetUp()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"IBIIIS_progress_test_{System.Guid.NewGuid():N}.json");
            ProgressStore.UseFile(savePath);
            DialogueProgress.ResetVersions();
        }
        [TearDown] public void TearDown()
        {
            ProgressStore.UseFile(null);
            foreach (var path in new[] { savePath, savePath + ".tmp" }) if (File.Exists(path)) File.Delete(path);
            var dialogue = Object.FindAnyObjectByType<DialogueController>(); if (dialogue != null) Object.DestroyImmediate(dialogue.gameObject);
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o is Component c ? c.gameObject : o);
            made.Clear();
            DialogueProgress.Graph = null; DialogueProgress.ResetVersions();
        }
        private T Track<T>(T o) where T : Object { made.Add(o); return o; }
        private static ScenarioStep Say(string text) => new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate(text) };
        private ScenarioScript Talk(string id, params string[] lines)
        {
            var s = Track(ScriptableObject.CreateInstance<ScenarioScript>()); s.name = id;
            var o = s.Options; o.use = ScenarioUse.Talk; s.SetOptions(o);
            s.Set(id, lines.Select(Say).ToArray());
            return s;
        }
        private ScenarioScript Battle(string id)
        {
            var s = Track(ScriptableObject.CreateInstance<ScenarioScript>()); s.name = id;
            s.Set(id, new[] { new ScenarioStep { type = ScenarioStepType.BattleSection }, Say("싸우자"), new ScenarioStep { type = ScenarioStepType.ClearSection } });
            return s;
        }
        // 사용자 그림(2026-10-15): A 첫 대본 → A 반복 → A 클리어(전투 클리어) → A 반복 2 / 적 진입 불가 → 전투(A 첫 대본 봄) / B 첫 대본 → B 다음(A 클리어 봄).
        private DialogueGraph Example(out ScenarioScript aFirst, out ScenarioScript aRepeat, out ScenarioScript aClear, out ScenarioScript aRepeat2,
            out ScenarioScript blocked, out ScenarioScript battle, out ScenarioScript bFirst, out ScenarioScript bNext)
        {
            aFirst = Talk("a_first", "처음 보는 얼굴이네"); aRepeat = Talk("a_repeat", "적은 위쪽에 있어"); aClear = Talk("a_clear", "해치웠구나"); aRepeat2 = Talk("a_repeat2", "또 왔구나");
            blocked = Talk("enemy_blocked", "길이 막혀 있다"); battle = Battle("proto"); bFirst = Talk("b_first", "바빠"); bNext = Talk("b_next", "A가 보냈구나");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("npc_a", aFirst); g.SetFirst("stage-proto", blocked); g.SetFirst("npc_b", bFirst);
            g.Link(aFirst, aRepeat); g.Link(aRepeat, aClear); g.Link(aClear, aRepeat2, DialogueCondition.Clear(battle)); g.Link(aRepeat2);
            g.Link(blocked, battle); g.Link(battle, null, DialogueCondition.Seen(aFirst));
            g.Link(bFirst, bNext); g.Link(bNext, null, DialogueCondition.Seen(aClear));
            return g;
        }

        [Test] public void ObjectsSayTheLastUnlockedScriptOfTheirChain()
        {
            var g = Example(out var aFirst, out var aRepeat, out var aClear, out var aRepeat2, out var blocked, out var battle, out var bFirst, out var bNext);
            var seen = new HashSet<ScenarioScript>(); var cleared = new HashSet<string>();
            ScenarioScript Now(string id) => DialogueProgress.Current(g, id, seen.Contains, cleared.Contains);
            Assert.AreSame(aFirst, Now("npc_a")); Assert.AreSame(blocked, Now("stage-proto"), "A에게 말 걸기 전에는 진입 불가 대화"); Assert.AreSame(bFirst, Now("npc_b"));
            seen.Add(aFirst);
            Assert.AreSame(aRepeat, Now("npc_a"), "첫 대본을 보면 반복 대본");
            Assert.AreSame(battle, Now("stage-proto"), "진입 불가 대화를 안 봤어도 A 첫 대본을 보면 전투 진입 대본이 열림");
            seen.Add(battle); cleared.Add("stage-proto");
            Assert.AreSame(aClear, Now("npc_a"), "클리어하면 반복 대본을 안 봤어도 클리어 대본");
            Assert.AreSame(bFirst, Now("npc_b"), "A 클리어 대본을 보기 전까지 B는 첫 대본");
            seen.Add(aClear);
            Assert.AreSame(aRepeat2, Now("npc_a"), "클리어 대본을 보면 다음 반복 대본(끝이라 계속)");
            Assert.AreSame(bNext, Now("npc_b"), "B 첫 대본을 안 봤어도 A 클리어 대본을 보면 B 다음 대본");
            Assert.AreEqual("stage-proto", g.OwnerOf(battle), "클리어 조건은 그 전투 대본을 순서에 둔 적의 Stage Id");
            Assert.IsEmpty(g.Problems());
        }
        [Test] public void GraphProblemsCatchCyclesSharedScriptsAndWrongConditionTargets()
        {
            var a = Talk("a"); var b = Talk("b"); var c = Talk("c"); var fight = Battle("fight"); var after = Talk("after");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("x", a); g.Link(a, b); g.Link(b, a);
            g.SetFirst("y", b);
            g.SetFirst("z", fight); g.Link(fight, after);
            g.Link(c, null, DialogueCondition.Seen(fight), DialogueCondition.Clear(c));
            var problems = g.Problems();
            Assert.That(problems.Any(p => p.Contains("고리")), "순서가 고리로 돌아옴");
            Assert.That(problems.Any(p => p.Contains("두 대상")), "대본 하나가 두 대상의 순서에");
            Assert.That(problems.Any(p => p.Contains("'봄' 조건") && p.Contains("대화 대본이 아닙니다")), "봄 조건 대상은 대화 대본");
            Assert.That(problems.Any(p => p.Contains("'클리어' 조건") && p.Contains("전투 진입 대본이 아닙니다")), "클리어 조건 대상은 전투 진입 대본");
            Assert.That(problems.Any(p => p.Contains("전투 진입 대본 뒤에 조건 없이")), "전투 진입 대본 뒤 조건 없는 대본");
            CollectionAssert.AreEqual(new[] { a, b }, g.Chain("x"), "고리 앞에서 멈춤");
            var same1 = Talk("same"); var same2 = Talk("same"); var blank = Talk("");
            var g2 = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g2.SetFirst("p", same1); g2.Link(same1, same2); g2.Link(same2, blank);
            var idProblems = g2.Problems();
            Assert.That(idProblems.Any(p => p.Contains("대본 ID 'same'가 같아")), "순서에 든 두 대본의 대본 ID가 같음(Project 창 복제)");
            Assert.That(idProblems.Any(p => p.Contains("대본 ID가 비어")), "대본 ID 빔");
        }
        [Test] public void TalkScriptsHaveVersionsWithUniqueStepIdsAndNoSections()
        {
            var s = Talk("again", "또 왔구나");
            s.SetMoreVersions(new[] { Say("B에게는 가 봤어?") }, new[] { Say("오늘 날씨 좋다") });
            Assert.AreEqual(3, s.VersionCount);
            Assert.AreEqual("B에게는 가 봤어?", s.VersionSteps(1)[0].line.text); Assert.AreSame(s.Steps, s.VersionSteps(0)); Assert.AreSame(s.Steps, s.VersionSteps(9), "범위 밖은 버전 1");
            var ids = Enumerable.Range(0, 3).Select(v => s.VersionSteps(v)[0].id).ToList();
            Assert.AreEqual(3, ids.Distinct().Count(), "버전 사이에서도 단계 ID(번역 키)가 겹치지 않음");
            Assert.IsFalse(s.EnsureSections(), "대화 대본에는 구간 경계를 넣지 않음"); Assert.AreEqual(1, s.Steps.Length);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 }, Enumerable.Range(0, 4).Select(_ => DialogueProgress.NextVersion(s)).ToArray(), "차례대로");
            var o = s.Options; o.versionOrder = ScenarioVersionOrder.Random; s.SetOptions(o);
            Assert.AreEqual(2, DialogueProgress.NextVersion(s, n => n - 1)); Assert.AreEqual(2, DialogueProgress.NextVersion(s, n => 99), "무작위 값은 범위 안으로");
            var fight = Battle("fight2"); fight.SetMoreVersions(new[] { Say("x") });
            Assert.AreEqual(1, fight.VersionCount, "전투 진입 대본은 버전 없음");
        }
        [Test] public void EditorVersionEditsAddRemoveAndKeepStepIdsUnique()
        {
            var s = Talk("edit", "하나");
            Assert.AreEqual(1, s.AddVersion()); Assert.AreEqual(2, s.VersionCount);
            Assert.AreEqual(ScenarioStepType.Line, s.VersionSteps(1)[0].type, "새 버전은 빈 지문 한 줄");
            s.SetVersionSteps(1, new[] { s.Steps[0], Say("둘") });
            Assert.AreEqual(3, new[] { s.Steps[0].id, s.VersionSteps(1)[0].id, s.VersionSteps(1)[1].id }.Distinct().Count(), "다른 버전에서 가져온 단계도 새 ID");
            Assert.IsTrue(s.RemoveVersion(0)); Assert.AreEqual(1, s.VersionCount);
            Assert.AreEqual("둘", s.Steps[1].line.text, "버전 1을 지우면 버전 2가 버전 1");
            Assert.IsFalse(s.RemoveVersion(0), "하나뿐인 버전은 지우지 않음");
            Assert.AreEqual(-1, Battle("fight3").AddVersion(), "전투 진입 대본에는 버전을 더하지 않음");
        }
        [Test] public void TranslationCollectsEveryVersionUnderItsOwnStepId()
        {
            var s = Talk("again2", "또 왔구나");
            s.SetMoreVersions(new[] { Say("B에게는 가 봤어?") }, new[] { Say("오늘 날씨 좋다") });
            var list = new List<IBIIIS.Editor.LocalizedEntry>();
            IBIIIS.Editor.LocalizationSources.ScenarioEntries(s, list);
            CollectionAssert.AreEqual(new[] { "또 왔구나", "B에게는 가 봤어?", "오늘 날씨 좋다" }, list.Select(e => e.Korean).ToArray(), "버전 1·2·3 대사 모두");
            CollectionAssert.AreEqual(Enumerable.Range(0, 3).Select(v => s.VersionSteps(v)[0].id).ToArray(), list.Select(e => e.Key).ToArray(), "키는 각 버전의 단계 ID");
            Assert.That(list.All(e => e.Table == GameText.Scenario("again2")), "한 대본 표");
            StringAssert.Contains("버전 2", list[1].Context);
            var one = new List<IBIIIS.Editor.LocalizedEntry>(); IBIIIS.Editor.LocalizationSources.ScenarioEntries(Talk("single", "하나"), one);
            Assert.That(!one[0].Context.Contains("버전"), "버전이 하나면 설명에 버전 번호 없음");
        }
        [Test] public void ZoneShownRecordsPersistAndOldSavesStillLoad()
        {
            File.WriteAllText(savePath, "{\"version\":1,\"clearedStages\":[\"s\"],\"seenScenarios\":[\"t\"]}");
            ProgressStore.Reload();
            Assert.IsFalse(ProgressStore.IsZoneShown("gate", "warn"));
            ProgressStore.MarkZoneShown("gate", "warn"); ProgressStore.MarkZoneShown("gate", "warn");
            ProgressStore.Reload();
            Assert.IsTrue(ProgressStore.IsZoneShown("gate", "warn")); Assert.IsFalse(ProgressStore.IsZoneShown("other", "warn"), "구역마다 따로");
            Assert.IsTrue(ProgressStore.IsCleared("s")); Assert.IsTrue(ProgressStore.IsSeen("t"), "다른 기록 유지");
        }
        private static void Finish(DialogueController dialogue) { for (int i = 0; i < 20 && dialogue.IsOpen; i++) dialogue.Runner.Advance(); }
        private OverworldPlayer Player(Vector3 position)
        {
            var player = Track(new GameObject("Player")).AddComponent<OverworldPlayer>(); player.transform.position = position;
            return player;
        }
        [Test] public void TalkScriptPlaysAVersionAndRecordsSeenAfterTheEnd()
        {
            var s = Talk("hello", "안녕", "또 봐"); s.SetMoreVersions(new[] { Say("반가워") });
            var player = Player(Vector3.zero); bool done = false;
            var dialogue = AdventureDialogue.PlayTalk(s, player, () => done = true);
            Assert.AreEqual("안녕", dialogue.Runner.Current.text, "버전 1"); Assert.IsTrue(player.InputLocked);
            Assert.IsFalse(ProgressStore.IsSeen("hello"), "끝나기 전에는 본 기록 없음");
            Finish(dialogue);
            Assert.IsTrue(done); Assert.IsTrue(ProgressStore.IsSeen("hello")); Assert.IsFalse(player.InputLocked);
            dialogue = AdventureDialogue.PlayTalk(s, player, null);
            Assert.AreEqual("반가워", dialogue.Runner.Current.text, "다음에는 버전 2");
            Finish(dialogue);
        }
        [Test] public void NpcInTheGraphTalksThroughItsChain()
        {
            var g = Example(out var aFirst, out var aRepeat, out _, out _, out _, out _, out _, out _);
            DialogueProgress.Graph = g;
            var npc = Track(new GameObject("NPC_A")).AddComponent<NpcSpeaker>();
            var so = new UnityEditor.SerializedObject(npc); so.FindProperty("npcId").stringValue = "npc_a"; so.ApplyModifiedPropertiesWithoutUndo();
            var player = Player(Vector3.zero);
            Assert.IsTrue(npc.UsesGraph); Assert.AreSame(aFirst, npc.CurrentScript); Assert.IsNotNull(npc.PromptVerb);
            npc.Interact(player);
            var dialogue = Object.FindAnyObjectByType<DialogueController>();
            Assert.AreEqual("처음 보는 얼굴이네", dialogue.Runner.Current.text); Assert.IsNull(npc.PromptVerb, "대화 중에는 안내 없음");
            Finish(dialogue);
            Assert.AreSame(aRepeat, npc.CurrentScript, "본 뒤에는 반복 대본");
            npc.Interact(player); Assert.AreEqual("적은 위쪽에 있어", dialogue.Runner.Current.text);
            Finish(dialogue);
        }
        private static void Call(MonoBehaviour component, string method) => component.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(component, null);
        [Test] public void ZoneShowsEachScriptOnlyOnTheFirstEntry()
        {
            var warn = Talk("gate_warn", "앞에 몬스터가 길을 막고 있다"); var open = Talk("gate_open", "길이 열렸다"); var fight = Battle("gate_fight");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("gate", warn); g.Link(warn, open); g.Link(open, null, DialogueCondition.Clear(fight)); g.SetFirst("stage-gate", fight);
            DialogueProgress.Graph = g;
            var zone = Track(new GameObject("Zone")).AddComponent<DialogueZone>(); zone.transform.position = new Vector3(10, 0, 0);
            var so = new UnityEditor.SerializedObject(zone); so.FindProperty("zoneId").stringValue = "gate"; so.ApplyModifiedPropertiesWithoutUndo();
            Call(zone, "OnEnable");
            try
            {
                Assert.IsNull(zone.Problem, "그래프를 쓰면 대화 칸이 비어도 정상");
                Assert.IsNull(DialogueZone.UpdateInside(Vector3.zero));
                Assert.AreSame(zone, DialogueZone.UpdateInside(new Vector3(10, 0, 0)), "처음 들어옴");
                Assert.IsTrue(zone.Play(null));
                var dialogue = Object.FindAnyObjectByType<DialogueController>(); Assert.AreEqual("앞에 몬스터가 길을 막고 있다", dialogue.Runner.Current.text);
                Finish(dialogue);
                Assert.IsTrue(ProgressStore.IsZoneShown("gate", "gate_warn"));
                DialogueZone.UpdateInside(Vector3.zero);
                Assert.IsNull(DialogueZone.UpdateInside(new Vector3(10, 0, 0)), "같은 대본이면 다시 들어와도 안 나옴");
                ProgressStore.MarkCleared("stage-gate");
                DialogueZone.UpdateInside(Vector3.zero);
                Assert.AreSame(zone, DialogueZone.UpdateInside(new Vector3(10, 0, 0)), "새 대본이 열리면 다음 진입 때 한 번");
                Assert.IsTrue(zone.Play(null)); Assert.AreEqual("길이 열렸다", dialogue.Runner.Current.text);
                Finish(dialogue);
                DialogueZone.UpdateInside(Vector3.zero);
                Assert.IsNull(DialogueZone.UpdateInside(new Vector3(10, 0, 0)));
            }
            finally { Call(zone, "OnDisable"); }
        }
        [Test] public void EnemyWithATalkScriptTalksThenThePlayerStepsBackInsteadOfBattle()
        {
            var g = Example(out var aFirst, out _, out _, out _, out var blocked, out var battle, out _, out _);
            DialogueProgress.Graph = g;
            var enemy = Track(new GameObject("Enemy")).AddComponent<StageEnemy>();
            var so = new UnityEditor.SerializedObject(enemy); so.FindProperty("stageId").stringValue = "stage-proto"; so.ApplyModifiedPropertiesWithoutUndo();
            typeof(StageEnemy).GetField("battleScenePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(enemy, "Assets/IBIIIS/Scripts/Tests/NoSuchBattle.unity");
            var settings = Track(ScriptableObject.CreateInstance<OverworldSettings>());
            so = new UnityEditor.SerializedObject(settings); so.FindProperty("contactHoldSeconds").floatValue = 0; so.ApplyModifiedPropertiesWithoutUndo();
            var player = Player(new Vector3(-.6f, 0, 0));
            so = new UnityEditor.SerializedObject(player); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreSame(blocked, enemy.CurrentScript);
            enemy.Engage(player);
            Assert.AreEqual(2, enemy.EngagePhase, "멈춤 0초 → 대화");
            var dialogue = Object.FindAnyObjectByType<DialogueController>(); Assert.AreEqual("길이 막혀 있다", dialogue.Runner.Current.text);
            Finish(dialogue);
            Assert.AreEqual(3, enemy.EngagePhase, "대화 뒤 물러나기"); Assert.IsTrue(player.WalkingBack); Assert.IsTrue(player.InputLocked);
            Assert.IsNull(StageFlow.Current, "전투에 들어가지 않음");
            for (int i = 0; i < 200 && player.WalkingBack; i++) player.AdvanceWalk(.02f);
            Assert.IsFalse(player.WalkingBack); Assert.IsFalse(player.InputLocked); Assert.IsFalse(enemy.Engaging, "다 물러나면 다시 닿을 수 있음");
            Assert.That(OverworldInteractable.FlatDistance(player.transform.position, enemy.transform.position), Is.EqualTo(enemy.ContactRadius + OverworldSettings.DefaultReturnClearance).Within(.02f), "닿음 거리 바깥으로");
            Assert.IsTrue(ProgressStore.IsSeen("enemy_blocked"));
            ProgressStore.MarkSeen(aFirst.TalkSeenId);
            Assert.AreSame(battle, enemy.CurrentScript, "A 첫 대본을 보면 전투 진입 대본");
        }
        [Test] public void StepBackEndingInsideContactWaitsUntilThePlayerLeavesBeforeTouchingAgain()
        {
            var g = Example(out _, out _, out _, out _, out var blocked, out _, out _, out _);
            DialogueProgress.Graph = g; StageEnemy.IgnoreContactFor(0);
            var enemy = Track(new GameObject("Enemy")).AddComponent<StageEnemy>();
            var so = new UnityEditor.SerializedObject(enemy); so.FindProperty("stageId").stringValue = "stage-proto"; so.ApplyModifiedPropertiesWithoutUndo();
            typeof(StageEnemy).GetField("battleScenePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(enemy, "Assets/IBIIIS/Scripts/Tests/NoSuchBattle.unity");
            var settings = Track(ScriptableObject.CreateInstance<OverworldSettings>());
            // 물러나기가 닿음 거리 안에서 끝나게(벽에 막히거나 3초 제한) — 테스트는 컨트롤러 없이 움직이므로 아주 느린 걸음으로 같은 상황을 만든다.
            so = new UnityEditor.SerializedObject(settings); so.FindProperty("contactHoldSeconds").floatValue = 0; so.FindProperty("moveSpeed").floatValue = .1f; so.ApplyModifiedPropertiesWithoutUndo();
            var player = Player(new Vector3(-.3f, 0, 0));
            so = new UnityEditor.SerializedObject(player); so.FindProperty("settings").objectReferenceValue = settings; so.ApplyModifiedPropertiesWithoutUndo();
            Call(enemy, "OnEnable");
            Assert.AreSame(enemy, StageEnemy.FindTouching(player.transform.position), "대화 전에는 닿음");
            enemy.Engage(player);
            Finish(Object.FindAnyObjectByType<DialogueController>());
            for (int i = 0; i < 200 && player.WalkingBack; i++) player.AdvanceWalk(.02f);
            Assert.IsFalse(enemy.Engaging); Assert.IsFalse(player.InputLocked);
            Assert.That(OverworldInteractable.FlatDistance(player.transform.position, enemy.transform.position), Is.LessThanOrEqualTo(enemy.ContactRadius), "물러나기가 닿음 거리 안에서 끝남");
            Assert.IsNull(StageEnemy.FindTouching(player.transform.position), "거리 밖으로 나가기 전에는 다시 닿지 않음(같은 대화 반복 방지)");
            Assert.IsNull(StageEnemy.FindTouching(new Vector3(-3, 0, 0)), "거리 밖");
            Assert.AreSame(enemy, StageEnemy.FindTouching(player.transform.position), "한 번 나갔다 오면 다시 닿음");
            Assert.AreSame(blocked, enemy.CurrentScript);
        }
    }
}
