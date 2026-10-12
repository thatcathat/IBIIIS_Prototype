using UnityEngine;

namespace IBIIIS
{
    /// <summary>제자리에 서 있는 캐릭터 그림(미니맵 NPC 등)의 숨쉬기. 발밑 기준점 그림의 크기만 살짝 늘였다 줄인다(표시 전용).
    /// 같은 장면의 NPC들이 함께 움직이지 않도록 시작 위상을 무작위로 정한다.</summary>
    public sealed class BreathingSprite : MonoBehaviour
    {
        [SerializeField, Tooltip("선택. 숨쉬기 크기·주기(Breath Amount·Period)와 Enabled를 공용 손맛 설정에서 읽습니다. 비우면 기본값(0.03, 1.6초).")]
        private MotionFeedbackSettings settings;
        [SerializeField, Range(0, 3), Tooltip("공용 숨쉬기 크기에 곱하는 배율. 0이면 이 캐릭터는 숨쉬지 않습니다.")] private float amountScale = 1;
        private Vector3 baseScale;
        private float time;
        private bool captured;
        public void Configure(MotionFeedbackSettings shared) { settings = shared; }
        private void OnEnable()
        {
            if (!captured) { baseScale = transform.localScale; captured = true; }
            time = Random.value * (settings != null ? settings.BreathPeriod : 1.6f);
        }
        private void OnDisable() { if (captured) transform.localScale = baseScale; }
        /// <summary>시간을 진행하고 크기를 적용한다(테스트에서 직접 호출 가능).</summary>
        public void Advance(float seconds)
        {
            if (!captured) { baseScale = transform.localScale; captured = true; }
            time += seconds;
            bool enabledShared = settings == null || settings.Enabled;
            float amount = (settings != null ? settings.BreathAmount : .03f) * amountScale;
            var pose = enabledShared ? MotionPoses.Breath(time, amount, settings != null ? settings.BreathPeriod : 1.6f) : MotionPose.Identity;
            transform.localScale = new Vector3(baseScale.x * pose.Squash.x, baseScale.y * pose.Squash.y, baseScale.z);
        }
        private void Update() => Advance(Time.deltaTime);
    }
}
