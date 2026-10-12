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
    }
    /// <summary>대본에서 허용할 플레이어 행동(여러 개 선택 가능).</summary>
    [Flags]
    public enum TutorialActions { None = 0, Move = 1, Wait = 2, Dash = 4, Roll = 8 }
    /// <summary>대본 한 단계. 종류(Type)에 따라 쓰는 칸만 의미가 있다(Inspector는 쓰는 칸만 보여 준다).</summary>
    [Serializable]
    public struct TutorialStep
    {
        public TutorialStepType type;
        [Tooltip("Line: 보여 줄 대사. 입력 대기·이름 입력: 그동안 보일 안내 줄(인물을 비우면 지문).")] public DialogueLine line;
        [Tooltip("입력 대기에서 허용할 행동.")] public TutorialActions actions;
        [Tooltip("허용할 방향(위=(0,1), 왼쪽=(-1,0)). (0,0)이면 모든 방향.")] public Vector2Int direction;
        [Tooltip("도착하거나 누를 칸(열, 행). 맵 좌표 기준(0,0 = 왼쪽 아래).")] public Vector2Int cell;
        [Tooltip("초. 자유 이동: 처음 움직인 뒤 기다릴 시간. 칸 누르기: 누른 뒤 연출 시간. 기다리기: 기다릴 시간. 감정 표시: 보이는 시간(0이면 다음 대사가 끝날 때까지).")] public float seconds;
        [Tooltip("화면에 깜빡일 키 표시. 공백으로 나눔(예: \"Shift W\" → Shift + W).")] public string keys;
        [Tooltip("감정 표시·켜고 끄기 대상: \"player\" 또는 진행기(TutorialDirector) Actors의 ID.")] public string target;
        [Tooltip("감정 표시 글자(예: !, ?) 또는 이름 입력 기본값.")] public string text;
        [Tooltip("켜고 끄기: 켤지(체크) 끌지.")] public bool active;
        [Tooltip("넘어갈 씬 경로(Assets/...unity).")] public string scenePath;
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
