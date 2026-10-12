using System.Linq;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>IBIIIS Dialogue Editor의 편집 규칙(DialogueScriptEdits)과 단계 ID.</summary>
    public sealed class TutorialGraphEditTests
    {
        private static TutorialStep Line(string text) => new TutorialStep { type = TutorialStepType.Line, line = DialogueLine.Narrate(text) };
        private static TutorialStep Emote(string mark = "?") => new TutorialStep { type = TutorialStepType.Emote, target = "player", text = mark };
        private static TutorialStep Show() => new TutorialStep { type = TutorialStepType.SetActive, target = "fly", active = true };
        // 세로줄: 0=[?, 가] 1=[나] 2=[다] 3=[라]
        private static TutorialStep[] Sample() => new[] { Emote(), Line("가"), Line("나"), Line("다"), Line("라") };
        private static string Texts(TutorialStep[] steps) => string.Concat(steps.Select(s => s.type == TutorialStepType.Line ? s.line.text : s.type == TutorialStepType.Emote ? s.text : s.type == TutorialStepType.EndDisplay ? "끝" : s.type == TutorialStepType.SetActive ? "@" : "#"));

        [Test] public void DraggingTheEndCreatesThenMovesAnEndStep()
        {
            var steps = Sample();
            Assert.AreEqual(4, DialogueScriptEdits.Columns(steps).Count);
            var edited = DialogueScriptEdits.MoveDisplayEdge(steps, 0, false, 2, out int owner);
            Assert.AreEqual("?가나다끝라", Texts(edited), "끝을 세로줄 2(다)에 놓으면 다 뒤에 표시 끝 단계");
            Assert.IsTrue(edited[owner].untilEnd);
            var span = TutorialTimeline.SpanOf(edited, owner).Value;
            Assert.AreEqual((3, 4, TutorialSpanEnd.EndStep), (span.End, span.Marker, span.EndKind));
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
            var span = TutorialTimeline.SpanOf(moved, owner).Value;
            var columns = DialogueScriptEdits.Columns(moved);
            Assert.AreEqual((2, 3), (DialogueScriptEdits.ColumnOf(columns, span.Start), DialogueScriptEdits.ColumnOf(columns, span.End)));
            Assert.AreEqual("가?나다끝라", Texts(DialogueScriptEdits.MoveSpan(moved, owner, -1, out _)), "앞으로 한 줄: 1~2");
            Assert.DoesNotThrow(() => DialogueScriptEdits.MoveSpan(moved, owner, 99, out _), "대본 끝을 넘어가면 마지막 세로줄까지만");
            Assert.DoesNotThrow(() => DialogueScriptEdits.MoveSpan(moved, owner, -99, out _), "대본 앞을 넘어가면 첫 세로줄까지만");
        }
        [Test] public void MovingAColumnCarriesItsStackedStepsButLeavesEndSteps()
        {
            // 세로줄: 0=[가] 1=[@, 끝, 나] 2=[다] — @는 등장, 끝은 앞 표시의 끝
            var steps = new[] { Emote(), Line("가"), new TutorialStep { type = TutorialStepType.EndDisplay, target = "player" }, Show(), Line("나"), Line("다") };
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
            var span = TutorialTimeline.SpanOf(edited, owner).Value;
            Assert.IsTrue(edited[owner].untilEnd); Assert.AreEqual(TutorialSpanEnd.EndStep, span.EndKind);
            Assert.AreEqual(owner, span.Start); Assert.AreEqual(3, span.End, "다 대사(3번)가 끝날 때까지");
        }
        [Test] public void DeletingADisplayAlsoRemovesItsEndStep()
        {
            var edited = DialogueScriptEdits.MoveDisplayEdge(Sample(), 0, false, 2, out int owner); // ?가나다끝라
            Assert.AreEqual("가나다라", Texts(DialogueScriptEdits.Delete(edited, new[] { owner })));
        }
        [Test] public void PastedStepsGetNewIds()
        {
            var script = ScriptableObject.CreateInstance<TutorialScript>();
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
                new TutorialStep { type = TutorialStepType.Pose },                                         // 그림 없음(오류)
                Line(""),                                                                                   // 빈 대사(주의)
                new TutorialStep { type = TutorialStepType.EndDisplay, display = TutorialDisplay.Pose },  // 짝 없는 끝(주의)
                new TutorialStep { type = TutorialStepType.WaitAction },                                    // 허용 행동 없음(오류)
                new TutorialStep { type = TutorialStepType.LoadScene, scenePath = "Assets/없는씬.unity" },  // 없는 씬(오류)
            };
            var issues = DialogueScriptEdits.Validate(steps);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, issues.Select(i => i.Index));
            Assert.AreEqual(3, issues.Count(i => i.Error));
            Assert.IsEmpty(DialogueScriptEdits.Validate(Sample()), "정상 대본은 문제 없음");
        }
    }
}
