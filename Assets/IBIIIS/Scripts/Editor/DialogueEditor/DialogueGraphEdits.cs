using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace IBIIIS.Editor
{
    /// <summary>대화 그래프(DialogueGraph) 편집 규칙(기획서 7.6). 그래프 화면과 떨어져 있어 자동 테스트한다.
    /// 검사 함수는 문제가 있으면 이유(한국어), 없으면 null을 돌려준다. 편집은 Undo에 남기고 바로 저장한다.</summary>
    public static class DialogueGraphEdits
    {
        /// <summary>대본에 들어오는 선: 그 대본을 첫 대본으로 둔 대상 ID 또는 '다음'으로 가리키는 앞 대본(둘 다 없으면 null, null).</summary>
        public static (string objectId, ScenarioScript previous) IncomingOf(DialogueGraph graph, ScenarioScript script)
        {
            if (graph == null || script == null) return (null, null);
            foreach (var o in graph.Objects) if (o != null && o.first == script) return (o.objectId, null);
            foreach (var s in graph.Scripts) if (s != null && s.next == script && s.script != null) return (null, s.script);
            return (null, null);
        }
        private static string Describe((string objectId, ScenarioScript previous) from) => from.objectId != null ? $"대상 '{from.objectId}'의 첫 대본" : $"'{from.previous.name}' 뒤";
        /// <summary>대상의 첫 대본으로 이을 수 있는지.</summary>
        public static string FirstProblem(DialogueGraph graph, string objectId, ScenarioScript script)
        {
            if (script == null) return "대본이 없습니다.";
            if (string.IsNullOrEmpty(objectId)) return "오브젝트 ID가 비어 있습니다.";
            var from = IncomingOf(graph, script);
            if ((from.objectId != null && from.objectId != objectId) || from.previous != null) return $"'{script.name}'은(는) 이미 {Describe(from)}에 있습니다(대본 하나는 한 대상의 순서에 한 번만).";
            return null;
        }
        /// <summary>from의 '다음'을 to로 이을 수 있는지(순서는 한 줄, 고리 금지, 대본 하나는 한 대상에만).</summary>
        public static string NextProblem(DialogueGraph graph, ScenarioScript from, ScenarioScript to)
        {
            if (from == null || to == null) return "대본이 없습니다.";
            if (from == to) return "자기 자신을 다음으로 이을 수 없습니다.";
            var incoming = IncomingOf(graph, to);
            if (incoming.objectId != null || (incoming.previous != null && incoming.previous != from)) return $"'{to.name}'은(는) 이미 {Describe(incoming)}에 있습니다(대본 하나는 한 대상의 순서에 한 번만).";
            // to에서 다음을 따라가다 from이 나오면 고리
            var seen = new HashSet<ScenarioScript>();
            for (var s = to; s != null && seen.Add(s); s = graph.NextOf(s))
                if (s == from) return "순서가 고리로 돌아옵니다.";
            return null;
        }
        /// <summary>source의 '봄'·'클리어' 출구를 target의 조건으로 이을 수 있는지.</summary>
        public static string ConditionProblem(ScenarioScript target, DialogueCondition condition)
        {
            if (target == null || condition.script == null) return "대본이 없습니다.";
            if (condition.script == target) return "자기 자신을 조건으로 쓸 수 없습니다.";
            if (condition.type == DialogueConditionType.Seen && !condition.script.IsTalk) return $"'봄' 조건은 대화 대본에서만 잇습니다('{condition.script.name}'은(는) 전투 진입 대본 — '클리어'를 쓰세요).";
            if (condition.type == DialogueConditionType.Clear && condition.script.IsTalk) return $"'클리어' 조건은 전투 진입 대본에서만 잇습니다.";
            return null;
        }
        /// <summary>조건 목록을 고친다: removed 자리는 빼고, replaced 자리는 바꾸고, added는 뒤에 더한다. 같은 조건은 한 번만.</summary>
        public static DialogueCondition[] EditConditions(IReadOnlyList<DialogueCondition> current, ICollection<int> removed, IReadOnlyDictionary<int, DialogueCondition> replaced, IEnumerable<DialogueCondition> added)
        {
            var list = new List<DialogueCondition>();
            for (int i = 0; i < (current?.Count ?? 0); i++)
            {
                if (removed != null && removed.Contains(i) && (replaced == null || !replaced.ContainsKey(i))) continue;
                var c = replaced != null && replaced.TryGetValue(i, out var r) ? r : current[i];
                if (!list.Any(x => Same(x, c))) list.Add(c);
            }
            if (added != null) foreach (var c in added) if (!list.Any(x => Same(x, c))) list.Add(c);
            return list.ToArray();
        }
        private static bool Same(DialogueCondition a, DialogueCondition b) => a.type == b.type && a.script == b.script;

        /// <summary>선 하나: 나가는 쪽(대상의 첫 대본 / 대본의 다음·봄·클리어)과 들어가는 쪽(대본의 앞 대본 PrevSlot / 조건 자리 0.. / 조건 추가 AddSlot).</summary>
        public enum LinkKind { First, Next, Seen, Clear }
        public const int PrevSlot = -2, AddSlot = -1;
        public struct Link
        {
            public LinkKind Kind; public string ObjectId; public ScenarioScript From; public ScenarioScript To; public int Slot;
            internal bool SameInput(Link o) => To == o.To && Slot == o.Slot;
            // 첫 대본·다음 출구는 선이 하나뿐(봄·클리어 출구는 여러 개)
            internal bool SameOutput(Link o) => Kind == o.Kind && (Kind == LinkKind.First ? ObjectId == o.ObjectId : Kind == LinkKind.Next && From == o.From);
        }
        /// <summary>한 번의 선 편집(지운 선·이은 선)을 그래프에 반영하고 잇지 못한 첫 이유를 돌려준다.
        /// 이은 선은 편집 전 그래프로 검사한다. 이미 이어진 슬롯에 새 선을 놓으면 화면이 옛 선을 먼저 지우므로,
        /// 새 선이 막히면 그 선 때문에 밀려난 옛 선(같은 들어가는 슬롯, 또는 하나뿐인 같은 출구)의 지우기도 취소한다.</summary>
        public static string ApplyLinks(DialogueGraph g, IList<Link> removed, IList<Link> created)
        {
            string problem = null;
            var accepted = new List<Link>(); var keep = new HashSet<int>();
            foreach (var c in created ?? Array.Empty<Link>())
            {
                string why = c.Kind == LinkKind.First ? FirstProblem(g, c.ObjectId, c.To)
                    : c.Kind == LinkKind.Next ? (c.Slot == PrevSlot ? NextProblem(g, c.From, c.To) : "'다음'은 '앞 대본' 슬롯에 잇습니다.")
                    : c.Slot == PrevSlot ? "'봄'·'클리어'는 조건 슬롯에 잇습니다." : ConditionProblem(c.To, c.Kind == LinkKind.Seen ? DialogueCondition.Seen(c.From) : DialogueCondition.Clear(c.From));
                if (c.Kind == LinkKind.First && c.Slot != PrevSlot) why = "'첫 대본'은 '앞 대본' 슬롯에 잇습니다.";
                if (why == null) { accepted.Add(c); continue; }
                problem ??= why;
                if (removed != null) for (int i = 0; i < removed.Count; i++) if (removed[i].SameInput(c) || removed[i].SameOutput(c)) keep.Add(i);
            }
            var conditionRemovals = new Dictionary<ScenarioScript, HashSet<int>>();
            for (int i = 0; i < (removed?.Count ?? 0); i++)
            {
                if (keep.Contains(i)) continue;
                var r = removed[i];
                if (r.Kind == LinkKind.First) { var obj = g.ObjectOf(r.ObjectId); if (obj != null && obj.first == r.To) obj.first = null; }
                else if (r.Kind == LinkKind.Next) { if (g.NextOf(r.From) == r.To) g.SetNext(r.From, null); }
                else if (r.Slot >= 0) { if (!conditionRemovals.TryGetValue(r.To, out var set)) conditionRemovals[r.To] = set = new HashSet<int>(); set.Add(r.Slot); }
            }
            var replaced = new Dictionary<ScenarioScript, Dictionary<int, DialogueCondition>>(); var added = new Dictionary<ScenarioScript, List<DialogueCondition>>();
            foreach (var c in accepted)
            {
                if (c.Kind == LinkKind.First) { var obj = g.ObjectOf(c.ObjectId); if (obj != null) obj.first = c.To; else g.SetFirst(c.ObjectId, c.To); }
                else if (c.Kind == LinkKind.Next) g.SetNext(c.From, c.To);
                else
                {
                    var condition = c.Kind == LinkKind.Seen ? DialogueCondition.Seen(c.From) : DialogueCondition.Clear(c.From);
                    if (c.Slot >= 0) { if (!replaced.TryGetValue(c.To, out var map)) replaced[c.To] = map = new Dictionary<int, DialogueCondition>(); map[c.Slot] = condition; }
                    else { if (!added.TryGetValue(c.To, out var list)) added[c.To] = list = new List<DialogueCondition>(); list.Add(condition); }
                }
            }
            foreach (var s in conditionRemovals.Keys.Concat(replaced.Keys).Concat(added.Keys).Distinct().ToList())
            {
                conditionRemovals.TryGetValue(s, out var r); replaced.TryGetValue(s, out var map); added.TryGetValue(s, out var add);
                g.SetConditions(s, EditConditions(g.ConditionsOf(s), r, map, add));
            }
            return problem;
        }

        /// <summary>그래프 편집을 한 번의 Undo로 묶어 하고 저장한다.</summary>
        public static void Edit(DialogueGraph graph, string undoName, Action<DialogueGraph> change)
        {
            if (graph == null) return;
            Undo.RecordObject(graph, undoName);
            change(graph);
            EditorUtility.SetDirty(graph);
            if (EditorUtility.IsPersistent(graph)) AssetDatabase.SaveAssetIfDirty(graph);
        }
    }
}
