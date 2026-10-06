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
        public float MoveSpeed => float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) ? 3.5f : Mathf.Max(.1f, moveSpeed);
        public MapCameraSettings CameraSettings => cameraSettings;
        public float CameraDistance => Mathf.Max(1, cameraDistance);
        public float CameraFollowSharpness => Mathf.Max(0, cameraFollowSharpness);
        public UnityEngine.InputSystem.InputActionAsset InputActions => inputActions;
    }
}
