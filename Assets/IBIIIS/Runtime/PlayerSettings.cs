using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Player Settings")]
    public sealed class PlayerSettings : ScriptableObject
    {
        [SerializeField, Tooltip("선택. 공용 플레이어 외형. 비어 있으면 임시 노란 표식을 표시합니다.")] private GameObject visualPrefab;
        [SerializeField, Min(.01f), Tooltip("한 칸 이동 시간 (초). 이동 시작 시 읽으며 진행 중인 이동에는 영향을 주지 않습니다.")] private float moveDuration = .25f;
        [SerializeField, Tooltip("대기 중에는 이동 가능한 인접 칸, 이동 중에는 목적지를 표시합니다.")] private bool showMoveHints = true;
        [SerializeField] private Color moveHintColor = new Color(.2f, .9f, 1f, 1f);
        [SerializeField] private Color destinationColor = new Color(1f, .8f, .15f, 1f);
        [SerializeField, Tooltip("선택. 이동 표시의 테두리 재질. 원본은 변경하지 않습니다.")] private Material moveHintMaterial;
        [SerializeField, Min(.01f), Tooltip("적 한 칸 이동 연출 시간(초). 두 칸 적은 두 번 진행합니다.")] private float enemyStepDuration = .25f;
        [SerializeField, Tooltip("Play 시작 시 적의 인식 범위(노란 테두리)와 공격 범위(빨간 칸)를 표시합니다. Play 중 Tab으로 전환합니다.")] private bool showEnemyRanges = true;
        [SerializeField, Tooltip("선택. 전투 입력(키 배치)이 정의된 Input Actions 에셋. 비우면 코드의 기본 키 배치를 사용합니다. 액션 맵 'Battle'과 필수 액션이 있어야 합니다. `IBIIIS > Create Default Input Actions`로 기본 에셋을 만듭니다.")]
        private UnityEngine.InputSystem.InputActionAsset inputActions;
        public UnityEngine.InputSystem.InputActionAsset InputActions => inputActions;
        [SerializeField, Tooltip("선택. 적 충돌 연출 설정. 비우면 기본 수치로 납작해짐·멈춤·흔들림·날아가기만 하고 이펙트·효과음은 없습니다. `IBIIIS > Create Default Collision Feedback`로 기본 에셋을 만듭니다.")]
        private CollisionFeedbackSettings collisionFeedback;
        public CollisionFeedbackSettings CollisionFeedback => collisionFeedback;
        [SerializeField, Tooltip("선택. 이동 손맛(뜀·납작함·착지 먼지·발소리·막힌 입력 반응) 설정. 비우면 기본 수치로 움직임만 주고 먼지·소리는 없습니다. `IBIIIS > Create Default Motion Feedback`로 기본 에셋을 만듭니다.")]
        private MotionFeedbackSettings motionFeedback;
        public MotionFeedbackSettings MotionFeedback => motionFeedback;
        [SerializeField, Tooltip("선택. 적 인식 표시(머리 위 !·?) 설정. 비우면 표시하지 않습니다. `IBIIIS > Create Default Enemy Alert`로 기본 에셋을 만듭니다.")]
        private EnemyAlertSettings enemyAlert;
        public EnemyAlertSettings EnemyAlert => enemyAlert;
        public bool ShowEnemyRanges => showEnemyRanges;
        public float EnemyStepDuration => float.IsNaN(enemyStepDuration) || float.IsInfinity(enemyStepDuration) ? .25f : Mathf.Max(.01f, enemyStepDuration);
        public GameObject VisualPrefab => visualPrefab;
        public float MoveDuration => float.IsNaN(moveDuration) || float.IsInfinity(moveDuration) ? .25f : Mathf.Max(.01f, moveDuration);
        public bool ShowMoveHints => showMoveHints;
        public Color MoveHintColor => moveHintColor;
        public Color DestinationColor => destinationColor;
        public Material MoveHintMaterial => moveHintMaterial;
    }
}