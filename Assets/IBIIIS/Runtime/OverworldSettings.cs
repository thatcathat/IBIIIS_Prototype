using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵(Overworld) 공용 설정. 플레이어 이동 속도, 따라가는 카메라, 입력 배치를 관리한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Overworld Settings")]
    public sealed class OverworldSettings : ScriptableObject
    {
        // 기본값(설정 에셋이 없을 때와 새 에셋의 초기값). 코드는 이 값만 참조한다. 모두 임시 값.
        public const float DefaultMoveSpeed = 3.5f, DefaultRunSpeed = 6, DefaultCameraViewHeight = 14, DefaultCameraFollowSharpness = 8;
        public const float DefaultRollDistance = 1.5f, DefaultRollDuration = .35f, DefaultRollEaseOut = 1, DefaultRollRecovery = .2f, DefaultEvadeCooldown = .25f;
        public const float DefaultStrideLength = .9f, DefaultRunStrideLength = 1.3f, DefaultWalkHopScale = .5f, DefaultStopSquashScale = .6f;
        public const int DefaultDustEverySteps = 3;
        [SerializeField, Min(.1f), Tooltip("플레이어 이동 속도(칸/초). 1칸 = 1 Unity 단위. 임시 기본값 3.5.")] private float moveSpeed = DefaultMoveSpeed;
        [SerializeField, Tooltip("선택. 카메라 Projection·내려다보는 각도·화각. 비우면 카메라의 현재 값을 유지합니다. 기본은 미니맵 전용 OverworldCameraSettings(화각 30)이며, 전투와 같게 하려면 GlobalCameraSettings를 연결합니다.")]
        private MapCameraSettings cameraSettings;
        [SerializeField, Min(1), Tooltip("플레이어 위치에서 화면 세로로 보이는 범위(칸). 카메라 거리는 화각에서 자동으로 계산하므로 화각을 바꿔도 보이는 범위는 그대로입니다. 임시 14(이전 거리 9·화각 75와 같은 범위).")]
        private float cameraViewHeight = DefaultCameraViewHeight;
        [SerializeField, Min(0), Tooltip("카메라가 따라가는 빠르기. 0이면 지연 없이 바로 붙습니다.")] private float cameraFollowSharpness = DefaultCameraFollowSharpness;
        [SerializeField, Tooltip("선택. 미니맵 키 배치 에셋. 액션 맵 'Overworld'와 Move(Vector2)·Interact(Button) 액션이 있어야 합니다. 비우면 코드의 기본 배치(WASD·방향키, F)를 사용합니다.")]
        private UnityEngine.InputSystem.InputActionAsset inputActions;
        [Header("달리기(대시)·구르기")]
        [SerializeField, Min(.1f), Tooltip("대시 키(Shift)를 누르고 있는 동안의 달리기 속도(칸/초). 임시 6. 걷기는 Move Speed.")] private float runSpeed = DefaultRunSpeed;
        [SerializeField, Min(.1f), Tooltip("달릴 때 한 걸음 길이(칸). 걷기(Stride Length)보다 길게 해 보폭을 넓힙니다. 임시 1.3.")] private float runStrideLength = DefaultRunStrideLength;
        [SerializeField, Min(.1f), Tooltip("구르기로 나아가는 거리(칸). 임시 1.5.")] private float rollDistance = DefaultRollDistance;
        [SerializeField, Min(.05f), Tooltip("구르기에 걸리는 시간(초). 임시 0.35.")] private float rollDuration = DefaultRollDuration;
        [SerializeField, Range(0, 1), Tooltip("구르기 감속 정도. 0이면 일정한 속도, 1이면 처음에 가장 빠르게(평균의 2배) 튀어 나가 끝에 멈춥니다. 총 거리·시간은 같습니다. 대시는 항상 일정한 속도.")] private float rollEaseOut = DefaultRollEaseOut;
        [SerializeField, Min(0), Tooltip("구르기가 끝난 뒤 일어나는 시간(초). 이 동안 이동·대시·구르기·상호작용 입력을 모두 무시합니다. 벽에 막혀 일찍 끝나도 같습니다. 임시 0.2.")] private float rollRecovery = DefaultRollRecovery;
        [SerializeField, Min(0), Tooltip("구르기 회복 시간이 끝난 뒤 다시 구를 수 있을 때까지의 시간(초). 임시 0.25.")] private float evadeCooldown = DefaultEvadeCooldown;
        public float RollEaseOut => Mathf.Clamp01(rollEaseOut);
        public float RollRecovery => Mathf.Max(0, rollRecovery);
        public float RunSpeed => float.IsNaN(runSpeed) || float.IsInfinity(runSpeed) ? DefaultRunSpeed : Mathf.Max(.1f, runSpeed);
        public float RunStrideLength => Mathf.Max(.1f, runStrideLength);
        public float RollDistance => Mathf.Max(.1f, rollDistance);
        public float RollDuration => Mathf.Max(.05f, rollDuration);
        public float EvadeCooldown => Mathf.Max(0, evadeCooldown);
        [Header("걷기 손맛 (표시 전용)")]
        [SerializeField, Tooltip("선택. 뜀 높이·납작함·먼지·발소리·숨쉬기 등 공용 손맛 설정(전투와 같은 MotionFeedback 에셋). 비우면 기본 수치로, 먼지·발소리 없이 동작합니다. 이 에셋의 Enabled를 끄면 미니맵 손맛도 모두 꺼집니다.")]
        private MotionFeedbackSettings motionFeedback;
        [SerializeField, Min(.1f), Tooltip("한 걸음 길이(칸). 이 거리를 걸을 때마다 한 번 뛰고 발소리를 냅니다. 이동 속도와 무관하게 발과 이동이 맞습니다.")] private float strideLength = DefaultStrideLength;
        [SerializeField, Range(0, 1), Tooltip("걸을 때 뜀 크기 배율(전투 1칸 뜀 대비). 높이·웅크림·늘어남에 함께 곱합니다. 0이면 걸음 뜀을 끕니다.")] private float walkHopScale = DefaultWalkHopScale;
        [SerializeField, Tooltip("걸음마다·멈출 때 발소리를 냅니다(공용 설정의 Footstep Sound). 구르기 착지 소리는 이 설정과 관계없이 납니다.")] private bool footsteps = true;
        [SerializeField, Min(0), Tooltip("몇 걸음마다 발밑 먼지를 낼지. 0이면 걸을 때 먼지를 내지 않습니다(멈출 때 먼지는 별도).")] private int dustEverySteps = DefaultDustEverySteps;
        [SerializeField, Range(0, 1), Tooltip("멈출 때 착지처럼 납작해지는 정도의 배율(공용 Landing Squash 대비). 0이면 멈춤 반응과 멈춤 먼지를 끕니다.")] private float stopSquashScale = DefaultStopSquashScale;
        [SerializeField, Tooltip("멈춰 있을 때 숨쉬기(공용 Breath Amount·Period).")] private bool breathing = true;
        [SerializeField, Tooltip("벽·NPC에 막혀 거의 움직이지 못하면 그쪽으로 부딪히는 반응(공용 Bump 설정). 한 번 막힐 때 한 번만 냅니다.")] private bool wallBump = true;
        public MotionFeedbackSettings MotionFeedback => motionFeedback;
        public float StrideLength => Mathf.Max(.1f, strideLength);
        public float WalkHopScale => Mathf.Clamp01(walkHopScale);
        public bool Footsteps => footsteps;
        public int DustEverySteps => Mathf.Max(0, dustEverySteps);
        public float StopSquashScale => Mathf.Clamp01(stopSquashScale);
        public bool Breathing => breathing;
        public bool WallBump => wallBump;
        public float MoveSpeed => float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) ? DefaultMoveSpeed : Mathf.Max(.1f, moveSpeed);
        public MapCameraSettings CameraSettings => cameraSettings;
        public float CameraViewHeight => Mathf.Max(1, cameraViewHeight);
        public float CameraFollowSharpness => Mathf.Max(0, cameraFollowSharpness);
        public UnityEngine.InputSystem.InputActionAsset InputActions => inputActions;
        [SerializeField, Tooltip("선택. 게임 화면 UI(미니맵 안내·결과 팝업) 설정. 전투 Player Settings와 같은 에셋을 연결합니다. 비우면 임시 IMGUI 표시를 씁니다.")]
        private GameUiSettings gameUi;
        public GameUiSettings GameUi => gameUi;
    }
}
