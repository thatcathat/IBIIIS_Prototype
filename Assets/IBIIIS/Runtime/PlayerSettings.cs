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
        public GameObject VisualPrefab => visualPrefab;
        public float MoveDuration => float.IsNaN(moveDuration) || float.IsInfinity(moveDuration) ? .25f : Mathf.Max(.01f, moveDuration);
        public bool ShowMoveHints => showMoveHints;
        public Color MoveHintColor => moveHintColor;
        public Color DestinationColor => destinationColor;
        public Material MoveHintMaterial => moveHintMaterial;
    }
}