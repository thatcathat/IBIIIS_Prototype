using System;
using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Tile Definition")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string id;
        [SerializeField, Tooltip("팔레트에 표시할 이름")] private string displayName = "새 타일";
        [SerializeField, Tooltip("이 타일 위로 이동할 수 있는지 여부")] private bool walkable = true;
        [SerializeField, Tooltip("맵 편집기 및 대체 외형의 색상")] private Color color = new Color(.3f, .6f, .5f);
        [SerializeField, Tooltip("선택 사항. 한 칸 크기로 제작한 외형 프리팹. 없으면 임시 블록 사용")] private GameObject visualPrefab;
        public string Id => id;
        public string DisplayName => displayName;
        public bool Walkable => walkable;
        public Color Color => color;
        public GameObject VisualPrefab => visualPrefab;
        private void OnValidate() { if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N"); }
        public void Initialize(string stableId, string label, bool canWalk, Color tint)
        {
            id = stableId; displayName = label; walkable = canWalk; color = tint;
        }
    }
}
