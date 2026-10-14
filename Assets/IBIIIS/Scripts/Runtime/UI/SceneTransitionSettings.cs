using UnityEngine;

namespace IBIIIS
{
    /// <summary>씬 전환 설정(공용). Resources/IBIIIS/SceneTransitionSettings에 두며 모든 씬 이동(전투 입장·퇴장, 타이틀 → 튜토리얼, 대본의 씬 전환)이 쓴다.
    /// 없으면 기본값(0.3초 검은 페이드)으로 동작한다. 표시 전용이며 판정과 무관하다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Scene Transition Settings")]
    public sealed class SceneTransitionSettings : ScriptableObject
    {
        public const string ResourcePath = "IBIIIS/SceneTransitionSettings";
        public const float DefaultSeconds = .3f;
        [SerializeField, Min(0), Tooltip("지금 씬을 가리는 시간(초). 0이면 바로 가립니다. 임시 0.3.")] private float outSeconds = DefaultSeconds;
        [SerializeField, Min(0), Tooltip("새 씬을 불러온 뒤 드러내는 시간(초). 0이면 바로 보입니다. 임시 0.3.")] private float inSeconds = DefaultSeconds;
        [SerializeField, Tooltip("기본 페이드 색.")] private Color fadeColor = Color.black;
        [SerializeField, Tooltip("선택. 전환 화면 프리팹(예: 로딩 화면). ISceneTransitionView를 구현한 컴포넌트가 있어야 하며, 가리는 정도·단계·불러오기 진행을 받아 직접 그립니다. 비우면 기본 페이드.")] private GameObject viewPrefab;
        public float OutSeconds => Mathf.Max(0, outSeconds);
        public float InSeconds => Mathf.Max(0, inSeconds);
        public Color FadeColor => fadeColor;
        public GameObject ViewPrefab => viewPrefab;
        /// <summary>Resources에서 공용 설정을 읽는다(없으면 null → 기본값).</summary>
        public static SceneTransitionSettings Load() => Resources.Load<SceneTransitionSettings>(ResourcePath);
    }
}
