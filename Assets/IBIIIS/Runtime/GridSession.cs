using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    public enum BattlePhase { Waiting, Moving, Won, Lost }
    public enum PlayerAction { Move, Wait, Dash, Roll }
    public sealed class EnemyState
    {
        public GameObject Prefab { get; internal set; }
        public Vector2Int Position { get; internal set; }
        public Vector2Int Direction { get; internal set; }
        public Vector2Int StepFrom { get; internal set; }
        public Vector2Int StepTo { get; internal set; }
        public bool Alive { get; internal set; } = true;
        public bool Recognized { get; internal set; }
        public int MoveCells { get; internal set; }
        internal EnemyActionStep[] Actions;
        // 이번 행동에서 실행 중인 행동 목록의 위치와, 현재 전진 단위에서 남은 칸 수. GridSession이 행동마다 초기화한다.
        internal int ActionIndex, CellsLeft;
        internal bool Moving;
        internal Vector2Int[] Recognition, Attack, RecognizedAttack;
        internal readonly List<Vector2Int> Path = new List<Vector2Int>();
    }
    public enum EnemyRange { Recognition, Attack }
    /// <summary>적끼리 같은 칸에 도착해 함께 사라진 사건. 연출용 기록이며 판정에는 쓰지 않는다.</summary>
    public sealed class EnemyCollision
    {
        public Vector2Int Cell { get; }
        /// <summary>이번 플레이어 행동 안에서 몇 번째 적 이동 단계였는지(1부터).</summary>
        public int Step { get; }
        /// <summary>충돌한 적의 GridSession.Enemies 인덱스.</summary>
        public IReadOnlyList<int> Enemies { get; }
        public EnemyCollision(Vector2Int cell, int step, IReadOnlyList<int> enemies) { Cell = cell; Step = step; Enemies = enemies; }
    }
    public sealed class GridSession
    {
        // 플레이어 행동 시작 직전의 전체 전투 상태. 행동 한 번마다 하나를 쌓아 되돌린다.
        private sealed class Snapshot
        {
            public Vector2Int Position, Destination;
            public BattlePhase Phase;
            public bool EvasionLocked;
            public Vector2Int[] AttackCells;
            public EnemyMemo[] Enemies;
        }
        private struct EnemyMemo
        {
            public Vector2Int Position, Direction, StepFrom, StepTo;
            public bool Alive, Recognized;
            public Vector2Int[] Path;
        }
        private readonly Stack<Snapshot> history = new Stack<Snapshot>();
        private readonly bool[,] walkable;
        // 맵 분석기가 행동마다 이동 가능 여부를 묻기 때문에 델리게이트와 충돌 계산 버퍼를 한 번만 만들어 재사용한다.
        private readonly Func<Vector2Int, bool> available;
        private readonly List<int> collisionGroup = new List<int>();
        private bool[] grouped = Array.Empty<bool>();
        private readonly List<EnemyState> enemies = new List<EnemyState>();
        private readonly HashSet<Vector2Int> attackCells = new HashSet<Vector2Int>();
        private float elapsed, duration, enemyDuration, enemyElapsed;
        private bool enemiesComplete;
        private bool evasion;
        // 적은 플레이어가 이번 행동으로 어디로 갈지 모른다. 인식·조준은 행동 시작 직전의 플레이어 위치만 사용한다.
        private Vector2Int aimOrigin;
        private readonly List<EnemyCollision> collisions = new List<EnemyCollision>();
        private int enemyStepIndex;
        /// <summary>이번 행동에서 끝난 적 이동 단계 수. 연출이 단계별 착지를 알기 위해 읽는다.</summary>
        public int EnemyStepsCompleted => enemyStepIndex;
        /// <summary>현재(또는 마지막) 플레이어 행동 동안 일어난 적 충돌. 다음 행동 시작·되돌리기 때 비운다.</summary>
        public IReadOnlyList<EnemyCollision> Collisions => collisions;
        private readonly List<int> attackers = new List<int>();
        /// <summary>마지막 행동으로 패배했다면 플레이어를 맞힌 적의 인덱스(공격 범위 또는 이동 경로). 연출용이며 다음 행동 시작·되돌리기 때 비운다.</summary>
        public IReadOnlyList<int> Attackers => attackers;
        public IReadOnlyList<EnemyState> Enemies => enemies;
        public IEnumerable<Vector2Int> AttackCells => attackCells;
        public Vector2Int Position { get; private set; }
        public Vector2Int Destination { get; private set; }
        public BattlePhase Phase { get; private set; } = BattlePhase.Waiting;
        public bool IsMoving => IsBusy && elapsed < duration;
        public bool IsBusy => Phase == BattlePhase.Moving;
        public bool IsEnemiesMoving => IsBusy && !enemiesComplete;
        public float EnemyProgress => IsEnemiesMoving ? Mathf.Clamp01(enemyElapsed / enemyDuration) : 0;
        public bool EvasionLocked { get; private set; }
        public float Progress => IsBusy ? Mathf.Clamp01(elapsed / duration) : 0;
        public float ActiveDuration => duration;
        public int AliveCount { get { int count = 0; foreach (var e in enemies) if (e.Alive) count++; return count; } }
        public GridSession(GridMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var errors = map.ValidateMap();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            walkable = new bool[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++) walkable[x, y] = map.IsWalkable(new Vector2Int(x, y));
            available = Available;
            Position = Destination = map.Start;
            foreach (var spawn in map.Enemies)
            {
                var d = spawn.Prefab.GetComponent<EnemyDefinition>();
                enemies.Add(new EnemyState { Prefab = spawn.Prefab, Position = spawn.Position, StepFrom = spawn.Position, StepTo = spawn.Position,
                    Direction = spawn.Direction, MoveCells = d.MoveCells, Actions = d.Actions, Recognition = d.Recognition, Attack = d.Attack, RecognizedAttack = d.RecognizedAttack });
            }
        }
        public static bool CanStep(Vector2Int position, Vector2Int direction, Func<Vector2Int, bool> available)
            => Math.Abs((long)direction.x) + Math.Abs((long)direction.y) == 1 && available(position + direction);
        private bool IsWalkable(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < walkable.GetLength(0) && p.y < walkable.GetLength(1) && walkable[p.x, p.y];
        private bool Available(Vector2Int p)
        {
            if (!IsWalkable(p)) return false;
            foreach (var e in enemies) if (e.Alive && e.Position == p) return false;
            return true;
        }
        public bool CanMove(Vector2Int direction) => CanAct(PlayerAction.Move, direction);
        public bool CanAct(PlayerAction action, Vector2Int direction)
        {
            if (Phase != BattlePhase.Waiting) return false;
            if (action == PlayerAction.Wait) return direction == Vector2Int.zero;
            if (action == PlayerAction.Move) return CanStep(Position, direction, available);
            if (EvasionLocked) return false;
            if (action == PlayerAction.Dash) return CanStep(Position, direction, available) && Available(Position + direction * 2);
            if (action == PlayerAction.Roll) return Math.Abs((long)direction.x) == 1 && Math.Abs((long)direction.y) == 1 && Available(Position + direction);
            return false;
        }
        public bool TryMove(Vector2Int direction, float moveDuration = .25f) => TryAct(PlayerAction.Move, direction, moveDuration);
        public bool TryAct(PlayerAction action, Vector2Int direction, float moveDuration = .25f, float enemyStepDuration = .25f)
        {
            if (!CanAct(action, direction)) return false;
            if (!ValidTime(moveDuration) || !ValidTime(enemyStepDuration)) throw new ArgumentOutOfRangeException(nameof(moveDuration));
            if (RecordHistory) history.Push(Capture());
            evasion = action == PlayerAction.Dash || action == PlayerAction.Roll;
            Destination = Position + direction * (action == PlayerAction.Dash ? 2 : 1);
            collisions.Clear(); attackers.Clear(); enemyStepIndex = 0; aimOrigin = Position; duration = moveDuration; enemyDuration = enemyStepDuration; elapsed = 0; Phase = BattlePhase.Moving; attackCells.Clear(); BeginEnemies(); return true;
        }
        // 맵 분석기가 같은 세션으로 수많은 행동을 시험할 때 되돌리기 기록이 쌓이지 않게 끈다.
        internal bool RecordHistory = true;
        /// <summary>앞으로의 전개를 정하는 상태만 문자열로 저장한다: 플레이어 위치, 회피기 쿨다운, 각 적의 생존, 살아 있는 적의 위치·방향.
        /// 죽은 적의 위치·방향은 이후 판정에 쓰이지 않으므로(모든 판정이 생존을 먼저 본다) 고정값으로 기록해, 어디서 충돌했는지만 다른 상태를 같은 상태로 본다.
        /// 입력 대기 상태에서만 의미가 있다.</summary>
        internal string SaveCore()
        {
            var data = new char[3 + enemies.Count * 5];
            data[0] = (char)(Position.x + 1); data[1] = (char)(Position.y + 1); data[2] = (char)(EvasionLocked ? 1 : 0);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; int at = 3 + i * 5;
                if (!e.Alive) { data[at] = data[at + 1] = data[at + 2] = data[at + 3] = (char)0; data[at + 4] = (char)0; continue; } // LoadCore 뒤 위치 (-1,-1)·방향 (-1,-1)이지만 쓰이지 않는다
                data[at] = (char)(e.Position.x + 1); data[at + 1] = (char)(e.Position.y + 1);
                data[at + 2] = (char)(e.Direction.x + 1); data[at + 3] = (char)(e.Direction.y + 1); data[at + 4] = (char)1;
            }
            return new string(data);
        }
        internal void LoadCore(string core)
        {
            Position = Destination = new Vector2Int(core[0] - 1, core[1] - 1); EvasionLocked = core[2] == 1; Phase = BattlePhase.Waiting;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; int at = 3 + i * 5;
                e.Position = e.StepFrom = e.StepTo = new Vector2Int(core[at] - 1, core[at + 1] - 1);
                e.Direction = new Vector2Int(core[at + 2] - 1, core[at + 3] - 1); e.Alive = core[at + 4] == 1; e.Recognized = false; e.Path.Clear();
            }
            attackCells.Clear(); collisions.Clear(); attackers.Clear(); elapsed = enemyElapsed = 0; enemiesComplete = true; evasion = false;
        }
        public int UndoCount => history.Count;
        /// <summary>행동이 진행 중이 아니고 되돌릴 행동이 있을 때 true. 승리·패배 상태에서도 되돌릴 수 있다.</summary>
        public bool CanUndo => !IsBusy && history.Count > 0;
        public bool TryUndo()
        {
            if (!CanUndo) return false;
            var snapshot = history.Pop();
            Position = snapshot.Position; Destination = snapshot.Destination; Phase = snapshot.Phase; EvasionLocked = snapshot.EvasionLocked;
            attackCells.Clear(); foreach (var cell in snapshot.AttackCells) attackCells.Add(cell);
            for (int i = 0; i < enemies.Count; i++)
            {
                var memo = snapshot.Enemies[i]; var e = enemies[i];
                e.Position = memo.Position; e.Direction = memo.Direction; e.StepFrom = memo.StepFrom; e.StepTo = memo.StepTo;
                e.Alive = memo.Alive; e.Recognized = memo.Recognized;
                e.Path.Clear(); e.Path.AddRange(memo.Path);
            }
            collisions.Clear(); attackers.Clear(); elapsed = enemyElapsed = 0; enemiesComplete = true; evasion = false;
            return true;
        }
        private Snapshot Capture()
        {
            var snapshot = new Snapshot { Position = Position, Destination = Destination, Phase = Phase, EvasionLocked = EvasionLocked,
                AttackCells = new List<Vector2Int>(attackCells).ToArray(), Enemies = new EnemyMemo[enemies.Count] };
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                snapshot.Enemies[i] = new EnemyMemo { Position = e.Position, Direction = e.Direction, StepFrom = e.StepFrom, StepTo = e.StepTo,
                    Alive = e.Alive, Recognized = e.Recognized, Path = e.Path.ToArray() };
            }
            return snapshot;
        }
        /// <summary>적의 현재 위치·방향 기준 범위. 공격 범위는 지금 플레이어를 인식하는지에 따라 기본/인식 후 범위를 고른다.
        /// 적이 이동·재조준한 뒤의 예측이 아니며, 이동 가능한 칸만 반환한다.</summary>
        public IEnumerable<Vector2Int> RangeCells(EnemyState enemy, EnemyRange kind)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var offsets = kind == EnemyRange.Recognition ? enemy.Recognition : Recognizes(enemy, Position) ? enemy.RecognizedAttack : enemy.Attack;
            foreach (var offset in offsets)
            {
                var cell = enemy.Position + LocalToGrid(offset, enemy.Direction);
                if (IsWalkable(cell)) yield return cell;
            }
        }
        private static bool ValidTime(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        public static Vector2Int LocalToGrid(Vector2Int local, Vector2Int facing) => new Vector2Int(facing.y, -facing.x) * local.x + facing * local.y;
        /// <summary>지금(입력 대기 중의) 플레이어 칸이 이 적의 인식 범위 안이면 true. 다음 행동에서 이 적이 플레이어 쪽으로 조준하고, 지금 공격 범위가 인식 후 범위인지와 같은 기준이다.</summary>
        public bool IsRecognizing(EnemyState enemy) => enemy != null && enemy.Alive && Recognizes(enemy, Position);
        private bool Recognizes(EnemyState e, Vector2Int target)
        {
            foreach (var offset in e.Recognition) if (e.Position + LocalToGrid(offset, e.Direction) == target) return true;
            return false;
        }
        private void Aim(EnemyState e, Vector2Int target)
        {
            var delta = target - e.Position;
            if (Math.Abs(delta.x) > Math.Abs(delta.y)) e.Direction = new Vector2Int(delta.x > 0 ? 1 : -1, 0);
            else if (Math.Abs(delta.y) > Math.Abs(delta.x)) e.Direction = new Vector2Int(0, delta.y > 0 ? 1 : -1);
            else if ((delta.x * e.Direction.x + delta.y * e.Direction.y) <= 0) e.Direction = e.Direction.x != 0 ? new Vector2Int(0, delta.y > 0 ? 1 : -1) : new Vector2Int(delta.x > 0 ? 1 : -1, 0);
        }
        private void BeginEnemies()
        {
            enemiesComplete = AliveCount == 0; enemyElapsed = 0;
            if (enemiesComplete) return;
            foreach (var e in enemies) if (e.Alive) { e.Path.Clear(); e.Recognized = false; e.ActionIndex = 0; e.CellsLeft = 0; }
            foreach (var e in enemies) if (e.Alive) RunInstantActions(e);
            PlanEnemyStep();
        }
        // 시간을 쓰지 않는 행동(조준·회전)을 다음 전진 단위 직전까지 실행한다. 전진 단위를 만나면 남은 칸 수를 설정하고 멈춘다.
        private void RunInstantActions(EnemyState e)
        {
            while (e.CellsLeft == 0 && e.ActionIndex < e.Actions.Length)
            {
                var step = e.Actions[e.ActionIndex++];
                switch (step.Type)
                {
                    case EnemyActionType.AimAtPlayer: if (Recognizes(e, aimOrigin)) Aim(e, aimOrigin); break;
                    case EnemyActionType.Turn: e.Direction = EnemyActionStep.Rotate(e.Direction, step.Turn); break;
                    case EnemyActionType.MoveForward: e.CellsLeft = step.Cells; break;
                }
            }
        }
        private void PlanEnemyStep()
        {
            enemyElapsed = 0;
            foreach (var e in enemies)
            {
                e.StepFrom = e.StepTo = e.Position; e.Moving = false;
                if (!e.Alive || e.CellsLeft == 0) continue;
                e.Moving = true;
                var next = e.Position + e.Direction;
                if (!IsWalkable(next)) { e.Direction = -e.Direction; next = e.Position + e.Direction; }
                if (IsWalkable(next)) e.StepTo = next;
            }
        }
        private void CommitEnemyStep()
        {
            enemyStepIndex++;
            foreach (var e in enemies)
            {
                if (!e.Alive) continue;
                e.Position = e.StepTo;
                if (e.Moving) { e.Path.Add(e.Position); e.CellsLeft--; }
            }
            // 같은 칸에 도착한 살아 있는 적을 묶는다. 묶음은 가장 작은 적 인덱스 순, 묶음 안은 인덱스 오름차순(충돌 기록 순서 유지).
            // 묶음을 모두 정한 뒤 한꺼번에 제거하므로 세 마리 이상이 같은 칸에 와도 모두 사라진다.
            if (grouped.Length < enemies.Count) grouped = new bool[enemies.Count];
            Array.Clear(grouped, 0, enemies.Count);
            int firstCollision = collisions.Count;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].Alive || grouped[i]) continue;
                collisionGroup.Clear(); collisionGroup.Add(i);
                for (int j = i + 1; j < enemies.Count; j++)
                    if (enemies[j].Alive && !grouped[j] && enemies[j].Position == enemies[i].Position) { grouped[j] = true; collisionGroup.Add(j); }
                if (collisionGroup.Count > 1) collisions.Add(new EnemyCollision(enemies[i].Position, enemyStepIndex, collisionGroup.ToArray()));
            }
            for (int c = firstCollision; c < collisions.Count; c++) foreach (var i in collisions[c].Enemies) enemies[i].Alive = false;
            bool another = false;
            foreach (var e in enemies) if (e.Alive) { RunInstantActions(e); if (e.CellsLeft > 0) another = true; }
            if (another) PlanEnemyStep(); else enemiesComplete = true;
        }
        private void FinishAction()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.Alive) continue;
                e.Recognized = Recognizes(e, Position);
                bool struck = e.Path.Contains(Position);
                foreach (var offset in e.Recognized ? e.RecognizedAttack : e.Attack)
                {
                    var p = e.Position + LocalToGrid(offset, e.Direction); attackCells.Add(p); if (p == Position) struck = true;
                }
                if (struck) attackers.Add(i);
            }
            bool hit = attackers.Count > 0;
            EvasionLocked = evasion;
            Phase = hit ? BattlePhase.Lost : enemies.Count > 0 && AliveCount == 0 ? BattlePhase.Won : BattlePhase.Waiting;
        }
        public void Advance(float seconds) => Advance(seconds, false);
        /// <summary>시간을 진행한다. stopAfterCollision이면 적 충돌이 일어난 단계에서 멈추고 남은 시간을 돌려준다(연출의 일시 정지용).
        /// 시간을 어떻게 나눠 진행해도 판정 결과는 같다.</summary>
        public float Advance(float seconds, bool stopAfterCollision)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            while (IsBusy && seconds > 0)
            {
                float untilPlayer = IsMoving ? duration - elapsed : float.PositiveInfinity;
                float untilEnemy = IsEnemiesMoving ? enemyDuration - enemyElapsed : float.PositiveInfinity;
                float step = Mathf.Min(seconds, untilPlayer, untilEnemy);
                bool playerMoving = IsMoving, enemyMoving = IsEnemiesMoving;
                if (playerMoving) elapsed = Mathf.Min(duration, elapsed + step);
                if (enemyMoving) enemyElapsed = Mathf.Min(enemyDuration, enemyElapsed + step);
                seconds -= step;
                if (playerMoving && elapsed >= duration) Position = Destination;
                int before = collisions.Count;
                if (enemyMoving && enemyElapsed >= enemyDuration) CommitEnemyStep();
                if (!IsMoving && enemiesComplete) FinishAction();
                if (stopAfterCollision && collisions.Count > before) return seconds;
            }
            return 0;
        }
    }
    public static class MovementWorldTime
    {
        private static readonly HashSet<object> owners = new HashSet<object>();
        private static readonly HashSet<object> moving = new HashSet<object>();
        private static float previousScale = 1;
        /// <summary>전투 시간을 관리 중인 오브젝트가 있으면 true. 미니맵은 이 관리에 참여하지 않는다.</summary>
        public static bool HasOwners => owners.Count > 0;
        public static void Register(object owner)
        {
            if (owners.Contains(owner)) return;
            if (owners.Count == 0) previousScale = Time.timeScale;
            owners.Add(owner); Apply();
        }
        public static void SetMoving(object owner, bool value)
        {
            if (!owners.Contains(owner)) return;
            if (value) moving.Add(owner); else moving.Remove(owner);
            Apply();
        }
        public static void Unregister(object owner)
        {
            if (!owners.Remove(owner)) return;
            moving.Remove(owner);
            if (owners.Count == 0) Time.timeScale = previousScale; else Apply();
        }
        private static void Apply() { Time.timeScale = moving.Count > 0 ? 1 : 0; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (owners.Count > 0) Time.timeScale = previousScale;
            owners.Clear(); moving.Clear();
        }
    }
}