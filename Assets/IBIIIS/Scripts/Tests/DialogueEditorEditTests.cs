using System.Linq;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>IBIIIS Dialogue Editor의 편집 규칙(DialogueScriptEdits)과 단계 ID.</summary>
    public sealed class DialogueEditorEditTests
    {
        private static ScenarioStep Line(string text) => new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate(text) };
        private static ScenarioStep Emote(string mark = "?") => new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = mark };
        private static ScenarioStep Show() => new ScenarioStep { type = ScenarioStepType.SetActive, target = "fly", active = true };
        // 세로줄: 0=[?, 가] 1=[나] 2=[다] 3=[라]
        private static ScenarioStep[] Sample() => new[] { Emote(), Line("가"), Line("나"), Line("다"), Line("라") };
        private static string Texts(ScenarioStep[] steps) => string.Concat(steps.Select(s => s.type == ScenarioStepType.Line ? s.line.text : s.type == ScenarioStepType.Emote ? s.text : s.type == ScenarioStepType.EndDisplay ? "끝" : s.type == ScenarioStepType.SetActive ? "@" : "#"));

        [Test] public void DraggingTheEndCreatesThenMovesAnEndStep()
        {
            var steps = Sample();
            Assert.AreEqual(4, DialogueScriptEdits.Columns(steps).Count);
            var edited = DialogueScriptEdits.MoveDisplayEdge(steps, 0, false, 2, out int owner);
            Assert.AreEqual("?가나다끝라", Texts(edited), "끝을 세로줄 2(다)에 놓으면 다 뒤에 표시 끝 단계");
            Assert.IsTrue(edited[owner].untilEnd);
            var span = ScenarioTimeline.SpanOf(edited, owner).Value;
            Assert.AreEqual((3, 4, ScenarioSpanEnd.EndStep), (span.End, span.Marker, span.EndKind));
            var again = DialogueScriptEdits.MoveDisplayEdge(edited, owner, false, 1, out owner);
            Assert.AreEqual("?가나끝다라", Texts(again), "다시 끌면 표시 끝 단계를 옮김(새로 만들지 않음)");
            var before = DialogueScriptEdits.MoveDisplayEdge(again, owner, false, 0, out owner);
            Assert.AreEqual("?가끝나다라", Texts(before), "시작 세로줄까지만 당길 수 있음");
        }
        [Test] public void DraggingTheStartMovesTheStepButNotPastItsEndStep()
        {
            var edited = DialogueScriptEdits.MoveDisplayEdge(Sample(), 0, false, 2, out int owner); // ?가나다끝라
            var moved = DialogueScriptEdits.MoveDisplayEdge(edited, owner, true, 1, out owner);
            Assert.AreEqual("가?나다끝라", Texts(moved), "시작을 세로줄 1(나)로: 나 바로 앞");
            var clamped = DialogueScriptEdits.MoveDisplayEdge(moved, owner, true, 3, out owner);
            Assert.AreEqual("가나?다끝라", Texts(clamped), "끝 세로줄(다)보다 뒤로는 못 감");
            Assert.IsNull(DialogueScriptEdits.MoveDisplayEdge(clamped, owner, true, 2, out _), "같은 세로줄이면 바뀌지 않음");
        }
        [Test] public void MovingAWholeSpanKeepsItsLength()
        {
            var edited = DialogueScriptEdits.MoveDisplayEdge(Sample(), 0, false, 1, out int owner); // ?가나끝다라 : 세로줄 0~1
            var moved = DialogueScriptEdits.MoveSpan(edited, owner, 2, out owner);
            Assert.AreEqual("가나?다라끝", Texts(moved), "두 세로줄 뒤로: 2~3");
            var span = ScenarioTimeline.SpanOf(moved, owner).Value;
            var columns = DialogueScriptEdits.Columns(moved);
            Assert.AreEqual((2, 3), (DialogueScriptEdits.ColumnOf(columns, span.Start), DialogueScriptEdits.ColumnOf(columns, span.End)));
            Assert.AreEqual("가?나다끝라", Texts(DialogueScriptEdits.MoveSpan(moved, owner, -1, out _)), "앞으로 한 줄: 1~2");
            Assert.DoesNotThrow(() => DialogueScriptEdits.MoveSpan(moved, owner, 99, out _), "대본 끝을 넘어가면 마지막 세로줄까지만");
            Assert.DoesNotThrow(() => DialogueScriptEdits.MoveSpan(moved, owner, -99, out _), "대본 앞을 넘어가면 첫 세로줄까지만");
        }
        [Test] public void MovingAColumnCarriesItsStackedStepsButLeavesEndSteps()
        {
            // 세로줄: 0=[가] 1=[@, 끝, 나] 2=[다] — @는 등장, 끝은 앞 표시의 끝
            var steps = new[] { Emote(), Line("가"), new ScenarioStep { type = ScenarioStepType.EndDisplay, target = "player" }, Show(), Line("나"), Line("다") };
            steps[0].untilEnd = true;
            var moved = DialogueScriptEdits.MoveColumn(steps, 1, 3, out int main);
            Assert.AreEqual("?가끝다@나", Texts(moved), "나 세로줄을 맨 끝으로: 등장은 함께, 표시 끝은 제자리");
            Assert.AreEqual(5, main);
            Assert.IsNull(DialogueScriptEdits.MoveColumn(steps, 1, 2, out _), "바로 뒤 틈이면 그대로");
            Assert.AreEqual("@나?가끝다", Texts(DialogueScriptEdits.MoveColumn(steps, 1, 0, out _)), "맨 앞으로");
        }
        [Test] public void InsertingFollowsColumns()
        {
            var steps = Sample();
            var inColumn = DialogueScriptEdits.InsertInColumn(steps, 2, Show(), out int at);
            Assert.AreEqual("?가나@다라", Texts(inColumn), "세로줄 2의 기다리는 단계(다) 바로 앞"); Assert.AreEqual(3, at);
            var after = DialogueScriptEdits.InsertAfterColumn(steps, 0, Line("새"), out at);
            Assert.AreEqual("?가새나다라", Texts(after)); Assert.AreEqual(2, at);
            Assert.AreEqual("새?가나다라", Texts(DialogueScriptEdits.InsertAfterColumn(steps, -1, Line("새"), out _)), "-1이면 맨 앞");
        }
        [Test] public void DrawingASpanAddsAHeldDisplayWithItsEndStep()
        {
            var steps = new[] { Line("가"), Line("나"), Line("다"), Line("라") };
            var edited = DialogueScriptEdits.AddSpan(steps, 2, 1, Emote("!"), out int owner);
            Assert.AreEqual("가!나다끝라", Texts(edited), "거꾸로 끌어도 1~2");
            var span = ScenarioTimeline.SpanOf(edited, owner).Value;
            Assert.IsTrue(edited[owner].untilEnd); Assert.AreEqual(ScenarioSpanEnd.EndStep, span.EndKind);
            Assert.AreEqual(owner, span.Start); Assert.AreEqual(3, span.End, "다 대사(3번)가 끝날 때까지");
        }
        [Test] public void DeletingADisplayAlsoRemovesItsEndStep()
        {
            var edited = DialogueScriptEdits.MoveDisplayEdge(Sample(), 0, false, 2, out int owner); // ?가나다끝라
            Assert.AreEqual("가나다라", Texts(DialogueScriptEdits.Delete(edited, new[] { owner })));
        }
        [Test] public void PastedStepsGetNewIds()
        {
            var script = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                script.Set("t", Sample());
                var ids = script.Steps.Select(s => s.id).ToList();
                Assert.IsTrue(ids.All(id => !string.IsNullOrEmpty(id)) && ids.Distinct().Count() == ids.Count, "모든 단계에 서로 다른 ID");
                var pasted = DialogueScriptEdits.InsertSteps(script.Steps, 1, new[] { script.Steps[3] }, out int at);
                script.Set("t", pasted);
                Assert.AreEqual(1, at);
                Assert.AreEqual(ids[3], script.Steps[4].id, "원본 ID는 유지");
                Assert.AreNotEqual(ids[3], script.Steps[1].id, "붙여 넣은 복사본은 새 ID");
                var duplicated = script.Steps.ToArray(); duplicated[2].id = duplicated[0].id; script.Set("t", duplicated);
                Assert.AreEqual(script.Steps.Length, script.Steps.Select(s => s.id).Distinct().Count(), "겹친 ID는 고쳐짐");
            }
            finally { Object.DestroyImmediate(script); }
        }
        [Test] public void ValidationFindsBrokenSteps()
        {
            var steps = new[]
            {
                new ScenarioStep { type = ScenarioStepType.Pose },                                         // 그림 없음(오류)
                Line(""),                                                                                   // 빈 대사(주의)
                new ScenarioStep { type = ScenarioStepType.EndDisplay, display = ScenarioDisplay.Pose },  // 짝 없는 끝(주의)
                new ScenarioStep { type = ScenarioStepType.WaitAction },                                    // 허용 행동 없음(오류)
                new ScenarioStep { type = ScenarioStepType.LoadScene, scenePath = "Assets/없는씬.unity" },  // 없는 씬(오류)
            };
            var issues = DialogueScriptEdits.Validate(steps);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, issues.Select(i => i.Index));
            Assert.AreEqual(3, issues.Count(i => i.Error));
            Assert.IsEmpty(DialogueScriptEdits.Validate(Sample()), "정상 대본은 문제 없음");
        }
        private static ScenarioStep Clear() => new ScenarioStep { type = ScenarioStepType.ClearSection };
        [Test] public void ClearSectionSplitsScenesAndEndsDisplays()
        {
            var shown = Emote(); shown.untilEnd = true;
            var steps = new[] { shown, Line("가"), Clear(), Line("나"), Line("다") };
            CollectionAssert.AreEqual(new[] { (0, 1), (3, 4) }, ScenarioTimeline.Scenes(steps), "클리어 구간 시작 단계는 어느 장면에도 들지 않고 장면을 끊는다");
            Assert.AreEqual(3, DialogueScriptEdits.Columns(steps).Count);
            var span = ScenarioTimeline.SpanOf(steps, 0).Value;
            Assert.AreEqual((1, ScenarioSpanEnd.ScriptEnd), (span.End, span.EndKind), "표시는 구간 경계에서 끝난다");
            Assert.IsNull(DialogueScriptEdits.MoveDisplayEdge(steps, 0, false, 2, out _), "표시를 클리어 구간으로 늘리지 않음");
            Assert.IsNull(DialogueScriptEdits.MoveDisplayEdge(steps, 0, true, 1, out _), "표시를 클리어 구간으로 옮기지 않음");
            var before = DialogueScriptEdits.InsertBeforeColumn(steps, 1, Line("새"), out int at);
            Assert.AreEqual(3, at); Assert.AreEqual("?가#새나다", Texts(before), "클리어 구간 첫 세로줄 앞에 넣으면 클리어 구간에 들어간다");
            var twice = new[] { Line("가"), Clear(), Clear() };
            Assert.AreEqual(1, DialogueScriptEdits.Validate(twice).Count(i => i.Error), "클리어 구간 시작 단계가 두 개면 오류");
        }
        [Test] public void OldScriptsGetAClearSectionByTheirStartTiming()
        {
            var script = ScriptableObject.CreateInstance<ScenarioScript>();
            try
            {
                script.Set("old", new[] { Line("가"), Line("나") });
                Assert.AreEqual(2, script.ClearBoundary, "예전 전투 준비 시 대본: 전체가 전투 시작 구간");
                Assert.IsTrue(script.HasStartPart); Assert.IsFalse(script.HasClearPart);
                Assert.IsTrue(script.EnsureSections()); Assert.AreEqual(0, script.BattleMarker, "맨 앞에 입장 후 구간 시작(입장 전 비움)"); Assert.AreEqual(3, script.ClearMarker, "맨 뒤에 클리어 후 구간 시작");
                Assert.IsFalse(script.HasBeforePart); Assert.IsTrue(script.HasStartPart); Assert.IsFalse(script.HasClearPart);
                Assert.IsFalse(script.EnsureSections(), "다시 해도 그대로");

                script.Set("old-clear", new[] { Line("가") });
                var o = script.Options; o.startTiming = ScenarioStartTiming.BattleWon; script.SetOptions(o);
                Assert.AreEqual(-1, script.ClearBoundary, "예전 클리어 시 대본: 전체가 클리어 구간");
                Assert.IsFalse(script.HasStartPart); Assert.IsTrue(script.HasClearPart);
                Assert.IsTrue(script.EnsureSections()); Assert.AreEqual(0, script.BattleMarker); Assert.AreEqual(1, script.ClearMarker, "입장 후 구간 바로 뒤에 클리어 후 구간 시작");
                Assert.AreEqual(ScenarioStartTiming.BattleReady, script.Options.startTiming, "시작 시점은 더 쓰지 않음");
                Assert.IsTrue(script.HasClearPart); Assert.IsFalse(script.HasStartPart);

                script.Set("dup", new[] { Line("가"), Clear(), Line("나"), Clear() });
                Assert.IsTrue(script.EnsureSections()); Assert.AreEqual(4, script.Steps.Length, "두 번째 클리어 후 경계는 지우고 입장 후 경계를 맨 앞에");
                Assert.AreEqual(ScenarioStepType.BattleSection, script.Steps[0].type);
                script.Set("three", new[] { Line("전"), new ScenarioStep { type = ScenarioStepType.BattleSection }, Line("후"), Clear(), Line("끝") });
                Assert.IsFalse(script.EnsureSections(), "세 구간이 다 있으면 그대로");
                script.Range(ScenarioSection.Before, out int b0, out int b1); script.Range(ScenarioSection.Battle, out int m0, out int m1); script.Range(ScenarioSection.Clear, out int c0, out int c1);
                Assert.AreEqual((0, 1, 2, 3, 4, 5), (b0, b1, m0, m1, c0, c1), "구간 범위");
                Assert.AreEqual("three:before", script.SeenId(ScenarioSection.Before)); Assert.AreEqual("three", script.SeenId(ScenarioSection.Battle)); Assert.AreEqual("three:clear", script.SeenId(ScenarioSection.Clear));
                Assert.AreEqual(ScenarioSection.Before, ScenarioScript.SectionOf(script.Steps, 0)); Assert.AreEqual(ScenarioSection.Battle, ScenarioScript.SectionOf(script.Steps, 2)); Assert.AreEqual(ScenarioSection.Clear, ScenarioScript.SectionOf(script.Steps, 4));
                var bad = new[] { Line("전"), Emote(), new ScenarioStep { type = ScenarioStepType.BattleSection }, Line("후") };
                Assert.AreEqual(1, DialogueScriptEdits.Validate(bad).Count(i => i.Error), "어드벤처씬 구간의 감정 표시는 오류");
            }
            finally { Object.DestroyImmediate(script); }
        }
    }
}
