using UnityEngine;

namespace IBIIIS
{
    /// <summary>캐릭터 이동 손맛(뜀·납작함·착지 먼지·발소리, 막힌 입력 반응)의 공용 설정. 그림에만 적용하며 논리 위치·판정·이동 시간은 바꾸지 않는다.
    /// 슬롯이 비어 있으면 그 연출만 건너뛴다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Motion Feedback Settings")]
    public sealed class MotionFeedbackSettings : ScriptableObject
    {
        [SerializeField, Tooltip("끄면 이전처럼 그림이 칸 사이를 평평하게 미끄러집니다.")] private bool enabledFeedback = true;
        [Header("플레이어 이동(1칸)")]
        [SerializeField, Min(0), Tooltip("통통 뛰는 최고 높이(칸).")] private float hopHeight = .25f;
        [SerializeField, Range(0, .6f), Tooltip("출발할 때 웅크리는(납작해지는) 정도.")] private float takeoffSquash = .22f;
        [SerializeField, Range(.05f, .5f), Tooltip("웅크림이 일어나는 구간(이동 진행도 비율, 0~1).")] private float takeoffPortion = .18f;
        [SerializeField, Range(0, .6f), Tooltip("공중에서 위아래로 늘어나는 정도.")] private float airStretch = .15f;
        [Header("착지")]
        [SerializeField, Range(0, .6f), Tooltip("착지할 때 납작해지는 정도.")] private float landingSquash = .3f;
        [SerializeField, Min(.01f), Tooltip("착지 납작함이 돌아오는 시간(초). 입력은 막지 않습니다.")] private float landingTime = .14f;
        [SerializeField, Tooltip("선택. 착지 칸에 생기는 먼지 프리팹. SpriteRenderer가 있으면 커지며 사라지고 카메라를 향합니다.")] private GameObject landingDust;
        [SerializeField, Min(0), Tooltip("먼지 최대 크기(칸).")] private float dustScale = .55f;
        [SerializeField, Min(.01f), Tooltip("먼지가 사라지는 시간(초).")] private float dustTime = .3f;
        [SerializeField, Tooltip("선택. 착지 발소리.")] private AudioClip footstepSound;
        [SerializeField, Range(0, 1)] private float footstepVolume = .6f;
        [Header("대시(2칸)")]
        [SerializeField, Range(0, 40), Tooltip("좌우 대시 때 진행 방향으로 기우는 각도(도). 위아래 대시는 기울지 않고 낮게만 웅크립니다.")] private float dashLean = 12;
        [SerializeField, Range(0, .6f), Tooltip("대시 중 낮게 웅크리는 정도.")] private float dashSquash = .12f;
        [SerializeField, Range(0, 8), Tooltip("대시 중 뒤에 남기는 잔상 수. 이동 구간을 같은 간격으로 나눈 지점마다 하나씩 남깁니다.")] private int afterimageCount = 3;
        [SerializeField, Tooltip("잔상 색(알파 = 처음 진하기).")] private Color afterimageColor = new Color(.55f, .8f, 1f, .5f);
        [SerializeField, Min(.01f), Tooltip("잔상이 사라지는 시간(초).")] private float afterimageTime = .22f;
        [SerializeField, Range(0, .6f), Tooltip("대시가 끝나 미끄러지며 멈출 때 납작해지는 정도.")] private float dashStopSquash = .22f;
        [SerializeField, Tooltip("선택. 대시 시작 효과음.")] private AudioClip dashSound;
        [SerializeField, Range(0, 1)] private float dashVolume = .6f;
        [Header("구르기")]
        [SerializeField, Min(0), Tooltip("구르기 중 뜨는 최고 높이(칸).")] private float rollHopHeight = .15f;
        [SerializeField, Range(0, .6f), Tooltip("구르기 착지 때 납작해지는 정도.")] private float rollLandingSquash = .22f;
        [SerializeField, Tooltip("선택. 구르기 시작 효과음.")] private AudioClip rollSound;
        [SerializeField, Range(0, 1)] private float rollVolume = .6f;
        [Header("대기·숨쉬기")]
        [SerializeField, Range(0, .6f), Tooltip("대기 행동 때 제자리에서 한 번 눌렸다 펴지는 정도(끄덕임).")] private float waitSquash = .14f;
        [SerializeField, Range(0, .2f), Tooltip("입력 대기 중 숨쉬기 크기 변화(0=끔). 게임 시간이 멈춰 있어도 움직입니다.")] private float breathAmount = .03f;
        [SerializeField, Min(.1f), Tooltip("숨쉬기 한 번의 주기(초).")] private float breathPeriod = 1.6f;
        [Header("적")]
        [SerializeField, Min(0), Tooltip("적이 한 칸 이동할 때 뛰는 최고 높이(칸). 플레이어보다 낮고 무겁게.")] private float enemyHopHeight = .12f;
        [SerializeField, Range(0, .6f), Tooltip("적이 출발할 때 웅크리는 정도.")] private float enemyTakeoffSquash = .18f;
        [SerializeField, Range(0, .6f), Tooltip("적이 공중에서 늘어나는 정도.")] private float enemyAirStretch = .08f;
        [SerializeField, Range(0, .6f), Tooltip("적이 착지할 때 납작해지는 정도.")] private float enemyLandingSquash = .3f;
        [SerializeField, Min(.01f), Tooltip("적 착지 납작함이 돌아오는 시간(초).")] private float enemyLandingTime = .16f;
        [SerializeField, Min(0), Tooltip("적 착지 먼지 크기(칸). 먼지 프리팹은 플레이어와 같은 것을 씁니다. 0이면 먼지 없음.")] private float enemyDustScale = .7f;
        [SerializeField, Tooltip("선택. 적 발소리. 같은 순간 여러 적이 착지해도 한 번만 냅니다.")] private AudioClip enemyFootstepSound;
        [SerializeField, Range(0, 1)] private float enemyFootstepVolume = .5f;
        [SerializeField, Range(0, .6f), Tooltip("적이 방향을 틀 때(조준·벽 반사) 짧게 눌렸다 펴지는 정도.")] private float turnSquash = .18f;
        [SerializeField, Min(.01f), Tooltip("방향 전환 반응 시간(초).")] private float turnTime = .12f;
        [SerializeField, Min(0), Tooltip("벽 앞에서 돌아설 때 벽 쪽으로 부딪히는 거리(칸).")] private float wallBumpDistance = .15f;
        [SerializeField, Min(.01f), Tooltip("벽에 부딪혔다 돌아오는 시간(초).")] private float wallBumpTime = .14f;
        [Header("막힌 입력")]
        [SerializeField, Min(0), Tooltip("갈 수 없는 방향을 눌렀을 때 그쪽으로 부딪히는 거리(칸).")] private float bumpDistance = .14f;
        [SerializeField, Min(.01f), Tooltip("부딪혔다 돌아오는 시간(초).")] private float bumpTime = .16f;
        [SerializeField, Range(0, .6f), Tooltip("부딪힐 때 납작해지는 정도.")] private float bumpSquash = .15f;
        [SerializeField, Tooltip("선택. 막힌 입력 효과음.")] private AudioClip bumpSound;
        [SerializeField, Range(0, 1)] private float bumpVolume = .6f;

        public bool Enabled => enabledFeedback;
        public float HopHeight => hopHeight;
        public float TakeoffSquash => takeoffSquash;
        public float TakeoffPortion => takeoffPortion;
        public float AirStretch => airStretch;
        public float LandingSquash => landingSquash;
        public float LandingTime => landingTime;
        public GameObject LandingDust => landingDust;
        public float DustScale => dustScale;
        public float DustTime => dustTime;
        public AudioClip FootstepSound => footstepSound;
        public float FootstepVolume => footstepVolume;
        public float DashLean => dashLean;
        public float DashSquash => dashSquash;
        public int AfterimageCount => afterimageCount;
        public Color AfterimageColor => afterimageColor;
        public float AfterimageTime => afterimageTime;
        public float DashStopSquash => dashStopSquash;
        public AudioClip DashSound => dashSound;
        public float DashVolume => dashVolume;
        public float RollHopHeight => rollHopHeight;
        public float RollLandingSquash => rollLandingSquash;
        public AudioClip RollSound => rollSound;
        public float RollVolume => rollVolume;
        public float WaitSquash => waitSquash;
        public float BreathAmount => breathAmount;
        public float BreathPeriod => breathPeriod;
        public float EnemyHopHeight => enemyHopHeight;
        public float EnemyTakeoffSquash => enemyTakeoffSquash;
        public float EnemyAirStretch => enemyAirStretch;
        public float EnemyLandingSquash => enemyLandingSquash;
        public float EnemyLandingTime => enemyLandingTime;
        public float EnemyDustScale => enemyDustScale;
        public AudioClip EnemyFootstepSound => enemyFootstepSound;
        public float EnemyFootstepVolume => enemyFootstepVolume;
        public float TurnSquash => turnSquash;
        public float TurnTime => turnTime;
        public float WallBumpDistance => wallBumpDistance;
        public float WallBumpTime => wallBumpTime;
        public float BumpDistance => bumpDistance;
        public float BumpTime => bumpTime;
        public float BumpSquash => bumpSquash;
        public AudioClip BumpSound => bumpSound;
        public float BumpVolume => bumpVolume;
    }
}
