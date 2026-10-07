using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵(Overworld) 공용 설정. 플레이어 이동 속도, 따라가는 카메라, 입력 배치를 관리한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Overworld Settings")]
    public sealed class OverworldSettings : ScriptableObject
    {
        [SerializeField, Min(.1f), Tooltip("플레이어 이동 속도(칸/초). 1칸 = 1 Unity 단위. 임시 기본값 3.5.")] private float moveSpeed = 3.5f;
        [SerializeField, Tooltip("선택. 카메라 Projection·내려다보는 각도·화각. 비우면 카메라의 현재 값을 유지합니다. 전투와 같은 GlobalCameraSettings를 연결할 수 있습니다.")]
        private MapCameraSettings cameraSettings;
        [SerializeField, Min(1), Tooltip("카메라가 플레이어에서 떨어진 거리(칸). 클수록 멀리서 넓게 봅니다.")] private float cameraDistance = 9;
        [SerializeField, Min(0), Tooltip("카메라가 따라가는 빠르기. 0이면 지연 없이 바로 붙습니다.")] private float cameraFollowSharpness = 8;
        [SerializeField, Tooltip("선택. 미니맵 키 배치 에셋. 액션 맵 'Overworld'와 Move(Vector2)·Interact(Button) 액션이 있어야 합니다. 비우면 코드의 기본 배치(WASD·방향키, F)를 사용합니다.")]
        private UnityEngine.InputSystem.InputActionAsset inputActions;
        [Header("걷기 손맛 (표시 전용)")]
        [SerializeField, Tooltip("선택. 뜀 높이·납작함·먼지·발소리·숨쉬기 등 공용 손맛 설정(전투와 같은 MotionFeedback 에셋). 비우면 기본 수치로, 먼지·발소리 없이 동작합니다. 이 에셋의 Enabled를 끄면 미니맵 손맛도 모두 꺼집니다.")]
        private MotionFeedbackSettings motionFeedback;
        [SerializeField, Min(.1f), Tooltip("한 걸음 길이(칸). 이 거리를 걸을 때마다 한 번 뛰고 발소리를 냅니다. 이동 속도와 무관하게 발과 이동이 맞습니다.")] private float strideLength = .9f;
        [SerializeField, Range(0, 1), Tooltip("걸을 때 뜀 크기 배율(전투 1칸 뜀 대비). 높이·웅크림·늘어남에 함께 곱합니다. 0이면 걸음 뜀을 끕니다.")] private float walkHopScale = .5f;
        [SerializeField, Tooltip("걸음마다 발소리를 냅니다(공용 설정의 Footstep Sound).")] private bool footsteps = true;
        [SerializeField, Min(0), Tooltip("몇 걸음마다 발밑 먼지를 낼지. 0이면 걸을 때 먼지를 내지 않습니다(멈출 때 먼지는 별도).")] private int dustEverySteps = 3;
        [SerializeField, Range(0, 1), Tooltip("멈출 때 착지처럼 납작해지는 정도의 배율(공용 Landing Squash 대비). 0이면 멈춤 반응과 멈춤 먼지를 끕니다.")] private float stopSquashScale = .6f;
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
        public float MoveSpeed => float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) ? 3.5f : Mathf.Max(.1f, moveSpeed);
        public MapCameraSettings CameraSettings => cameraSettings;
        public float CameraDistance => Mathf.Max(1, cameraDistance);
        public float CameraFollowSharpness => Mathf.Max(0, cameraFollowSharpness);
        public UnityEngine.InputSystem.InputActionAsset InputActions => inputActions;
    }
}
