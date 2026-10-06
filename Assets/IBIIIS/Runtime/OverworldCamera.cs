using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵에서 플레이어를 따라가는 카메라. 각도·화각은 Overworld Settings의 카메라 설정, 거리·따라가는 빠르기는 Overworld Settings를 쓴다.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OverworldCamera : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. 따라갈 대상(미니맵 플레이어)")] private Transform target;
        [SerializeField, Tooltip("필수. 카메라 거리·빠르기 설정")] private OverworldSettings settings;
        [SerializeField, Tooltip("대상 발밑에서 바라볼 지점의 높이(칸)")] private float focusHeight = .5f;
        private bool snapped;
        public Transform Target => target;
        public void Configure(Transform value, OverworldSettings shared) { target = value; settings = shared; }
        private void Awake()
        {
            if (settings != null && settings.CameraSettings != null) settings.CameraSettings.Apply(GetComponent<Camera>());
            if (target == null) Debug.LogWarning($"[IBIIIS] {name}: 따라갈 대상이 없습니다.", this);
        }
        /// <summary>현재 회전에서 대상을 화면 가운데에 두는 카메라 위치.</summary>
        public Vector3 DesiredPosition
        {
            get
            {
                float distance = settings != null ? settings.CameraDistance : 9;
                return target.position + Vector3.up * focusHeight - transform.rotation * Vector3.forward * distance;
            }
        }
        /// <summary>지연 없이 대상 위치로 옮긴다.</summary>
        public void Snap() { if (target != null) { transform.position = DesiredPosition; snapped = true; } }
        private void LateUpdate()
        {
            if (target == null) return;
            float sharpness = settings != null ? settings.CameraFollowSharpness : 8;
            // 첫 프레임(전투에서 돌아와 위치를 옮긴 직후 포함)은 바로 붙인다.
            if (!snapped || sharpness <= 0) { Snap(); return; }
            transform.position = Vector3.Lerp(transform.position, DesiredPosition, 1 - Mathf.Exp(-sharpness * Time.deltaTime));
        }
    }
}
