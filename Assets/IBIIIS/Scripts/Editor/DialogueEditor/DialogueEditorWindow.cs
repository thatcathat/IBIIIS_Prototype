using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor: 대본(ScenarioScript)의 단계 하나하나를 노드로 왼쪽→오른쪽으로 잇고, 장면마다 줄을 바꿔 묶어 보여 주는 창.
    /// 같은 순간에 일어나는 단계(바로 넘어가는 감정 표시·자세·등장 등과 뒤따르는 대사·입력)는 같은 세로줄에 쌓고, 노드 줄 아래 트랙에
    /// 자세·감정 표시가 보이는 범위를 가로 막대로 표시한다. 노드·막대를 끌어 옮기고, 대사는 노드에서 바로 고치며, 우클릭으로 단계를 추가한다.
    /// 데이터는 대본 에셋 그대로이며, 범위 계산은 진행기와 같은 ScenarioTimeline, 편집 규칙은 DialogueScriptEdits를 쓴다.</summary>
    public sealed class DialogueEditorWindow : EditorWindow
    {
        private const float LineNodeWidth = 220, SmallNodeWidth = 130, Gap = 18, TrackRow = 22, DragThreshold = 5;
        private static readonly Color PoseColor = new Color(.95f, .45f, .65f), EmoteColor = new Color(.35f, .8f, .85f);
        private static readonly Color InputColor = new Color(.95f, .65f, .25f), WorldColor = new Color(.45f, .8f, .45f), FlowColor = new Color(.6f, .6f, .65f);
        private static readonly Color SelectedBorder = new Color(.45f, .7f, 1f), Highlight = new Color(.95f, .8f, .3f, .3f);
        private static readonly Color StartColor = new Color(.55f, .8f, .4f), WarnColor = new Color(.95f, .7f, .3f);
        private static readonly Color ErrorColor = new Color(1f, .4f, .35f), ArrowColor = new Color(.62f, .64f, .72f, .9f);
        // 세로줄 배경: 고른 단계가 있는 세로줄만 푸르게, 끄는 동안 놓을 범위는 노랗게.
        private static readonly Color SelectedColumnColor = new Color(.45f, .7f, 1f, .1f), BandTint = new Color(.95f, .8f, .3f, .18f);

        [SerializeField] private ScenarioScript script;
        // 선택한 단계의 고유 ID(마지막이 기준 단계). 단계를 넣거나 빼도 선택이 따라간다.
        [SerializeField] private List<string> selection = new List<string>();
        private SerializedObject serialized;
        private ObjectField scriptField;
        private Button issuesButton;
        private ScrollView issuesList, graph;
        private Button duplicateButton, connectButton;
        private Label connectionLabel;
        private VisualElement createForm;
        private VisualElement detail;
        private bool rebuildQueued, showIssues;
        // 시작 노드(대본 설정)를 고른 상태. 단계를 고르면 풀린다.
        [SerializeField] private bool startSelected;
        // 이 대본을 쓰는 진행기(열린·닫힌 씬). 씬이 바뀔 때 다시 찾는다.
        private List<DialogueScriptScenes.Entry> linked = new List<DialogueScriptScenes.Entry>();
        private string linkedKey = "";
        // 세로줄 목록(전체 대본 순서, DialogueScriptEdits.Columns와 같은 순서). 끌어 놓을 자리를 찾는 데 쓴다.
        // Band는 세로줄 전체 높이의 배경 띠, Hidden은 접힌 장면의 세로줄(놓을 자리로 고르지 않음).
        private sealed class Column { public int From, To; public bool HasMain, Hidden; public VisualElement Element, Group, Band; public StyleColor Background, BandBackground; }
        private readonly List<Column> columns = new List<Column>();
        // 단계 번호 → 그 단계를 보여 주는 요소(노드·막대·경고). 검사 목록에서 이동할 때 쓴다.
        private readonly Dictionary<int, VisualElement> stepElements = new Dictionary<int, VisualElement>();
        // 복사한 단계(에디터를 켜 둔 동안 대본끼리도 붙여 넣을 수 있음).
        private static List<ScenarioStep> clipboard;
        // 접은 장면(장면을 끝내는 단계의 ID). 창을 다시 열어도 유지된다.
        [SerializeField] private List<string> collapsedScenes = new List<string>();
        // 단계 번호 → 검사 문제(노드·막대에 배지로 보임).
        private readonly Dictionary<int, List<DialogueScriptEdits.Issue>> issuesAt = new Dictionary<int, List<DialogueScriptEdits.Issue>>();
        // 같은 장면 안 노드 사이 연결선. 장면 사이는 선 대신 꼬리표(↓ 다음 장면·↳ 앞 장면에서)와 장면 사이 ▼로 보인다.
        private readonly List<(VisualElement from, VisualElement to)> links = new List<(VisualElement, VisualElement)>();
        private VisualElement linkLayer;

        // ── 끌기 상태 ──
        private enum DragKind { None, SpanStart, SpanEnd, SpanBody, Column, Instant, Draw }
        private DragKind dragKind;
        private int dragIndex = -1, dragColumn = -1;
        private Vector2 dragOrigin;
        private bool dragMoved, dragAdditive, dragRange;
        private VisualElement dragElement;
        private readonly List<int> tinted = new List<int>();
        private Label previewLabel;
        private VisualElement dropMarker, popup;

        [MenuItem("IBIIIS/Dialogue Editor")]
        public static void Open() => Open(Selection.activeObject as ScenarioScript);
        public static DialogueEditorWindow Open(ScenarioScript target)
        {
            var window = GetWindow<DialogueEditorWindow>();
            window.titleContent = new GUIContent("IBIIIS Dialogue Editor");
            if (target != null) window.SetScript(target);
            window.Show();
            return window;
        }
        // 대본 에셋을 더블클릭하면 이 창으로 연다.
        [OnOpenAsset]
        private static bool OpenAsset(int instanceId, int line)
        {
            // 더블클릭한 에셋은 그 순간 선택되어 있으므로 선택으로 판단한다(인스턴스 ID 변환 API는 Unity 6에서 구식).
            if (!(Selection.activeObject is ScenarioScript target)) return false;
            Open(target); return true;
        }

        private void OnEnable() { Undo.undoRedoPerformed += OnUndoRedo; EditorApplication.hierarchyChanged += RefreshConnection; EditorApplication.projectChanged += RefreshConnection; EditorApplication.playModeStateChanged += OnPlayMode; }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndoRedo; EditorApplication.hierarchyChanged -= RefreshConnection; EditorApplication.projectChanged -= RefreshConnection; EditorApplication.playModeStateChanged -= OnPlayMode; }
        private void OnPlayMode(PlayModeStateChange _) => RefreshConnection();
        private void OnUndoRedo() { RefreshConnection(); if (serialized != null && serialized.targetObject != null) { serialized.Update(); RebuildAll(); Save(); } }
        // Dialogue Editor에서 고친 내용은 바로 파일에 저장한다(Ctrl+S를 잊어도 남게). 에셋이 아닌 대본(테스트용 임시)은 저장하지 않는다.
        private void Save() { if (script != null && EditorUtility.IsPersistent(script)) AssetDatabase.SaveAssetIfDirty(script); }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.focusable = true;
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
            scriptField = new ObjectField("대본") { objectType = typeof(ScenarioScript), allowSceneObjects = false, style = { flexGrow = 1, maxWidth = 380 } };
            scriptField.RegisterValueChangedCallback(e => SetScript(e.newValue as ScenarioScript));
            toolbar.Add(scriptField);
            toolbar.Add(new Button(() => ShowCreateForm(false)) { text = "새 대본" });
            toolbar.Add(duplicateButton = new Button(() => ShowCreateForm(true)) { text = "대본 복제" });
            toolbar.Add(connectButton = new Button(ConnectToScene) { text = "씬에 연결" });
            toolbar.Add(connectionLabel = new Label { style = { color = new Color(.65f, .7f, .75f), fontSize = 10, marginRight = 6 } });
            issuesButton = new Button(() => { showIssues = !showIssues; RefreshIssues(); }) { text = "검사" };
            toolbar.Add(issuesButton);
            toolbar.Add(new Label("더블클릭: 대사 고치기 · 우클릭: 추가 · 끌기: 옮기기·범위 · 아래 빈 줄 끌기: 표시 추가 · Ctrl+C/V/D·Delete · Ctrl/Shift+클릭: 여러 개") { style = { color = new Color(.7f, .7f, .7f), unityFontStyleAndWeight = FontStyle.Italic, marginLeft = 8, fontSize = 10 } });
            root.Add(toolbar);
            issuesList = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 140, display = DisplayStyle.None, paddingLeft = 8, borderBottomWidth = 1, borderBottomColor = new Color(.35f, .35f, .4f) } };
            root.Add(issuesList);
            var split = new TwoPaneSplitView(1, 240, TwoPaneSplitViewOrientation.Vertical) { style = { flexGrow = 1 } };
            graph = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1 }, focusable = true };
            detail = new ScrollView(ScrollViewMode.Vertical) { style = { paddingLeft = 8, paddingRight = 8, paddingTop = 6, borderTopWidth = 1, borderTopColor = new Color(.35f, .35f, .4f) } };
            split.Add(graph); split.Add(detail);
            root.Add(split);
            // 복사·붙여넣기·복제·삭제는 에디터 명령으로 온다(글자 입력 중에는 그 칸이 처리).
            root.RegisterCallback<ValidateCommandEvent>(e => { if (HandlesCommand(e.commandName) && !Typing(e.target)) e.StopPropagation(); });
            root.RegisterCallback<ExecuteCommandEvent>(e => { if (HandlesCommand(e.commandName) && !Typing(e.target)) { RunCommand(e.commandName); e.StopPropagation(); } });
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && !Typing(e.target)) { ClosePopup(); selection.Clear(); startSelected = false; RebuildAll(); e.StopPropagation(); } });
            // 표시 추가 창 밖을 누르면 닫는다.
            root.RegisterCallback<PointerDownEvent>(e => { if (popup != null && !(e.target is VisualElement v && (v == popup || popup.Contains(v)))) ClosePopup(); }, TrickleDown.TrickleDown);
            if (script == null) script = Selection.activeObject as ScenarioScript;
            SetScript(script);
        }
        private static bool HandlesCommand(string name) => name == "Copy" || name == "Paste" || name == "Duplicate" || name == "Delete" || name == "SoftDelete";
        // 글자 입력 칸(노드 안 대사 편집·아래 편집 칸) 안에서는 단축키를 그 칸에 맡긴다.
        private static bool Typing(IEventHandler target) => target is VisualElement v && (v is TextField || v.GetFirstAncestorOfType<TextField>() != null || v is IMGUIContainer || v.GetFirstAncestorOfType<IMGUIContainer>() != null);
        private void RunCommand(string name)
        {
            if (script == null) return;
            switch (name)
            {
                case "Copy": Copy(); break;
                case "Paste": Paste(); break;
                case "Duplicate": Copy(); Paste(); break;
                default: DeleteSelected(); break;
            }
        }
        /// <summary>시작 노드를 골라 아래 칸에 대본 설정과 연결된 씬을 보인다(진행기 Inspector의 버튼).</summary>
        public void SelectStart() { selection.Clear(); startSelected = true; RebuildAll(); }
        public void SetScript(ScenarioScript value)
        {
            if (value != script) { selection.Clear(); startSelected = false; }
            script = value;
            // 단계 ID가 없는 예전 대본이면 채워 저장한다(선택·복사가 ID를 씀).
            if (script != null && script.EnsureStepIds()) { EditorUtility.SetDirty(script); Save(); }
            serialized = script != null ? new SerializedObject(script) : null;
            if (scriptField != null) scriptField.SetValueWithoutNotify(script);
            duplicateButton?.SetEnabled(script != null);
            RefreshConnection();
            if (rootVisualElement != null && serialized != null)
            {
                rootVisualElement.Unbind();
                // 다른 곳(Inspector·아래 편집 칸)에서 대본이 바뀌면 노드 화면을 다시 그린다(편집 칸은 유지).
                rootVisualElement.TrackSerializedObjectValue(serialized, _ => QueueRebuild());
            }
            RebuildAll();
        }
        private void QueueRebuild()
        {
            if (rebuildQueued) return;
            rebuildQueued = true;
            EditorApplication.delayCall += () => { rebuildQueued = false; if (this != null && dragKind == DragKind.None) { RebuildGraph(); Save(); } };
        }
        private void RebuildAll() { RebuildGraph(); RebuildDetail(); }

        // ── 선택 ──
        private int IndexOfId(string id)
        {
            if (script == null || string.IsNullOrEmpty(id)) return -1;
            var steps = script.Steps; for (int i = 0; i < steps.Length; i++) if (steps[i].id == id) return i;
            return -1;
        }
        private int Primary => selection.Count > 0 ? IndexOfId(selection[selection.Count - 1]) : -1;
        private bool IsSelected(int index) => script != null && index >= 0 && index < script.Steps.Length && selection.Contains(script.Steps[index].id);
        private List<int> SelectedIndices() => selection.Select(IndexOfId).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
        // 클릭: 하나만 선택. Ctrl(⌘)+클릭: 더하기·빼기. Shift+클릭: 기준 단계부터 범위.
        private void SelectIndex(int index, bool additive = false, bool range = false)
        {
            startSelected = false;
            var steps = script.Steps;
            if (index < 0 || index >= steps.Length) selection.Clear();
            else if (range && Primary >= 0)
            {
                int a = Mathf.Min(Primary, index), b = Mathf.Max(Primary, index);
                for (int i = a; i <= b; i++) if (!selection.Contains(steps[i].id)) selection.Add(steps[i].id);
                selection.Remove(steps[index].id); selection.Add(steps[index].id);
            }
            else if (additive) { if (!selection.Remove(steps[index].id)) selection.Add(steps[index].id); }
            else { selection.Clear(); selection.Add(steps[index].id); }
            graph?.Focus();
            RebuildAll();
        }
        /// <summary>새 단계 목록을 대본에 쓰고(되돌리기 가능·바로 저장) 지정한 단계를 고른다. edited가 null이면 다시 그리기만 한다.</summary>
        private void Commit(ScenarioStep[] edited, string undoName, params int[] select)
        {
            if (edited == null || script == null) { RebuildGraph(); return; }
            serialized?.ApplyModifiedProperties();
            Undo.RecordObject(script, undoName);
            script.Set(script.Id, edited); EditorUtility.SetDirty(script);
            serialized?.Update();
            selection = select.Where(i => i >= 0 && i < script.Steps.Length).Select(i => script.Steps[i].id).ToList();
            RebuildAll(); Save();
        }

        // ── 노드 화면 ──
        // 장면마다 한 줄: 세로줄마다 위에 바로 넘어가는 단계, 아래에 기다리는 단계. 줄 아래 트랙에 자세·감정 표시가 보이는 범위를 가로 막대로 그린다.
        private void RebuildGraph()
        {
            if (graph == null) return;
            var scroll = graph.scrollOffset;
            graph.Clear(); columns.Clear(); stepElements.Clear(); links.Clear(); linkLayer = null;
            RefreshIssues();
            if (script == null) { graph.Add(new Label("대본(Scenario Script)을 고르세요. Project 창에서 대본 에셋을 더블클릭해도 열립니다.") { style = { marginLeft = 10, marginTop = 10 } }); return; }
            graph.Add(StartNode());
            var steps = script.Steps;
            if (steps.Length == 0) { graph.Add(new Button(() => Commit(new[] { NewLine(null) }, "대본 단계 추가", 0)) { text = "첫 대사 추가", style = { marginLeft = 10, marginTop = 10, alignSelf = Align.FlexStart } }); return; }
            var spans = new Dictionary<int, ScenarioSpan>(); var lanes = AssignLanes(steps, spans);
            int primary = Primary;
            var selectedSpan = primary >= 0 && spans.TryGetValue(primary, out var s) ? s : (ScenarioSpan?)null;
            var column = new VisualElement { style = { paddingLeft = 8, paddingTop = 8, paddingBottom = 8, alignItems = Align.FlexStart } };
            var scenes = ScenarioTimeline.Scenes(steps);
            for (int n = 0; n < scenes.Count; n++)
            {
                if (n > 0) column.Add(new Label("▼") { pickingMode = PickingMode.Ignore, style = { fontSize = 9, color = new Color(.5f, .52f, .58f), marginLeft = 22, height = 12, unityTextAlign = TextAnchor.MiddleLeft } });
                column.Add(Scene(n, scenes[n], steps, spans, lanes, selectedSpan, n < scenes.Count - 1));
            }
            // 연결선은 맨 위 층에 그린다(누르기는 아래 노드로 통과).
            linkLayer = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 } };
            linkLayer.generateVisualContent += DrawLinks;
            column.Add(linkLayer);
            column.RegisterCallback<GeometryChangedEvent>(_ => linkLayer?.MarkDirtyRepaint());
            graph.Add(column);
            graph.schedule.Execute(() => graph.scrollOffset = scroll);
        }
        // 범위 막대마다 겹치지 않는 트랙 줄(레인)을 정한다.
        private static Dictionary<int, int> AssignLanes(ScenarioStep[] steps, Dictionary<int, ScenarioSpan> spans)
        {
            var lanes = new Dictionary<int, int>(); var laneEnds = new List<int>();
            for (int i = 0; i < steps.Length; i++)
            {
                var span = ScenarioTimeline.SpanOf(steps, i); if (span == null) continue;
                spans[i] = span.Value;
                int lane = laneEnds.FindIndex(end => end < i);
                if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(0); }
                laneEnds[lane] = span.Value.End; lanes[i] = lane;
            }
            return lanes;
        }
        // 노드 대신 트랙 막대로 보이는 단계.
        private static bool IsTrackStep(ScenarioStepType type) => type == ScenarioStepType.Emote || type == ScenarioStepType.Pose || type == ScenarioStepType.EndDisplay;
        private static float NodeWidth(ScenarioStep step) => step.type == ScenarioStepType.Line ? LineNodeWidth : SmallNodeWidth;
        private VisualElement Scene(int number, (int start, int end) scene, ScenarioStep[] steps, Dictionary<int, ScenarioSpan> spans, Dictionary<int, int> lanes, ScenarioSpan? selectedSpan, bool hasNext)
        {
            var group = new VisualElement { style = { marginBottom = 2, paddingLeft = 8, paddingRight = 8, paddingTop = 4, paddingBottom = 8, backgroundColor = new Color(1, 1, 1, .03f), borderTopLeftRadius = 8, borderTopRightRadius = 8, borderBottomLeftRadius = 8, borderBottomRightRadius = 8 } };
            SetBorder(group, new Color(.4f, .42f, .48f), 1);
            var last = steps[scene.end];
            bool collapsed = collapsedScenes.Contains(last.id);
            var header = SceneHeader(number, scene, steps, last, collapsed);
            group.Add(header);
            // 본문: 순간 번호 눈금 → 위 줄(바로 넘어가는 단계) → 아래 줄(기다리는 단계) → 트랙. 세로줄 배경 띠는 본문 뒤에 깐다.
            var body = new VisualElement { style = { position = Position.Relative } };
            var bands = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 } };
            body.Add(bands);
            // 세로줄 = 같은 순간. 감정 표시·자세·표시 끝은 노드 없이 아래 트랙 막대로만 보인다(막대 시작 = 그 단계의 세로줄).
            // 위 줄에는 막대가 없는 바로 넘어가는 단계(켜기·끄기·적 합류·클리어 기록)를 실행 순서대로 쌓아 아래에 붙이고,
            // 아래 줄에는 그 순간의 기다리는 단계(대사·입력 등)를 위에 붙여 나란히 둔다. 모든 줄이 같은 폭·간격을 써서 세로줄이 맞는다.
            var ruler = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexEnd } };
            var bottom = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Stretch } };
            var columnOf = new Dictionary<int, int>(); var left = new List<float>(); var width = new List<float>(); float x = 0;
            var moments = ScenarioTimeline.Moments(steps, scene.start, scene.end);
            int primary = Primary; VisualElement previousMain = null;
            for (int m = 0; m < moments.Count; m++)
            {
                var (from, to) = moments[m];
                bool hasMain = !ScenarioTimeline.IsInstant(steps[to].type);
                float w = hasMain ? NodeWidth(steps[to]) : SmallNodeWidth;
                int columnIndex = columns.Count;
                if (m > 0)
                {
                    foreach (var row in new[] { ruler, top, bottom }) row.Add(new VisualElement { style = { width = Gap, flexShrink = 0 } });
                    x += Gap;
                }
                bool chosenColumn = primary >= from && primary <= to;
                ruler.Add(new Label($"{columnIndex + 1}") { tooltip = $"{columnIndex + 1}번째 순간 ({from}~{to}번 단계)", style = { width = w, flexShrink = 0, fontSize = 9, unityTextAlign = TextAnchor.MiddleCenter, color = chosenColumn ? SelectedBorder : new Color(.5f, .52f, .58f), unityFontStyleAndWeight = chosenColumn ? FontStyle.Bold : FontStyle.Normal } });
                var stack = new VisualElement { style = { width = w, flexShrink = 0 } };
                for (int i = from; i <= (hasMain ? to - 1 : to); i++)
                {
                    columnOf[i] = m;
                    if (!IsTrackStep(steps[i].type)) stack.Add(InstantNode(i, steps[i], spans, selectedSpan, columnIndex));
                }
                top.Add(stack);
                var main = hasMain ? Node(to, steps[to], spans, selectedSpan, columnIndex) : new VisualElement { style = { width = w, flexShrink = 0, minHeight = 30 } };
                if (hasMain) columnOf[to] = m;
                bottom.Add(main);
                stack.AddManipulator(new ContextualMenuManipulator(e => ColumnMenu(e, columnIndex)));
                main.AddManipulator(new ContextualMenuManipulator(e => ColumnMenu(e, columnIndex)));
                var bandColor = chosenColumn ? SelectedColumnColor : new Color(0, 0, 0, 0);
                var band = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = x - 3, width = w + 6, top = 0, bottom = 0, backgroundColor = bandColor, borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4 } };
                bands.Add(band);
                columns.Add(new Column { From = from, To = to, HasMain = hasMain, Hidden = collapsed, Element = main, Group = group, Band = band, Background = main.style.backgroundColor, BandBackground = bandColor });
                if (!collapsed && previousMain != null) links.Add((previousMain, main));
                previousMain = main;
                left.Add(x); width.Add(w); x += w;
            }
            // 마지막 노드 오른쪽: 다음 장면으로 이어짐(아래 줄).
            if (hasNext) bottom.Add(new Label($"↓ 장면 {number + 2}") { tooltip = "다음 장면(아래 줄)으로 이어짐", style = { alignSelf = Align.Center, marginLeft = 8, fontSize = 10, color = new Color(.7f, .72f, .8f), paddingLeft = 6, paddingRight = 6, backgroundColor = new Color(1, 1, 1, .05f), borderTopLeftRadius = 7, borderTopRightRadius = 7, borderBottomLeftRadius = 7, borderBottomRightRadius = 7, flexShrink = 0 } });
            body.Add(ruler); body.Add(top); body.Add(bottom);
            // 트랙: 이 장면과 겹치는 범위를 세로줄 기준으로 그린다. 앞 장면에서 이어지면 «, 다음 장면으로 이어지면 »를 붙인다.
            int laneTop = -1;
            foreach (var pair in spans) if (pair.Value.Start <= scene.end && pair.Value.End >= scene.start) laneTop = Mathf.Max(laneTop, lanes[pair.Key]);
            // 범위가 없는 자세(그림이 비어 진행기가 건너뜀)와 끝낼 표시가 없는 표시 끝 단계는 막대 대신 빨간 경고를 막대 아래 줄에 띄운다.
            var markers = new HashSet<int>(spans.Values.Select(v => v.Marker));
            var missing = new List<int>();
            for (int i = scene.start; i <= scene.end; i++) if (IsTrackStep(steps[i].type) && !spans.ContainsKey(i) && !markers.Contains(i)) missing.Add(i);
            int rows = laneTop + 1 + missing.Count;
            var track = new VisualElement { style = { height = (rows + 1) * TrackRow, width = x, marginTop = 6 } };
            for (int k = 0; k < missing.Count; k++)
            {
                int i = missing[k], column = columnOf[i];
                var warning = new Label($"⚠ {i}번 {TypeName(steps[i].type)}: {(steps[i].type == ScenarioStepType.EndDisplay ? "끝낼 표시가 없음" : "그림이 없어 건너뜀")}") { style = { position = Position.Absolute, left = left[column], top = (laneTop + 1 + k) * TrackRow, minWidth = width[column], height = TrackRow - 4, backgroundColor = new Color(.75f, .2f, .2f, IsSelected(i) ? 1 : .85f), color = Color.white, fontSize = 10, paddingLeft = 6, paddingRight = 6, unityTextAlign = TextAnchor.MiddleLeft, whiteSpace = WhiteSpace.NoWrap, borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4 } };
                Draggable(warning, DragKind.Instant, i, ColumnStart(scene, columnOf, i));
                track.Add(warning); stepElements[i] = warning;
            }
            foreach (var pair in spans)
            {
                var span = pair.Value;
                if (span.Start > scene.end || span.End < scene.start) continue;
                int from = columnOf[Mathf.Max(span.Start, scene.start)], to = columnOf[Mathf.Min(span.End, scene.end)];
                var bar = Bar(pair.Key, steps[pair.Key], span, left[from], left[to] + width[to] - left[from], lanes[pair.Key], span.Start < scene.start, span.End > scene.end);
                track.Add(bar);
                if (span.Start >= scene.start) stepElements[pair.Key] = bar;
            }
            // 맨 아래 빈 줄: 끌어서 그 범위에 감정 표시·자세를 새로 만든다.
            var draw = new Label("＋ 여기를 끌어서 감정 표시·자세 추가") { style = { position = Position.Absolute, left = 0, top = rows * TrackRow, width = x, height = TrackRow - 4, color = new Color(.6f, .6f, .65f), fontSize = 10, paddingLeft = 6, unityTextAlign = TextAnchor.MiddleLeft, backgroundColor = new Color(1, 1, 1, .025f), borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4 } };
            SetBorder(draw, new Color(1, 1, 1, .08f), 1);
            Draggable(draw, DragKind.Draw, -1, -1);
            track.Add(draw);
            body.Add(track);
            // 접은 장면은 세로줄 계산(끌어 놓을 자리 번호)을 위해 그대로 만들되 보이지 않게 한다.
            if (collapsed) body.style.display = DisplayStyle.None;
            group.Add(body);
            return group;
        }
        // 장면 머리: 접기 단추, 장면 번호, 장면을 끝내는 방식(색 꼬리표), 단계 범위·순간 수, 이 장면의 검사 문제 수.
        private VisualElement SceneHeader(int number, (int start, int end) scene, ScenarioStep[] steps, ScenarioStep last, bool collapsed)
        {
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4, minHeight = 20 } };
            string key = last.id;
            var fold = new Label(collapsed ? "▶" : "▼") { tooltip = collapsed ? "장면 펼치기" : "장면 접기", style = { width = 16, unityTextAlign = TextAnchor.MiddleCenter, color = new Color(.75f, .77f, .82f), fontSize = 10 } };
            header.Add(fold);
            header.Add(new Label($"장면 {number + 1}") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(.85f, .87f, .92f), marginRight = 6 } });
            if (number > 0) header.Add(new Label($"↳ 장면 {number}에서") { style = { fontSize = 10, color = new Color(.6f, .62f, .68f), marginRight = 6 } });
            bool ends = ScenarioTimeline.EndsScene(last.type);
            var endColor = ends ? CategoryColor(last.type) : FlowColor;
            header.Add(Pill(ends ? (last.type == ScenarioStepType.LoadScene ? "씬 전환" : TypeName(last.type)) : "대본 끝", endColor));
            if (ends) header.Add(new Label(Summary(last)) { style = { fontSize = 10, color = new Color(.7f, .72f, .78f), marginLeft = 6, marginRight = 8 } });
            int moments = ScenarioTimeline.Moments(steps, scene.start, scene.end).Count;
            header.Add(new Label($"{scene.start}~{scene.end}번 · 순간 {moments}") { style = { fontSize = 10, color = new Color(.55f, .57f, .62f), marginLeft = 4 } });
            int errors = 0, warnings = 0;
            for (int i = scene.start; i <= scene.end; i++) if (issuesAt.TryGetValue(i, out var list)) foreach (var issue in list) { if (issue.Error) errors++; else warnings++; }
            if (errors > 0) header.Add(Pill($"⚠ 오류 {errors}", ErrorColor));
            if (warnings > 0) header.Add(Pill($"주의 {warnings}", WarnColor));
            if (collapsed)
            {
                int lines = 0; for (int i = scene.start; i <= scene.end; i++) if (steps[i].type == ScenarioStepType.Line) lines++;
                header.Add(new Label($"접힘 — 대사 {lines}개") { style = { fontSize = 10, color = new Color(.6f, .62f, .68f), marginLeft = 8, unityFontStyleAndWeight = FontStyle.Italic } });
            }
            header.tooltip = collapsed ? "눌러서 펼치기" : "눌러서 접기";
            header.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (!collapsedScenes.Remove(key)) collapsedScenes.Add(key);
                RebuildGraph(); e.StopPropagation();
            });
            return header;
        }
        private static Label Pill(string text, Color color) => new Label(text)
        {
            style = { fontSize = 10, color = new Color(.1f, .1f, .12f), backgroundColor = new Color(color.r, color.g, color.b, .85f), paddingLeft = 6, paddingRight = 6, marginLeft = 4, borderTopLeftRadius = 7, borderTopRightRadius = 7, borderBottomLeftRadius = 7, borderBottomRightRadius = 7 }
        };
        // 연결선: 같은 장면 안에서 노드 오른쪽 가운데 → 다음 노드 왼쪽 가운데로 부드러운 곡선과 화살촉.
        private void DrawLinks(MeshGenerationContext context)
        {
            if (linkLayer == null) return;
            var painter = context.painter2D;
            painter.lineWidth = 1.5f; painter.strokeColor = ArrowColor; painter.fillColor = ArrowColor;
            Rect Local(VisualElement element) { var b = element.worldBound; var min = linkLayer.WorldToLocal(b.min); var max = linkLayer.WorldToLocal(b.max); return Rect.MinMaxRect(min.x, min.y, max.x, max.y); }
            foreach (var (fromElement, toElement) in links)
            {
                if (fromElement.panel == null || toElement.panel == null) continue;
                var a = Local(fromElement); var b = Local(toElement);
                if (float.IsNaN(a.x) || float.IsNaN(b.x) || a.width <= 0 || b.width <= 0) continue;
                var start = new Vector2(a.xMax + 1, a.center.y); var end = new Vector2(b.xMin - 2, b.center.y);
                float bend = Mathf.Max(4, (end.x - start.x) * .5f);
                painter.BeginPath(); painter.MoveTo(start);
                painter.BezierCurveTo(start + new Vector2(bend, 0), end - new Vector2(bend, 0), end);
                painter.Stroke();
                painter.BeginPath(); painter.MoveTo(end + new Vector2(1, 0)); painter.LineTo(end + new Vector2(-5, -3.5f)); painter.LineTo(end + new Vector2(-5, 3.5f)); painter.ClosePath(); painter.Fill();
            }
        }
        // 맨 앞 시작 노드: 대본 설정(시작 시점·한 번만·건너뛰기·연출)과 연결된 씬 수·대상 ID 경고를 보인다. 누르면 아래에서 편집한다.
        // 대본마다 하나이고 단계가 아니므로 옮기거나 지우거나 복사하지 않는다.
        private VisualElement StartNode()
        {
            var o = script.Options;
            var node = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, alignSelf = Align.FlexStart, marginLeft = 16, marginTop = 8, paddingLeft = 8, paddingRight = 8, paddingTop = 4, paddingBottom = 4, backgroundColor = startSelected ? new Color(.25f, .33f, .45f) : new Color(.19f, .2f, .23f), borderTopLeftRadius = 12, borderTopRightRadius = 12, borderBottomLeftRadius = 12, borderBottomRightRadius = 12 } };
            SetBorder(node, startSelected ? SelectedBorder : new Color(StartColor.r, StartColor.g, StartColor.b, .6f), startSelected ? 2 : 1);
            node.style.borderLeftWidth = 4; node.style.borderLeftColor = StartColor;
            node.Add(new Label("▶ 시작") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = StartColor, marginRight = 8 } });
            node.Add(new Label(o.startTiming == ScenarioStartTiming.BattleWon ? "클리어 시" : "전투 준비 시") { style = { color = new Color(.9f, .9f, .92f), marginRight = 6 } });
            void Badge(string text, Color color) => node.Add(new Label(text) { style = { fontSize = 10, color = color, marginLeft = 4, paddingLeft = 5, paddingRight = 5, backgroundColor = new Color(1, 1, 1, .06f), borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6 } });
            var plain = new Color(.75f, .77f, .82f);
            if (o.onlyOnce) Badge("한 번만", plain);
            Badge(o.allowSkip ? $"건너뛰기 {o.SkipHold:0.##}초" : "건너뛰기 끔", plain);
            if (o.hideEnemyAlerts) Badge("적 말풍선 숨김", plain);
            if (o.scriptControlsDanger) Badge("위험 그림 대본이", plain);
            Badge(linked.Count == 0 ? "연결된 씬 없음" : $"연결 씬 {linked.Count}", linked.Count == 0 ? WarnColor : plain);
            int missing = DialogueScriptScenes.ActorIssues(script.Steps, linked).Count;
            if (missing > 0) Badge($"⚠ 없는 대상 ID {missing}", WarnColor);
            node.tooltip = "누르면 아래에서 대본 설정(시작 시점·한 번만·건너뛰기·전투 연출)과 이 대본을 쓰는 씬을 봅니다.\n대본마다 하나이며 옮기거나 지울 수 없습니다.";
            node.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; selection.Clear(); startSelected = true; graph?.Focus(); RebuildAll(); e.StopPropagation(); });
            return node;
        }
        private int ColumnStart((int start, int end) scene, Dictionary<int, int> columnOf, int index) => columns.Count - (columnOf.Values.Max() + 1) + columnOf[index];
        // 바로 넘어가는 단계: 한 줄짜리 작은 노드(왼쪽 색 띠 = 종류). 끌어서 다른 세로줄로 옮긴다.
        private VisualElement InstantNode(int index, ScenarioStep step, Dictionary<int, ScenarioSpan> spans, ScenarioSpan? selectedSpan, int column)
        {
            var color = CategoryColor(step.type);
            bool chosen = IsSelected(index);
            var node = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 3, paddingLeft = 5, paddingRight = 5, paddingTop = 2, paddingBottom = 2, backgroundColor = chosen ? new Color(.25f, .33f, .45f) : selectedSpan != null && selectedSpan.Value.Covers(index) ? new Color(.27f, .28f, .33f) : new Color(.19f, .2f, .23f), borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4 } };
            SetBorder(node, chosen ? SelectedBorder : new Color(color.r, color.g, color.b, .5f), 1);
            node.style.borderLeftWidth = 4; node.style.borderLeftColor = color;
            node.Add(new Label($"{TypeName(step.type)} · {Detail(step)}") { style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1, fontSize = 10, color = new Color(.88f, .88f, .9f) } });
            var badge = IssueBadge(index, node, chosen);
            if (badge != null) node.Add(badge);
            node.Add(new Label($"{index}") { style = { fontSize = 9, color = new Color(.5f, .5f, .55f), marginLeft = 4, flexShrink = 0 } });
            node.tooltip = Summary(step) + (spans.TryGetValue(index, out var own) ? $"\n{EndText(own)}" : "") + (step.sprite != null ? $"\n그림: {step.sprite.name}" : "") + IssueText(index) + "\n끌어서 다른 세로줄로 옮기기";
            Draggable(node, DragKind.Instant, index, column);
            stepElements[index] = node;
            return node;
        }
        // 기다리는 단계 노드. 끌면 그 세로줄 전체(위에 쌓인 단계 포함)를 옮기고, 대사는 더블클릭해 바로 고친다.
        private VisualElement Node(int index, ScenarioStep step, Dictionary<int, ScenarioSpan> spans, ScenarioSpan? selectedSpan, int column)
        {
            bool line = step.type == ScenarioStepType.Line;
            var color = line ? new Color(.55f, .57f, .62f) : CategoryColor(step.type);
            var node = new VisualElement { style = { width = NodeWidth(step), flexShrink = 0, backgroundColor = new Color(.19f, .2f, .23f), paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5 } };
            bool chosen = IsSelected(index);
            SetBorder(node, chosen ? SelectedBorder : new Color(color.r, color.g, color.b, .6f), chosen ? 2 : 1);
            if (!chosen && selectedSpan != null && selectedSpan.Value.Covers(index)) node.style.backgroundColor = new Color(.27f, .28f, .33f);
            node.style.borderTopWidth = 3; // 위쪽 굵은 띠로 종류 색을 보인다
            node.style.borderTopColor = color;
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
            head.Add(new Label(line ? (step.line.speaker != null || !string.IsNullOrEmpty(step.line.nameOverride) ? "대사" : "지문") : TypeName(step.type)) { style = { fontSize = 10, color = color } });
            var right = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var badge = IssueBadge(index, node, chosen);
            if (badge != null) right.Add(badge);
            right.Add(new Label($"{index}") { style = { fontSize = 10, color = new Color(.5f, .5f, .55f), marginLeft = 4 } });
            head.Add(right);
            node.Add(head);
            if (line) LineContent(node, step.line);
            else
            {
                string text = Detail(step);
                if (spans.TryGetValue(index, out var own)) text += $"\n({EndText(own)})";
                node.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal, color = new Color(.85f, .85f, .88f), fontSize = 11 } });
                node.tooltip = Summary(step) + (step.sprite != null ? $"\n그림: {step.sprite.name}" : "");
            }
            node.tooltip += IssueText(index);
            Draggable(node, DragKind.Column, index, column, line ? (System.Action)(() => BeginInlineEdit(index)) : null);
            stepElements[index] = node;
            return node;
        }
        // 검사 문제가 있는 단계: 테두리를 빨강(오류)·노랑(주의)으로 바꾸고 ⚠ 배지를 돌려준다(없으면 null).
        private Label IssueBadge(int index, VisualElement node, bool chosen)
        {
            if (!issuesAt.TryGetValue(index, out var list) || list.Count == 0) return null;
            bool error = list.Any(i => i.Error); var color = error ? ErrorColor : WarnColor;
            if (!chosen) { node.style.borderRightColor = node.style.borderBottomColor = color; node.style.borderRightWidth = node.style.borderBottomWidth = 2; } // 왼쪽·위 띠는 종류 색
            return new Label(list.Count > 1 ? $"⚠{list.Count}" : "⚠") { tooltip = string.Join("\n", list.Select(i => (i.Error ? "오류: " : "주의: ") + i.Message)), style = { fontSize = 10, color = new Color(.1f, .1f, .12f), backgroundColor = color, paddingLeft = 3, paddingRight = 3, borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6, flexShrink = 0, marginLeft = 4 } };
        }
        private string IssueText(int index) => issuesAt.TryGetValue(index, out var list) ? "\n" + string.Join("\n", list.Select(i => "⚠ " + (i.Error ? "오류: " : "주의: ") + i.Message)) : "";
        // 대사는 화자 초상화·이름(인물 이름 색)과 본문 세 줄까지만 보인다(전체는 마우스를 올리거나 더블클릭해 고칠 때).
        private static void LineContent(VisualElement node, DialogueLine line)
        {
            string name = !string.IsNullOrEmpty(line.nameOverride) ? line.nameOverride : line.speaker != null ? line.speaker.DisplayName : null;
            if (name != null)
            {
                var who = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
                var portrait = line.speaker != null ? line.speaker.Portrait(line.expression) : null;
                if (portrait != null)
                    who.Add(new Image { sprite = portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, style = { width = 24, height = 24, marginRight = 5, backgroundColor = new Color(0, 0, 0, .25f), borderTopLeftRadius = 12, borderTopRightRadius = 12, borderBottomLeftRadius = 12, borderBottomRightRadius = 12, flexShrink = 0 } });
                who.Add(new Label(name) { style = { unityFontStyleAndWeight = FontStyle.Bold, color = line.speaker != null ? line.speaker.NameColor : Color.white } });
                if (!string.IsNullOrEmpty(line.expression)) who.Add(new Label(line.expression) { style = { fontSize = 9, color = new Color(.6f, .62f, .68f), marginLeft = 4 } });
                node.Add(who);
            }
            var text = new Label(string.IsNullOrEmpty(line.text) ? "(비어 있음 — 더블클릭해 입력)" : line.text) { enableRichText = false, style = { whiteSpace = WhiteSpace.Normal, maxHeight = 46, overflow = Overflow.Hidden } };
            if (name == null || string.IsNullOrEmpty(line.text)) { text.style.unityFontStyleAndWeight = FontStyle.Italic; text.style.color = new Color(.75f, .75f, .78f); }
            node.Add(text); node.tooltip = line.text + "\n더블클릭: 바로 고치기 · 끌기: 세로줄 옮기기";
        }
        private VisualElement Bar(int owner, ScenarioStep step, ScenarioSpan span, float x, float width, int lane, bool continuesBefore, bool continuesAfter)
        {
            var color = step.type == ScenarioStepType.Pose ? PoseColor : EmoteColor;
            bool chosen = IsSelected(owner);
            string name = step.type == ScenarioStepType.Pose ? $"자세 {(step.sprite != null ? step.sprite.name : "")}" : $"{step.target} {(step.sprite != null ? step.sprite.name : step.text)}";
            string label = (continuesBefore ? "« " : "") + name + (span.EndKind == ScenarioSpanEnd.Seconds ? $" ⏱{step.seconds:0.##}초" : "") + (continuesAfter ? "  »" : "");
            var bar = new Label(label)
            {
                tooltip = $"{Summary(step)}\n{span.Start}번부터 {span.End}번까지 — {EndText(span)}\n가운데를 끌면 통째로, 양 끝을 끌면 시작·끝만 옮김",
                style =
                {
                    position = Position.Absolute, left = x, top = lane * TrackRow, width = width, height = TrackRow - 4,
                    backgroundColor = new Color(color.r, color.g, color.b, chosen ? 1 : .75f), color = new Color(.1f, .1f, .12f), fontSize = 10,
                    paddingLeft = 10, paddingRight = 10, unityTextAlign = TextAnchor.MiddleLeft, overflow = Overflow.Hidden, whiteSpace = WhiteSpace.NoWrap,
                    borderTopLeftRadius = continuesBefore ? 0 : 4, borderBottomLeftRadius = continuesBefore ? 0 : 4, borderTopRightRadius = continuesAfter ? 0 : 4, borderBottomRightRadius = continuesAfter ? 0 : 4,
                    unityFontStyleAndWeight = chosen ? FontStyle.Bold : FontStyle.Normal,
                }
            };
            if (chosen) SetBorder(bar, SelectedBorder, 1);
            // 자세 그림·감정 표시 아이콘을 막대 앞에 작게 보인다.
            if (step.sprite != null)
            {
                bar.Add(new Image { sprite = step.sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = continuesBefore ? 3 : 10, top = 1, width = TrackRow - 6, height = TrackRow - 6 } });
                bar.style.paddingLeft = (continuesBefore ? 3 : 10) + TrackRow - 4;
            }
            if (issuesAt.TryGetValue(owner, out var problems) && problems.Count > 0)
            {
                SetBorder(bar, problems.Any(i => i.Error) ? ErrorColor : WarnColor, 2);
                bar.tooltip += IssueText(owner);
            }
            Draggable(bar, DragKind.SpanBody, owner, -1);
            // 양 끝 손잡이: 앞쪽은 그 단계를 옮기고, 뒤쪽은 표시 끝 단계를 만들거나 옮긴다. 다른 장면으로 이어지는 쪽에는 없다.
            if (!continuesBefore) bar.Add(Handle(owner, true));
            if (!continuesAfter) bar.Add(Handle(owner, false));
            return bar;
        }
        private VisualElement Handle(int owner, bool start)
        {
            var handle = new VisualElement { tooltip = start ? "끌어서 시작 세로줄 바꾸기" : "끌어서 끝 세로줄 바꾸기(표시 끝 단계를 만들거나 옮김)", style = { position = Position.Absolute, top = 0, bottom = 0, width = 8, left = start ? 0 : StyleKeyword.Auto, right = start ? StyleKeyword.Auto : 0, backgroundColor = new Color(0, 0, 0, .35f) } };
            Draggable(handle, start ? DragKind.SpanStart : DragKind.SpanEnd, owner, -1);
            return handle;
        }

        // ── 끌기 ──
        // 누르고 조금 움직이면 끌기, 그대로 떼면 클릭(선택)이다. doubleClick은 두 번 누를 때(대사 바로 고치기).
        private void Draggable(VisualElement element, DragKind kind, int index, int column, System.Action doubleClick = null)
        {
            element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || dragKind != DragKind.None) return;
                e.StopPropagation();
                if (e.clickCount == 2 && doubleClick != null) { doubleClick(); return; }
                dragKind = kind; dragIndex = index; dragColumn = column; dragOrigin = e.position; dragMoved = false; dragElement = element;
                dragAdditive = e.actionKey; dragRange = e.shiftKey;
                if (kind == DragKind.SpanBody || kind == DragKind.Draw) dragColumn = ColumnAt(e.position);
                element.CapturePointer(e.pointerId);
            });
            element.RegisterCallback<PointerMoveEvent>(e => { if (dragElement == element && element.HasPointerCapture(e.pointerId)) DragMove(e.position); });
            element.RegisterCallback<PointerUpEvent>(e =>
            {
                if (dragElement != element || !element.HasPointerCapture(e.pointerId)) return;
                element.ReleasePointer(e.pointerId); e.StopPropagation();
                DragEnd(e.position);
            });
            element.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragElement == element && dragKind != DragKind.None) { ClearPreview(); dragKind = DragKind.None; dragElement = null; } });
        }
        private void DragMove(Vector2 point)
        {
            if (!dragMoved && (point - dragOrigin).magnitude < DragThreshold) return;
            dragMoved = true;
            ClearTint();
            string text;
            if (dragKind == DragKind.Column)
            {
                int gap = GapAt(point);
                ShowDropMarker(gap);
                text = gap == dragColumn || gap == dragColumn + 1 ? "여기에 놓으면 그대로" : $"이 세로줄(위에 쌓인 단계 포함)을 {(gap < columns.Count ? $"{columns[gap].To}번 앞" : "맨 끝")}으로 옮김";
            }
            else if (dragKind == DragKind.Draw)
            {
                int a = dragColumn, b = ColumnAt(point);
                if (a < 0 || b < 0) text = "여기에 놓을 수 없음";
                else { Tint(Mathf.Min(a, b), Mathf.Max(a, b)); text = $"놓으면 이 범위에 표시 추가  ({columns[Mathf.Min(a, b)].To}번 ~ {columns[Mathf.Max(a, b)].To}번)"; }
            }
            else text = PreviewEdit(point);
            ShowLabel(text, point);
        }
        // 막대·작은 노드를 끄는 동안: 놓으면 될 결과를 계산해 범위를 칠하고 설명을 돌려준다(대본은 아직 바꾸지 않음).
        private string PreviewEdit(Vector2 point)
        {
            var edited = EditFor(dragKind, point, out int owner);
            var steps = edited ?? script.Steps; if (edited == null) owner = dragIndex;
            var after = DialogueScriptEdits.Columns(steps);
            var span = ScenarioTimeline.SpanOf(steps, owner);
            if (after.Count != columns.Count) return edited == null ? "(바뀌지 않음)" : "놓으면 옮김";
            if (span == null)
            {
                int c = DialogueScriptEdits.ColumnOf(after, owner); if (c >= 0) Tint(c, c);
                return c >= 0 ? $"놓으면 이 세로줄로: {Describe(steps, after[c])}" + (edited == null ? "\n(바뀌지 않음)" : "") : "";
            }
            int a = DialogueScriptEdits.ColumnOf(after, span.Value.Start), b = DialogueScriptEdits.ColumnOf(after, span.Value.End);
            Tint(a, b);
            return $"놓으면  시작: {Describe(steps, after[a])}\n          끝: {Describe(steps, after[b])} {EndWord(span.Value.EndKind)}" + (edited == null ? "\n(바뀌지 않음)" : "");
        }
        // kind를 따로 받는다: 손을 뗄 때는 끌기 상태를 먼저 지운 뒤 계산하기 때문.
        private ScenarioStep[] EditFor(DragKind kind, Vector2 point, out int owner)
        {
            owner = dragIndex;
            int target = ColumnAt(point);
            if (target < 0) return null;
            switch (kind)
            {
                case DragKind.SpanStart: case DragKind.Instant: return DialogueScriptEdits.MoveDisplayEdge(script.Steps, dragIndex, true, target, out owner);
                case DragKind.SpanEnd: return DialogueScriptEdits.MoveDisplayEdge(script.Steps, dragIndex, false, target, out owner);
                case DragKind.SpanBody: return dragColumn < 0 ? null : DialogueScriptEdits.MoveSpan(script.Steps, dragIndex, target - dragColumn, out owner);
                default: return null;
            }
        }
        private void DragEnd(Vector2 point)
        {
            var kind = dragKind; bool moved = dragMoved;
            ClearPreview(); dragKind = DragKind.None; dragElement = null;
            if (!moved)
            {
                if (kind == DragKind.Draw) { int c = ColumnAt(point); if (c >= 0) ShowAddPopup(point, c, c); }
                else SelectIndex(dragIndex, dragAdditive, dragRange);
                return;
            }
            switch (kind)
            {
                case DragKind.Column:
                {
                    var edited = DialogueScriptEdits.MoveColumn(script.Steps, dragColumn, GapAt(point), out int main);
                    Commit(edited, "세로줄 옮기기", main); break;
                }
                case DragKind.Draw:
                {
                    int b = ColumnAt(point);
                    if (dragColumn >= 0 && b >= 0) ShowAddPopup(point, Mathf.Min(dragColumn, b), Mathf.Max(dragColumn, b)); else RebuildGraph();
                    break;
                }
                default:
                {
                    var edited = EditFor(kind, point, out int owner);
                    Commit(edited, kind == DragKind.SpanEnd ? "표시 끝 옮기기" : kind == DragKind.SpanBody ? "표시 범위 옮기기" : "단계 옮기기", owner); break;
                }
            }
        }
        // 화면 좌표가 가리키는 세로줄: 가장 가까운 장면 묶음(세로 거리)에서 가로로 가장 가까운 세로줄. 장면 사이 빈 곳에서 놓아도 가까운 줄로 붙는다.
        private int ColumnAt(Vector2 point)
        {
            int best = -1; float bestScore = float.MaxValue;
            for (int c = 0; c < columns.Count; c++)
            {
                if (columns[c].Hidden) continue; // 접힌 장면에는 놓지 않는다
                var group = columns[c].Group.worldBound; var rect = columns[c].Element.worldBound;
                float dy = point.y < group.yMin ? group.yMin - point.y : point.y > group.yMax ? point.y - group.yMax : 0;
                float dx = point.x < rect.xMin ? rect.xMin - point.x : point.x > rect.xMax ? point.x - rect.xMax : 0;
                float score = dy * 1000 + dx;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
        // 세로줄을 놓을 틈(0 = 맨 앞, 세로줄 수 = 맨 끝): 다른 세로줄 위에 놓으면 그 세로줄 자리로 간다
        // (뒤쪽 세로줄이면 그 뒤, 앞쪽 세로줄이면 그 앞). 자기 자리 위면 그대로.
        private int GapAt(Vector2 point)
        {
            int c = ColumnAt(point); if (c < 0) return -1;
            if (dragColumn >= 0) return c > dragColumn ? c + 1 : c < dragColumn ? c : dragColumn;
            return point.x < columns[c].Element.worldBound.center.x ? c : c + 1;
        }
        private void ShowDropMarker(int gap)
        {
            if (gap < 0 || columns.Count == 0) { dropMarker?.RemoveFromHierarchy(); dropMarker = null; return; }
            var column = columns[Mathf.Min(gap, columns.Count - 1)];
            if (column.Hidden) { dropMarker?.RemoveFromHierarchy(); dropMarker = null; return; }
            var rect = column.Element.worldBound; var group = column.Group.worldBound;
            float x = gap < columns.Count ? rect.xMin - Gap / 2 : rect.xMax + Gap / 2;
            var local = rootVisualElement.WorldToLocal(new Vector2(x, group.yMin));
            if (dropMarker == null) { dropMarker = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, width = 3, backgroundColor = new Color(.95f, .8f, .3f) } }; rootVisualElement.Add(dropMarker); }
            dropMarker.style.left = local.x - 1; dropMarker.style.top = local.y; dropMarker.style.height = group.height;
        }
        private void Tint(int from, int to)
        {
            for (int c = Mathf.Max(0, from); c <= to && c < columns.Count; c++) { columns[c].Element.style.backgroundColor = Highlight; if (columns[c].Band != null) columns[c].Band.style.backgroundColor = BandTint; tinted.Add(c); }
        }
        private void ClearTint()
        {
            foreach (int c in tinted) if (c < columns.Count) { columns[c].Element.style.backgroundColor = columns[c].Background; if (columns[c].Band != null) columns[c].Band.style.backgroundColor = columns[c].BandBackground; }
            tinted.Clear();
        }
        private void ShowLabel(string text, Vector2 point)
        {
            if (previewLabel == null)
            {
                previewLabel = new Label { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, backgroundColor = new Color(.1f, .1f, .12f, .92f), color = new Color(.95f, .9f, .7f), paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 3, borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4, fontSize = 11 } };
                rootVisualElement.Add(previewLabel);
            }
            previewLabel.text = text;
            var local = rootVisualElement.WorldToLocal(point); previewLabel.style.left = local.x + 14; previewLabel.style.top = local.y + 14;
        }
        private void ClearPreview()
        {
            ClearTint();
            previewLabel?.RemoveFromHierarchy(); previewLabel = null;
            dropMarker?.RemoveFromHierarchy(); dropMarker = null;
        }
        // 빈 줄을 끈 범위에 넣을 표시를 고르는 작은 창.
        private void ShowAddPopup(Vector2 point, int from, int to)
        {
            ClosePopup(); Tint(from, to);
            popup = new VisualElement { style = { position = Position.Absolute, backgroundColor = new Color(.16f, .17f, .2f), paddingLeft = 6, paddingRight = 6, paddingTop = 6, paddingBottom = 6, borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6 } };
            SetBorder(popup, new Color(.45f, .47f, .55f), 1);
            popup.Add(new Label($"{columns[from].To}번 ~ {columns[to].To}번에 추가") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            void Add(string label, ScenarioStep display) => popup.Add(new Button(() => { ClosePopup(); Commit(DialogueScriptEdits.AddSpan(script.Steps, from, to, display, out int owner), "표시 추가", owner); }) { text = label });
            Add("감정 표시  ?", new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "?" });
            Add("감정 표시  !", new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "!" });
            Add("감정 표시  ;;", new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = ";;" });
            Add("자세 (그림은 아래 칸에서 고름)", new ScenarioStep { type = ScenarioStepType.Pose });
            popup.Add(new Button(ClosePopup) { text = "취소" });
            var local = rootVisualElement.WorldToLocal(point);
            popup.style.left = local.x; popup.style.top = local.y;
            rootVisualElement.Add(popup);
        }
        private void ClosePopup() { if (popup == null) return; popup.RemoveFromHierarchy(); popup = null; ClearTint(); }

        // ── 우클릭 추가 ──
        private void ColumnMenu(ContextualMenuPopulateEvent e, int column)
        {
            if (script == null) return;
            e.menu.AppendAction("이 세로줄에 추가/감정 표시", _ => AddInColumn(column, new ScenarioStep { type = ScenarioStepType.Emote, target = "player", text = "!" }));
            e.menu.AppendAction("이 세로줄에 추가/자세", _ => AddInColumn(column, new ScenarioStep { type = ScenarioStepType.Pose }));
            e.menu.AppendAction("이 세로줄에 추가/등장·숨김", _ => AddInColumn(column, new ScenarioStep { type = ScenarioStepType.SetActive, active = true }));
            e.menu.AppendAction("이 세로줄에 추가/적 합류", _ => AddInColumn(column, new ScenarioStep { type = ScenarioStepType.SpawnEnemy, direction = Vector2Int.down }));
            e.menu.AppendAction("이 세로줄에 추가/클리어 기록", _ => AddInColumn(column, new ScenarioStep { type = ScenarioStepType.RecordClear }));
            foreach (var (label, make) in WaitingSteps(column))
            {
                e.menu.AppendAction("앞에 새 세로줄/" + label, _ => AddColumn(column - 1, make()));
                e.menu.AppendAction("뒤에 새 세로줄/" + label, _ => AddColumn(column, make()));
            }
            e.menu.AppendSeparator();
            e.menu.AppendAction("이 세로줄 삭제", _ => DeleteColumn(column));
            e.StopPropagation();
        }
        private IEnumerable<(string, System.Func<ScenarioStep>)> WaitingSteps(int column)
        {
            var columnsNow = DialogueScriptEdits.Columns(script.Steps);
            var speaker = LastSpeaker(column >= 0 && column < columnsNow.Count ? columnsNow[column].to : script.Steps.Length - 1);
            yield return ("대사", () => NewLine(speaker));
            yield return ("지문", () => NewLine(null));
            yield return ("입력 대기", () => new ScenarioStep { type = ScenarioStepType.WaitAction, actions = ScenarioActions.Move });
            yield return ("네 방향 누르기", () => new ScenarioStep { type = ScenarioStepType.WaitAllDirections, keys = "W A S D" });
            yield return ("칸까지 이동", () => new ScenarioStep { type = ScenarioStepType.WaitReachCell, actions = ScenarioActions.Move });
            yield return ("칸 클릭", () => new ScenarioStep { type = ScenarioStepType.WaitClickCell, keys = "좌클릭" });
            yield return ("이름 입력", () => new ScenarioStep { type = ScenarioStepType.NameInput });
            yield return ("자유 진행", () => new ScenarioStep { type = ScenarioStepType.FreePlay, actions = ScenarioActions.Move | ScenarioActions.Wait | ScenarioActions.Dash });
            yield return ("기다림", () => new ScenarioStep { type = ScenarioStepType.Wait, seconds = 1 });
            yield return ("씬 전환", () => new ScenarioStep { type = ScenarioStepType.LoadScene });
        }
        private static ScenarioStep NewLine(DialogueCharacter speaker) => new ScenarioStep { type = ScenarioStepType.Line, line = new DialogueLine { speaker = speaker, text = "" } };
        // 넣을 자리 앞에서 마지막으로 말한 인물(새 대사의 기본 화자).
        private DialogueCharacter LastSpeaker(int before)
        {
            var steps = script.Steps;
            for (int i = Mathf.Min(before, steps.Length - 1); i >= 0; i--) if (steps[i].type == ScenarioStepType.Line && steps[i].line.speaker != null) return steps[i].line.speaker;
            return null;
        }
        private void AddInColumn(int column, ScenarioStep step) => Commit(DialogueScriptEdits.InsertInColumn(script.Steps, column, step, out int at), "단계 추가", at);
        private void AddColumn(int after, ScenarioStep step)
        {
            Commit(DialogueScriptEdits.InsertAfterColumn(script.Steps, after, step, out int at), "단계 추가", at);
            if (step.type == ScenarioStepType.Line) graph.schedule.Execute(() => BeginInlineEdit(at)); // 새 대사는 바로 입력
        }
        private void DeleteColumn(int column)
        {
            var c = DialogueScriptEdits.Columns(script.Steps)[column];
            var indices = Enumerable.Range(c.from, c.to - c.from + 1).Where(i => script.Steps[i].type != ScenarioStepType.EndDisplay).ToList();
            if (!EditorUtility.DisplayDialog("세로줄 삭제", $"{c.from}번 ~ {c.to}번 단계 {indices.Count}개를 지웁니다(표시 끝 단계는 앞 표시의 끝이라 남김).", "삭제", "취소")) return;
            Commit(DialogueScriptEdits.Delete(script.Steps, indices), "세로줄 삭제", Mathf.Min(c.from, script.Steps.Length - indices.Count - 1));
        }

        // ── 새 대본 만들기 ──
        // 위쪽 막대 아래에 입력 칸을 띄운다: 파일 이름, 대본 ID(파일 이름에서 자동, 고칠 수 있음), 시작 내용(빈 대본·현재 대본 복제).
        // 만들기를 누르면 Unity 저장 창으로 위치를 고르고, 만든 대본을 바로 연다. ID가 비었거나 겹치면 만들기를 막는다.
        private void ShowCreateForm(bool duplicate)
        {
            CloseCreateForm();
            bool copyable = script != null;
            createForm = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, paddingLeft = 8, paddingRight = 8, paddingTop = 4, paddingBottom = 6, backgroundColor = new Color(.2f, .22f, .27f), borderBottomWidth = 1, borderBottomColor = new Color(.35f, .35f, .4f) } };
            createForm.Add(new Label(duplicate ? "대본 복제" : "새 대본") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } });
            var nameField = new TextField("파일 이름") { value = duplicate && copyable ? CopyName(script.name) : "NewScript", style = { minWidth = 240 } };
            var idField = new TextField("대본 ID") { value = DialogueScriptAssets.SuggestId(nameField.value), style = { minWidth = 220 } };
            var start = new DropdownField("시작 내용", new List<string> { "빈 대본(지문 한 줄)", "현재 대본 복제" }, duplicate && copyable ? 1 : 0) { style = { minWidth = 260 } };
            start.SetEnabled(copyable);
            foreach (var field in new VisualElement[] { nameField, idField, start }) field.Q<Label>().style.minWidth = 56;
            var error = new Label { style = { color = new Color(1f, .55f, .5f), marginLeft = 8 } };
            var create = new Button { text = "만들기…" };
            bool idEdited = false;
            void Check()
            {
                string problem = string.IsNullOrWhiteSpace(nameField.value) ? "파일 이름을 입력하세요." : DialogueScriptAssets.IdProblem(idField.value);
                error.text = problem ?? ""; create.SetEnabled(problem == null);
            }
            nameField.RegisterValueChangedCallback(e => { if (!idEdited) idField.SetValueWithoutNotify(DialogueScriptAssets.SuggestId(e.newValue)); Check(); });
            idField.RegisterValueChangedCallback(_ => { idEdited = true; Check(); });
            create.clicked += () =>
            {
                AssetPaths.EnsureFolder(DialogueScriptAssets.DefaultFolder); // 저장 창이 기본 폴더에서 열리게
                string path = EditorUtility.SaveFilePanelInProject(duplicate ? "대본 복제 저장" : "새 대본 저장", nameField.value.Trim(), "asset", "대본 파일을 저장할 위치를 고르세요.", DialogueScriptAssets.DefaultFolder);
                if (string.IsNullOrEmpty(path)) return;
                bool blank = start.index != 1 || !copyable;
                var created = DialogueScriptAssets.Create(path, idField.value.Trim(), blank ? DialogueScriptAssets.BlankSteps() : script.Steps, out string problem);
                if (created == null) { error.text = problem; return; }
                CloseCreateForm();
                SetScript(created);
                selection = new List<string> { created.Steps[0].id };
                RebuildAll();
                EditorGUIUtility.PingObject(created);
                if (blank) graph.schedule.Execute(() => BeginInlineEdit(0)); // 빈 대본은 첫 지문을 바로 입력
            };
            createForm.Add(nameField); createForm.Add(idField); createForm.Add(start); createForm.Add(create);
            createForm.Add(new Button(CloseCreateForm) { text = "취소" });
            createForm.Add(error);
            rootVisualElement.Insert(1, createForm);
            Check();
            nameField.schedule.Execute(() => nameField.Focus());
        }
        private void CloseCreateForm() { createForm?.RemoveFromHierarchy(); createForm = null; }
        // 복제 기본 이름: 끝의 Script 앞에 Copy를 넣는다(Tutorial1Script → Tutorial1CopyScript).
        private static string CopyName(string name) => name.EndsWith("Script") && name.Length > 6 ? name.Substring(0, name.Length - 6) + "CopyScript" : name + "Copy";

        // ── 씬에 연결 ──
        // 프로젝트의 모든 씬에서 대본 진행기(ScenarioDirector)를 찾아 목록으로 보여 주고, 고른 진행기에 지금 대본을 꽂는다.
        // 열린 씬: 그 자리에서 연결(Ctrl+Z 가능, 씬 저장은 사용자). 닫힌 씬: 확인 후 씬 파일을 잠깐 열어 연결·저장하고 닫는다(되돌리기 없음).
        // Play 중과 파일로 저장되지 않은 대본은 막는다.
        private void ConnectToScene()
        {
            if (script == null) return;
            var entries = DialogueScriptScenes.DirectorsInProject();
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("씬에 연결", "프로젝트의 어느 씬에도 대본 진행기(ScenarioDirector)가 없습니다.\n전투 씬의 Grid Map Player 오브젝트에 ScenarioDirector를 추가한 뒤 다시 누르세요.", "확인");
                return;
            }
            var menu = new GenericMenu();
            foreach (var entry in entries)
            {
                var current = entry.Script;
                string where = entry.Open ? "열린 씬" : "닫힌 씬";
                string label = $"{where}/{entry.SceneName.Replace("/", "∕")}  ·  {entry.ObjectName.Replace("/", "∕")}  (지금: {(current != null ? current.name : "없음")})";
                if (current == script) menu.AddDisabledItem(new GUIContent(label), true);
                else menu.AddItem(new GUIContent(label), false, () => ConnectTo(entry));
            }
            menu.DropDown(connectButton.worldBound);
        }
        private void ConnectTo(DialogueScriptScenes.Entry entry)
        {
            var current = entry.Script;
            string target = $"{entry.SceneName} / {entry.ObjectName}";
            if (!entry.Open)
            {
                if (!EditorUtility.DisplayDialog("닫힌 씬에 연결", $"{entry.ScenePath} 씬 파일을 잠깐 열어 {entry.ObjectName}의 대본을 '{(current != null ? current.name : "없음")}'에서 '{script.name}'(으)로 바꾸고 저장한 뒤 닫습니다.\nCtrl+Z로 되돌릴 수 없습니다(되돌리려면 다시 연결하세요).", "연결하고 저장", "취소")) return;
            }
            else if (current != null && !EditorUtility.DisplayDialog("씬에 연결", $"{target}의 대본을 '{current.name}'에서 '{script.name}'(으)로 바꿉니다.\nCtrl+Z로 되돌릴 수 있고, 씬은 직접 저장해야 합니다.", "바꾸기", "취소")) return;
            if (!DialogueScriptScenes.Connect(entry, script, out string problem)) { EditorUtility.DisplayDialog("씬에 연결", problem, "확인"); return; }
            if (entry.Open) { EditorGUIUtility.PingObject(entry.Director); ShowNotification(new GUIContent($"{entry.SceneName} 씬에 연결했습니다. 씬을 저장하세요(Ctrl+S).")); }
            else { EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.ScenePath)); ShowNotification(new GUIContent($"{entry.SceneName} 씬 파일에 연결하고 저장했습니다.")); }
            RefreshConnection();
        }
        // 지금 대본이 연결된 씬(열린 씬·닫힌 씬 모두)을 막대에 적는다. 연결·등장 오브젝트가 바뀌었으면 시작 노드·검사·아래 칸을 다시 그린다.
        private void RefreshConnection()
        {
            if (connectButton == null || connectionLabel == null) return;
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            connectButton.SetEnabled(script != null && EditorUtility.IsPersistent(script) && !playing);
            connectButton.tooltip = playing ? "Play 중에는 연결할 수 없습니다." : "프로젝트 씬의 대본 진행기(ScenarioDirector)를 골라 이 대본을 연결합니다.";
            linked = script != null ? DialogueScriptScenes.DirectorsInProject().FindAll(entry => entry.Script == script) : new List<DialogueScriptScenes.Entry>();
            if (script == null) { connectionLabel.text = ""; linkedKey = ""; return; }
            connectionLabel.text = linked.Count == 0 ? "연결된 씬 없음" : "연결: " + string.Join(", ", linked.ConvertAll(entry => entry.SceneName + (entry.Open ? "" : "(닫힘)")));
            string key = string.Join("|", linked.ConvertAll(entry => $"{entry.ScenePath}:{entry.ObjectName}:{entry.Open}:{entry.PlayOnStart}:{string.Join(",", entry.ActorIds)}"));
            if (key == linkedKey) return;
            linkedKey = key;
            QueueRebuild();
            if (startSelected) RebuildDetail();
        }

        // ── 대사 바로 고치기 ──
        private void BeginInlineEdit(int index)
        {
            if (script == null || index < 0 || index >= script.Steps.Length || !stepElements.TryGetValue(index, out var node)) return;
            var step = script.Steps[index]; string id = step.id;
            var characters = AssetDatabase.FindAssets("t:DialogueCharacter").Select(g => AssetDatabase.LoadAssetAtPath<DialogueCharacter>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToList();
            var names = new List<string> { "(지문)" }; names.AddRange(characters.Select(c => $"{c.DisplayName}  ({c.name})"));
            int current = step.line.speaker != null ? characters.IndexOf(step.line.speaker) + 1 : 0;
            node.Clear();
            var who = new DropdownField(names, Mathf.Max(0, current)) { style = { marginLeft = 0, marginRight = 0 } };
            var text = new TextField { multiline = true, value = step.line.text ?? "", style = { whiteSpace = WhiteSpace.Normal, minHeight = 60, marginLeft = 0, marginRight = 0 } };
            void Commit2()
            {
                int i = IndexOfId(id); if (i < 0) return;
                var edited = (ScenarioStep[])script.Steps.Clone();
                edited[i].line.speaker = who.index <= 0 ? null : characters[who.index - 1];
                edited[i].line.text = text.value;
                Commit(edited, "대사 고치기", i);
            }
            text.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && e.actionKey) { Commit2(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { RebuildGraph(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(new Button(Commit2) { text = "확인 (Ctrl+Enter)" });
            buttons.Add(new Button(RebuildGraph) { text = "취소 (Esc)" });
            foreach (var child in new VisualElement[] { who, text, buttons }) { child.RegisterCallback<PointerDownEvent>(e => e.StopPropagation()); node.Add(child); }
            text.schedule.Execute(() => text.Focus());
        }

        // ── 복사·붙여넣기·삭제 ──
        private void Copy()
        {
            var indices = SelectedIndices(); if (indices.Count == 0) return;
            clipboard = indices.Select(i => script.Steps[i]).ToList();
        }
        // 기준 단계(없으면 맨 끝) 뒤에 넣는다.
        private void Paste()
        {
            if (clipboard == null || clipboard.Count == 0) return;
            var indices = SelectedIndices();
            int position = indices.Count > 0 ? indices.Max() + 1 : script.Steps.Length;
            var edited = DialogueScriptEdits.InsertSteps(script.Steps, position, clipboard, out int at);
            Commit(edited, "단계 붙여넣기", Enumerable.Range(at, clipboard.Count).ToArray());
        }
        private void DeleteSelected()
        {
            var indices = SelectedIndices(); if (indices.Count == 0) return;
            if (indices.Count > 1 && !EditorUtility.DisplayDialog("단계 삭제", $"선택한 단계 {indices.Count}개를 지웁니다.", "삭제", "취소")) return;
            Commit(DialogueScriptEdits.Delete(script.Steps, indices), "단계 삭제", Mathf.Min(indices[0], script.Steps.Length - indices.Count - 1));
        }

        // ── 검사 ──
        private void RefreshIssues()
        {
            if (issuesButton == null || issuesList == null) return;
            issuesList.Clear(); issuesAt.Clear();
            var issues = script != null ? DialogueScriptEdits.Validate(script.Steps) : new List<DialogueScriptEdits.Issue>();
            if (script != null) issues.AddRange(DialogueScriptScenes.ActorIssues(script.Steps, linked));
            foreach (var issue in issues) { if (!issuesAt.TryGetValue(issue.Index, out var list)) issuesAt[issue.Index] = list = new List<DialogueScriptEdits.Issue>(); list.Add(issue); }
            int errors = issues.Count(i => i.Error), warnings = issues.Count - errors;
            issuesButton.text = issues.Count == 0 ? "검사: 문제 없음" : $"검사: 오류 {errors} · 주의 {warnings}";
            issuesButton.style.color = errors > 0 ? new Color(1f, .45f, .4f) : warnings > 0 ? new Color(.95f, .8f, .3f) : new Color(.5f, .85f, .5f);
            issuesList.style.display = showIssues && issues.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var issue in issues)
            {
                int index = issue.Index;
                var row = new Label($"{(issue.Error ? "오류" : "주의")}  {index}번 · {issue.Message}") { style = { color = issue.Error ? new Color(1f, .55f, .5f) : new Color(.95f, .82f, .45f), paddingTop = 1, paddingBottom = 1 } };
                row.RegisterCallback<PointerDownEvent>(_ => { ExpandSceneOf(index); SelectIndex(index); graph.schedule.Execute(() => { if (stepElements.TryGetValue(index, out var element)) graph.ScrollTo(element); }); });
                issuesList.Add(row);
            }
        }

        // 그 단계가 있는 장면이 접혀 있으면 펼친다(검사 목록에서 이동할 때).
        private void ExpandSceneOf(int index)
        {
            if (script == null) return;
            foreach (var (start, end) in ScenarioTimeline.Scenes(script.Steps)) if (index >= start && index <= end) collapsedScenes.Remove(script.Steps[end].id);
        }

        // ── 표시 문구 ──
        private static string TypeName(ScenarioStepType type)
        {
            switch (type)
            {
                case ScenarioStepType.Emote: return "감정 표시";
                case ScenarioStepType.Pose: return "자세";
                case ScenarioStepType.WaitAction: return "입력 대기";
                case ScenarioStepType.WaitFreeMove: return "자유 이동";
                case ScenarioStepType.WaitReachCell: return "칸까지 이동";
                case ScenarioStepType.WaitClickCell: return "칸 클릭";
                case ScenarioStepType.WaitAllDirections: return "네 방향";
                case ScenarioStepType.NameInput: return "이름 입력";
                case ScenarioStepType.SetActive: return "켜기·끄기";
                case ScenarioStepType.Wait: return "기다림";
                case ScenarioStepType.LoadScene: return "씬 전환";
                case ScenarioStepType.SpawnEnemy: return "적 합류";
                case ScenarioStepType.FreePlay: return "자유 진행";
                case ScenarioStepType.RecordClear: return "클리어 기록";
                case ScenarioStepType.EndDisplay: return "표시 끝";
                default: return type.ToString();
            }
        }
        // 작은 노드 본문: 종류 이름은 머리에 있으므로 요약에서 종류 이름을 뺀 내용.
        private static string Detail(ScenarioStep step)
        {
            string summary = Summary(step), name = TypeName(step.type);
            if (summary.StartsWith(name)) summary = summary.Substring(name.Length).TrimStart(' ', ':', '·');
            return string.IsNullOrEmpty(summary) ? name : summary;
        }
        private static Color CategoryColor(ScenarioStepType type)
        {
            switch (type)
            {
                case ScenarioStepType.Pose: return PoseColor;
                case ScenarioStepType.Emote: return EmoteColor;
                case ScenarioStepType.SetActive: case ScenarioStepType.SpawnEnemy: return WorldColor;
                case ScenarioStepType.Wait: case ScenarioStepType.LoadScene: case ScenarioStepType.RecordClear: return FlowColor;
                default: return InputColor;
            }
        }
        private static string EndText(ScenarioSpan span)
        {
            switch (span.EndKind)
            {
                case ScenarioSpanEnd.LineDone: return $"{span.End}번 대사를 넘길 때까지";
                case ScenarioSpanEnd.NextAction: return $"{span.End}번 입력에서 행동할 때까지";
                case ScenarioSpanEnd.Replaced: return $"{span.End}번 단계가 끝나고 다음 표시로 바뀔 때까지";
                case ScenarioSpanEnd.EndStep: return $"{span.End}번 단계가 끝날 때까지(표시 끝 {span.Marker}번)";
                case ScenarioSpanEnd.Seconds: return "정한 시간 동안";
                default: return "대본 끝까지";
            }
        }
        // 세로줄 하나를 짧게: 대사면 앞부분, 아니면 요약.
        private static string Describe(ScenarioStep[] steps, (int from, int to, bool hasMain) column)
        {
            if (!column.hasMain) return $"{column.to}번 단계";
            var main = steps[column.to];
            if (main.type != ScenarioStepType.Line) return $"{column.to}번 {Summary(main)}";
            string text = main.line.text ?? ""; if (text.Length > 18) text = text.Substring(0, 18) + "…";
            return $"{column.to}번 \"{text}\"";
        }
        private static string EndWord(ScenarioSpanEnd kind)
        {
            switch (kind)
            {
                case ScenarioSpanEnd.NextAction: return "(여기서 행동할 때)";
                case ScenarioSpanEnd.Seconds: return "(정한 시간 동안)";
                case ScenarioSpanEnd.ScriptEnd: return "(대본 끝까지)";
                case ScenarioSpanEnd.Replaced: return "(다음 표시로 바뀔 때)";
                default: return "(이 단계가 끝날 때)";
            }
        }
        /// <summary>단계 한 줄 요약(노드 화면·편집 칸 제목).</summary>
        public static string Summary(ScenarioStep step)
        {
            string keys = step.KeyCaps.Length > 0 ? $" [{string.Join(" + ", step.KeyCaps)}]" : "";
            switch (step.type)
            {
                case ScenarioStepType.Line: return "대사";
                case ScenarioStepType.Emote: return $"감정 표시 {(step.sprite != null ? step.sprite.name : step.text)} → {step.target}" + (step.seconds > 0 && !step.untilEnd ? $" {step.seconds:0.##}초" : "");
                case ScenarioStepType.Pose: return $"자세 {(step.sprite != null ? step.sprite.name : "(그림 없음)")} · {step.motion}" + (step.endMotion != PoseMotion.None ? $" / 돌아올 때 {step.endMotion}" : "") + (!step.untilAction && !step.untilEnd && step.seconds > 0 ? $" {step.seconds:0.##}초" : "");
                case ScenarioStepType.WaitAction: return $"입력 대기: {step.actions}" + (step.direction != Vector2Int.zero ? $" {Direction(step.direction)}" : "") + keys;
                case ScenarioStepType.WaitFreeMove: return $"자유 이동 {step.seconds:0.##}초" + keys;
                case ScenarioStepType.WaitReachCell: return $"칸 {step.cell}까지 이동" + keys;
                case ScenarioStepType.WaitClickCell: return $"칸 {step.cell} 클릭" + keys;
                case ScenarioStepType.WaitAllDirections: return "네 방향 모두 누르기" + keys;
                case ScenarioStepType.NameInput: return $"이름 입력 (기본값 {step.text})";
                case ScenarioStepType.SetActive: return $"{step.target} {(step.active ? "등장" : "숨김")}";
                case ScenarioStepType.Wait: return $"{step.seconds:0.##}초 기다림";
                case ScenarioStepType.LoadScene: return $"씬 전환 → {System.IO.Path.GetFileNameWithoutExtension(step.scenePath)}";
                case ScenarioStepType.SpawnEnemy: return $"적 합류 {(step.prefab != null ? step.prefab.name : "(프리팹 없음)")} {step.cell} {Direction(step.direction)}";
                case ScenarioStepType.FreePlay: return $"자유 진행: {step.actions}";
                case ScenarioStepType.RecordClear: return $"클리어 기록 {step.text}";
                case ScenarioStepType.EndDisplay: return $"표시 끝 {(step.display == ScenarioDisplay.Pose ? "자세" : $"감정 표시 → {step.target}")}";
                default: return step.type.ToString();
            }
        }
        private static string Direction(Vector2Int d) => d == Vector2Int.up ? "↑" : d == Vector2Int.down ? "↓" : d == Vector2Int.left ? "←" : d == Vector2Int.right ? "→" : d.ToString();
        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
        }

        // ── 아래 편집 칸 ──
        private void RebuildDetail()
        {
            if (detail == null) return;
            detail.Unbind(); detail.Clear();
            if (serialized == null) return;
            serialized.Update();
            if (startSelected) { StartDetail(); return; }
            var steps = serialized.FindProperty("steps");
            var indices = SelectedIndices();
            if (indices.Count == 0)
            {
                detail.Add(new Label("위에서 노드·막대를 누르면 여기서 편집합니다. 세로줄을 우클릭하면 단계를 추가할 수 있습니다.") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 8 } });
                detail.Add(new Button(() => AddColumn(DialogueScriptEdits.Columns(script.Steps).Count - 1, NewLine(LastSpeaker(script.Steps.Length - 1)))) { text = "맨 끝에 대사 추가", style = { alignSelf = Align.FlexStart } });
                return;
            }
            if (indices.Count > 1)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
                row.Add(new Label($"단계 {indices.Count}개 선택 ({string.Join(", ", indices.Take(12))}{(indices.Count > 12 ? " …" : "")})") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12 } });
                row.Add(new Button(Copy) { text = "복사 (Ctrl+C)" });
                row.Add(new Button(() => { Copy(); Paste(); }) { text = "복제 (Ctrl+D)" });
                row.Add(new Button(DeleteSelected) { text = "삭제 (Delete)" });
                detail.Add(row);
                return;
            }
            int index = indices[0];
            var step = script.Steps[index];
            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginBottom = 6 } };
            top.Add(new Label($"{index}번 단계 · {Summary(step)}") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 12 } });
            top.Add(new Button(() => Swap(index, -1)) { text = "◀ 앞으로" });
            top.Add(new Button(() => Swap(index, 1)) { text = "뒤로 ▶" });
            top.Add(new Button(() => { Copy(); Paste(); }) { text = "복제" });
            top.Add(new Button(DeleteSelected) { text = "삭제" });
            top.Add(new Button(() => { Selection.activeObject = script; EditorGUIUtility.PingObject(script); }) { text = "Inspector에서 보기" });
            detail.Add(top);
            var span = ScenarioTimeline.SpanOf(script.Steps, index);
            if (span != null) detail.Add(new Label($"보이는 범위: {span.Value.Start}번 ~ {span.Value.End}번 ({EndText(span.Value)})") { style = { color = step.type == ScenarioStepType.Pose ? PoseColor : EmoteColor, whiteSpace = WhiteSpace.Normal, marginBottom = 6 } });
            var field = new PropertyField(steps.GetArrayElementAtIndex(index), "단계");
            field.Bind(serialized);
            detail.Add(field);
            detail.Add(new Label("변경은 바로 대본 에셋에 저장되며 Ctrl+Z로 되돌릴 수 있습니다.") { style = { color = new Color(.6f, .6f, .6f), marginTop = 8, whiteSpace = WhiteSpace.Normal, fontSize = 10 } });
        }
        // 시작 노드를 골랐을 때: 대본 설정(바로 저장·Ctrl+Z)과 이 대본을 쓰는 진행기(씬·등장 오브젝트·없는 대상 ID).
        private static readonly (string name, string label)[] SettingLabels =
        {
            ("startTiming", "시작 시점"), ("onlyOnce", "처음 한 번만"), ("allowSkip", "건너뛰기 허용"), ("skipHoldSeconds", "건너뛰기 누르는 시간(초)"),
            ("hideEnemyAlerts", "적 인식 말풍선 숨김"), ("scriptControlsDanger", "위험 그림을 대본이 정함"),
        };
        private void StartDetail()
        {
            detail.Add(new Label("▶ 시작 · 대본 설정") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = StartColor, marginBottom = 2 } });
            detail.Add(new Label("이 대본을 쓰는 모든 씬에 적용됩니다. 변경은 바로 대본 에셋에 저장되며 Ctrl+Z로 되돌릴 수 있습니다. 항목에 마우스를 올리면 설명이 나옵니다.") { style = { color = new Color(.65f, .65f, .68f), fontSize = 10, whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });
            var settings = serialized.FindProperty("settings");
            foreach (var (name, label) in SettingLabels)
            {
                var field = new PropertyField(settings.FindPropertyRelative(name), label);
                field.Bind(serialized);
                detail.Add(field);
            }
            detail.Add(new Label("연결된 씬") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 2 } });
            if (linked.Count == 0)
            {
                detail.Add(new Label("이 대본을 쓰는 진행기(ScenarioDirector)가 없습니다. 위쪽 '씬에 연결'로 전투 씬의 진행기에 연결해야 게임에 나옵니다.") { style = { color = WarnColor, whiteSpace = WhiteSpace.Normal } });
                return;
            }
            detail.Add(new Label("전투·등장 오브젝트(Actors)·자동 시작은 씬의 진행기 Inspector에서 고칩니다.") { style = { color = new Color(.65f, .65f, .68f), fontSize = 10, whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });
            var targets = DialogueScriptScenes.TargetIds(script.Steps).Select(t => t.id).ToList();
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            foreach (var entry in linked)
            {
                var box = new VisualElement { style = { marginBottom = 6, paddingLeft = 8, paddingRight = 8, paddingTop = 4, paddingBottom = 4, backgroundColor = new Color(1, 1, 1, .03f), borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6 } };
                SetBorder(box, new Color(.4f, .42f, .48f), 1);
                var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap } };
                head.Add(new Label($"{entry.SceneName} · {entry.ObjectName}") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 6 } });
                head.Add(new Label(entry.Open ? "열린 씬" : "닫힌 씬") { style = { fontSize = 10, color = new Color(.6f, .6f, .65f), marginRight = 8 } });
                var e2 = entry;
                if (entry.Open) head.Add(new Button(() => { Selection.activeObject = e2.Director; EditorGUIUtility.PingObject(e2.Director); }) { text = "진행기 선택" });
                else
                {
                    var open = new Button(() => { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(e2.ScenePath); }) { text = "씬 열기" };
                    open.SetEnabled(!playing);
                    head.Add(open);
                }
                box.Add(head);
                if (!entry.PlayOnStart) box.Add(new Label("자동 시작 꺼짐(Play On Start) — 코드에서 Begin()을 불러야 시작합니다.") { style = { color = WarnColor, fontSize = 11, whiteSpace = WhiteSpace.Normal } });
                var actors = entry.ActorIds;
                box.Add(new Label("등장 오브젝트(Actors): " + (actors.Count == 0 ? "없음" : string.Join(", ", actors))) { style = { fontSize = 11, whiteSpace = WhiteSpace.Normal } });
                var missing = targets.Where(id => !actors.Contains(id)).ToList();
                if (missing.Count > 0) box.Add(new Label($"⚠ 대본이 부르지만 Actors에 없는 ID: {string.Join(", ", missing)} — 그 단계는 건너뜁니다. 진행기 Actors에 추가하거나 대본의 대상을 고치세요.") { style = { color = WarnColor, fontSize = 11, whiteSpace = WhiteSpace.Normal } });
                detail.Add(box);
            }
        }
        // 단계 하나를 바로 앞·뒤 단계와 바꾼다.
        private void Swap(int index, int delta)
        {
            int target = index + delta; var steps = script.Steps;
            if (target < 0 || target >= steps.Length) return;
            var edited = (ScenarioStep[])steps.Clone(); (edited[index], edited[target]) = (edited[target], edited[index]);
            Commit(edited, "단계 순서 바꾸기", target);
        }
    }
    /// <summary>대본 Inspector 위에 IBIIIS Dialogue Editor 열기 버튼을 둔다. 아래는 기본 Inspector 그대로.</summary>
    [CustomEditor(typeof(ScenarioScript))]
    public sealed class ScenarioScriptEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new Button(() => DialogueEditorWindow.Open((ScenarioScript)target)) { text = "IBIIIS Dialogue Editor로 열기", style = { height = 26, marginBottom = 4 } });
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
    }
}
