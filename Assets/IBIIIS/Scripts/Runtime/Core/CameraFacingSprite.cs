using UnityEngine;

namespace IBIIIS
{
    /// <summary>2.5D 캐릭터 그림을 카메라와 나란히 세운다(회전을 카메라 회전과 같게). 플레이어·적·NPC가 같은 규칙을 쓴다.
    /// 컴포넌트로 붙이면 Play 중 매 프레임 맞춘다. 표시 전용이며 위치·판정에는 관여하지 않는다.</summary>
    public sealed class CameraFacingSprite : MonoBehaviour
    {
        [SerializeField, Tooltip("선택. 그림이 바라볼 카메라. 비우면 Main Camera를 사용합니다.")] private Camera viewCamera;
        /// <summary>그림을 카메라와 나란히 세우는 회전.</summary>
        public static Quaternion RotationFor(Camera camera) => camera.transform.rotation;
        /// <summary>대상을 카메라와 나란히 세운다. 카메라가 없으면 그대로 둔다.</summary>
        public static void Face(Transform target, Camera camera) { if (target != null && camera != null) target.rotation = RotationFor(camera); }
        public void Face() => Face(transform, viewCamera != null ? viewCamera : Camera.main);
        private void LateUpdate() => Face();
    }
}
