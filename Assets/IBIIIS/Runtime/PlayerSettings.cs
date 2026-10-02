using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Player Settings")]
    public sealed class PlayerSettings : ScriptableObject
    {
        [SerializeField, Tooltip("선택. 모든 연결된 맵에 사용하는 플레이어 외형. 비어 있으면 임시 노란 표식을 표시합니다.")]
        private GameObject visualPrefab;
        [SerializeField, Min(1), Tooltip("임시 규칙. 정상 한 칸 이동의 턴 비용. 1 이상.")]
        private int moveTurnCost = 1;
        [SerializeField, Min(0), Tooltip("임시 규칙. 이동할 수 없는 칸으로 입력했을 때의 턴 비용. 0 이상.")]
        private int blockedTurnCost;
        public GameObject VisualPrefab => visualPrefab;
        public int MoveTurnCost => moveTurnCost;
        public int BlockedTurnCost => blockedTurnCost;
    }
}
