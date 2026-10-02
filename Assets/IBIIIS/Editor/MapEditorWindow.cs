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
        [SerializeField] private TileDefinition selectedTile;
        private MapCanvas canvas;
        private Label status;
        private int undoGroup = -1;
        private string tool = "칠하기";
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
            picker.SetValueWithoutNotify(map); picker.RegisterValueChangedCallback(e => { EndStroke(); map = e.newValue as GridMap; selectedTile = null; Refresh(); });
            var tilePicker = rootVisualElement.Q<ObjectField>("tile"); tilePicker.objectType = typeof(TileDefinition); tilePicker.allowSceneObjects = false;
            status = rootVisualElement.Q<Label>("status");
            canvas = new MapCanvas(this); canvas.AddToClassList("canvas"); rootVisualElement.Q("canvas-host").Add(canvas);
            var tools = rootVisualElement.Q<DropdownField>("tool"); tools.choices = new List<string> { "칠하기", "지우기", "시작 위치" }; tools.SetValueWithoutNotify(tool);
            tools.RegisterValueChangedCallback(e => SelectTool(e.newValue));
            Hook("new", NewMap); Hook("save", Save); Hook("undo", Undo.PerformUndo); Hook("redo", Undo.PerformRedo);
            Hook("environment", () => { if (map != null) { Selection.activeObject = map; EditorGUIUtility.PingObject(map); } });
            Hook("resize", Resize); Hook("reset-view", () => canvas.ResetView());
            Hook("add-tile", () => { var tile = tilePicker.value as TileDefinition; if (map == null || tile == null) return; Undo.RecordObject(map, "Register tile"); map.AddTile(tile); selectedTile = tile; Changed(); });
            Hook("new-tile", NewTile); Hook("edit-tile", () => { if (selectedTile != null) Selection.activeObject = selectedTile; });
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
                map = next; selectedTile = null; rootVisualElement.Q<ObjectField>("map").SetValueWithoutNotify(map); canvas.ResetView(); Refresh();
                status.text = $"맵·씬 생성 완료: {scenePath} · 타일과 시작 위치를 지정하세요.";
            }
            catch (Exception e) { EditorUtility.DisplayDialog("맵·씬 생성 실패", e.Message, "확인"); }
        }
        private void NewTile()
        {
            var path = EditorUtility.SaveFilePanelInProject("새 타일", "NewTile", "asset", "타일 저장 위치");
            if (string.IsNullOrEmpty(path)) return;
            var tile = CreateInstance<TileDefinition>(); tile.Initialize(Guid.NewGuid().ToString("N"), "새 타일", true, new Color(.3f, .6f, .5f));
            AssetDatabase.CreateAsset(tile, AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(tile);
            if (map != null) { Undo.RecordObject(map, "Register tile"); map.AddTile(tile); Changed(); }
            selectedTile = tile; Selection.activeObject = tile; Refresh();
        }
        private void Resize()
        {
            if (map == null) return;
            int w = rootVisualElement.Q<IntegerField>("width").value, h = rootVisualElement.Q<IntegerField>("height").value;
            if (w < 1 || h < 1 || w > GridMap.MaxSize || h > GridMap.MaxSize) { status.text = "가로·세로는 1~64칸이어야 합니다."; return; }
            if ((w < map.Width || h < map.Height) && !EditorUtility.DisplayDialog("맵 크기 축소", "새 범위 밖의 타일과 시작 위치가 제거됩니다. 실행 취소로 복구할 수 있습니다.", "축소", "취소")) return;
            Undo.RecordObject(map, "Resize map"); map.Resize(w, h); Changed();
        }
        private void Save()
        {
            if (map == null) return;
            EndStroke(); AssetDatabase.SaveAssetIfDirty(map); Refresh();
            status.text = "저장 완료. " + status.text;
        }
        private void Changed() { EditorUtility.SetDirty(map); Refresh(); }
        private void Refresh()
        {
            if (canvas == null || status == null) return;
            SelectTool(tool);
            canvas.Map = map; canvas.MarkDirtyRepaint();
            var palette = rootVisualElement.Q<ScrollView>("palette"); palette.Clear();
            if (map == null) { status.text = "새 맵을 만들거나 상단에서 맵 에셋을 선택하세요."; return; }
            rootVisualElement.Q<IntegerField>("width").SetValueWithoutNotify(map.Width); rootVisualElement.Q<IntegerField>("height").SetValueWithoutNotify(map.Height);
            if (selectedTile == null && map.Palette.Count > 0) selectedTile = map.Palette[0];
            foreach (var tile in map.Palette)
            {
                if (tile == null) continue;
                var button = new Button(() => { selectedTile = tile; SelectTool("칠하기"); Refresh(); }) { text = tile.DisplayName + (tile.Walkable ? "  · 이동 가능" : "  · 이동 불가") };
                if (tile == selectedTile) button.AddToClassList("selected-tile"); palette.Add(button);
            }
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            var errors = map.ValidateMap();
            status.text = $"현재 도구: {tool} · " + (EditorUtility.IsDirty(map) ? "저장 전 변경 있음 · " : "") + $"{map.Width} × {map.Height}칸 · " +
                (errors.Count == 0 ? "플레이 준비 완료" : $"확인 필요 {errors.Count}건: {errors[0]}");
        }
        internal void BeginStroke() { EndStroke(); Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Paint map"); if (map != null) Undo.RegisterCompleteObjectUndo(map, "Paint map"); }
        internal void EndStroke() { if (undoGroup >= 0) { Undo.CollapseUndoOperations(undoGroup); undoGroup = -1; } }
        internal void Paint(Vector2Int p, bool inspectOnly)
        {
            if (map == null || !map.Contains(p)) return;
            if (!inspectOnly)
            {
                if (tool == "시작 위치") { if (map.IsWalkable(p)) map.SetStart(p); else { status.text = "시작 위치는 이동 가능한 타일에 지정하세요."; return; } }
                else if (tool == "지우기") map.SetTile(p, null);
                else if (selectedTile != null)
                {
                    bool inPalette = false; foreach (var tile in map.Palette) if (tile == selectedTile) inPalette = true;
                    if (!inPalette) { status.text = "현재 맵의 팔레트에서 타일을 선택하세요."; return; }
                    map.SetTile(p, selectedTile);
                }
                EditorUtility.SetDirty(map); canvas.MarkDirtyRepaint(); UpdateStatus();
            }
            var current = map.GetTile(p);
            rootVisualElement.Q<Label>("selection").text = $"선택 ({p.x}, {p.y})\n" + (current == null ? "빈 칸 또는 누락된 타일" : current.DisplayName + (current.Walkable ? "\n이동 가능" : "\n이동 불가"));
        }
    }
    [CustomEditor(typeof(GridMap))]
    public sealed class GridMapInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement(); root.Add(new Button(() => MapEditorWindow.OpenMap((GridMap)target)) { text = "맵 에디터에서 열기" });
            root.Add(new PropertyField(serializedObject.FindProperty("palette")));
            root.Add(MapEnvironmentEditor.CreateMapControls((GridMap)target)); return root;
        }
    }
}
