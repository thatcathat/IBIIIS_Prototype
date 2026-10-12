using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor의 대본 편집 규칙(화면과 분리, 자동 테스트 대상). 모두 새 단계 목록을 돌려주며 입력 목록은 바꾸지 않는다.
    /// 세로줄은 노드 화면과 같은 순서(장면 → 같은 순간 묶음)다.</summary>
    public static class DialogueScriptEdits
    {
        /// <summary>대본 전체의 세로줄: 첫 단계, 마지막 단계, 기다리는 단계(대사·입력 등)가 있는지.</summary>
        public static List<(int from, int to, bool hasMain)> Columns(IReadOnlyList<ScenarioStep> steps)
        {
            var list = new List<(int, int, bool)>();
            foreach (var scene in ScenarioTimeline.Scenes(steps))
                foreach (var (from, to) in ScenarioTimeline.Moments(steps, scene.start, scene.end)) list.Add((from, to, !ScenarioTimeline.IsInstant(steps[to].type)));
            return list;
        }
        public static int ColumnOf(List<(int from, int to, bool hasMain)> columns, int index) => columns.FindIndex(c => index >= c.from && index <= c.to);
        private const int Created = -1;
        private static ScenarioStep[] Build(List<int> ids, List<ScenarioStep> items, ScenarioStep created = default)
            => ids.Select(id => id == Created ? created : items[id]).ToArray();
        // 세로줄의 기다리는 단계 바로 앞 위치(없으면 세로줄 끝 다음). removed가 빠진 ids 기준.
        private static int BeforeMain(List<int> ids, (int from, int to, bool hasMain) column)
        {
            if (column.hasMain && ids.Contains(column.to)) return ids.IndexOf(column.to);
            int last = ids.FindLastIndex(id => id >= column.from && id <= column.to);
            if (last >= 0) return last + 1;
            int next = ids.FindIndex(id => id > column.to); return next < 0 ? ids.Count : next;
        }
        // 세로줄 끝(기다리는 단계) 바로 뒤 위치.
        private static int AfterColumn(List<int> ids, (int from, int to, bool hasMain) column)
        {
            int last = ids.FindLastIndex(id => id >= column.from && id <= column.to);
            if (last >= 0) return last + 1;
            int next = ids.FindIndex(id => id > column.to); return next < 0 ? ids.Count : next;
        }

        /// <summary>막대 끝을 세로줄 target에 놓았을 때(바뀌지 않으면 null). newOwner는 옮긴 뒤 그 표시 단계의 번호.
        /// 앞쪽: 그 단계를 세로줄의 기다리는 단계 바로 앞으로 옮긴다(표시 끝 단계가 있으면 끝 세로줄보다 뒤로는 못 감).
        /// 뒤쪽: 그 단계를 '표시 끝 단계까지 유지'로 바꾸고, 표시 끝 단계를 세로줄의 기다리는 단계 바로 뒤로 옮기거나 새로 만든다(시작보다 앞으로는 못 감).
        /// 감정 표시·자세가 아닌 바로 넘어가는 단계도 앞쪽 규칙으로 다른 세로줄에 옮길 수 있다.</summary>
        public static ScenarioStep[] MoveDisplayEdge(ScenarioStep[] steps, int owner, bool start, int target, out int newOwner)
        {
            newOwner = owner;
            var columns = Columns(steps);
            if (target < 0 || target >= columns.Count) return null;
            var span = ScenarioTimeline.SpanOf(steps, owner);
            int startColumn = ColumnOf(columns, owner), endColumn = span != null ? ColumnOf(columns, span.Value.End) : startColumn;
            var ids = Enumerable.Range(0, steps.Length).ToList(); var items = new List<ScenarioStep>(steps);
            ScenarioStep marker = default;
            if (start)
            {
                if (span != null && span.Value.Marker >= 0 && target > endColumn) target = endColumn;
                if (target == startColumn) return null;
                ids.Remove(owner);
                ids.Insert(BeforeMain(ids, columns[target]), owner);
            }
            else
            {
                if (span == null) return null;
                if (target < startColumn) target = startColumn;
                var step = items[owner]; step.untilEnd = true; items[owner] = step;
                if (span.Value.Marker >= 0) ids.Remove(span.Value.Marker);
                int pos = AfterColumn(ids, columns[target]);
                if (pos <= ids.IndexOf(owner)) pos = ids.IndexOf(owner) + 1; // 시작보다 앞에 두지 않는다
                marker = EndFor(step);
                ids.Insert(pos, Created);
            }
            newOwner = ids.IndexOf(owner);
            return Build(ids, items, marker);
        }
        private static ScenarioStep EndFor(ScenarioStep display) => new ScenarioStep
        {
            type = ScenarioStepType.EndDisplay, display = display.type == ScenarioStepType.Pose ? ScenarioDisplay.Pose : ScenarioDisplay.Emote,
            target = display.type == ScenarioStepType.Emote ? display.target : "player",
        };
        /// <summary>막대 전체(시작과 끝)를 세로줄 delta만큼 옮긴다(바뀌지 않으면 null). 표시 끝 단계가 없는 막대는 시작만 옮기고 끝은 규칙대로 따라간다.</summary>
        public static ScenarioStep[] MoveSpan(ScenarioStep[] steps, int owner, int delta, out int newOwner)
        {
            newOwner = owner;
            var span = ScenarioTimeline.SpanOf(steps, owner);
            if (span == null || delta == 0) return null;
            var columns = Columns(steps);
            int startColumn = ColumnOf(columns, owner), endColumn = ColumnOf(columns, span.Value.End);
            int last = columns.Count - 1;
            delta = Mathf.Clamp(delta, -startColumn, last - (span.Value.Marker >= 0 ? endColumn : startColumn));
            if (delta == 0) return null;
            if (span.Value.Marker < 0) return MoveDisplayEdge(steps, owner, true, startColumn + delta, out newOwner);
            // 앞쪽으로 옮길 때는 시작 먼저, 뒤쪽으로 옮길 때는 끝 먼저(서로 막지 않게).
            ScenarioStep[] first, second;
            if (delta > 0)
            {
                first = MoveDisplayEdge(steps, owner, false, endColumn + delta, out newOwner) ?? steps;
                second = MoveDisplayEdge(first, newOwner, true, startColumn + delta, out newOwner);
            }
            else
            {
                first = MoveDisplayEdge(steps, owner, true, startColumn + delta, out newOwner) ?? steps;
                second = MoveDisplayEdge(first, newOwner, false, endColumn + delta, out newOwner);
            }
            return second ?? first;
        }
        /// <summary>세로줄 하나(바로 넘어가는 단계와 기다리는 단계)를 틈 gap(0 = 맨 앞, 세로줄 수 = 맨 끝) 자리로 옮긴다(바뀌지 않으면 null).
        /// 그 세로줄에 있는 표시 끝 단계는 앞 표시의 끝이므로 제자리에 둔다.</summary>
        public static ScenarioStep[] MoveColumn(ScenarioStep[] steps, int column, int gap, out int newMain)
        {
            var columns = Columns(steps);
            newMain = column >= 0 && column < columns.Count ? columns[column].to : -1;
            if (column < 0 || column >= columns.Count || gap < 0 || gap > columns.Count || gap == column || gap == column + 1) return null;
            var ids = Enumerable.Range(0, steps.Length).ToList();
            var block = new List<int>();
            for (int i = columns[column].from; i <= columns[column].to; i++) if (steps[i].type != ScenarioStepType.EndDisplay) block.Add(i);
            foreach (var id in block) ids.Remove(id);
            int pos = gap < columns.Count ? ids.FindIndex(id => id >= columns[gap].from) : ids.Count;
            if (pos < 0) pos = ids.Count;
            ids.InsertRange(pos, block);
            newMain = ids.IndexOf(columns[column].to);
            return Build(ids, new List<ScenarioStep>(steps));
        }
        /// <summary>바로 넘어가는 단계(감정 표시·자세·등장 등)를 세로줄에 넣는다(기다리는 단계 바로 앞). at은 넣은 위치.</summary>
        public static ScenarioStep[] InsertInColumn(ScenarioStep[] steps, int column, ScenarioStep step, out int at)
        {
            var columns = Columns(steps); var ids = Enumerable.Range(0, steps.Length).ToList();
            at = column >= 0 && column < columns.Count ? BeforeMain(ids, columns[column]) : ids.Count;
            ids.Insert(at, Created);
            return Build(ids, new List<ScenarioStep>(steps), step);
        }
        /// <summary>세로줄 column 뒤에 단계를 넣는다(-1이면 맨 앞). 기다리는 단계면 새 세로줄이 된다. at은 넣은 위치.</summary>
        public static ScenarioStep[] InsertAfterColumn(ScenarioStep[] steps, int column, ScenarioStep step, out int at)
        {
            var columns = Columns(steps); var ids = Enumerable.Range(0, steps.Length).ToList();
            at = column < 0 ? 0 : column < columns.Count ? AfterColumn(ids, columns[column]) : ids.Count;
            ids.Insert(at, Created);
            return Build(ids, new List<ScenarioStep>(steps), step);
        }
        /// <summary>세로줄 from~to 범위로 감정 표시·자세를 새로 만든다: 시작 세로줄에 넣고 '표시 끝 단계까지 유지'로, 끝 세로줄 뒤에 표시 끝 단계를 둔다. owner는 만든 단계 위치.</summary>
        public static ScenarioStep[] AddSpan(ScenarioStep[] steps, int fromColumn, int toColumn, ScenarioStep display, out int owner)
        {
            if (toColumn < fromColumn) (fromColumn, toColumn) = (toColumn, fromColumn);
            display.untilEnd = true;
            var withOwner = InsertInColumn(steps, fromColumn, display, out owner);
            var columns = Columns(withOwner); var ids = Enumerable.Range(0, withOwner.Length).ToList();
            int pos = toColumn < columns.Count ? AfterColumn(ids, columns[toColumn]) : ids.Count;
            if (pos <= owner) pos = owner + 1;
            ids.Insert(pos, Created);
            return Build(ids, new List<ScenarioStep>(withOwner), EndFor(display));
        }
        /// <summary>단계들을 지운다. 지우는 감정 표시·자세의 표시 끝 단계도 함께 지운다(짝 없는 끝이 남지 않게).</summary>
        public static ScenarioStep[] Delete(ScenarioStep[] steps, IEnumerable<int> indices)
        {
            var remove = new HashSet<int>(indices.Where(i => i >= 0 && i < steps.Length));
            foreach (var i in remove.ToList()) { var span = ScenarioTimeline.SpanOf(steps, i); if (span != null && span.Value.Marker >= 0) remove.Add(span.Value.Marker); }
            return steps.Where((_, i) => !remove.Contains(i)).ToArray();
        }
        /// <summary>단계 묶음을 position 앞에 넣는다(복사본, ID는 비워 대본이 새로 채움). at은 첫 단계 위치.</summary>
        public static ScenarioStep[] InsertSteps(ScenarioStep[] steps, int position, IReadOnlyList<ScenarioStep> inserted, out int at)
        {
            at = Mathf.Clamp(position, 0, steps.Length);
            var list = new List<ScenarioStep>(steps);
            list.InsertRange(at, inserted.Select(s => { s.id = null; return s; }));
            return list.ToArray();
        }

        // ── 검사 ──
        public readonly struct Issue
        {
            public readonly int Index; public readonly string Message; public readonly bool Error;
            public Issue(int index, string message, bool error) { Index = index; Message = message; Error = error; }
        }
        /// <summary>진행이 멈추거나 의도와 달라질 수 있는 단계를 찾는다(오류: 진행기가 건너뛰거나 실패, 주의: 의도 확인 필요).</summary>
        public static List<Issue> Validate(ScenarioStep[] steps)
        {
            var issues = new List<Issue>();
            var markers = new HashSet<int>();
            for (int i = 0; i < steps.Length; i++) { var span = ScenarioTimeline.SpanOf(steps, i); if (span != null && span.Value.Marker >= 0) markers.Add(span.Value.Marker); }
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                switch (s.type)
                {
                    case ScenarioStepType.Line: if (string.IsNullOrWhiteSpace(s.line.text)) issues.Add(new Issue(i, "대사가 비어 있음", false)); break;
                    case ScenarioStepType.Pose: if (s.sprite == null) issues.Add(new Issue(i, "자세 그림이 없어 건너뜀", true)); else Hold(s, i); break;
                    case ScenarioStepType.Emote:
                        if (string.IsNullOrEmpty(s.target)) issues.Add(new Issue(i, "감정 표시 대상 ID가 비어 있음", true));
                        else if (string.IsNullOrEmpty(s.text) && s.sprite == null) issues.Add(new Issue(i, "감정 표시 글자·아이콘이 모두 비어 있음", false));
                        Hold(s, i); break;
                    case ScenarioStepType.EndDisplay: if (!markers.Contains(i)) issues.Add(new Issue(i, "끝낼 표시가 없음(앞에 '표시 끝 단계까지 유지'인 같은 대상 표시가 없음)", false)); break;
                    case ScenarioStepType.SetActive: if (string.IsNullOrEmpty(s.target)) issues.Add(new Issue(i, "켜기·끄기 대상 ID가 비어 있음", true)); break;
                    case ScenarioStepType.SpawnEnemy:
                        if (s.prefab == null || s.prefab.GetComponent<EnemyDefinition>() == null) issues.Add(new Issue(i, "적 합류: EnemyDefinition이 있는 프리팹이 필요함", true)); break;
                    case ScenarioStepType.WaitAction: case ScenarioStepType.WaitFreeMove: case ScenarioStepType.WaitReachCell: case ScenarioStepType.FreePlay:
                        if (s.actions == ScenarioActions.None) issues.Add(new Issue(i, "허용 행동이 없어 진행할 수 없음", true)); break;
                    case ScenarioStepType.LoadScene:
                        if (string.IsNullOrEmpty(s.scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(s.scenePath) == null) issues.Add(new Issue(i, $"씬을 찾을 수 없음: {s.scenePath}", true)); break;
                    case ScenarioStepType.Wait: if (s.seconds <= 0) issues.Add(new Issue(i, "기다릴 시간이 0초", false)); break;
                }
            }
            return issues;
            // '표시 끝 단계까지 유지'인데 표시 끝 단계가 없으면 대본 끝까지 남는다.
            void Hold(ScenarioStep s, int i)
            {
                if (!s.untilEnd) return;
                var span = ScenarioTimeline.SpanOf(steps, i);
                if (span != null && span.Value.EndKind == ScenarioSpanEnd.ScriptEnd) issues.Add(new Issue(i, "'표시 끝 단계까지 유지'인데 표시 끝 단계가 없어 대본 끝(씬 전환)까지 남음", false));
            }
        }
    }
}
