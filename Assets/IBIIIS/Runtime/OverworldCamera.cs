using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵에서 플레이어를 따라가는 카메라. 각도·화각은 Overworld Settings의 카메라 설정, 보이는 범위·따라가는 빠르기는 Overworld Settings를 쓴다.
    /// 거리는 "보이는 세로 범위"와 화각으로 계산하므로 화각을 좁혀 가장자리 왜곡을 줄여도 화면에 담기는 범위는 같다.</summary>
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
            if (settings == null) Debug.LogWarning($"[IBIIIS] {name}: Overworld Settings가 없어 카메라 현재 각도·화각과 기본 범위(14칸)·빠르기(8)를 사용합니다.", this);
            if (target == null) Debug.LogWarning($"[IBIIIS] {name}: 따라갈 대상이 없습니다.", this);
        }
        /// <summary>현재 회전에서 대상을 화면 가운데에 두는 카메라 위치.</summary>
        public Vector3 DesiredPosition => target.position + Vector3.up * focusHeight - transform.rotation * Vector3.forward * Distance;
        private float ViewHeight => settings != null ? settings.CameraViewHeight : OverworldSettings.DefaultCameraViewHeight;
        /// <summary>대상까지의 거리. Perspective면 보이는 세로 범위가 화면 높이에 맞는 거리, Orthographic이면 충분히 떨어진 거리(값을 읽기만 하고 카메라를 바꾸지 않는다).</summary>
        public float Distance
        {
            get
            {
                var camera = GetComponent<Camera>();
                if (camera.orthographic) return Mathf.Max(20, ViewHeight);
                return ViewHeight / 2 / Mathf.Tan(Mathf.Clamp(camera.fieldOfView, 1, 179) * Mathf.Deg2Rad / 2);
            }
        }
        // Orthographic이면 보이는 세로 범위에 맞게 Size를 맞춘다(Snap·매 프레임).
        private void ApplyViewSize() { var camera = GetComponent<Camera>(); if (camera.orthographic) camera.orthographicSize = ViewHeight / 2; }
        /// <summary>지연 없이 대상 위치로 옮긴다.</summary>
        public void Snap() { if (target != null) { ApplyViewSize(); transform.position = DesiredPosition; snapped = true; } }
        private void LateUpdate()
        {
            if (target == null) return;
            ApplyViewSize();
            float sharpness = settings != null ? settings.CameraFollowSharpness : OverworldSettings.DefaultCameraFollowSharpness;
            // 첫 프레임(전투에서 돌아와 위치를 옮긴 직후 포함)은 바로 붙인다.
            if (!snapped || sharpness <= 0) { Snap(); return; }
            transform.position = Vector3.Lerp(transform.position, DesiredPosition, 1 - Mathf.Exp(-sharpness * Time.deltaTime));
        }
    }
}
