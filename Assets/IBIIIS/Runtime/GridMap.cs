using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Grid Map")]
    public sealed class GridMap : ScriptableObject
    {
        public const int MaxSize = 64;
        [SerializeField, HideInInspector] private int width = 12;
        [SerializeField, HideInInspector] private int height = 12;
        [SerializeField, HideInInspector] private string[] cells = new string[144];
        [SerializeField, HideInInspector] private bool hasStart;
        [SerializeField, HideInInspector] private Vector2Int start;
        [SerializeField, Tooltip("사용할 타일 목록. 같은 ID가 있는 타일을 중복 등록하지 마세요.")]
        private List<TileDefinition> palette = new List<TileDefinition>();
        [SerializeField, Tooltip("선택. Scene에서 편집할 장식 배치 프리팹. 이동 판정과 무관하며 원점은 (0,0) 칸 중심입니다.")]
        private GameObject environmentPrefab;
        public GameObject EnvironmentPrefab => environmentPrefab;
        public void SetEnvironmentPrefab(GameObject value) { environmentPrefab = value; }
        public int Width => width;
        public int Height => height;
        public bool HasStart => hasStart;
        public Vector2Int Start => start;
        public IReadOnlyList<TileDefinition> Palette => palette;
        public bool Contains(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;
        public string GetId(Vector2Int p) => Contains(p) && cells != null && cells.Length == width * height ? cells[p.y * width + p.x] : null;
        public TileDefinition GetTile(Vector2Int p)
        {
            var id = GetId(p);
            if (string.IsNullOrEmpty(id)) return null;
            return palette.Find(t => t != null && t.Id == id);
        }
        public bool IsWalkable(Vector2Int p) => GetTile(p) != null && GetTile(p).Walkable;
        public void AddTile(TileDefinition tile) { if (tile != null && !palette.Contains(tile)) palette.Add(tile); }
        public void SetTile(Vector2Int p, TileDefinition tile)
        {
            if (!Contains(p)) throw new ArgumentOutOfRangeException(nameof(p));
            if (tile != null && !palette.Contains(tile)) throw new ArgumentException("타일을 먼저 팔레트에 등록하세요.");
            cells[p.y * width + p.x] = tile == null ? null : tile.Id;
        }
        public void SetStart(Vector2Int p)
        {
            if (!IsWalkable(p)) throw new ArgumentException("시작 위치는 이동 가능한 타일이어야 합니다.");
            start = p; hasStart = true;
        }
        public void Resize(int newWidth, int newHeight)
        {
            if (newWidth < 1 || newHeight < 1 || newWidth > MaxSize || newHeight > MaxSize)
                throw new ArgumentOutOfRangeException("맵 크기는 각 축 1~64칸입니다.");
            var next = new string[newWidth * newHeight];
            for (int y = 0; y < Math.Min(height, newHeight); y++)
                for (int x = 0; x < Math.Min(width, newWidth); x++) next[y * newWidth + x] = GetId(new Vector2Int(x, y));
            width = newWidth; height = newHeight; cells = next;
            if (!Contains(start)) hasStart = false;
        }
        public List<string> ValidateMap(bool requireStart = true)
        {
            var errors = new List<string>();
            if (width < 1 || height < 1 || width > MaxSize || height > MaxSize || cells == null || cells.Length != width * height)
            { errors.Add("맵 크기 또는 셀 데이터가 올바르지 않습니다."); return errors; }
            var ids = new HashSet<string>();
            foreach (var tile in palette)
            {
                if (tile == null) { errors.Add("팔레트에 누락된 타일 참조가 있습니다."); continue; }
                if (string.IsNullOrEmpty(tile.Id) || !ids.Add(tile.Id)) errors.Add($"타일 '{tile.name}'의 ID가 비어 있거나 중복됩니다.");
            }
            for (int i = 0; i < cells.Length; i++)
                if (!string.IsNullOrEmpty(cells[i]) && !ids.Contains(cells[i])) errors.Add($"({i % width}, {i / width}): 타일 ID '{cells[i]}'의 참조가 없습니다.");
            if (requireStart && (!hasStart || !IsWalkable(start))) errors.Add("플레이어 시작 위치를 이동 가능한 타일에 지정하세요.");
            return errors;
        }
    }
}
