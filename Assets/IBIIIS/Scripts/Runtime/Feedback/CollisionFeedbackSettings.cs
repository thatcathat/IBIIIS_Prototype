using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>최소~최대 사이에서 무작위 값을 고르는 범위. 최소가 최대보다 크면 둘을 바꿔 사용한다.</summary>
    [Serializable]
    public struct FloatRange
    {
        [SerializeField] private float min, max;
        public FloatRange(float min, float max) { this.min = min; this.max = max; }
        public float Min => Mathf.Min(min, max);
        public float Max => Mathf.Max(min, max);
        /// <summary>범위 안의 무작위 값. t(0~1)를 주면 그 위치의 값을 돌려준다.</summary>
        public float Sample(float t) => Mathf.Lerp(Min, Max, Mathf.Clamp01(t));
        public float Sample() => Sample(UnityEngine.Random.value);
    }
    /// <summary>적 충돌·플레이어 피격 연출의 공용 설정. 표시·소리 전용이며 판정 결과를 바꾸지 않는다. 슬롯이 비어 있으면 그 연출만 건너뛴다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Collision Feedback Settings")]
    public sealed class CollisionFeedbackSettings : ScriptableObject
    {
        [SerializeField, Tooltip("끄면 충돌한 적을 이전처럼 바로 숨깁니다.")] private bool enabledFeedback = true;
        [Header("맞닿음·멈춤")]
        [SerializeField, Min(0), Tooltip("충돌 순간 적이 납작해졌다 돌아오며 붉게 번쩍이는 시간(초).")] private float squashTime = .08f;
        [SerializeField, Min(0), Tooltip("맞닿음 뒤 모든 움직임을 멈추는 시간(초). 맞닿음 시간과 합쳐 다른 적·플레이어도 멈춥니다.")] private float hitStopTime = .08f;
        [SerializeField, Range(0, .6f), Tooltip("납작해지는 정도(0=없음).")] private float squashAmount = .3f;
        [Header("충격")]
        [SerializeField, Tooltip("선택. 충돌 칸에 생성할 이펙트 프리팹. SpriteRenderer가 있으면 커지며 사라지고, 카메라를 향합니다. 파티클은 스스로 재생됩니다.")] private GameObject impactPrefab;
        [SerializeField, Min(.01f), Tooltip("충격 이펙트를 보여 주는 시간(초).")] private float impactTime = .25f;
        [SerializeField, Min(0), Tooltip("충격 이펙트 최대 크기(칸).")] private float impactScale = 1.3f;
        [SerializeField, Min(0), Tooltip("화면 흔들림 세기(칸). 0이면 흔들지 않습니다.")] private float shakeAmplitude = .06f;
        [SerializeField, Min(0), Tooltip("화면 흔들림 시간(초).")] private float shakeTime = .18f;
        [SerializeField, Tooltip("선택. 충돌 효과음.")] private AudioClip impactSound;
        [SerializeField, Range(0, 1)] private float impactVolume = .8f;
        [Header("퇴장")]
        [SerializeField, Min(.01f), Tooltip("적이 튕겨 날아가 사라지는 시간(초).")] private float flyTime = .35f;
        [SerializeField, Tooltip("날아가는 수평 거리(칸). 적마다 최소~최대 사이에서 무작위로 고릅니다.")] private FloatRange flyDistance = new FloatRange(1f, 1.8f);
        [SerializeField, Tooltip("날아가는 최고 높이(칸). 적마다 최소~최대 사이에서 무작위로 고릅니다.")] private FloatRange flyHeight = new FloatRange(.5f, .9f);
        [SerializeField, Tooltip("날아가며 도는 바퀴 수. 적마다 최소~최대 사이에서 무작위로 고릅니다. 도는 방향은 충돌한 적마다 번갈아 바뀝니다.")] private FloatRange spinTurns = new FloatRange(1f, 2f);
        [Header("플레이어 피격(패배)")]
        [SerializeField, Tooltip("선택. 플레이어가 맞는 순간 플레이어와 때린 적 사이에 생성할 이펙트 프리팹. 크기·시간은 위 충격 이펙트 값을 씁니다.")] private GameObject playerHitPrefab;
        [SerializeField, Tooltip("선택. 플레이어가 맞는 효과음.")] private AudioClip playerHitSound;
        [SerializeField, Range(0, 1)] private float playerHitVolume = .9f;
        [SerializeField, Min(0), Tooltip("때린 적이 플레이어 쪽으로 몸을 내미는 거리(칸). 0이면 내밀지 않고 바로 맞습니다.")] private float lungeDistance = .3f;
        [SerializeField, Min(.01f), Tooltip("몸을 내밀었다 돌아오는 시간(초). 가장 많이 내민 순간(절반)에 맞습니다.")] private float lungeTime = .2f;
        [SerializeField, Min(0), Tooltip("맞은 플레이어가 적 반대쪽으로 밀리는 거리(칸). 날아가지 않고 그 자리에 쓰러집니다.")] private float knockbackDistance = .25f;
        [SerializeField, Min(.01f), Tooltip("밀리는 시간(초).")] private float knockbackTime = .18f;
        [SerializeField, Range(.3f, 1f), Tooltip("적이 플레이어 칸으로 들어와 이길 때 적 그림이 멈추는 거리(칸, 두 캐릭터 중심 사이). 한 칸에 겹쳐 보이지 않게 합니다.")] private float contactDistance = .6f;

        public bool Enabled => enabledFeedback;
        public float SquashTime => squashTime;
        public float HitStopTime => hitStopTime;
        public float SquashAmount => squashAmount;
        /// <summary>충돌 뒤 다른 움직임을 멈추는 전체 시간(맞닿음 + 멈춤).</summary>
        public float HoldTime => squashTime + hitStopTime;
        public GameObject ImpactPrefab => impactPrefab;
        public float ImpactTime => impactTime;
        public float ImpactScale => impactScale;
        public float ShakeAmplitude => shakeAmplitude;
        public float ShakeTime => shakeTime;
        public AudioClip ImpactSound => impactSound;
        public float ImpactVolume => impactVolume;
        public float FlyTime => flyTime;
        public FloatRange FlyDistance => flyDistance;
        public FloatRange FlyHeight => flyHeight;
        public FloatRange SpinTurns => spinTurns;
        public GameObject PlayerHitPrefab => playerHitPrefab;
        public AudioClip PlayerHitSound => playerHitSound;
        public float PlayerHitVolume => playerHitVolume;
        public float LungeDistance => lungeDistance;
        public float LungeTime => lungeTime;
        public float KnockbackDistance => knockbackDistance;
        public float KnockbackTime => knockbackTime;
        public float ContactDistance => contactDistance;
        /// <summary>피격 연출을 시작해 맞는 순간까지의 시간(몸을 내밀지 않으면 0).</summary>
        public float LungeImpactTime => lungeDistance > 0 ? lungeTime * .5f : 0;
        /// <summary>한 번의 충돌 연출이 끝날 때까지의 시간.</summary>
        public float TotalTime => Mathf.Max(HoldTime + flyTime, impactTime, shakeTime);
    }
}
