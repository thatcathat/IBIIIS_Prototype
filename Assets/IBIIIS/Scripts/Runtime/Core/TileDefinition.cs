using System;
using UnityEngine;

namespace IBIIIS
{
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string id;
        [SerializeField, Tooltip("바닥 팔레트에 표시할 이름")] private string displayName = "새 타일";
        [SerializeField, HideInInspector] private bool walkable = true;
        [SerializeField, Tooltip("맵 에디터 표시 색상. 재질이 없으면 Scene에서도 이 색상을 사용합니다.")] private Color color = new Color(.3f, .6f, .5f);
        [SerializeField, HideInInspector] private GameObject visualPrefab;
        [SerializeField, Tooltip("선택. 평면 바닥 재질. 지정하면 Scene에서 재질 원본의 색상과 텍스처를 사용합니다.")] private Material surfaceMaterial;
        public Material SurfaceMaterial => surfaceMaterial;
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
