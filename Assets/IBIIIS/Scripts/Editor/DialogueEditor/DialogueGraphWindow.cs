using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor: 어드벤처씬 대화 진행 그래프(기획서 7.6, 개발 가이드의 대화 진행 재설계).
    /// 모든 대본과 대상(NPC·근접 대화 구역·적)을 한 화면에서 보고 대상의 대본 순서·조건을 선으로 잇는다. 그래프는 Overworld Settings의 Dialogue Graph.
    /// 대본 노드를 두 번 누르면(또는 ▸) 노드 안에서 그 대본의 타임라인(대사 편집)과 버전을 편집한다. 대본 에셋을 두 번 누르거나 Inspector 버튼으로도 열린다.</summary>
    public sealed class DialogueGraphWindow : EditorWindow
    {
        [SerializeField] private float zoom = 1;
        [SerializeField] private Vector2 pan;
        [SerializeField] private bool viewSaved;
        // 펼친 대본(창을 다시 열거나 스크립트를 다시 컴파일해도 펼친 채로).
        [SerializeField] private ScenarioScript expandedScript;
        // 창이 처음 그려지기 전에 펼치라고 한 대본(크기가 정해진 뒤 펼치고 그 노드로 화면을 옮긴다).
        private ScenarioScript pendingOpen; private bool pendingSelectStart;
        private DialogueGraphView view;
        private Label status, hint;
        private Button issuesButton;
        private ScrollView issuesList;
        private ToolbarSearchField search;
        private string filter = "all";
        private readonly List<(Button button, string kind)> filterButtons = new List<(Button, string)>();
        private bool showIssues, refreshQueued, visible = true, hiddenChanges;
        private double lastRefresh;
        // 다시 그리기는 최소 이 간격(초)으로 모은다(Scene 뷰에서 끄는 동안 매 프레임 다시 그리지 않게).
        private const double RefreshInterval = .25;
        private Button zoomLabel;
        private const string Hint = "휠: 확대·축소 · 휠 누른 채 끌기·Space+왼쪽 끌기: 화면 이동 · '첫 대본'·'다음'을 '앞 대본'에, '봄'·'클리어'를 조건 슬롯에 끌어 잇기 · 선 고르고 Delete·선 끝 빈 곳에 놓기: 지우기 · Hierarchy 오브젝트 끌어 오기: 대상 추가 · 누르기: Project 창·Hierarchy에서 고르기 · 대본 두 번 누르기·▸: 노드 안에서 펼쳐 편집 · 빈 곳 우클릭: 새 대화 대본";

        [MenuItem("IBIIIS/Dialogue Editor")]
        public static DialogueGraphWindow Open()
        {
            var window = GetWindow<DialogueGraphWindow>();
            window.titleContent = new GUIContent("IBIIIS Dialogue Editor");
            window.Show();
            return window;
        }
        /// <summary>창을 열고 대본 노드를 펼친다. selectStart면 타임라인의 시작 노드(대본 설정)를 고른다.</summary>
        public static DialogueGraphWindow OpenScript(ScenarioScript script, bool selectStart = false)
        {
            var window = Open();
            if (script == null) return window;
            if (window.view != null && window.view.layout.width >= 50) window.ExpandNow(script, selectStart);
            else { window.pendingOpen = script; window.pendingSelectStart = selectStart; }
            return window;
        }
        [OnOpenAsset]
        private static bool OpenAsset(int instanceId, int line)
        {
            // 여는 대상이 바로 그 대본일 때만(그래프에서 대본을 고른 채 콘솔의 .cs 오류 등을 두 번 눌러도 그 파일이 열리게).
            if (!(Selection.activeObject is ScenarioScript script) || script.GetInstanceID() != instanceId) return false;
            OpenScript(script); return true;
        }
        private void ExpandNow(ScenarioScript script, bool selectStart)
        {
            view.Expand(script);
            if (view.Expanded != script) { ShowNotification(new GUIContent($"'{script.name}'을(를) 그래프에서 찾지 못했습니다.")); return; }
            if (selectStart) view.Timeline?.SelectStart();
        }
        /// <summary>게임이 쓰는 대화 그래프(Overworld Settings의 Dialogue Graph). 없으면 null.</summary>
        public static DialogueGraph ProjectGraph() => AssetDatabase.LoadAssetAtPath<OverworldSettings>(AssetPaths.OverworldSettings)?.DialogueGraph;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshSoon; EditorApplication.projectChanged += RefreshSoon; EditorApplication.hierarchyChanged += RefreshSoon;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
        }
        private void OnDisable()
        {
            SaveView();
            Undo.undoRedoPerformed -= RefreshSoon; EditorApplication.projectChanged -= RefreshSoon; EditorApplication.hierarchyChanged -= RefreshSoon;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.update -= RefreshNow; refreshQueued = false;
        }
        // Inspector에서 대화 그래프·대본·씬 대상의 값을 바꿔도 곧 다시 그린다.
        // 펼친 대본 자체의 변경은 건너뛴다(타임라인이 스스로 다시 그림 — 그래프를 다시 그리면 입력 중인 칸이 포커스를 잃는다). 용도·버전 수·재생 방식이 바뀌었을 때만 다시 그린다.
        private void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            string open = view?.Expanded != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(view.Expanded, out string guid, out long _) ? guid : null;
            for (int i = 0; i < stream.length; i++)
            {
                if (open != null && stream.GetEventType(i) == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var change);
                    if (change.guid.ToString() == open && !view.ExpandedShapeChanged) continue;
                }
                RefreshSoon(); return;
            }
        }
        // 곧 한 번 다시 그린다(update는 창이 뒤에 있어도 돈다). 창이 다른 탭 뒤에 가려져 있으면 보일 때 다시 그린다.
        private void RefreshSoon()
        {
            if (view == null) return;
            if (!visible) { hiddenChanges = true; return; }
            if (refreshQueued) return;
            refreshQueued = true; EditorApplication.update += RefreshNow;
        }
        private void OnBecameVisible() { visible = true; if (hiddenChanges) { hiddenChanges = false; RefreshSoon(); } }
        private void OnBecameInvisible() { visible = false; }
        private void RefreshNow()
        {
            if (this == null || view == null) { EditorApplication.update -= RefreshNow; refreshQueued = false; return; }
            if (EditorApplication.timeSinceStartup < lastRefresh + RefreshInterval) return; // 다음 갱신 때 다시
            EditorApplication.update -= RefreshNow; refreshQueued = false; lastRefresh = EditorApplication.timeSinceStartup;
            var graph = ProjectGraph();
            ShowStatus(graph == null ? NoGraph : null);
            view.Load(graph);
            ApplyFilter();
        }
        private const string NoGraph = "대화 그래프가 없습니다. Overworld Settings의 Dialogue Graph 슬롯을 확인하세요(메뉴 IBIIIS > Create Default Dialogue가 만듭니다).";
        private void SaveView() { if (view == null) return; zoom = view.Zoom; pan = view.Pan; viewSaved = true; expandedScript = view.Expanded; }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.focusable = true;
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4 } };
            toolbar.Add(new Button(() => { var s = view.CreateTalk(view.contentViewContainer.WorldToLocal(view.worldBound.center)); if (s != null) view.Select(s); }) { text = "새 대화 대본", tooltip = "대화 대본(용도: 대화)을 만들어 그래프 가운데에 놓습니다." });
            search = new ToolbarSearchField { tooltip = "대본 이름·ID·대사, 대상 ID·이름으로 찾기", style = { width = 170, marginLeft = 6 } };
            search.RegisterValueChangedCallback(_ => ApplyFilter());
            toolbar.Add(search);
            foreach (var (label, kind) in new[] { ("전체", "all"), ("대화", "talk"), ("전투 진입", "battle"), ("대상", "object") })
            {
                var button = new Button { text = label, tooltip = "이 종류만 또렷하게" }; var k = kind;
                button.clicked += () => { filter = k; ApplyFilter(); };
                filterButtons.Add((button, kind)); toolbar.Add(button);
            }
            issuesButton = new Button(() => { showIssues = !showIssues; RefreshIssues(); }) { tooltip = "그래프 검사 결과(대본 순서 고리, 두 대상에 든 대본, 조건 대상, 씬에 없는 대상 등)" };
            toolbar.Add(issuesButton);
            var zoomBar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };
            zoomBar.Add(new Button(() => view.ZoomAt(view.Zoom / DialogueZoom.Step)) { text = "−", tooltip = "축소 (휠 아래)" });
            zoomBar.Add(zoomLabel = new Button(() => view.ZoomAt(1)) { tooltip = "눌러서 100%로", style = { minWidth = 44 } });
            zoomBar.Add(new Button(() => view.ZoomAt(view.Zoom * DialogueZoom.Step)) { text = "+", tooltip = "확대 (휠 위)" });
            zoomBar.Add(new Button(() => view.FrameAll()) { text = "전체 맞춤", tooltip = "전체가 창에 들어오게" });
            toolbar.Add(zoomBar);
            root.Add(toolbar);
            root.Add(hint = new Label(Hint) { style = { color = new Color(.7f, .7f, .7f), unityFontStyleAndWeight = FontStyle.Italic, fontSize = 10, paddingLeft = 8, paddingBottom = 3, whiteSpace = WhiteSpace.Normal } });
            root.Add(status = new Label { style = { color = new Color(1f, .75f, .4f), paddingLeft = 8, paddingBottom = 3, whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } });
            issuesList = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 140, display = DisplayStyle.None, paddingLeft = 8, borderBottomWidth = 1, borderBottomColor = new Color(.35f, .35f, .4f) } };
            root.Add(issuesList);
            view = new DialogueGraphView();
            view.IssuesChanged = RefreshIssues;
            view.ViewChanged = UpdateZoomLabel;
            root.Add(view);
            // Space를 누르고 있는 동안 왼쪽 끌기 = 화면 이동(검색 칸에 입력 중일 때는 제외).
            // 펼친 타임라인 안(대사 입력·끌기)에서는 Space를 화면 이동으로 쓰지 않는다.
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Space || !(e.target is VisualElement target)) return;
                if (target is TextField || target.GetFirstAncestorOfType<TextField>() != null || target.GetFirstAncestorOfType<ToolbarSearchField>() != null) return;
                if (view.Timeline != null && (target == view.Timeline || view.Timeline.Contains(target))) return;
                view.SpaceHeld = true;
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(e => { if (e.keyCode == KeyCode.Space) view.SpaceHeld = false; }, TrickleDown.TrickleDown);
            root.RegisterCallback<FocusOutEvent>(_ => view.SpaceHeld = false);
            var graph = ProjectGraph();
            ShowStatus(graph == null ? NoGraph : null);
            view.Load(graph);
            ApplyFilter();
            // 처음 열면 전체가 들어오게, 다시 열면 보던 위치·배율로.
            EventCallback<GeometryChangedEvent> first = null;
            first = _ =>
            {
                if (float.IsNaN(view.layout.width) || view.layout.width < 50) return;
                view.UnregisterCallback(first);
                if (viewSaved) view.SetView(zoom, pan); else view.FrameAll();
                if (pendingOpen != null) { var s = pendingOpen; pendingOpen = null; ExpandNow(s, pendingSelectStart); }
                else if (expandedScript != null) view.Expand(expandedScript, false);
                UpdateZoomLabel();
            };
            view.RegisterCallback(first);
        }
        private void ShowStatus(string text) { status.text = text ?? ""; status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex; }
        private void ApplyFilter()
        {
            view?.SetFilter(search != null ? search.value : "", filter);
            foreach (var (button, kind) in filterButtons) button.style.backgroundColor = kind == filter ? new Color(.23f, .3f, .4f) : StyleKeyword.Null;
        }
        private void UpdateZoomLabel() { if (zoomLabel != null && view != null) zoomLabel.text = $"{view.Zoom * 100:0}%"; }
        private void RefreshIssues()
        {
            if (view == null || issuesButton == null) return;
            var issues = view.Issues;
            issuesButton.text = issues.Count == 0 ? "검사 ✓" : $"검사 ⚠{issues.Count}";
            issuesList.Clear();
            foreach (var issue in issues) issuesList.Add(new Label("⚠ " + issue) { style = { whiteSpace = WhiteSpace.Normal, color = new Color(1f, .78f, .5f), fontSize = 11, marginTop = 2 } });
            if (issues.Count == 0) issuesList.Add(new Label("문제 없음") { style = { color = new Color(.6f, .8f, .6f) } });
            issuesList.style.display = showIssues ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
