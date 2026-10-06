using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    public enum MapTool { Paint, Erase, Start, PlaceEnemy, EraseEnemy, Select }

    public sealed class MapEditorWindow : EditorWindow
    {
        private static readonly (MapTool tool, string label, KeyCode key, string tip)[] Tools =
        {
            (MapTool.Paint, "칠하기", KeyCode.B, "선택한 바닥으로 이동 영역을 칠합니다."),
            (MapTool.Erase, "지우기", KeyCode.E, "이동 영역을 지웁니다. 그 칸의 적과 시작 위치도 지워집니다."),
            (MapTool.Start, "시작 위치", KeyCode.S, "플레이어 시작 칸을 지정합니다."),
            (MapTool.PlaceEnemy, "적 배치", KeyCode.N, "선택한 적을 배치 방향으로 놓습니다. 같은 칸에 다시 놓으면 교체합니다."),
            (MapTool.EraseEnemy, "적 지우기", KeyCode.X, "바닥은 남기고 적만 지웁니다."),
            (MapTool.Select, "선택", KeyCode.V, "칸을 선택해 정보를 보고, 적을 회전·삭제합니다."),
        };
        private static readonly (Vector2Int dir, string label)[] Facings = { (Vector2Int.up, "↑ 위"), (Vector2Int.right, "→ 오른쪽"), (Vector2Int.down, "↓ 아래"), (Vector2Int.left, "← 왼쪽") };

        [SerializeField] private GridMap map;
        [SerializeField] private TileDefinition selectedFloor;
        [SerializeField] private GameObject selectedEnemy;
        [SerializeField] private Vector2Int placeFacing = Vector2Int.up;
        [SerializeField] private MapTool tool = MapTool.Paint;
        [SerializeField] private bool showAllRanges;
        [SerializeField] private bool hasSelection;
        [SerializeField] private Vector2Int selection;
        private MapCanvas canvas;
        private Label status;
        private MapAnalysis analysis;
        private MapReplay replay;
        internal MapReplay Replay => replay;
        private int undoGroup = -1;

        // 캔버스가 읽는 편집 상태
        internal GridMap Map => map;
        internal MapTool Tool => tool;
        internal TileDefinition SelectedFloor => selectedFloor;
        internal GameObject SelectedEnemy => selectedEnemy;
        internal Vector2Int PlaceFacing => placeFacing;
        internal bool ShowAllRanges => showAllRanges;
        internal Vector2Int? Selection => hasSelection ? selection : (Vector2Int?)null;

        [MenuItem("IBIIIS/Map Editor")]
        public static void Open() { GetWindow<MapEditorWindow>("IBIIIS Map Editor").Show(); }
        public static void OpenMap(GridMap value) { var window = GetWindow<MapEditorWindow>("IBIIIS Map Editor"); window.map = value; window.hasSelection = false; window.CreateGUI(); window.Show(); }
        private void OnEnable() { Undo.undoRedoPerformed += UndoRedoPerformed; EditorApplication.projectChanged += ProjectChanged; EditorApplication.playModeStateChanged += PlayModeChanged; }
        private void OnDisable() { EndStroke(); Undo.undoRedoPerformed -= UndoRedoPerformed; EditorApplication.projectChanged -= ProjectChanged; EditorApplication.playModeStateChanged -= PlayModeChanged; }
        private void PlayModeChanged(PlayModeStateChange _) { rootVisualElement.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode); }

        public void CreateGUI()
        {
            rootVisualElement.Clear(); minSize = new Vector2(820, 540);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetPaths.MapEditorLayout);
            if (tree == null) { rootVisualElement.Add(new Label("MapEditor.uxml을 찾을 수 없습니다.")); return; }
            tree.CloneTree(rootVisualElement);
            rootVisualElement.Q(className: "map-root").style.flexGrow = 1;
            var picker = rootVisualElement.Q<ObjectField>("map"); picker.objectType = typeof(GridMap); picker.allowSceneObjects = false;
            picker.SetValueWithoutNotify(map);
            picker.RegisterValueChangedCallback(e => { EndStroke(); map = e.newValue as GridMap; selectedFloor = null; hasSelection = false; ClearAnalysis(); Refresh(); });
            status = rootVisualElement.Q<Label>("status");
            canvas = new MapCanvas(this); canvas.AddToClassList("canvas");
            var host = rootVisualElement.Q("canvas-host"); host.Insert(0, canvas);
            BuildToolRail(); BuildFacingButtons();
            Hook("new", NewMap); Hook("save", Save); Hook("undo", Undo.PerformUndo); Hook("redo", Undo.PerformRedo);
            Hook("reset-view", () => canvas.ResetView());
            var allRanges = rootVisualElement.Q<Toggle>("show-all-ranges"); allRanges.SetValueWithoutNotify(showAllRanges);
            allRanges.RegisterValueChangedCallback(e => { showAllRanges = e.newValue; canvas.MarkDirtyRepaint(); });
            Hook("environment", () => { if (map != null) { UnityEditor.Selection.activeObject = map; EditorGUIUtility.PingObject(map); } });
            Hook("analyze", Analyze);
            Hook("copy-report", () => { if (analysis != null) { EditorGUIUtility.systemCopyBuffer = $"[{map?.name}]\n{analysis.ToReport()}"; Message("검증 보고서를 클립보드에 복사했습니다."); } });
            Hook("show-win", () => StartReplay("최단 승리 경로", analysis?.WinPath));
            Hook("show-loss", () => StartReplay("가장 빠른 패배 경로", analysis?.LossPath));
            Hook("replay-close", CloseReplay);
            Hook("replay-first", () => StepReplay(int.MinValue)); Hook("replay-prev", () => StepReplay(-1));
            Hook("replay-next", () => StepReplay(1)); Hook("replay-last", () => StepReplay(int.MaxValue));
            rootVisualElement.Q<SliderInt>("replay-slider").RegisterValueChangedCallback(e => { if (replay != null) { replay.Index = e.newValue; RefreshReplay(); } }); Hook("resize", Resize);
            Hook("rotate-cw", () => RotateSelectedEnemy(true)); Hook("rotate-ccw", () => RotateSelectedEnemy(false)); Hook("delete-enemy", DeleteSelectedEnemy);
            rootVisualElement.Q("map-settings").RegisterCallback<SerializedPropertyChangeEvent>(_ => { canvas.MarkDirtyRepaint(); if (map != null) UpdateStatus(); });
            var floorPicker = rootVisualElement.Q<ObjectField>("floor-asset"); floorPicker.objectType = typeof(TileDefinition); floorPicker.allowSceneObjects = false;
            Hook("new-floor", NewFloor);
            Hook("add-floor", () =>
            {
                var tile = floorPicker.value as TileDefinition;
                if (map == null || tile == null) return;
                try { Undo.RecordObject(map, "Register floor"); map.AddFloor(tile); selectedFloor = tile; SelectTool(MapTool.Paint); Changed(); }
                catch (ArgumentException e) { Message(e.Message); }
            });
            rootVisualElement.Q("floor-settings").RegisterCallback<SerializedPropertyChangeEvent>(_ => { RefreshPalette(); canvas.MarkDirtyRepaint(); if (map != null) UpdateStatus(); });
            Hook("default-enemies", () => { EnemyPrefabSetup.EnsureDefaults(); enemyPrefabs = null; RefreshEnemyPalette(); Message($"기본 적 3종: {AssetPaths.Enemies} — 적 탭에서 선택하세요."); });
            // 단축키: 입력 칸에 글자를 치는 중에는 무시한다.
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Refresh(); PlayModeChanged(default);
        }
        private void Hook(string name, Action action) { rootVisualElement.Q<Button>(name).clicked += action; }

        // ---------- 도구·방향 ----------
        private void BuildToolRail()
        {
            var rail = rootVisualElement.Q("tool-rail"); rail.Clear();
            foreach (var entry in Tools)
            {
                var button = new Button(() => SelectTool(entry.tool)) { text = $"{entry.label}  {entry.key}", tooltip = $"{entry.tip} (단축키 {entry.key})", name = "tool-" + entry.tool };
                button.AddToClassList("tool-button"); rail.Add(button);
            }
        }
        private void BuildFacingButtons()
        {
            var row = rootVisualElement.Q("facing-buttons"); row.Clear();
            foreach (var entry in Facings) row.Add(new Button(() => { placeFacing = entry.dir; RefreshFacing(); canvas.MarkDirtyRepaint(); }) { text = entry.label, name = "facing-" + entry.dir });
            RefreshFacing();
        }
        private void RefreshFacing()
        {
            foreach (var entry in Facings) rootVisualElement.Q<Button>("facing-" + entry.dir)?.EnableInClassList("facing-button--active", placeFacing == entry.dir);
        }
        internal void SelectTool(MapTool value)
        {
            bool changed = tool != value;
            if (changed) EndStroke();
            tool = value;
            foreach (var entry in Tools) rootVisualElement.Q<Button>("tool-" + entry.tool)?.EnableInClassList("tool-button--active", entry.tool == tool);
            var tabs = rootVisualElement.Q<TabView>("tabs");
            if (changed && tabs != null && tool == MapTool.Paint) tabs.activeTab = rootVisualElement.Q<Tab>("tab-paint");
            if (changed && tabs != null && tool == MapTool.PlaceEnemy) tabs.activeTab = rootVisualElement.Q<Tab>("tab-enemy");
            canvas?.MarkDirtyRepaint();
            if (map != null && status != null) UpdateStatus();
        }
        private static Vector2Int Rotate(Vector2Int d, bool clockwise) => clockwise ? new Vector2Int(d.y, -d.x) : new Vector2Int(-d.y, d.x);
        private static bool IsTyping(IEventHandler target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element.ClassListContains("unity-base-text-field")) return true;
            return false;
        }
        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.None || e.actionKey || e.altKey || IsTyping(e.target) || map == null) return;
            foreach (var entry in Tools) if (e.keyCode == entry.key && !e.shiftKey) { SelectTool(entry.tool); e.StopPropagation(); return; }
            switch (e.keyCode)
            {
                case KeyCode.R:
                    if (tool == MapTool.Select && SelectedEnemyPlacement() != null) RotateSelectedEnemy(!e.shiftKey);
                    else { placeFacing = Rotate(placeFacing, !e.shiftKey); RefreshFacing(); canvas.MarkDirtyRepaint(); }
                    break;
                case KeyCode.Delete: case KeyCode.Backspace: if (SelectedEnemyPlacement() == null) return; DeleteSelectedEnemy(); break;
                case KeyCode.F: canvas.ResetView(); break;
                case KeyCode.LeftArrow: if (replay == null) return; StepReplay(-1); break;
                case KeyCode.RightArrow: if (replay == null) return; StepReplay(1); break;
                case KeyCode.Home: if (replay == null) return; StepReplay(int.MinValue); break;
                case KeyCode.End: if (replay == null) return; StepReplay(int.MaxValue); break;
                case KeyCode.Escape:
                    if (replay != null) { CloseReplay(); break; }
                    hasSelection = false; RefreshSelection(); canvas.MarkDirtyRepaint(); break;
                default: return;
            }
            e.StopPropagation();
        }

        // ---------- 선택 ----------
        internal void SelectCell(Vector2Int p)
        {
            if (map == null || !map.Contains(p)) { hasSelection = false; }
            else { hasSelection = true; selection = p; }
            RefreshSelection(); canvas.MarkDirtyRepaint();
        }
        private EnemyPlacement SelectedEnemyPlacement() => hasSelection && map != null ? map.EnemyAt(selection) : null;
        private void RotateSelectedEnemy(bool clockwise)
        {
            var enemy = SelectedEnemyPlacement(); if (enemy == null) return;
            try { Undo.RecordObject(map, "Rotate enemy"); map.PlaceEnemy(enemy.Position, enemy.Prefab, Rotate(enemy.Direction, clockwise)); Changed(); }
            catch (ArgumentException ex) { Message(ex.Message); }
        }
        private void DeleteSelectedEnemy()
        {
            var enemy = SelectedEnemyPlacement(); if (enemy == null) return;
            Undo.RecordObject(map, "Delete enemy"); map.RemoveEnemy(enemy.Position); Changed();
        }
        private void RefreshSelection()
        {
            var label = rootVisualElement.Q<Label>("selection"); var actions = rootVisualElement.Q("enemy-actions");
            if (label == null) return;
            var enemy = SelectedEnemyPlacement();
            actions.style.display = enemy != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (map == null || !hasSelection || !map.Contains(selection)) { label.text = "선택 도구(V)나 오른쪽 클릭으로 칸을 선택하세요."; return; }
            label.text = $"({selection.x}, {selection.y}) · " + DescribeCell(selection) + (enemy != null ? "\n" + DescribeEnemy(enemy) : "");
        }
        internal string DescribeCell(Vector2Int p)
        {
            string floor = map.IsWalkable(p) ? (map.GetTile(p) != null ? map.GetTile(p).DisplayName : "기본 바닥") + " · 이동 가능" : "빈 칸 · 이동 불가";
            return floor + (map.HasStart && map.Start == p ? " · 시작 위치" : "");
        }
        private static string FacingName(Vector2Int d) => Facings.FirstOrDefault(f => f.dir == d).label ?? d.ToString();
        private static string DescribeEnemy(EnemyPlacement enemy)
        {
            var definition = enemy.Prefab != null ? enemy.Prefab.GetComponent<EnemyDefinition>() : null;
            if (definition == null) return "적: 프리팹 누락";
            return $"적: {enemy.Prefab.name} · 방향 {FacingName(enemy.Direction)}\n행동: {DescribeActions(definition)}";
        }
        private static string DescribeActions(EnemyDefinition definition)
            => string.Join(" → ", definition.Actions.Select(a => a.Type == EnemyActionType.AimAtPlayer ? "조준" : a.Type == EnemyActionType.MoveForward ? $"전진 {a.Cells}칸" : $"회전({a.Turn})"));
        // 프로젝트의 적 프리팹 목록. 프로젝트가 바뀔 때만 다시 찾는다.
        private List<GameObject> enemyPrefabs;
        private void ProjectChanged() { enemyPrefabs = null; Refresh(); }
        private static List<GameObject> FindEnemyPrefabs()
        {
            var found = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<EnemyDefinition>() != null) found.Add(prefab);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }
        private void RefreshEnemyPalette()
        {
            var palette = rootVisualElement.Q("enemy-palette"); if (palette == null) return;
            palette.Clear();
            // 도메인 리로드 직후에는 에셋 검색이 빈 결과를 줄 수 있으므로 빈 목록은 캐시하지 않고 다시 찾는다.
            if (enemyPrefabs == null || enemyPrefabs.Count == 0) enemyPrefabs = FindEnemyPrefabs();
            if (selectedEnemy != null && !enemyPrefabs.Contains(selectedEnemy)) selectedEnemy = null;
            if (enemyPrefabs.Count == 0) { palette.Add(new Label("프로젝트에 EnemyDefinition이 있는 프리팹이 없습니다. 아래 버튼으로 기본 적을 만드세요.") { name = "enemy-empty" }); return; }
            foreach (var prefab in enemyPrefabs)
            {
                var definition = prefab.GetComponent<EnemyDefinition>(); var target = prefab;
                var button = new Button(() =>
                {
                    if (!definition.IsValid) { Message($"{target.name}: EnemyDefinition 설정이 올바르지 않아 배치할 수 없습니다. 프리팹을 확인하세요."); return; }
                    selectedEnemy = target; RefreshEnemyPalette(); RefreshEnemySummary(); SelectTool(MapTool.PlaceEnemy);
                }) { text = prefab.name, tooltip = $"{AssetDatabase.GetAssetPath(prefab)}\n{DescribeActions(definition)}" };
                button.AddToClassList("swatch"); button.AddToClassList("swatch--enemy");
                button.EnableInClassList("swatch--selected", selectedEnemy == prefab); button.EnableInClassList("swatch--invalid", !definition.IsValid);
                var chip = new VisualElement { pickingMode = PickingMode.Ignore }; chip.AddToClassList("swatch-color"); var c = definition.EditorColor; chip.style.backgroundColor = new Color(c.r, c.g, c.b, 1); button.Add(chip);
                var sprite = prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
                var image = new VisualElement { pickingMode = PickingMode.Ignore }; image.AddToClassList("swatch-image");
                if (sprite != null) image.style.backgroundImage = new StyleBackground(sprite); else { image.style.backgroundColor = new Color(c.r, c.g, c.b, .6f); image.style.marginLeft = 22; image.style.marginRight = 22; }
                button.Add(image); palette.Add(button);
            }
        }
        private void RefreshEnemySummary()
        {
            var label = rootVisualElement.Q<Label>("enemy-summary"); if (label == null) return;
            var definition = selectedEnemy != null ? selectedEnemy.GetComponent<EnemyDefinition>() : null;
            label.text = definition == null ? "적 프리팹을 선택하세요." : $"{selectedEnemy.name}: {DescribeActions(definition)} · 인식 {definition.Recognition?.Length ?? 0}칸 · 공격 {definition.Attack?.Length ?? 0}칸(인식 후 {definition.RecognizedAttack?.Length ?? 0}칸)";
        }

        // ---------- 편집 ----------
        internal void BeginStroke() { EndStroke(); Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Paint map"); if (map != null) Undo.RegisterCompleteObjectUndo(map, "Paint map"); }
        internal void EndStroke() { if (undoGroup >= 0) { Undo.CollapseUndoOperations(undoGroup); undoGroup = -1; } }
        /// <summary>현재 도구로 칸을 바꿀 수 있는지. 캔버스의 미리보기와 실제 적용이 같은 기준을 쓴다.</summary>
        internal bool CanApply(Vector2Int p)
        {
            if (map == null || !map.Contains(p)) return false;
            switch (tool)
            {
                case MapTool.Start: return map.IsWalkable(p) && map.EnemyAt(p) == null;
                case MapTool.PlaceEnemy:
                    var definition = selectedEnemy != null ? selectedEnemy.GetComponent<EnemyDefinition>() : null;
                    return definition != null && definition.IsValid && map.IsWalkable(p) && !(map.HasStart && map.Start == p);
                case MapTool.EraseEnemy: return map.EnemyAt(p) != null;
                case MapTool.Select: return true;
                default: return true;
            }
        }
        internal void Paint(Vector2Int p)
        {
            if (map == null || !map.Contains(p)) return;
            try
            {
                switch (tool)
                {
                    case MapTool.Start: map.SetStart(p); break;
                    case MapTool.Erase: map.SetWalkable(p, false); break;
                    case MapTool.EraseEnemy: map.RemoveEnemy(p); break;
                    case MapTool.PlaceEnemy:
                        if (selectedEnemy == null) { Message("적 탭에서 배치할 적을 먼저 선택하세요."); return; }
                        map.PlaceEnemy(p, selectedEnemy, placeFacing); break;
                    case MapTool.Paint: map.PaintFloor(p, selectedFloor); break;
                    default: return;
                }
            }
            catch (ArgumentException e) { Message(e.Message); return; }
            EditorUtility.SetDirty(map); ClearAnalysis(); canvas.MarkDirtyRepaint(); UpdateStatus(); RefreshSelection();
        }
        internal void SetHover(Vector2Int? p)
        {
            var label = rootVisualElement.Q<Label>("status-hover"); if (label == null) return;
            if (map == null || p == null || !map.Contains(p.Value)) { label.style.display = DisplayStyle.None; return; }
            var enemy = map.EnemyAt(p.Value);
            label.style.display = DisplayStyle.Flex;
            label.text = $"({p.Value.x}, {p.Value.y}) {DescribeCell(p.Value)}" + (enemy != null && enemy.Prefab != null ? $" · {enemy.Prefab.name} {FacingName(enemy.Direction)}" : "");
        }
        private void NewMap()
        {
            var path = EditorUtility.SaveFilePanelInProject("새 맵과 씬", "NewMap", "asset", "같은 폴더에 같은 이름의 GridMap(.asset)과 씬(.unity)을 생성합니다. Maps 폴더를 고르면 맵 이름 폴더를 만들어 넣습니다.", AssetPaths.Maps);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                // Maps 바로 아래를 고르면 Maps/<이름>/ 폴더에 맵·씬 쌍을 둔다.
                if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') == AssetPaths.Maps)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!AssetDatabase.IsValidFolder(AssetPaths.Maps + "/" + name)) AssetDatabase.CreateFolder(AssetPaths.Maps, name);
                    path = $"{AssetPaths.Maps}/{name}/{name}.asset";
                }
                var next = MapEditorSetup.CreateMapWithScene(path, out var scenePath);
                map = next; selectedFloor = null; hasSelection = false; ClearAnalysis(); rootVisualElement.Q<ObjectField>("map").SetValueWithoutNotify(map); canvas.ResetView(); Refresh();
                Message($"맵·씬 생성 완료: {scenePath} · 바닥과 시작 위치를 지정하세요.");
            }
            catch (Exception e) { EditorUtility.DisplayDialog("맵·씬 생성 실패", e.Message, "확인"); }
        }
        private void NewFloor()
        {
            if (map == null) { Message("먼저 맵을 선택하세요."); return; }
            var path = EditorUtility.SaveFilePanelInProject("새 바닥", "NewFloor", "asset", "바닥 종류의 이름과 저장 위치를 지정하세요.", AssetPaths.Tiles);
            if (string.IsNullOrEmpty(path)) return;
            var tile = CreateInstance<TileDefinition>();
            tile.Initialize(Guid.NewGuid().ToString("N"), System.IO.Path.GetFileNameWithoutExtension(path), true, map.MovementColor);
            AssetDatabase.CreateAsset(tile, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(tile);
            Undo.RecordObject(map, "Register floor"); map.AddFloor(tile); selectedFloor = tile; SelectTool(MapTool.Paint); Changed();
        }
        private void ChooseFloor(TileDefinition tile) { EndStroke(); selectedFloor = tile; SelectTool(MapTool.Paint); Refresh(); }
        private void RefreshPalette()
        {
            var palette = rootVisualElement.Q("floor-palette"); palette.Clear();
            if (map == null) return;
            palette.Add(Swatch("기본 바닥", map.MovementColor, selectedFloor == null, () => ChooseFloor(null)));
            foreach (var tile in map.Palette)
                if (tile != null && tile.Walkable) palette.Add(Swatch(tile.DisplayName, tile.Color, selectedFloor == tile, () => ChooseFloor(tile)));
        }
        private static Button Swatch(string text, Color color, bool selected, Action onClick)
        {
            var button = new Button(onClick) { text = text, tooltip = text };
            button.AddToClassList("swatch"); button.EnableInClassList("swatch--selected", selected);
            var chip = new VisualElement(); chip.AddToClassList("swatch-color"); chip.style.backgroundColor = new Color(color.r, color.g, color.b, 1); chip.pickingMode = PickingMode.Ignore;
            button.Add(chip); return button;
        }
        private void Resize()
        {
            if (map == null) return;
            int w = rootVisualElement.Q<IntegerField>("width").value, h = rootVisualElement.Q<IntegerField>("height").value;
            if (w < 1 || h < 1 || w > GridMap.MaxSize || h > GridMap.MaxSize) { Message("가로·세로는 1~64칸이어야 합니다."); return; }
            if ((w < map.Width || h < map.Height) && !EditorUtility.DisplayDialog("맵 크기 축소", "새 범위 밖의 타일·적 배치·시작 위치가 제거됩니다. 실행 취소로 복구할 수 있습니다.", "축소", "취소")) return;
            Undo.RecordObject(map, "Resize map"); map.Resize(w, h); if (hasSelection && !map.Contains(selection)) hasSelection = false; Changed();
        }
        private void Save()
        {
            if (map == null) return;
            EndStroke(); AssetDatabase.SaveAssetIfDirty(map); foreach (var tile in map.Palette) if (tile != null) AssetDatabase.SaveAssetIfDirty(tile); Refresh();
            Message("저장 완료.");
        }

        // ---------- 검증 ----------
        private void UndoRedoPerformed() { ClearAnalysis(); Refresh(); }
        // 맵 검증은 실행 시점의 맵 기준이다. 맵이 바뀌면 오래된 결과와 재생이 남지 않도록 지운다.
        private void ClearAnalysis()
        {
            bool hadReplay = replay != null;
            analysis = null; replay = null; RefreshAnalysisPanel();
            if (hadReplay) canvas?.MarkDirtyRepaint();
        }
        private void Analyze()
        {
            if (map == null) { Message("먼저 맵을 선택하세요."); return; }
            EndStroke(); replay = null;
            analysis = MapAnalysisRunner.Compute(map, rootVisualElement.Q<IntegerField>("max-states").value);
            RefreshAnalysisPanel(); canvas.MarkDirtyRepaint();
            Message(analysis.Errors.Count > 0 ? "맵 오류: " + analysis.Errors[0] : analysis.States == 0 && analysis.Notes.Count > 0 ? "맵 검증: " + analysis.Notes[0] : analysis.Solvable ? $"맵 검증: 클리어 가능(최단 {analysis.ShortestWin}행동)" : analysis.Completed ? "맵 검증: 클리어 불가능" : "맵 검증: 결과 불완전");
            Debug.Log($"[IBIIIS] 맵 분석: {map.name}\n{analysis.ToReport()}", map);
        }
        private void RefreshAnalysisPanel()
        {
            var panel = rootVisualElement.Q("analysis-panel"); if (panel == null) return;
            panel.style.display = analysis != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (analysis == null) { RefreshReplay(); return; }
            var badge = rootVisualElement.Q<Label>("analysis-badge");
            string kind, text;
            if (analysis.Errors.Count > 0) { kind = "bad"; text = "맵 오류"; }
            else if (analysis.States == 0) { kind = "warn"; text = "검증 대상 아님"; }
            else if (analysis.Solvable) { kind = "ok"; text = "클리어 가능"; }
            else if (analysis.Completed) { kind = "bad"; text = "클리어 불가능"; }
            else { kind = "warn"; text = "결과 불완전"; }
            badge.text = text;
            foreach (var k in new[] { "ok", "warn", "bad" }) badge.EnableInClassList("badge--" + k, k == kind);
            rootVisualElement.Q<Label>("analysis-scope").text = analysis.States == 0 ? "" :
                $"상태 {analysis.States:N0}개 · 깊이 {analysis.MaxDepthReached}행동 · " + (analysis.Completed ? "끝까지 탐색" : analysis.Cancelled ? "취소됨" : "한도에 도달해 중단");
            var metrics = rootVisualElement.Q("analysis-metrics"); metrics.Clear();
            foreach (var error in analysis.Errors) Metric(metrics, "오류", error);
            foreach (var note in analysis.Notes) Metric(metrics, "참고", note);
            if (analysis.States > 0)
            {
                Metric(metrics, "최단 승리", analysis.Solvable ? $"{analysis.ShortestWin}행동" : analysis.Completed ? "없음 — 어떤 순서로도 적을 모두 제거할 수 없음" : "탐색 범위에서 찾지 못함");
                Metric(metrics, "가장 빠른 패배", analysis.EarliestLoss > 0 ? $"{analysis.EarliestLoss}행동" : analysis.Completed ? "없음 — 패배할 수 있는 경우가 없음" : "탐색 범위에서 없음");
                Metric(metrics, "승리 불가 상태", analysis.DeadStates >= 0 ? $"{analysis.DeadStates:N0} / {analysis.States:N0}개 (되돌리기·재시작으로만 복구)" : "탐색이 불완전해 계산하지 않음");
            }
            rootVisualElement.Q<Button>("show-win").SetEnabled(analysis.WinPath != null);
            rootVisualElement.Q<Button>("show-loss").SetEnabled(analysis.LossPath != null);
            RefreshReplay();
        }
        private static void Metric(VisualElement parent, string name, string value)
        {
            var row = new VisualElement(); row.AddToClassList("metric");
            var label = new Label(name); label.AddToClassList("metric-name");
            var text = new Label(value); text.AddToClassList("metric-value"); text.selection.isSelectable = true;
            row.Add(label); row.Add(text); parent.Add(row);
        }
        private void StartReplay(string title, IReadOnlyList<SolverMove> moves)
        {
            if (map == null || analysis == null || moves == null) return;
            EndStroke();
            try { replay = MapReplay.Build(map, title, moves); }
            catch (ArgumentException e) { Message(e.Message); replay = null; }
            if (replay != null && replay.Problem != null) Message(replay.Problem);
            RefreshReplay(); canvas.Focus();
        }
        private void CloseReplay() { replay = null; RefreshReplay(); }
        private void StepReplay(int delta)
        {
            if (replay == null) return;
            long next = delta == int.MinValue ? 0 : delta == int.MaxValue ? replay.Frames.Count - 1 : (long)replay.Index + delta;
            replay.Index = (int)Math.Max(0, Math.Min(replay.Frames.Count - 1, next)); RefreshReplay();
        }
        private const string NormalLegend = "노란 테두리: 시작 위치 · 삼각형: 적과 진행 방향 · 범위: 노란 테두리=인식, 빨간 칸=공격, 빨간 테두리=인식 후 추가 공격 · 자홍: 누락 데이터";
        private void RefreshReplay()
        {
            var panel = rootVisualElement.Q("replay"); if (panel == null) return;
            var legend = rootVisualElement.Q<Label>("legend");
            if (replay == null)
            {
                panel.style.display = DisplayStyle.None;
                if (legend != null) legend.text = NormalLegend;
                canvas?.MarkDirtyRepaint(); return;
            }
            panel.style.display = DisplayStyle.Flex;
            if (legend != null) legend.text = "경로 재생 중 — 흰 원: 플레이어 · 흰 선: 이번 행동의 이동 · 빨간 칸: 이번 행동 뒤 공격 범위 · 흐린 ×: 제거된 적 · ←/→ 단계 이동 · Esc 닫기";
            rootVisualElement.Q<Label>("replay-title").text = $"{replay.Title} ({replay.Frames.Count - 1}행동)";
            var slider = rootVisualElement.Q<SliderInt>("replay-slider");
            slider.lowValue = 0; slider.highValue = Mathf.Max(1, replay.Frames.Count - 1); slider.SetValueWithoutNotify(replay.Index);
            var frame = replay.Current;
            string phase = frame.Phase == BattlePhase.Won ? "승리" : frame.Phase == BattlePhase.Lost ? "패배" : "진행 중";
            rootVisualElement.Q<Label>("replay-step").text = $"{replay.Index} / {replay.Frames.Count - 1} · {frame.Label} · {phase} · 남은 적 {frame.AliveCount} · 플레이어 ({frame.Player.x}, {frame.Player.y})" + (replay.Problem != null ? "\n" + replay.Problem : "");
            var steps = rootVisualElement.Q("replay-steps"); steps.Clear();
            for (int i = 0; i < replay.Frames.Count; i++)
            {
                int index = i; var f = replay.Frames[i];
                var button = new Button(() => { replay.Index = index; RefreshReplay(); }) { text = f.Label };
                button.AddToClassList("replay-step-button");
                button.EnableInClassList("replay-step-button--current", i == replay.Index);
                button.EnableInClassList("replay-step-button--won", f.Phase == BattlePhase.Won);
                button.EnableInClassList("replay-step-button--lost", f.Phase == BattlePhase.Lost);
                steps.Add(button);
            }
            canvas?.MarkDirtyRepaint();
        }

        // ---------- 갱신 ----------
        private void Changed() { EditorUtility.SetDirty(map); ClearAnalysis(); Refresh(); }
        private void Message(string text) { if (status != null) status.text = text; }
        private void Refresh()
        {
            if (canvas == null || status == null) return;
            if (selectedFloor != null && (map == null || !selectedFloor.Walkable || !map.Palette.Contains(selectedFloor))) selectedFloor = null;
            SelectTool(tool); RefreshPalette(); RefreshEnemyPalette(); RefreshEnemySummary(); RefreshFacing();
            var floorSettings = rootVisualElement.Q("floor-settings"); floorSettings.Unbind(); floorSettings.Clear();
            if (selectedFloor != null)
            {
                var floorData = new SerializedObject(selectedFloor);
                floorSettings.Add(new PropertyField(floorData.FindProperty("displayName"), "바닥 이름"));
                floorSettings.Add(new PropertyField(floorData.FindProperty("color"), "표시 색상"));
                floorSettings.Add(new PropertyField(floorData.FindProperty("surfaceMaterial"), "바닥 재질"));
                floorSettings.Bind(floorData);
            }
            canvas.MarkDirtyRepaint();
            var settings = rootVisualElement.Q("map-settings"); settings.Unbind(); settings.Clear();
            if (map != null)
            {
                var serialized = new SerializedObject(map);
                settings.Add(new PropertyField(serialized.FindProperty("groundMaterial"), "배경 바닥 재질"));
                settings.Add(new PropertyField(serialized.FindProperty("groundMargin"), "외곽 여유 (칸)"));
                settings.Add(new PropertyField(serialized.FindProperty("movementColor"), "기본 바닥 색상"));
                settings.Bind(serialized);
                rootVisualElement.Q<IntegerField>("width").SetValueWithoutNotify(map.Width); rootVisualElement.Q<IntegerField>("height").SetValueWithoutNotify(map.Height);
            }
            RefreshSelection(); UpdateStatus();
        }
        private void Chip(string name, string text, string state = null)
        {
            var chip = rootVisualElement.Q<Label>(name); if (chip == null) return;
            chip.text = text; chip.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            chip.EnableInClassList("chip--warn", state == "warn"); chip.EnableInClassList("chip--ok", state == "ok");
        }
        private void UpdateStatus()
        {
            var save = rootVisualElement.Q<Button>("save");
            if (map == null)
            {
                Chip("status-dirty", null); Chip("status-size", null); Chip("status-enemies", null); Chip("status-errors", null); SetHover(null);
                if (save != null) save.text = "저장";
                Message("새 맵을 만들거나 상단에서 맵 에셋을 선택하세요."); return;
            }
            bool dirty = EditorUtility.IsDirty(map) || map.Palette.Any(t => t != null && EditorUtility.IsDirty(t));
            if (save != null) save.text = dirty ? "저장 *" : "저장";
            Chip("status-dirty", dirty ? "● 저장 전 변경" : "저장됨", dirty ? "warn" : null);
            Chip("status-size", $"{map.Width} × {map.Height}칸");
            Chip("status-enemies", $"적 {map.Enemies.Count}{(map.Enemies.Count % 2 != 0 ? " (홀수)" : "")}");
            var errors = map.ValidateMap();
            Chip("status-errors", errors.Count == 0 ? "플레이 준비 완료" : $"확인 필요 {errors.Count}: {errors[0]}", errors.Count == 0 ? "ok" : "warn");
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
