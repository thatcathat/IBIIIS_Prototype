using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>검증 결과 경로를 실제 GridSession으로 다시 실행해 단계별 상태를 기록한다. 맵 에디터 캔버스가 이 기록을 그린다. 맵은 수정하지 않는다.</summary>
    public sealed class MapReplay
    {
        public sealed class Frame
        {
            public string Label;
            /// <summary>이 단계 행동을 시작한 플레이어 위치. 시작 프레임은 Player와 같다.</summary>
            public Vector2Int From, Player;
            public BattlePhase Phase;
            public Vector2Int[] EnemyPositions, EnemyDirections;
            public bool[] EnemyAlive;
            /// <summary>이 행동이 끝난 뒤 판정에 쓰인 공격 칸.</summary>
            public HashSet<Vector2Int> Attack;
            public int AliveCount;
        }
        public readonly string Title;
        public readonly List<Frame> Frames = new List<Frame>();
        /// <summary>경로를 끝까지 재생하지 못했으면 이유. 맵이 분석 후 바뀌었을 때만 생긴다.</summary>
        public string Problem { get; private set; }
        public int Index;
        public Frame Current => Frames[Mathf.Clamp(Index, 0, Frames.Count - 1)];
        private MapReplay(string title) { Title = title; }
        public static MapReplay Build(GridMap map, string title, IReadOnlyList<SolverMove> moves)
        {
            var replay = new MapReplay(title);
            var session = new GridSession(map);
            replay.Frames.Add(Capture(session, "시작", session.Position));
            for (int i = 0; i < moves.Count; i++)
            {
                var from = session.Position;
                if (!session.TryAct(moves[i].Action, moves[i].Direction)) { replay.Problem = $"{i + 1}번째 행동({moves[i]})을 실행할 수 없습니다. 맵이 분석 후 바뀌었으면 다시 검증하세요."; break; }
                session.Advance(1000f);
                replay.Frames.Add(Capture(session, $"{i + 1}. {moves[i]}", from));
            }
            return replay;
        }
        private static Frame Capture(GridSession session, string label, Vector2Int from)
        {
            int count = session.Enemies.Count;
            var frame = new Frame { Label = label, From = from, Player = session.Position, Phase = session.Phase, AliveCount = session.AliveCount,
                EnemyPositions = new Vector2Int[count], EnemyDirections = new Vector2Int[count], EnemyAlive = new bool[count], Attack = new HashSet<Vector2Int>(session.AttackCells) };
            for (int i = 0; i < count; i++)
            {
                var enemy = session.Enemies[i];
                frame.EnemyPositions[i] = enemy.Position; frame.EnemyDirections[i] = enemy.Direction; frame.EnemyAlive[i] = enemy.Alive;
            }
            return frame;
        }
    }
}
