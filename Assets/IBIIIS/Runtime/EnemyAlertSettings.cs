using UnityEngine;

namespace IBIIIS
{
    /// <summary>전투 인식 말풍선 설정(표시 전용). 입력 대기 중 적이 플레이어를 새로 인식하면 플레이어 말풍선 → 인식한 적 모두의 대답 말풍선을 띄우고,
    /// 인식에서 벗어나면 그 적의 혼잣말 말풍선을 띄운다. 대사는 Battle Dialogue 에셋(공용 또는 적별)에서 고른다.
    /// 말풍선 모양은 Game UI 설정의 Speech Bubble 화면 구성과 GameUi.uss가 정한다. `IBIIIS > Create Default Enemy Alert`가 만들고 공용 플레이어 설정에 연결한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Enemy Alert Settings")]
    public sealed class EnemyAlertSettings : ScriptableObject
    {
        [SerializeField, Tooltip("끄면 인식 말풍선을 보여 주지 않습니다.")] private bool enabledAlert = true;
        [SerializeField, Tooltip("모든 적이 쓰는 공용 대사. 적 프리팹의 EnemyDefinition에 대사를 따로 연결하면 그 적은 그것을 씁니다.")] private BattleDialogue dialogue;
        [SerializeField, Min(0), Tooltip("적 발밑에서 말풍선 꼬리 끝까지의 높이(칸). 화면 위쪽으로 잽니다. 적 그림 높이(현재 약 1.27칸)보다 조금 크게 둡니다. 임시 1.45.")] private float height = 1.45f;
        [SerializeField, Min(0), Tooltip("플레이어 발밑에서 말풍선 꼬리 끝까지의 높이(칸). 임시 1.45.")] private float playerHeight = 1.45f;
        [SerializeField, Min(0), Tooltip("플레이어 말이 뜬 뒤 적이 대답하기까지의 시간(초). 플레이어 말이 비어 있으면 적이 바로 말합니다. 임시 0.4.")] private float replyDelay = .4f;
        [SerializeField, Min(.1f), Tooltip("인식 순간 대사(플레이어·적)를 보여 주는 시간(초). 마지막 25%는 흐려집니다. 임시 1.5.")] private float spottedTime = 1.5f;
        [SerializeField, Min(.1f), Tooltip("인식에서 벗어났을 때 적의 혼잣말을 보여 주는 시간(초). 임시 1.2.")] private float lostLineTime = 1.2f;
        [Header("등장: 아래에서 떠오르며 꼬리를 축으로 좌우로 기울며 흔들림")]
        [SerializeField, Min(.01f), Tooltip("말풍선이 떠올라 자리 잡기까지의 시간(초). 이 동안 흔들림이 줄어들어 도착하면 멈춥니다. 임시 0.35.")] private float appearTime = .35f;
        [SerializeField, Min(0), Tooltip("처음 나타나는 위치가 최종 위치보다 아래인 거리(화면 기준 1920×1080 픽셀). 임시 20.")] private float riseDistance = 20;
        [SerializeField, Range(0, 45), Tooltip("처음(가장 큰) 흔들림의 기울기(도). 흔들릴수록 폭이 줄어 도착하면 0이 됩니다. 임시 10.")] private float wobbleAngle = 10;
        [SerializeField, Range(0, 6), Tooltip("떠오르는 동안 좌우로 흔들리는 횟수. 임시 2.5.")] private float wobbles = 2.5f;
        [SerializeField, Range(.1f, 1), Tooltip("처음 크기 배율. 떠오르는 동안 1배로 커집니다. 임시 0.8.")] private float startScale = .8f;
        [SerializeField, Tooltip("선택. 새로 인식했을 때 효과음. 여러 적이 동시에 인식해도 한 번만 냅니다.")] private AudioClip alertSound;
        [SerializeField, Range(0, 1)] private float alertVolume = .6f;
        public bool Enabled => enabledAlert;
        public BattleDialogue Dialogue => dialogue;
        public float Height => Mathf.Max(0, height);
        public float PlayerHeight => Mathf.Max(0, playerHeight);
        public float ReplyDelay => Mathf.Max(0, replyDelay);
        public float SpottedTime => Mathf.Max(.1f, spottedTime);
        public float LostLineTime => Mathf.Max(.1f, lostLineTime);
        public float AppearTime => Mathf.Max(.01f, appearTime);
        public float RiseDistance => Mathf.Max(0, riseDistance);
        public float WobbleAngle => Mathf.Clamp(wobbleAngle, 0, 45);
        public float Wobbles => Mathf.Clamp(wobbles, 0, 6);
        public float StartScale => Mathf.Clamp(startScale, .1f, 1);
        public AudioClip AlertSound => alertSound;
        public float AlertVolume => alertVolume;
    }
}
