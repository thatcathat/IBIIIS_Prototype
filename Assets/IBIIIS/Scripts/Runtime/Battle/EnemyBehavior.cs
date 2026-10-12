using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>적이 플레이어 행동 한 번마다 순서대로 수행하는 행동 단위. 새 단위는 여기에 추가하고 GridSession의 적 실행부에 처리를 더한다.
    /// 프리팹에는 이 값이 정수로 저장된다. 기존 값을 바꾸거나 재사용하지 말고, 새 단위는 맨 끝에 새 값으로 추가한다(중간에 끼우면 기존 적의 행동이 조용히 바뀐다).</summary>
    public enum EnemyActionType
    {
        /// <summary>플레이어의 행동 시작 직전 위치가 인식 범위 안이면 그쪽을 향한다(거리가 더 먼 축 우선, 동률이면 직교 전환). 플레이어가 이번에 어디로 가는지는 쓰지 않는다. 시간을 쓰지 않는다.</summary>
        AimAtPlayer = 0,
        /// <summary>바라보는 방향으로 지정한 칸 수만큼 한 칸씩 이동한다. 막히면 180도 돌아 이동하고, 그것도 막히면 머문다.</summary>
        MoveForward = 1,
        /// <summary>제자리에서 방향만 바꾼다. 시간을 쓰지 않는다.</summary>
        Turn = 2,
    }
    /// <summary>프리팹에 정수로 저장된다. 값을 바꾸지 말고 새 값은 끝에 추가한다.</summary>
    public enum EnemyTurn { Left = 0, Right = 1, Around = 2 }
    [Serializable]
    public struct EnemyActionStep
    {
        public const int MaxMoveCells = 2;
        [SerializeField] private EnemyActionType type;
        [SerializeField, Range(1, MaxMoveCells)] private int cells;
        [SerializeField] private EnemyTurn turn;
        public EnemyActionType Type => type;
        public int Cells => cells;
        public EnemyTurn Turn => turn;
        public bool IsValid => type != EnemyActionType.MoveForward || (cells >= 1 && cells <= MaxMoveCells);
        public static EnemyActionStep Aim() => new EnemyActionStep { type = EnemyActionType.AimAtPlayer, cells = 1 };
        public static EnemyActionStep Move(int count) => new EnemyActionStep { type = EnemyActionType.MoveForward, cells = count };
        public static EnemyActionStep TurnBy(EnemyTurn value) => new EnemyActionStep { type = EnemyActionType.Turn, cells = 1, turn = value };
        /// <summary>그리드 방향 벡터를 회전한다. 오른쪽은 EnemyDefinition의 로컬 오프셋 X와 같은 쪽이다.</summary>
        public static Vector2Int Rotate(Vector2Int facing, EnemyTurn value)
            => value == EnemyTurn.Left ? new Vector2Int(-facing.y, facing.x) : value == EnemyTurn.Right ? new Vector2Int(facing.y, -facing.x) : -facing;
    }
}
