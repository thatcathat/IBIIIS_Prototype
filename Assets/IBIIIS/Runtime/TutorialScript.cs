using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대본 단계 종류. 프리팹·에셋에 정수로 저장되므로 기존 값을 바꾸거나 재사용하지 말고, 새 종류는 맨 끝에 새 값으로 추가한다.</summary>
    public enum TutorialStepType
    {
        /// <summary>대사·지문 한 줄. 넘기면 다음 단계.</summary>
        Line = 0,
        /// <summary>인물·오브젝트 머리 위 감정 표시(예: !, ?, 땀). 기다리지 않고 바로 다음 단계.</summary>
        Emote = 1,
        /// <summary>지정한 행동(종류·방향)을 한 번 하면 다음 단계. 그 외 입력은 무시.</summary>
        WaitAction = 2,
        /// <summary>허용한 행동으로 자유롭게 움직이다가, 처음 움직인 때부터 Seconds가 지나면 다음 단계.</summary>
        WaitFreeMove = 3,
        /// <summary>허용한 행동으로 움직여 Cell에 도착하면 다음 단계(그 칸이 점멸).</summary>
        WaitReachCell = 4,
        /// <summary>Cell을 마우스 왼쪽 버튼으로 누르면 연출 시간(Seconds) 뒤 다음 단계(그 칸이 점멸, 이동 입력은 무시).</summary>
        WaitClickCell = 5,
        /// <summary>이름 입력 창. 확인하면 다음 단계(입력값은 기록만 하고 대사는 바꾸지 않음).</summary>
        NameInput = 6,
        /// <summary>등장 오브젝트(Target)를 켜거나(Active) 끈다. 바로 다음 단계.</summary>
        SetActive = 7,
        /// <summary>Seconds 동안 기다린다(입력 무시).</summary>
        Wait = 8,
        /// <summary>다른 씬(Scene Path)으로 넘어간다.</summary>
        LoadScene = 9,
        /// <summary>적(Prefab)을 Cell에 Direction 방향으로 합류시킨다. 바로 다음 단계.</summary>
        SpawnEnemy = 10,
        /// <summary>허용한 행동으로 자유롭게 싸운다. 패배할 행동은 막고 경고 대사(Line)를 띄운다(넘겨야 닫히고 그동안 턴이 넘어가지 않음). 적이 모두 사라지면 다음 단계.</summary>
        FreePlay = 11,
        /// <summary>클리어 기록에 Text(기록 ID)를 남긴다(예: 튜토리얼 클리어). 바로 다음 단계.</summary>
        RecordClear = 12,
        /// <summary>플레이어 그림을 자세 그림(Sprite)으로 바꾸고 움직임(Motion)을 준다. Seconds 동안(0이면 다음 대사가 끝날 때까지). 바로 다음 단계.</summary>
        Pose = 13,
        /// <summary>네 방향(위·왼쪽·아래·오른쪽)을 모두 한 번씩 눌러야 다음 단계. 키 표시는 Keys에 위 왼쪽 아래 오른쪽 순서로 적고, 누른 키는 표시에서 빠진다.</summary>
        WaitAllDirections = 14,
    }
    /// <summary>자세 연출의 움직임. 표시 전용. 값은 에셋에 정수로 저장되므로 끝에만 추가한다.</summary>
    public enum PoseMotion { None = 0, Hop = 1, Shake = 2, Sigh = 3, Proud = 4 }
    /// <summary>대본에서 허용할 플레이어 행동(여러 개 선택 가능).</summary>
    [Flags]
    public enum TutorialActions { None = 0, Move = 1, Wait = 2, Dash = 4, Roll = 8 }
    /// <summary>대본 한 단계. 종류(Type)에 따라 쓰는 칸만 의미가 있다(Inspector는 쓰는 칸만 보여 준다).</summary>
    [Serializable]
    public struct TutorialStep
    {
        public TutorialStepType type;
        [Tooltip("Line: 보여 줄 대사. 입력 대기·이름 입력: 그동안 보일 안내 줄(인물을 비우면 지문). 자유 진행: 위험한 이동을 막을 때 보일 경고 대사.")] public DialogueLine line;
        [Tooltip("입력 대기에서 허용할 행동.")] public TutorialActions actions;
        [Tooltip("허용할 방향 또는 합류하는 적의 방향(위=(0,1), 왼쪽=(-1,0)). 입력 대기에서 (0,0)이면 모든 방향.")] public Vector2Int direction;
        [Tooltip("도착하거나 누를 칸(열, 행). 맵 좌표 기준(0,0 = 왼쪽 아래).")] public Vector2Int cell;
        [Tooltip("초. 자유 이동: 처음 움직인 뒤 기다릴 시간. 칸 누르기: 누른 뒤 연출 시간. 기다리기: 기다릴 시간. 감정 표시: 보이는 시간(0이면 다음 대사가 끝날 때까지).")] public float seconds;
        [Tooltip("화면에 깜빡일 키 표시. 공백으로 나눔(예: \"Shift W\" → Shift + W).")] public string keys;
        [Tooltip("감정 표시·켜고 끄기 대상: \"player\" 또는 진행기(TutorialDirector) Actors의 ID.")] public string target;
        [Tooltip("감정 표시 글자(예: !, ?) 또는 이름 입력 기본값.")] public string text;
        [Tooltip("켜고 끄기: 켤지(체크) 끌지.")] public bool active;
        [Tooltip("넘어갈 씬 경로(Assets/...unity).")] public string scenePath;
        [Tooltip("합류할 적 프리팹(EnemyDefinition 필요).")] public GameObject prefab;
        [Tooltip("칸 누르기: 누른 뒤 날아가는 그림(예: 칼). 감정 표시: 글자 대신 보일 아이콘. 자세: 플레이어가 잠시 바꿔 보일 자세 그림.")] public Sprite sprite;
        [Tooltip("자세: 그림을 바꾸며 주는 움직임(Hop 펄쩍, Shake 좌우 떨림, Sigh 한숨처럼 납작, Proud 으쓱하며 늘어남).")] public PoseMotion motion;
        [Tooltip("자세: 자세가 끝나 기본 그림으로 돌아올 때 주는 움직임. None이면 움직임 없이 바로 돌아옵니다.")] public PoseMotion endMotion;
        [Tooltip("자세: 체크하면 시간·대사와 관계없이 플레이어가 다음 행동을 시작할 때까지 유지합니다.")] public bool untilAction;
        public string[] KeyCaps => string.IsNullOrWhiteSpace(keys) ? Array.Empty<string>() : keys.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        /// <summary>이 행동이 허용되는지(입력 대기 단계).</summary>
        public bool Allows(PlayerAction action, Vector2Int dir)
        {
            var flag = action == PlayerAction.Move ? TutorialActions.Move : action == PlayerAction.Wait ? TutorialActions.Wait : action == PlayerAction.Dash ? TutorialActions.Dash : TutorialActions.Roll;
            return (actions & flag) != 0 && (direction == Vector2Int.zero || direction == dir);
        }
    }
    /// <summary>튜토리얼 등 대본(단계 목록). 전투 씬의 TutorialDirector가 위에서부터 실행한다. 대사·입력 제한·조건·연출을 Inspector에서 편집한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Tutorial Script")]
    public sealed class TutorialScript : ScriptableObject
    {
        [SerializeField, Tooltip("이 대본의 고정 ID. 기록 등이 이 값으로 연결되므로 정한 뒤 바꾸지 않습니다.")] private string scriptId = "tutorial";
        [SerializeField] private TutorialStep[] steps = Array.Empty<TutorialStep>();
        public string Id => scriptId;
        public TutorialStep[] Steps => steps ?? Array.Empty<TutorialStep>();
        public void Set(string id, TutorialStep[] value) { scriptId = id; steps = value ?? Array.Empty<TutorialStep>(); }
    }
}
