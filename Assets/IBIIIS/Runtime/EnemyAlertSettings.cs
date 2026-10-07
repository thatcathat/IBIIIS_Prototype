using UnityEngine;

namespace IBIIIS
{
    /// <summary>적 인식 표시 설정. 입력 대기 중 플레이어가 적의 인식 범위 안에 있으면 머리 위에 "!", 범위를 벗어나는 순간 "?"를 잠깐 보여 준다(표시 전용).
    /// `IBIIIS > Create Default Enemy Alert`가 임시 그림·효과음과 함께 만들고 공용 플레이어 설정에 연결한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Enemy Alert Settings")]
    public sealed class EnemyAlertSettings : ScriptableObject
    {
        [SerializeField, Tooltip("끄면 적 인식 표시를 보여 주지 않습니다.")] private bool enabledAlert = true;
        [SerializeField, Tooltip("인식 중일 때 머리 위에 띄울 그림(!). 비우면 표시하지 않습니다.")] private Sprite alertSprite;
        [SerializeField, Tooltip("선택. 인식에서 벗어나는 순간 잠깐 띄울 그림(?). 비우면 생략합니다.")] private Sprite lostSprite;
        [SerializeField, Min(0), Tooltip("적 발밑에서 표시 아래쪽까지의 높이(칸). 그림과 같이 화면 위쪽으로 잽니다. 적 그림 높이(현재 약 1.27칸)보다 조금 크게 둡니다. 임시 1.45.")] private float height = 1.45f;
        [SerializeField, Min(.05f), Tooltip("표시 크기(칸). 임시 0.6.")] private float size = .6f;
        [SerializeField, Min(.01f), Tooltip("새로 인식했을 때 톡 튀어나오는 시간(초). 임시 0.25.")] private float popTime = .25f;
        [SerializeField, Range(1, 3), Tooltip("튀어나올 때 가장 커지는 배율. 임시 1.5.")] private float popScale = 1.5f;
        [SerializeField, Min(.05f), Tooltip("인식에서 벗어났을 때 ?를 보여 주는 시간(초). 마지막 40%는 흐려집니다. 임시 0.7.")] private float lostTime = .7f;
        [SerializeField, Tooltip("선택. 새로 인식했을 때 효과음. 여러 적이 동시에 인식해도 한 번만 냅니다.")] private AudioClip alertSound;
        [SerializeField, Range(0, 1)] private float alertVolume = .6f;
        public bool Enabled => enabledAlert;
        public Sprite AlertSprite => alertSprite;
        public Sprite LostSprite => lostSprite;
        public float Height => Mathf.Max(0, height);
        public float Size => Mathf.Max(.05f, size);
        public float PopTime => Mathf.Max(.01f, popTime);
        public float PopScale => Mathf.Clamp(popScale, 1, 3);
        public float LostTime => Mathf.Max(.05f, lostTime);
        public AudioClip AlertSound => alertSound;
        public float AlertVolume => alertVolume;
    }
}
