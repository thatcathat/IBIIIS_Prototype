using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    public sealed class MapEditorWindow : EditorWindow
    {
        [SerializeField] private GridMap map;
        [SerializeField] private TileDefinition selectedFloor;
        [SerializeField] private GameObject selectedEnemy;
        [SerializeField] private string enemyFacing = "위 (+Z)";
        private MapCanvas canvas;
        private Label status;
        private int undoGroup = -1;
        private string tool = "이동 영역 배치";
        [MenuItem("IBIIIS/Map Editor")]
        public static void Open() { GetWindow<MapEditorWindow>("IBIIIS Map Editor").Show(); }
        public static void OpenMap(GridMap value) { var window = GetWindow<MapEditorWindow>("IBIIIS Map Editor"); window.map = value; window.CreateGUI(); window.Show(); }
        private void OnEnable() { Undo.undoRedoPerformed += Refresh; EditorApplication.projectChanged += Refresh; EditorApplication.playModeStateChanged += PlayModeChanged; }
        private void OnDisable() { EndStroke(); Undo.undoRedoPerformed -= Refresh; EditorApplication.projectChanged -= Refresh; EditorApplication.playModeStateChanged -= PlayModeChanged; }
        private void PlayModeChanged(PlayModeStateChange _) { rootVisualElement.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode); }
        public void CreateGUI()
        {
            rootVisualElement.Clear(); minSize = new Vector2(760, 520);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/IBIIIS/Editor/MapEditor.uxml");
            if (tree == null) { rootVisualElement.Add(new Label("MapEditor.uxml을 찾을 수 없습니다.")); return; }
            tree.CloneTree(rootVisualElement);
            var picker = rootVisualElement.Q<ObjectField>("map"); picker.objectType = typeof(GridMap); picker.allowSceneObjects = false;
            picker.SetValueWithoutNotify(map); picker.RegisterValueChangedCallback(e => { EndStroke(); map = e.newValue as GridMap; selectedFloor = null; Refresh(); });
            status = rootVisualElement.Q<Label>("status");
            canvas = new MapCanvas(this); canvas.AddToClassList("canvas"); rootVisualElement.Q("canvas-host").Add(canvas);
            var tools = rootVisualElement.Q<DropdownField>("tool"); tools.choices = new List<string> { "이동 영역 배치", "지우기", "시작 위치", "적 배치", "적 지우기" }; tools.SetValueWithoutNotify(tool);
            tools.RegisterValueChangedCallback(e => SelectTool(e.newValue));
            Hook("new", NewMap); Hook("save", Save); Hook("undo", Undo.PerformUndo); Hook("redo", Undo.PerformRedo);
            Hook("environment", () => { if (map != null) { Selection.activeObject = map; EditorGUIUtility.PingObject(map); } });
            Hook("resize", Resize); Hook("reset-view", () => canvas.ResetView());
            rootVisualElement.Q("map-settings").RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            { canvas.MarkDirtyRepaint(); if (map != null) UpdateStatus(); });
            var floorPicker = rootVisualElement.Q<ObjectField>("floor-asset"); floorPicker.objectType = typeof(TileDefinition); floorPicker.allowSceneObjects = false;
            Hook("new-floor", NewFloor);
            Hook("add-floor", () =>
            {
                var tile = floorPicker.value as TileDefinition;
                if (map == null || tile == null) return;
                try { Undo.RecordObject(map, "Register floor"); map.AddFloor(tile); selectedFloor = tile; SelectTool("이동 영역 배치"); Changed(); }
                catch (ArgumentException e) { status.text = e.Message; }
            });
            rootVisualElement.Q("floor-settings").RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            { RefreshPalette(); canvas.MarkDirtyRepaint(); if (map != null) UpdateStatus(); });
            var enemyPicker = rootVisualElement.Q<ObjectField>("enemy-prefab"); enemyPicker.objectType = typeof(GameObject); enemyPicker.allowSceneObjects = false;
            enemyPicker.SetValueWithoutNotify(selectedEnemy);
            enemyPicker.RegisterValueChangedCallback(e =>
            {
                var prefab = e.newValue as GameObject;
                if (prefab != null && (!PrefabUtility.IsPartOfPrefabAsset(prefab) || AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab)) != prefab || prefab.GetComponent<EnemyDefinition>() == null))
                { enemyPicker.SetValueWithoutNotify(selectedEnemy); status.text = "EnemyDefinition이 있는 프리팹 루트를 선택하세요."; return; }
                selectedEnemy = prefab; if (prefab != null) SelectTool("적 배치");
            });
            var facing = rootVisualElement.Q<DropdownField>("enemy-facing"); facing.choices = new List<string> { "위 (+Z)", "오른쪽 (+X)", "아래 (-Z)", "왼쪽 (-X)" }; facing.SetValueWithoutNotify(enemyFacing);
            facing.RegisterValueChangedCallback(e => enemyFacing = e.newValue);
            Hook("default-enemies", () => { EnemyPrefabSetup.EnsureDefaults(); status.text = "기본 적 3종: Assets/IBIIIS/Content/Enemies — 적 프리팹 슬롯에서 선택하세요."; });
            Refresh(); PlayModeChanged(default);
        }
        private void Hook(string name, Action action) { rootVisualElement.Q<Button>(name).clicked += action; }
        private void SelectTool(string value)
        {
            tool = value;
            rootVisualElement.Q<DropdownField>("tool").SetValueWithoutNotify(tool);
            if (map != null) UpdateStatus();
        }
        private void NewMap()
        {
            var path = EditorUtility.SaveFilePanelInProject("새 맵과 씬", "NewMap", "asset", "같은 폴더에 같은 이름의 GridMap(.asset)과 씬(.unity)을 생성합니다.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var next = MapEditorSetup.CreateMapWithScene(path, out var scenePath);
                map = next; selectedFloor = null; rootVisualElement.Q<ObjectField>("map").SetValueWithoutNotify(map); canvas.ResetView(); Refresh();
                status.text = $"맵·씬 생성 완료: {scenePath} · 타일과 시작 위치를 지정하세요.";
            }
            catch (Exception e) { EditorUtility.DisplayDialog("맵·씬 생성 실패", e.Message, "확인"); }
        }
        private void NewFloor()
        {
            if (map == null) { status.text = "먼저 맵을 선택하세요."; return; }
            var path = EditorUtility.SaveFilePanelInProject("새 바닥", "NewFloor", "asset", "바닥 종류의 이름과 저장 위치를 지정하세요.");
            if (string.IsNullOrEmpty(path)) return;
            var tile = CreateInstance<TileDefinition>();
            tile.Initialize(Guid.NewGuid().ToString("N"), System.IO.Path.GetFileNameWithoutExtension(path), true, map.MovementColor);
            AssetDatabase.CreateAsset(tile, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(tile);
            Undo.RecordObject(map, "Register floor"); map.AddFloor(tile); selectedFloor = tile; SelectTool("이동 영역 배치"); Changed();
        }
        private void ChooseFloor(TileDefinition tile)
        {
            EndStroke(); selectedFloor = tile; SelectTool("이동 영역 배치"); Refresh();
        }
        private void RefreshPalette()
        {
            var palette = rootVisualElement.Q<ScrollView>("floor-palette"); palette.Clear();
            if (map == null) return;
            var basic = new Button(() => ChooseFloor(null)) { text = "기본 바닥" };
            basic.EnableInClassList("selected-tile", selectedFloor == null); palette.Add(basic);
            foreach (var tile in map.Palette)
            {
                if (tile == null || !tile.Walkable) continue;
                var entry = new Button(() => ChooseFloor(tile)) { text = tile.DisplayName };
                entry.EnableInClassList("selected-tile", selectedFloor == tile); palette.Add(entry);
            }
        }
        private void Resize()
        {
            if (map == null) return;
            int w = rootVisualElement.Q<IntegerField>("width").value, h = rootVisualElement.Q<IntegerField>("height").value;
            if (w < 1 || h < 1 || w > GridMap.MaxSize || h > GridMap.MaxSize) { status.text = "가로·세로는 1~64칸이어야 합니다."; return; }
            if ((w < map.Width || h < map.Height) && !EditorUtility.DisplayDialog("맵 크기 축소", "새 범위 밖의 타일·적 배치·시작 위치가 제거됩니다. 실행 취소로 복구할 수 있습니다.", "축소", "취소")) return;
            Undo.RecordObject(map, "Resize map"); map.Resize(w, h); Changed();
        }
        private void Save()
        {
            if (map == null) return;
            EndStroke(); AssetDatabase.SaveAssetIfDirty(map); foreach (var tile in map.Palette) if (tile != null) AssetDatabase.SaveAssetIfDirty(tile); Refresh();
            status.text = "저장 완료. " + status.text;
        }
        private void Changed() { EditorUtility.SetDirty(map); Refresh(); }
        private void Refresh()
        {
            if (canvas == null || status == null) return;
            if (selectedFloor != null && (map == null || !selectedFloor.Walkable || !System.Linq.Enumerable.Contains(map.Palette, selectedFloor))) selectedFloor = null;
            SelectTool(tool);
            RefreshPalette();
            var floorSettings = rootVisualElement.Q("floor-settings"); floorSettings.Unbind(); floorSettings.Clear();
            if (selectedFloor != null)
            {
                var floorData = new SerializedObject(selectedFloor);
                floorSettings.Add(new PropertyField(floorData.FindProperty("displayName"), "바닥 이름"));
                floorSettings.Add(new PropertyField(floorData.FindProperty("color"), "표시 색상"));
                floorSettings.Add(new PropertyField(floorData.FindProperty("surfaceMaterial"), "바닥 재질"));
                floorSettings.Bind(floorData);
            }
            canvas.Map = map; canvas.MarkDirtyRepaint();
            var settings = rootVisualElement.Q("map-settings"); settings.Unbind(); settings.Clear();
            if (map != null)
            {
                var serialized = new SerializedObject(map);
                settings.Add(new PropertyField(serialized.FindProperty("groundMaterial"), "배경 바닥 재질"));
                settings.Add(new PropertyField(serialized.FindProperty("groundMargin"), "외곽 여유 (칸)"));
                settings.Add(new PropertyField(serialized.FindProperty("movementColor"), "기본 바닥 색상"));
                settings.Bind(serialized);
            }
            if (map == null) { status.text = "새 맵을 만들거나 상단에서 맵 에셋을 선택하세요."; return; }
            rootVisualElement.Q<IntegerField>("width").SetValueWithoutNotify(map.Width); rootVisualElement.Q<IntegerField>("height").SetValueWithoutNotify(map.Height);
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            var errors = map.ValidateMap();
            status.text = $"현재 도구: {tool} · 바닥: {(selectedFloor != null ? selectedFloor.DisplayName : "기본 바닥")} · " + (EditorUtility.IsDirty(map) || System.Linq.Enumerable.Any(map.Palette, t => t != null && EditorUtility.IsDirty(t)) ? "저장 전 변경 있음 · " : "") + $"{map.Width} × {map.Height}칸 · 적 {map.Enemies.Count}마리{(map.Enemies.Count % 2 != 0 ? " (홀수 배치)" : "")} · " +
                (errors.Count == 0 ? "플레이 준비 완료" : $"확인 필요 {errors.Count}건: {errors[0]}");
        }
        internal void BeginStroke() { EndStroke(); Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Paint map"); if (map != null) Undo.RegisterCompleteObjectUndo(map, "Paint map"); }
        internal void EndStroke() { if (undoGroup >= 0) { Undo.CollapseUndoOperations(undoGroup); undoGroup = -1; } }
        internal void Paint(Vector2Int p, bool inspectOnly)
        {
            if (map == null || !map.Contains(p)) return;
            if (!inspectOnly)
            {
                try
                {
                    if (tool == "시작 위치") map.SetStart(p);
                    else if (tool == "지우기") map.SetWalkable(p, false);
                    else if (tool == "적 지우기") map.RemoveEnemy(p);
                    else if (tool == "적 배치")
                    {
                        var direction = enemyFacing == "위 (+Z)" ? Vector2Int.up : enemyFacing == "오른쪽 (+X)" ? Vector2Int.right : enemyFacing == "아래 (-Z)" ? Vector2Int.down : Vector2Int.left;
                        map.PlaceEnemy(p, selectedEnemy, direction);
                    }
                    else map.PaintFloor(p, selectedFloor);
                }
                catch (ArgumentException e) { status.text = e.Message; return; }
                EditorUtility.SetDirty(map); canvas.MarkDirtyRepaint(); UpdateStatus();
            }
            var enemy = map.EnemyAt(p);
            rootVisualElement.Q<Label>("selection").text = $"선택 ({p.x}, {p.y})\n" + (map.IsWalkable(p) ? (map.GetTile(p) != null ? map.GetTile(p).DisplayName : "기본 바닥") + " · 이동 가능" : "빈 칸 · 이동 불가") + (enemy != null ? $"\n적: {(enemy.Prefab != null ? enemy.Prefab.name : "누락")} / 방향 {enemy.Direction}" : "");
        }
    }
    [CustomEditor(typeof(GridMap))]
    public sealed class GridMapInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement(); root.Add(new Button(() => MapEditorWindow.OpenMap((GridMap)target)) { text = "맵 에디터에서 열기" });
            root.Add(new PropertyField(serializedObject.FindProperty("groundMaterial"), "배경 바닥 재질"));
            root.Add(new PropertyField(serializedObject.FindProperty("groundMargin"), "외곽 여유 (칸)"));
            root.Add(new PropertyField(serializedObject.FindProperty("movementColor"), "기본 바닥 색상"));
            root.Add(MapEnvironmentEditor.CreateMapControls((GridMap)target)); return root;
        }
    }
}
