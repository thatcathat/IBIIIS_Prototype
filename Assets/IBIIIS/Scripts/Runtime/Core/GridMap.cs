using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    [CreateAssetMenu(menuName = "IBIIIS/Grid Map")]
    public sealed class GridMap : ScriptableObject
    {
        public const int MaxSize = 64;
        [SerializeField, HideInInspector] private List<EnemyPlacement> enemies = new List<EnemyPlacement>();
        public IReadOnlyList<EnemyPlacement> Enemies => enemies;
        public EnemyPlacement EnemyAt(Vector2Int p) => enemies.Find(e => e != null && e.Position == p);
        public void RemoveEnemy(Vector2Int p) { enemies.RemoveAll(e => e != null && e.Position == p); }
        public void PlaceEnemy(Vector2Int p, GameObject prefab, Vector2Int direction)
        {
            var definition = prefab != null ? prefab.GetComponent<EnemyDefinition>() : null;
            if (definition == null || !definition.IsValid) throw new ArgumentException("루트에 유효한 EnemyDefinition이 있는 적 프리팹을 선택하세요.");
            if (!IsWalkable(p) || (hasStart && start == p)) throw new ArgumentException("적은 시작 위치를 제외한 이동 가능한 칸에 배치하세요.");
            if (Math.Abs((long)direction.x) + Math.Abs((long)direction.y) != 1) throw new ArgumentException("적의 방향은 상하좌우여야 합니다.");
            RemoveEnemy(p); enemies.Add(new EnemyPlacement(prefab, p, direction));
        }
        private const string WalkableCellId = "__ibiiis_walkable__";
        [SerializeField, Tooltip("선택. 비어 있으면 공용 기본 바닥 재질을 사용합니다.")] private Material groundMaterial;
        [SerializeField, Min(0), Tooltip("그리드 바깥 각 방향의 바닥 여유 폭 (칸). 기본값 50.")] private float groundMargin = 50;
        [SerializeField, Tooltip("이동 영역 표시 색상. 표시 방식은 임시 설정입니다.")] private Color movementColor = new Color(.28f, .55f, .46f);
        public Material GroundMaterial => groundMaterial;
        public float GroundMargin => Mathf.Max(0, groundMargin);
        public Color MovementColor => movementColor;
        public void SetWalkable(Vector2Int p, bool value)
        {
            if (!Contains(p)) throw new ArgumentOutOfRangeException(nameof(p));
            cells[p.y * width + p.x] = value ? WalkableCellId : null;
            if (!value) { if (hasStart && start == p) hasStart = false; RemoveEnemy(p); }
        }
        [SerializeField, HideInInspector] private int width = 12;
        [SerializeField, HideInInspector] private int height = 12;
        [SerializeField, HideInInspector] private string[] cells = new string[144];
        [SerializeField, HideInInspector] private bool hasStart;
        [SerializeField, HideInInspector] private Vector2Int start;
        [SerializeField, HideInInspector]
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
        /// <summary>삭제된 바닥 에셋을 가리키는 팔레트 항목 수.</summary>
        public int MissingTileCount { get { int count = 0; foreach (var tile in palette) if (tile == null) count++; return count; } }
        /// <summary>삭제된 바닥 에셋을 가리키는 팔레트 항목을 지우고 지운 개수를 돌려준다. 칸·시작 위치·적 배치는 바꾸지 않는다.
        /// 지운 바닥을 쓰는 칸이 있으면 그 칸은 셀 ID 검사가 계속 오류로 알린다.</summary>
        public int RemoveMissingTiles() => palette.RemoveAll(tile => tile == null);
        public bool Contains(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;
        public string GetId(Vector2Int p) => Contains(p) && cells != null && cells.Length == width * height ? cells[p.y * width + p.x] : null;
        public TileDefinition GetTile(Vector2Int p)
        {
            var id = GetId(p);
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < palette.Count; i++) if (palette[i] != null && palette[i].Id == id) return palette[i];
            return null;
        }
        public bool IsWalkable(Vector2Int p)
        {
            if (GetId(p) == WalkableCellId) return true;
            var tile = GetTile(p); return tile != null && tile.Walkable;
        }
        public void AddTile(TileDefinition tile) { if (tile != null && !palette.Contains(tile)) palette.Add(tile); }
        public void SetTile(Vector2Int p, TileDefinition tile)
        {
            if (!Contains(p)) throw new ArgumentOutOfRangeException(nameof(p));
            if (tile != null && !palette.Contains(tile)) throw new ArgumentException("타일을 먼저 팔레트에 등록하세요.");
            cells[p.y * width + p.x] = tile == null ? null : tile.Id;
        }
        public void AddFloor(TileDefinition tile)
        {
            if (tile == null || !tile.Walkable) throw new ArgumentException("이동 가능한 바닥만 등록할 수 있습니다.");
            if (string.IsNullOrEmpty(tile.Id) || tile.Id == WalkableCellId || palette.Exists(t => t != null && t != tile && t.Id == tile.Id))
                throw new ArgumentException("바닥 ID가 비어 있거나 중복됩니다. 새 바닥 만들기로 생성하세요.");
            AddTile(tile);
        }
        public void PaintFloor(Vector2Int p, TileDefinition tile)
        {
            if (tile == null) { SetWalkable(p, true); return; }
            if (!tile.Walkable) throw new ArgumentException("이동 불가 타일은 배치할 수 없습니다.");
            SetTile(p, tile);
        }
        public Color GetFloorColor(Vector2Int p) { var tile = GetTile(p); return tile != null ? tile.Color : movementColor; }
        public void SetStart(Vector2Int p)
        {
            if (!IsWalkable(p) || EnemyAt(p) != null) throw new ArgumentException("시작 위치는 적이 없는 이동 가능한 칸이어야 합니다.");
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
            enemies.RemoveAll(e => e != null && !Contains(e.Position));
        }
        public List<string> ValidateMap(bool requireStart = true)
        {
            var errors = new List<string>();
            if (width < 1 || height < 1 || width > MaxSize || height > MaxSize || cells == null || cells.Length != width * height)
            { errors.Add("맵 크기 또는 셀 데이터가 올바르지 않습니다."); return errors; }
            var ids = new HashSet<string> { WalkableCellId };
            foreach (var tile in palette)
            {
                if (tile == null) { errors.Add("팔레트에 삭제된 바닥 참조가 있습니다. 맵 에디터 '칠하기' 탭의 '삭제된 바닥 참조 정리'로 지우세요."); continue; }
                if (string.IsNullOrEmpty(tile.Id) || !ids.Add(tile.Id)) errors.Add($"타일 '{tile.name}'의 ID가 비어 있거나 중복됩니다.");
            }
            for (int i = 0; i < cells.Length; i++)
                if (!string.IsNullOrEmpty(cells[i]) && !ids.Contains(cells[i])) errors.Add($"({i % width}, {i / width}): 타일 ID '{cells[i]}'의 참조가 없습니다.");
            if (requireStart && (!hasStart || !IsWalkable(start))) errors.Add("플레이어 시작 위치를 이동 가능한 타일에 지정하세요.");
            var occupied = new HashSet<Vector2Int>();
            foreach (var enemy in enemies)
            {
                if (enemy == null) { errors.Add("누락된 적 배치가 있습니다."); continue; }
                var definition = enemy.Prefab != null ? enemy.Prefab.GetComponent<EnemyDefinition>() : null;
                if (enemy.Prefab == null) errors.Add($"{enemy.Position}: 적 프리팹이 없습니다(삭제되었거나 연결이 끊김).");
                else if (definition == null) errors.Add($"{enemy.Position}: 적 프리팹 '{enemy.Prefab.name}'에 EnemyDefinition이 없습니다.");
                else if (!definition.IsValid) errors.Add($"{enemy.Position}: 적 '{enemy.Prefab.name}' 설정 오류: {string.Join(", ", definition.DescribeProblems())}.");
                if (!IsWalkable(enemy.Position) || !occupied.Add(enemy.Position) || (hasStart && start == enemy.Position)) errors.Add($"{enemy.Position}: 적 배치가 이동 영역·시작 위치·다른 적과 충돌합니다.");
                if (Math.Abs((long)enemy.Direction.x) + Math.Abs((long)enemy.Direction.y) != 1) errors.Add($"{enemy.Position}: 적 방향이 잘못되었습니다.");
            }
            return errors;
        }
    }
}
