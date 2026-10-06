using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace IBIIIS
{
    public readonly struct SolverMove
    {
        public readonly PlayerAction Action;
        public readonly Vector2Int Direction;
        public SolverMove(PlayerAction action, Vector2Int direction) { Action = action; Direction = direction; }
        public override string ToString()
        {
            if (Action == PlayerAction.Wait) return "대기";
            string where = Action == PlayerAction.Roll ? (Direction.x < 0 ? "좌" : "우") + (Direction.y > 0 ? "상" : "하")
                : Direction.y > 0 ? "위" : Direction.y < 0 ? "아래" : Direction.x > 0 ? "오른쪽" : "왼쪽";
            return (Action == PlayerAction.Move ? "이동 " : Action == PlayerAction.Dash ? "대시 " : "구르기 ") + where;
        }
    }
    public sealed class MapAnalysisOptions
    {
        /// <summary>탐색할 서로 다른 상태의 최대 수. 넘으면 탐색을 멈추고 결과를 불완전으로 표시한다.</summary>
        public int MaxStates = 200000;
        /// <summary>탐색할 최대 행동 수(깊이).</summary>
        public int MaxDepth = 100;
        /// <summary>탐색 중 주기적으로 (펼친 상태 수, 발견한 상태 수)와 함께 호출한다. false를 반환하면 취소한다.</summary>
        public Func<int, int, bool> Progress;
    }
    public sealed class MapAnalysis
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Notes = new List<string>();
        /// <summary>모든 도달 가능한 상태를 끝까지 탐색했으면 true. 상태 수·깊이 제한이나 취소로 멈췄으면 false이며, 이때 "불가능"류 결론과 갇힌 상태 수는 확정되지 않는다.</summary>
        public bool Completed { get; internal set; }
        public bool Cancelled { get; internal set; }
        /// <summary>발견한 입력 대기 상태 수.</summary>
        public int States { get; internal set; }
        public int MaxDepthReached { get; internal set; }
        public bool Solvable => ShortestWin > 0;
        /// <summary>적을 모두 제거하는 가장 짧은 행동 수. 찾지 못했으면 -1.</summary>
        public int ShortestWin { get; internal set; } = -1;
        public List<SolverMove> WinPath { get; internal set; }
        /// <summary>플레이어가 패배하는 가장 짧은 행동 수. 탐색 범위에서 패배가 없으면 -1.</summary>
        public int EarliestLoss { get; internal set; } = -1;
        public List<SolverMove> LossPath { get; internal set; }
        /// <summary>더 이상 승리할 수 없는 입력 대기 상태 수(되돌리기·재시작 외에는 탈출 불가). 탐색이 불완전하면 -1.</summary>
        public int DeadStates { get; internal set; } = -1;
        public string ToReport()
        {
            var text = new StringBuilder();
            foreach (var error in Errors) text.AppendLine("오류: " + error);
            foreach (var note in Notes) text.AppendLine(note);
            if (Errors.Count > 0 || States == 0) return text.ToString().TrimEnd();
            text.AppendLine($"탐색: 입력 대기 상태 {States}개, 최대 {MaxDepthReached}행동 깊이 — {(Completed ? "끝까지 탐색 완료" : Cancelled ? "취소됨(불완전)" : "제한에 도달해 중단(불완전)")}");
            text.AppendLine(Solvable ? $"클리어: 가능 — 최단 {ShortestWin}행동\n  경로: {Join(WinPath)}" : Completed ? "클리어: 불가능 — 어떤 행동 순서로도 적을 모두 제거할 수 없음" : "클리어: 탐색 범위에서 찾지 못함(불완전)");
            text.AppendLine(EarliestLoss > 0 ? $"가장 빠른 패배: {EarliestLoss}행동\n  경로: {Join(LossPath)}" : Completed ? "가장 빠른 패배: 없음 — 패배할 수 있는 경우가 없음" : "가장 빠른 패배: 탐색 범위에서 없음(불완전)");
            text.Append(DeadStates >= 0 ? $"승리 불가 상태: {DeadStates}개 / {States}개 (되돌리기·재시작으로만 복구)" : "승리 불가 상태: 탐색이 불완전해 계산하지 않음");
            return text.ToString();
        }
        private static string Join(List<SolverMove> path) { var parts = new List<string>(); foreach (var move in path) parts.Add(move.ToString()); return string.Join(" → ", parts); }
    }
    /// <summary>맵의 모든 플레이어 행동 순서를 너비 우선으로 탐색해 클리어 가능 여부와 최단 경로, 가장 빠른 패배, 승리 불가 상태를 구한다.
    /// 전투 판정은 GridSession을 그대로 사용하므로 규칙이 바뀌어도 분석이 자동으로 따라간다. 같은 상태는 한 번만 탐색한다.</summary>
    public static class MapSolver
    {
        private static readonly SolverMove[] Moves = BuildMoves();
        private static SolverMove[] BuildMoves()
        {
            var list = new List<SolverMove> { new SolverMove(PlayerAction.Wait, Vector2Int.zero) };
            var straight = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            foreach (var d in straight) list.Add(new SolverMove(PlayerAction.Move, d));
            foreach (var d in straight) list.Add(new SolverMove(PlayerAction.Dash, d));
            foreach (var d in new[] { new Vector2Int(-1, 1), new Vector2Int(1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1) }) list.Add(new SolverMove(PlayerAction.Roll, d));
            return list.ToArray();
        }
        private const int Waiting = 0, Won = 1, Lost = 2;
        public static MapAnalysis Analyze(GridMap map, MapAnalysisOptions options = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            options = options ?? new MapAnalysisOptions();
            var result = new MapAnalysis();
            result.Errors.AddRange(map.ValidateMap());
            if (result.Errors.Count > 0) return result;
            if (map.Enemies.Count == 0) { result.Notes.Add("배치된 적이 없어 승리 조건이 없습니다. 이동 테스트용 맵입니다."); return result; }
            var session = new GridSession(map) { RecordHistory = false };
            var states = new List<string> { session.SaveCore() };
            var parent = new List<int> { -1 }; var move = new List<int> { -1 }; var depth = new List<int> { 0 }; var kind = new List<int> { Waiting };
            var visited = new Dictionary<string, int> { [states[0]] = 0 };
            var edgeFrom = new List<int>(); var edgeTo = new List<int>();
            bool truncated = false; int firstWin = -1, firstLoss = -1, expanded = 0;
            for (int head = 0; head < states.Count; head++)
            {
                if (kind[head] != Waiting) continue;
                if (depth[head] >= options.MaxDepth) { truncated = true; continue; }
                if (options.Progress != null && expanded % 500 == 0 && !options.Progress(expanded, states.Count)) { result.Cancelled = true; truncated = true; break; }
                expanded++;
                for (int m = 0; m < Moves.Length; m++)
                {
                    session.LoadCore(states[head]);
                    if (!session.TryAct(Moves[m].Action, Moves[m].Direction, .01f, .01f)) continue;
                    session.Advance(1000f);
                    int outcome = session.Phase == BattlePhase.Won ? Won : session.Phase == BattlePhase.Lost ? Lost : Waiting;
                    string core = session.SaveCore(); string key = outcome == Waiting ? core : (outcome == Won ? "W" : "L") + core;
                    if (!visited.TryGetValue(key, out int index))
                    {
                        if (states.Count >= options.MaxStates) { truncated = true; continue; }
                        index = states.Count; visited[key] = index; states.Add(core); parent.Add(head); move.Add(m); depth.Add(depth[head] + 1); kind.Add(outcome);
                        if (outcome == Won && firstWin < 0) firstWin = index;
                        if (outcome == Lost && firstLoss < 0) firstLoss = index;
                    }
                    if (outcome != Lost) { edgeFrom.Add(head); edgeTo.Add(index); }
                }
            }
            int waiting = 0, maxDepth = 0;
            for (int i = 0; i < states.Count; i++) { if (kind[i] == Waiting) waiting++; maxDepth = Mathf.Max(maxDepth, depth[i]); }
            result.States = waiting; result.MaxDepthReached = maxDepth; result.Completed = !truncated;
            if (firstWin >= 0) { result.ShortestWin = depth[firstWin]; result.WinPath = PathTo(firstWin, parent, move); }
            if (firstLoss >= 0) { result.EarliestLoss = depth[firstLoss]; result.LossPath = PathTo(firstLoss, parent, move); }
            if (!truncated) result.DeadStates = CountDeadStates(states.Count, kind, edgeFrom, edgeTo);
            return result;
        }
        private static List<SolverMove> PathTo(int node, List<int> parent, List<int> move)
        {
            var path = new List<SolverMove>();
            for (; parent[node] >= 0; node = parent[node]) path.Add(Moves[move[node]]);
            path.Reverse(); return path;
        }
        // 승리 상태에서 간선을 거꾸로 따라가 승리에 닿을 수 있는 대기 상태를 표시하고, 닿지 못하는 대기 상태 수를 센다.
        private static int CountDeadStates(int count, List<int> kind, List<int> edgeFrom, List<int> edgeTo)
        {
            var start = new int[count + 1];
            foreach (var to in edgeTo) start[to + 1]++;
            for (int i = 0; i < count; i++) start[i + 1] += start[i];
            var fill = (int[])start.Clone(); var sources = new int[edgeFrom.Count];
            for (int i = 0; i < edgeFrom.Count; i++) sources[fill[edgeTo[i]]++] = edgeFrom[i];
            var canWin = new bool[count]; var queue = new Queue<int>();
            for (int i = 0; i < count; i++) if (kind[i] == Won) { canWin[i] = true; queue.Enqueue(i); }
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                for (int i = start[node]; i < start[node + 1]; i++) { int from = sources[i]; if (!canWin[from]) { canWin[from] = true; queue.Enqueue(from); } }
            }
            int dead = 0;
            for (int i = 0; i < count; i++) if (kind[i] == Waiting && !canWin[i]) dead++;
            return dead;
        }
    }
}
