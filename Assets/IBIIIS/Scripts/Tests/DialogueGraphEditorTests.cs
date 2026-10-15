using System.Collections.Generic;
using System.Linq;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>대화 그래프 편집 규칙(DialogueGraphEdits)·씬 대상(DialogueGraphTargets)·그래프 화면(DialogueGraphView).</summary>
    public sealed class DialogueGraphEditorTests
    {
        private readonly List<Object> made = new List<Object>();
        [TearDown] public void TearDown() { foreach (var o in made) if (o != null) Object.DestroyImmediate(o is Component c ? c.gameObject : o); made.Clear(); }
        private T Track<T>(T o) where T : Object { made.Add(o); return o; }
        private ScenarioScript Script(string id, bool talk = true)
        {
            var s = Track(ScriptableObject.CreateInstance<ScenarioScript>()); s.name = id;
            var o = s.Options; o.use = talk ? ScenarioUse.Talk : ScenarioUse.Battle; s.SetOptions(o);
            s.Set(id, new[] { new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate(id) } });
            return s;
        }

        [Test] public void OrderIsOneLinePerObjectWithoutCycles()
        {
            var a = Script("a"); var b = Script("b"); var c = Script("c"); var d = Script("d");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("npc", a); g.Link(a, b); g.Link(b, c);
            g.SetFirst("other", d);
            Assert.IsNull(DialogueGraphEdits.NextProblem(g, c, Script("e")), "끝에 새 대본은 이음");
            Assert.IsNull(DialogueGraphEdits.NextProblem(g, a, b), "이미 이어진 같은 선은 그대로");
            Assert.IsNotNull(DialogueGraphEdits.NextProblem(g, c, a), "끝에서 첫 대본으로는 잇지 않음(첫 대본·고리)");
            var x = Script("x"); var y = Script("y"); g.Link(x, y);
            StringAssert.Contains("고리", DialogueGraphEdits.NextProblem(g, y, x) ?? "", "대상 없는 대본끼리도 고리는 막음");
            StringAssert.Contains("이미", DialogueGraphEdits.NextProblem(g, c, b) ?? "", "다른 대본 뒤에 있는 대본은 다시 잇지 않음");
            StringAssert.Contains("첫 대본", DialogueGraphEdits.NextProblem(g, c, d) ?? "", "다른 대상의 첫 대본은 잇지 않음");
            Assert.IsNotNull(DialogueGraphEdits.NextProblem(g, c, c), "자기 자신");
            Assert.IsNull(DialogueGraphEdits.FirstProblem(g, "npc", a), "같은 대상의 첫 대본 그대로");
            Assert.IsNotNull(DialogueGraphEdits.FirstProblem(g, "new", b), "이미 순서에 든 대본은 다른 대상의 첫 대본이 될 수 없음");
            Assert.IsNull(DialogueGraphEdits.FirstProblem(g, "new", Script("free")));
        }
        [Test] public void ConditionsComeFromTalkSeenOrBattleClearOnly()
        {
            var talk = Script("talk"); var fight = Script("fight", false); var target = Script("target");
            Assert.IsNull(DialogueGraphEdits.ConditionProblem(target, DialogueCondition.Seen(talk)));
            Assert.IsNull(DialogueGraphEdits.ConditionProblem(target, DialogueCondition.Clear(fight)));
            Assert.IsNotNull(DialogueGraphEdits.ConditionProblem(target, DialogueCondition.Seen(fight)), "전투 진입 대본에는 '봄'이 없음");
            Assert.IsNotNull(DialogueGraphEdits.ConditionProblem(target, DialogueCondition.Clear(talk)), "대화 대본에는 '클리어'가 없음");
            Assert.IsNotNull(DialogueGraphEdits.ConditionProblem(target, DialogueCondition.Seen(target)), "자기 자신");
            var current = new[] { DialogueCondition.Seen(talk), DialogueCondition.Clear(fight) };
            var edited = DialogueGraphEdits.EditConditions(current, new[] { 0 }, null, new[] { DialogueCondition.Clear(fight), DialogueCondition.Seen(target) });
            Assert.AreEqual(2, edited.Length, "지운 자리는 빠지고 같은 조건은 한 번만");
            Assert.AreSame(fight, edited[0].script); Assert.AreSame(target, edited[1].script);
            var replaced = DialogueGraphEdits.EditConditions(current, new[] { 0 }, new Dictionary<int, DialogueCondition> { { 0, DialogueCondition.Seen(target) } }, null);
            Assert.AreSame(target, replaced[0].script, "같은 자리 지우기+잇기 = 바꾸기(자리 유지)");
        }
        [Test] public void DeletedScriptsAreCleanedFromTheGraph()
        {
            var a = Script("a"); var b = Script("b"); var c = Script("c");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("npc", a); g.Link(a, b, DialogueCondition.Seen(c)); g.Link(b); g.Link(c);
            Object.DestroyImmediate(b); Object.DestroyImmediate(c);
            Assert.IsTrue(g.HasMissing);
            Assert.That(g.Problems().Any(p => p.Contains("다음 대본이 지워져")), "정리하기 전에는 검사가 알림(열 때 저절로 지우지 않음)");
            Assert.IsTrue(g.RemoveMissing());
            Assert.IsFalse(g.HasMissing);
            Assert.IsNull(g.NextOf(a)); Assert.AreEqual(0, g.ConditionsOf(a).Count); Assert.AreEqual(1, g.Scripts.Count);
            Assert.IsFalse(g.RemoveMissing(), "다시 해도 그대로");
            Assert.IsTrue(g.RemoveObject("npc")); Assert.IsFalse(g.HasObject("npc"));
        }
        [Test] public void DroppingOnAnOccupiedSlotIsCheckedBeforeTheOldLinkIsRemoved()
        {
            DialogueGraphEdits.Link Next(ScenarioScript from, ScenarioScript to) => new DialogueGraphEdits.Link { Kind = DialogueGraphEdits.LinkKind.Next, From = from, To = to, Slot = DialogueGraphEdits.PrevSlot };
            DialogueGraphEdits.Link First(string id, ScenarioScript to) => new DialogueGraphEdits.Link { Kind = DialogueGraphEdits.LinkKind.First, ObjectId = id, To = to, Slot = DialogueGraphEdits.PrevSlot };
            var a = Script("a"); var b = Script("b"); var c = Script("c"); var x = Script("x"); var free = Script("free");
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            g.SetFirst("o1", a); g.Link(a, b); g.Link(b, c); g.Link(c); g.SetFirst("o2", x); g.Link(x);
            // 다른 대상의 대본 가져오기: 화면이 a→b를 먼저 지우고 x→b를 알린다
            StringAssert.Contains("이미", DialogueGraphEdits.ApplyLinks(g, new[] { Next(a, b) }, new[] { Next(x, b) }));
            Assert.AreSame(b, g.NextOf(a), "막히면 밀려난 옛 선도 그대로"); Assert.IsNull(g.NextOf(x));
            // 고리가 되는 선: 옛 선(a→b)을 잃지 않음
            Assert.IsNotNull(DialogueGraphEdits.ApplyLinks(g, new[] { Next(a, b) }, new[] { Next(c, b) }));
            Assert.AreSame(b, g.NextOf(a)); Assert.IsNull(g.NextOf(c));
            // 첫 대본 자리에 다른 대본의 다음을 놓기: 첫 대본을 잃지 않음
            Assert.IsNotNull(DialogueGraphEdits.ApplyLinks(g, new[] { First("o1", a) }, new[] { Next(c, a) }));
            Assert.AreSame(a, g.ObjectOf("o1").first);
            // 막히지 않는 바꿔 잇기: a의 다음을 빈 대본으로(하나뿐인 출구라 a→b가 밀려남)
            Assert.IsNull(DialogueGraphEdits.ApplyLinks(g, new[] { Next(a, b) }, new[] { Next(a, free) }));
            Assert.AreSame(free, g.NextOf(a)); CollectionAssert.AreEqual(new[] { a, free }, g.Chain("o1"));
            // 그냥 지우기
            Assert.IsNull(DialogueGraphEdits.ApplyLinks(g, new[] { Next(a, free) }, null)); Assert.IsNull(g.NextOf(a));
        }
        [Test] public void DroppedObjectsBecomeNpcTargetsWithUniqueIds()
        {
            var plain = Track(new GameObject("Signboard"));
            var g = Track(ScriptableObject.CreateInstance<DialogueGraph>()); g.SetFirst("npc-1", null);
            Assert.AreEqual(DialogueGraphTargets.Kind.None, DialogueGraphTargets.Of(plain).Kind);
            var t = DialogueGraphTargets.EnsureTarget(plain, g);
            Assert.AreEqual(DialogueGraphTargets.Kind.Npc, t.Kind); Assert.IsNotNull(plain.GetComponent<NpcSpeaker>());
            Assert.AreEqual("npc-2", t.Id, "그래프에 이미 있는 ID는 피함"); Assert.AreEqual("Signboard", plain.GetComponent<NpcSpeaker>().SourceName, "이름은 오브젝트 이름");
            Assert.AreEqual(t.Id, DialogueGraphTargets.EnsureTarget(plain, g).Id, "이미 대상이면 그대로");
            var zone = Track(new GameObject("Zone")).AddComponent<DialogueZone>();
            Assert.AreEqual(DialogueGraphTargets.Kind.Zone, DialogueGraphTargets.Of(zone.gameObject).Kind);
            StringAssert.StartsWith("zone-", DialogueGraphTargets.EnsureTarget(zone.gameObject, g).Id, "ID가 빈 구역은 ID를 채움");
            Assert.IsTrue(DialogueGraphTargets.InOpenScenes().ContainsKey(t.Id), "열린 씬에서 ID로 찾음");
        }
        [Test] public void GraphViewDrawsObjectsScriptsAndEdgesOfTheProjectGraph()
        {
            var project = DialogueGraphWindow.ProjectGraph();
            Assume.That(project, Is.Not.Null, "Overworld Settings에 대화 그래프가 연결되어 있어야 함");
            var copy = Track(Object.Instantiate(project)); // 실제 그래프 파일은 고치지 않는다
            var view = new DialogueGraphView();
            view.Load(copy);
            int scripts = AssetDatabase.FindAssets("t:ScenarioScript", new[] { "Assets" }).Length;
            Assert.AreEqual(scripts, view.ScriptCount, "프로젝트의 모든 대본이 노드");
            Assert.AreEqual(copy.Objects.Count, view.ObjectCount, "그래프의 대상마다 노드");
            int expected = copy.Objects.Count(o => o.first != null) + copy.Scripts.Count(s => s.next != null) + copy.Scripts.Sum(s => s.Conditions.Count);
            Assert.AreEqual(expected, view.EdgeCount, "첫 대본·다음·조건마다 선 하나");
            Assert.IsFalse(view.Issues.Any(i => i.Contains("고리") || i.Contains("두 대상")), string.Join("; ", view.Issues));
        }
    }
}
