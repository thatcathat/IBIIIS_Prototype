using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Editor
{
    /// <summary>IBIIIS Dialogue Editor의 대화 그래프(Unity 기본 노드 그래프 GraphView, 기획서 7.6).
    /// 노드는 대상(NPC·근접 대화 구역·적 — 씬 오브젝트의 ID)과 대본(프로젝트의 모든 ScenarioScript). 선은
    /// 대상의 '첫 대본' → 대본의 '앞 대본', 대본의 '다음' → 다음 대본의 '앞 대본'(대상의 대본 순서, 하나만),
    /// 대본의 '봄'(대화 대본)·'클리어'(전투 진입 대본) → 다른 대본의 조건 슬롯(조건마다 하나, '+ 조건 추가'로 더함).
    /// 선을 잇거나 빼거나 지우면 대화 그래프 에셋(DialogueGraph)을 고친다(한 번의 Ctrl+Z). 대본 노드는 지우지 않는다.
    /// 휠 = 확대·축소, 휠 클릭·Alt+왼쪽 끌기·Space+왼쪽 끌기 = 화면 이동, 빈 곳 끌기 = 여러 개 고르기, 노드 끌기 = 옮기기(격자에 맞춰 그래프에 저장).
    /// 대본 노드를 펼치면(두 번 누르기·▸) 노드 안에 그 대본의 타임라인(ScenarioTimelineView)과 대화 대본의 버전 탭이 보인다. 한 번에 하나만 펼친다.</summary>
    public sealed class DialogueGraphView : GraphView
    {
        private const float Grid = 12, ColumnGap = 300, RowGap = 170;
        public static readonly Color FirstColor = new Color(.95f, .95f, .95f), NextColor = new Color(.78f, .8f, .86f), CondColor = new Color(1f, .42f, .36f);
        public static readonly Color TalkColor = new Color(.55f, .8f, .4f), BattleColor = new Color(.45f, .65f, 1f);
        public static readonly Color NpcColor = new Color(.89f, .76f, .42f), ZoneColor = new Color(.39f, .78f, .91f), EnemyColor = new Color(1f, .54f, .47f);
        private static readonly Color Faint = new Color(.58f, .6f, .66f), ErrorColor = new Color(1f, .45f, .4f), EmptyColor = new Color(.45f, .47f, .52f);

        private enum OutKind { First, Next, Seen, Clear }
        private sealed class OutSlot { public OutKind Kind; public string ObjectId; public ScenarioScript Script; }
        private const int PrevIndex = -2, AddIndex = -1;
        private sealed class InSlot { public ScenarioScript Script; public int Index; }
        private sealed class ObjectNode : Node { public string ObjectId; public Port First; }
        private sealed class ScriptNode : Node
        {
            public ScenarioScript Script;
            public Port Prev, AddPort, Next, Seen, ClearPort;
            public readonly List<Port> Conds = new List<Port>();
        }

        private DialogueGraph graph;
        private readonly List<ScenarioScript> scripts = new List<ScenarioScript>();
        private readonly Dictionary<string, ObjectNode> objectNodes = new Dictionary<string, ObjectNode>();
        private readonly Dictionary<ScenarioScript, ScriptNode> scriptNodes = new Dictionary<ScenarioScript, ScriptNode>();
        private Dictionary<string, DialogueGraphTargets.Target> targets = new Dictionary<string, DialogueGraphTargets.Target>();
        private readonly List<string> duplicateIds = new List<string>();
        private bool rebuilding, flushQueued, selectionQueued;
        private readonly List<(Port output, Port input)> pendingRemoved = new List<(Port, Port)>();
        private readonly List<Edge> pendingCreated = new List<Edge>();
        private Vector2? spacePanStart; private Vector3 spacePanOrigin;
        private string query = ""; private string filter = "all";

        // 펼친 대본(한 번에 하나)과 그 노드 안 타임라인. 다시 그려도 같은 타임라인을 새 노드에 옮겨 붙여 스크롤·선택·버전을 유지한다.
        private ScenarioScript expanded;
        private ScenarioTimelineView timeline;
        // 펼친 노드의 크기는 안의 타임라인 내용 크기를 따른다(가로·세로, 사용자 결정 2026-10-15).
        public ScenarioScript Expanded => expanded;
        public ScenarioTimelineView Timeline => timeline;
        // 펼친 노드를 그릴 때의 대본 모양(용도·버전 수·재생 방식). 창은 펼친 대본의 변경으로 다시 그리지 않으므로, 이 모양이 바뀌었을 때만 다시 그린다.
        private (bool talk, int versions, ScenarioVersionOrder order) expandedShape;
        private static (bool, int, ScenarioVersionOrder) Shape(ScenarioScript s) => (s.IsTalk, s.VersionCount, s.Options.versionOrder);
        /// <summary>펼친 대본의 용도·버전 수·재생 방식이 노드에 그린 것과 다르면 true(노드 제목·슬롯·버전 탭을 다시 그려야 함).</summary>
        public bool ExpandedShapeChanged => expanded != null && Shape(expanded) != expandedShape;
        /// <summary>그래프·씬이 바뀌어 검사 결과가 달라졌을 때(창의 검사 버튼).</summary>
        public Action IssuesChanged;
        /// <summary>창이 Space 키 상태를 알려 준다(Space+왼쪽 끌기 = 화면 이동).</summary>
        public bool SpaceHeld { get; set; }
        public DialogueGraph Graph => graph;
        // 화면 배율·위치: GraphView가 내용 요소의 인라인 style(scale·translate)에 바로 넣는 값(viewTransform 읽기는 Unity 6에서 구식).
        private Vector3 ViewScale { get { var s = contentViewContainer.style.scale; return s.keyword == StyleKeyword.Undefined ? s.value.value : Vector3.one; } }
        private Vector3 ViewPosition { get { var t = contentViewContainer.style.translate; return t.keyword == StyleKeyword.Undefined ? new Vector3(t.value.x.value, t.value.y.value, 0) : Vector3.zero; } }
        public float Zoom => ViewScale.x;
        public Vector2 Pan => ViewPosition;
        public Action ViewChanged;
        /// <summary>검사 결과(그래프 검사 + 씬 대상). 비어 있으면 문제 없음.</summary>
        public List<string> Issues { get; } = new List<string>();
        public int ScriptCount => scriptNodes.Count;
        public int ObjectCount => objectNodes.Count;
        public int EdgeCount => edges.ToList().Count;

        public DialogueGraphView()
        {
            style.flexGrow = 1;
            SetupZoom(DialogueZoom.Min, DialogueZoom.Max);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            var grid = new GridBackground(); Insert(0, grid); grid.StretchToParentSize();
            graphViewChanged = OnGraphViewChanged;
            viewTransformChanged = _ => ViewChanged?.Invoke();
            // 지우기(Delete): 선과 대상 노드만(대상 노드는 그래프에서 뺌). 대본 노드는 지우지 않는다. 복사·붙여넣기는 쓰지 않는다.
            deleteSelection = (operation, askUser) => DeleteElements(selection.OfType<GraphElement>().Where(e => e is Edge || e is ObjectNode).ToList());
            serializeGraphElements = _ => null; canPasteSerializedData = _ => false;
            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0 || !SpaceHeld) return;
                spacePanStart = e.mousePosition; spacePanOrigin = ViewPosition;
                this.CaptureMouse(); e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(e =>
            {
                if (spacePanStart == null) return;
                UpdateViewTransform(spacePanOrigin + (Vector3)(e.mousePosition - spacePanStart.Value), ViewScale);
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(e => { if (spacePanStart == null) return; spacePanStart = null; this.ReleaseMouse(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            // Hierarchy에서 오브젝트를 끌어 오면 대상 노드를 만든다(대상 컴포넌트가 없으면 붙일지 묻는다).
            RegisterCallback<DragUpdatedEvent>(e => { if (DraggedObjects().Any()) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation(); } });
            RegisterCallback<DragPerformEvent>(e =>
            {
                var dropped = DraggedObjects().ToList();
                if (dropped.Count == 0) return;
                DragAndDrop.AcceptDrag(); e.StopPropagation();
                var at = contentViewContainer.WorldToLocal(e.mousePosition);
                foreach (var go in dropped) { AddTarget(go, at); at += new Vector2(0, RowGap); }
            });
        }
        private static IEnumerable<GameObject> DraggedObjects() => DragAndDrop.objectReferences.OfType<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g));

        // ── 데이터 ──
        /// <summary>그래프·대본 목록·씬 대상을 다시 읽고 다시 그린다(확대·이동 상태는 유지). 자리가 없는 노드는 대본 순서대로 놓고 그래프에 저장한다.</summary>
        public void Load(DialogueGraph value)
        {
            graph = value;
            scripts.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:ScenarioScript", new[] { "Assets" }))
            {
                var s = AssetDatabase.LoadAssetAtPath<ScenarioScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) scripts.Add(s);
            }
            scripts.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            duplicateIds.Clear();
            targets = DialogueGraphTargets.InOpenScenes(duplicateIds);
            if (expanded != null && !scripts.Contains(expanded)) { expanded = null; timeline = null; } // 펼친 대본을 지웠으면 접음
            if (graph != null) PlaceMissing();
            Redraw();
        }
        private void Save() { if (graph == null) return; EditorUtility.SetDirty(graph); if (EditorUtility.IsPersistent(graph)) AssetDatabase.SaveAssetIfDirty(graph); }
        // 자리(0,0)가 없는 노드: 대상마다 한 줄(대상 → 대본 순서 오른쪽으로), 남은 대본은 그 아래 줄에 차례로. Undo에 남기지 않는다(처음 배치).
        private void PlaceMissing()
        {
            bool changed = false; float y = 0;
            foreach (var o in graph.Objects) { if (o == null) continue; y = Mathf.Max(y, o.position.y + RowGap); foreach (var s in graph.Chain(o.objectId)) y = Mathf.Max(y, graph.Entry(s).position.y + RowGap); }
            foreach (var o in graph.Objects)
            {
                if (o == null) continue;
                var chain = graph.Chain(o.objectId);
                bool anyNew = o.position == Vector2.zero || chain.Any(s => graph.Entry(s).position == Vector2.zero);
                if (!anyNew) continue;
                var row = o.position != Vector2.zero ? o.position.y : y;
                if (o.position == Vector2.zero) { o.position = new Vector2(0, row); changed = true; }
                for (int i = 0; i < chain.Count; i++)
                {
                    var e = graph.Entry(chain[i]);
                    if (e.position == Vector2.zero) { e.position = new Vector2(o.position.x + ColumnGap * (i + 1), row); changed = true; }
                }
                if (row >= y) y = row + RowGap;
            }
            int column = 0;
            foreach (var s in scripts)
            {
                var e = graph.EntryOf(s);
                if (e != null && e.position != Vector2.zero) continue;
                if (graph.OwnerOf(s) != null) continue;
                graph.Entry(s).position = new Vector2(ColumnGap * (column + 1), y); changed = true;
                if (++column >= 5) { column = 0; y += RowGap; }
            }
            if (changed) Save();
        }
        /// <summary>노드·선을 다시 만든다(그래프·자리·화면 위치는 그대로).</summary>
        public void Redraw()
        {
            rebuilding = true;
            var keepScripts = selection.OfType<ScriptNode>().Select(n => n.Script).ToList();
            var keepObjects = selection.OfType<ObjectNode>().Select(n => n.ObjectId).ToList();
            timeline?.RemoveFromHierarchy(); // 다시 그릴 노드에 옮겨 붙인다
            DeleteElements(graphElements.ToList());
            objectNodes.Clear(); scriptNodes.Clear();
            if (graph != null)
            {
                foreach (var o in graph.Objects) if (o != null && !string.IsNullOrEmpty(o.objectId) && !objectNodes.ContainsKey(o.objectId)) { var n = ObjectElement(o); objectNodes[o.objectId] = n; AddElement(n); }
                foreach (var s in scripts) { var n = ScriptElement(s); scriptNodes[s] = n; AddElement(n); }
                foreach (var edge in Edges()) AddElement(edge);
            }
            foreach (var s in keepScripts) if (scriptNodes.TryGetValue(s, out var n)) AddToSelection(n);
            foreach (var id in keepObjects) if (objectNodes.TryGetValue(id, out var n)) AddToSelection(n);
            ApplyFilter();
            rebuilding = false;
            RefreshIssues();
        }
        private void RefreshIssues()
        {
            Issues.Clear();
            if (graph == null) { Issues.Add("대화 그래프가 없습니다."); IssuesChanged?.Invoke(); return; }
            Issues.AddRange(graph.Problems());
            foreach (var o in graph.Objects)
            {
                if (o == null || string.IsNullOrEmpty(o.objectId)) continue;
                if (!targets.TryGetValue(o.objectId, out var target)) { Issues.Add($"대상 '{o.objectId}'을(를) 열린 씬에서 찾지 못했습니다(다른 씬에 있거나 지워졌을 수 있음)."); continue; }
                // NPC·구역은 대화 대본만 보여 준다(전투 진입 대본이 지금 대본이 되면 반응하지 않고 순서가 멈춤).
                if (target.Kind == DialogueGraphTargets.Kind.Npc || target.Kind == DialogueGraphTargets.Kind.Zone)
                    foreach (var s in graph.Chain(o.objectId))
                        if (!s.IsTalk) Issues.Add($"{DialogueGraphTargets.KindName(target.Kind)} '{o.objectId}'의 순서에 전투 진입 대본 '{s.name}'이(가) 있습니다. NPC·구역은 대화 대본만 보여 줍니다.");
            }
            foreach (var id in duplicateIds.Distinct()) Issues.Add($"열린 씬에 오브젝트 ID '{id}'를 쓰는 대상이 둘 이상입니다.");
            IssuesChanged?.Invoke();
        }

        // ── 노드 ──
        private ObjectNode ObjectElement(DialogueGraphObject o)
        {
            targets.TryGetValue(o.objectId, out var target);
            var node = new ObjectNode { ObjectId = o.objectId, title = target.GameObject != null ? target.GameObject.name : o.objectId };
            node.capabilities &= ~(Capabilities.Copiable | Capabilities.Renamable);
            node.style.minWidth = 200;
            node.SetPosition(new Rect(o.position, Vector2.zero));
            var color = target.Kind == DialogueGraphTargets.Kind.Zone ? ZoneColor : target.Kind == DialogueGraphTargets.Kind.Enemy ? EnemyColor : NpcColor;
            Tint(node, color);
            node.titleContainer.Add(Small(DialogueGraphTargets.KindName(target.Kind), Faint));
            node.First = OutPort(node, "첫 대본", FirstColor, Port.Capacity.Single, new OutSlot { Kind = OutKind.First, ObjectId = o.objectId }, "이 대상의 대본 순서 첫 대본으로 이을 대본의 '앞 대본' 슬롯에 끌어 놓습니다.");
            var extra = Extra();
            extra.Add(Small($"ID {o.objectId}", Faint));
            extra.Add(target.GameObject != null ? Small($"씬 {target.SceneName}", Faint) : Small("⚠ 열린 씬에 없음", ErrorColor));
            node.extensionContainer.Add(extra);
            node.RefreshExpandedState(); node.RefreshPorts();
            return node;
        }
        private ScriptNode ScriptElement(ScenarioScript s)
        {
            var node = new ScriptNode { Script = s, title = s.name };
            node.capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable | Capabilities.Renamable);
            node.style.minWidth = 230; node.style.maxWidth = 300;
            var entry = graph.EntryOf(s);
            node.SetPosition(new Rect(entry != null ? entry.position : Vector2.zero, Vector2.zero));
            Tint(node, s.IsTalk ? TalkColor : BattleColor);
            node.titleContainer.Add(Small((s.IsTalk ? "대화" : "전투 진입") + (s.VersionCount > 1 ? $" · 버전 {s.VersionCount}" : ""), Faint));
            bool open = expanded == s;
            var toggle = new Button(() => { if (open) Collapse(); else Expand(s, false); }) { text = open ? "▾ 접기" : "▸ 펼치기", tooltip = open ? "타임라인을 접습니다." : "노드 안에서 이 대본의 타임라인(대사·지문·감정 표시·연출, 버전)을 편집합니다. 한 번에 하나만 펼칩니다.", style = { fontSize = 10, marginRight = 4 } };
            toggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            node.titleContainer.Add(toggle);
            if (open) { node.style.minWidth = ScenarioTimelineView.MinWidth; node.style.maxWidth = StyleKeyword.None; node.style.width = StyleKeyword.Auto; }
            node.Prev = InPort(node, "앞 대본", NextColor, Port.Capacity.Single, PrevIndex, "대상의 '첫 대본' 또는 앞 대본의 '다음'을 받습니다(하나만).");
            var conditions = graph.ConditionsOf(s);
            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i]; int index = i;
                string name = c.script == null ? "(비어 있음)" : $"{(c.type == DialogueConditionType.Seen ? "봄" : "클리어")}: {c.script.name}";
                var port = InPort(node, name, c.script == null ? ErrorColor : CondColor, Port.Capacity.Single, index, "조건 하나(모두 맞으면 이 대본이 열림, 앞 대본을 봤는지는 따지지 않음). 선을 빼면 지웁니다. 우클릭: 지우기");
                port.AddManipulator(new ContextualMenuManipulator(e => { e.menu.AppendAction("이 조건 지우기", _ => EditGraph("그래프: 조건 지우기", g => g.SetConditions(s, DialogueGraphEdits.EditConditions(g.ConditionsOf(s), new[] { index }, null, null)))); e.StopPropagation(); }));
                node.Conds.Add(port);
            }
            node.AddPort = InPort(node, "+ 조건 추가", EmptyColor, Port.Capacity.Single, AddIndex, "다른 대본의 '봄'(대화 대본) 또는 '클리어'(전투 진입 대본)를 여기에 이으면 조건을 더합니다.");
            node.Next = OutPort(node, "다음", NextColor, Port.Capacity.Single, new OutSlot { Kind = OutKind.Next, Script = s }, "같은 대상 순서의 다음 대본(하나만). 조건이 없는 다음 대본은 이 대본을 보면 열립니다.");
            if (s.IsTalk) node.Seen = OutPort(node, "봄", CondColor, Port.Capacity.Multi, new OutSlot { Kind = OutKind.Seen, Script = s }, "이 대본을 끝까지 봤다 — 다른 대본의 조건 슬롯에 이으면 그 대본이 열리는 조건.");
            else node.ClearPort = OutPort(node, "클리어", CondColor, Port.Capacity.Multi, new OutSlot { Kind = OutKind.Clear, Script = s }, "이 전투 진입 대본을 순서에 둔 적의 스테이지를 클리어했다 — 다른 대본의 조건 슬롯에 잇습니다.");
            if (open) { node.extensionContainer.Add(ExpandedContent(s)); node.RefreshExpandedState(); node.RefreshPorts(); return node; }
            var extra = Extra();
            var first = s.Steps.Where(st => st.type == ScenarioStepType.Line).Select(st => st.line.text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (first != null) extra.Add(new Label("“" + (first.Length > 34 ? first.Substring(0, 34) + "…" : first) + "”") { style = { fontSize = 10, color = new Color(.83f, .84f, .87f), whiteSpace = WhiteSpace.Normal, marginBottom = 2 } });
            var owner = graph.OwnerOf(s);
            if (owner != null) { int n = graph.Chain(owner).IndexOf(s) + 1; extra.Add(Small($"◎ {OwnerName(owner)} · {n}번째", new Color(.73f, .78f, .85f))); }
            else extra.Add(Small("대상 순서에 없음", Faint));
            node.extensionContainer.Add(extra);
            node.RefreshExpandedState(); node.RefreshPorts();
            node.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0 && e.clickCount == 2 && !SpaceHeld) { Expand(s, false); e.StopImmediatePropagation(); } });
            return node;
        }
        // 펼친 노드 안: 대화 대본이면 버전 탭(버전 1·2…, + 버전, 이 버전 지우기, 차례대로·무작위), 그 아래 타임라인.
        private VisualElement ExpandedContent(ScenarioScript s)
        {
            var box = new VisualElement { style = { backgroundColor = new Color(.16f, .165f, .18f), paddingLeft = 4, paddingRight = 4, paddingBottom = 4 } };
            if (timeline == null || timeline.Script != s)
                timeline = new ScenarioTimelineView(s) { Notify = Notify, Created = c => { Load(graph); Expand(c); } };
            expandedShape = Shape(s);
            if (s.IsTalk)
            {
                var tabs = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, paddingTop = 4, paddingBottom = 2 } };
                tabs.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                tabs.Add(Small("버전", Faint));
                for (int v = 0; v < s.VersionCount; v++)
                {
                    int version = v;
                    var tab = new Button(() => { timeline.SetVersion(version); Redraw(); }) { text = $"버전 {v + 1}", tooltip = "이 버전의 대사를 편집합니다. 말할 때마다 버전이 바뀝니다(어느 버전이든 보면 본 것)." };
                    if (timeline.Version == v) tab.style.backgroundColor = new Color(.23f, .3f, .4f);
                    tabs.Add(tab);
                }
                tabs.Add(new Button(() => { int added = -1; EditScript(s, "버전 더하기", x => added = x.AddVersion()); timeline.SetVersion(added); Redraw(); }) { text = "+ 버전", tooltip = "빈 지문 한 줄의 버전을 더합니다(Ctrl+Z)." });
                if (s.VersionCount > 1)
                {
                    int now = timeline.Version;
                    tabs.Add(new Button(() => { EditScript(s, "버전 지우기", x => x.RemoveVersion(now)); timeline.SetVersion(Mathf.Max(0, now - 1), true); Redraw(); }) { text = "이 버전 지우기", tooltip = "지금 버전을 지웁니다(Ctrl+Z)." });
                    tabs.Add(Small("  말할 때마다", Faint));
                    foreach (var (label, order) in new[] { ("차례대로", ScenarioVersionOrder.InOrder), ("무작위", ScenarioVersionOrder.Random) })
                    {
                        var o = order;
                        var b = new Button(() => { EditScript(s, "버전 순서 바꾸기", x => { var opt = x.Options; opt.versionOrder = o; x.SetOptions(opt); }); Redraw(); }) { text = label };
                        if (s.Options.versionOrder == o) b.style.backgroundColor = new Color(.23f, .3f, .4f);
                        tabs.Add(b);
                    }
                }
                box.Add(tabs);
            }
            box.Add(timeline);
            return box;
        }
        // 대본을 고친다(Undo, 바로 저장).
        private void EditScript(ScenarioScript s, string undoName, Action<ScenarioScript> change)
        {
            Undo.RecordObject(s, undoName); change(s);
            EditorUtility.SetDirty(s); if (EditorUtility.IsPersistent(s)) AssetDatabase.SaveAssetIfDirty(s);
        }
        /// <summary>대본 노드를 펼쳐 노드 안에 타임라인을 보인다(다른 노드는 접힘). 그래프 안에서 펼칠 때(frame=false)는 화면·배율을 그대로 두고,
        /// 밖에서 열 때(대본 에셋 더블클릭·Inspector 버튼·복제, frame=true)는 배율은 그대로 두고 그 노드가 화면 위쪽 가운데에 오게 화면만 옮긴다(사용자 요청 2026-10-15).</summary>
        public void Expand(ScenarioScript s, bool frame = true)
        {
            if (s == null || !scriptNodes.ContainsKey(s)) return;
            if (expanded != s) { expanded = s; timeline = null; }
            Redraw();
            if (!scriptNodes.TryGetValue(s, out var node)) return;
            ClearSelection(); AddToSelection(node);
            if (frame) schedule.Execute(() =>
            {
                var view = layout.size; if (float.IsNaN(view.x)) return;
                // 배율은 그대로 두고 화면만 옮긴다(화면 좌표 = 그래프 좌표 × 배율 + 이동).
                var rect = node.GetPosition(); float zoom = Zoom;
                float width = float.IsNaN(node.layout.width) || node.layout.width <= 0 ? ScenarioTimelineView.MinWidth : node.layout.width;
                UpdateViewTransform(new Vector3(view.x * .5f - (rect.x + width * .5f) * zoom, 20 - rect.y * zoom, 0), ViewScale);
            });
        }
        public void Collapse() { expanded = null; timeline = null; Redraw(); }
        private string OwnerName(string objectId) => targets.TryGetValue(objectId, out var t) && t.GameObject != null ? t.GameObject.name : objectId;
        private static void Tint(Node node, Color color)
        {
            node.titleContainer.style.borderBottomWidth = 2; node.titleContainer.style.borderBottomColor = color;
            var label = node.titleContainer.Q<Label>("title-label"); if (label != null) label.style.color = color;
        }
        private static VisualElement Extra() => new VisualElement { style = { paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 5, backgroundColor = new Color(.16f, .165f, .18f) } };
        private static Label Small(string text, Color color) => new Label(text) { style = { fontSize = 10, color = color, marginTop = 1, marginRight = 6, whiteSpace = WhiteSpace.Normal, unityTextAlign = TextAnchor.MiddleLeft } };
        private Port InPort(ScriptNode node, string name, Color color, Port.Capacity capacity, int index, string tip)
        {
            var port = node.InstantiatePort(Orientation.Horizontal, Direction.Input, capacity, typeof(bool));
            port.portName = name; port.portColor = color; port.tooltip = tip; port.userData = new InSlot { Script = node.Script, Index = index };
            node.inputContainer.Add(port);
            return port;
        }
        private static Port OutPort(Node node, string name, Color color, Port.Capacity capacity, OutSlot slot, string tip)
        {
            var port = node.InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, typeof(bool));
            port.portName = name; port.portColor = color; port.tooltip = tip; port.userData = slot;
            node.outputContainer.Add(port);
            return port;
        }

        // ── 선 ──
        private IEnumerable<Edge> Edges()
        {
            foreach (var o in graph.Objects)
                if (o != null && o.first != null && objectNodes.TryGetValue(o.objectId, out var on) && scriptNodes.TryGetValue(o.first, out var fn))
                    yield return on.First.ConnectTo(fn.Prev);
            foreach (var e in graph.Scripts)
            {
                if (e == null || e.script == null || !scriptNodes.TryGetValue(e.script, out var from)) continue;
                if (e.next != null && scriptNodes.TryGetValue(e.next, out var to)) yield return from.Next.ConnectTo(to.Prev);
                var conditions = e.Conditions;
                for (int i = 0; i < conditions.Count && i < from.Conds.Count; i++)
                {
                    var c = conditions[i];
                    if (c.script == null || !scriptNodes.TryGetValue(c.script, out var src)) continue;
                    var output = c.type == DialogueConditionType.Seen ? src.Seen : src.ClearPort;
                    if (output != null) yield return output.ConnectTo(from.Conds[i]);
                }
            }
        }
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var result = new List<Port>();
            foreach (var port in ports.ToList())
            {
                if (port.direction == startPort.direction || port.node == startPort.node) continue;
                var output = startPort.direction == Direction.Output ? startPort : port; var input = startPort.direction == Direction.Output ? port : startPort;
                if (!(output.userData is OutSlot o) || !(input.userData is InSlot i)) continue;
                bool order = o.Kind == OutKind.First || o.Kind == OutKind.Next;
                if (order ? i.Index == PrevIndex : i.Index >= AddIndex) result.Add(port);
            }
            return result;
        }
        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (rebuilding) return change;
            if (change.elementsToRemove != null)
            {
                var removedObjects = change.elementsToRemove.OfType<ObjectNode>().Select(n => n.ObjectId).ToList();
                if (removedObjects.Count > 0) schedule.Execute(() => EditGraph("그래프: 대상 빼기", g => { foreach (var id in removedObjects) g.RemoveObject(id); }));
                change.elementsToRemove.RemoveAll(e => !(e is Edge) && !(e is ObjectNode));
                // 선 지우기 알림 직후 GraphView가 선의 양쪽 슬롯을 비우므로 슬롯 쌍으로 기억한다.
                pendingRemoved.AddRange(change.elementsToRemove.OfType<Edge>().Select(e => (e.output, e.input)));
            }
            if (change.edgesToCreate != null) { pendingCreated.AddRange(change.edgesToCreate); change.edgesToCreate.Clear(); } // 그래프를 고친 뒤 다시 그린다
            // 바꿔 잇기(이미 이어진 슬롯에 새 선)는 '지우기'와 '잇기' 알림이 따로 오므로 같은 프레임의 선 편집을 모아 한 번에 반영한다.
            if ((pendingRemoved.Count > 0 || pendingCreated.Count > 0) && !flushQueued)
            {
                flushQueued = true;
                schedule.Execute(() =>
                {
                    flushQueued = false;
                    var removed = pendingRemoved.ToList(); var created = pendingCreated.Select(e => (e.output, e.input)).ToList();
                    pendingRemoved.Clear(); pendingCreated.Clear();
                    ApplyEdges(removed, created);
                });
            }
            if (change.movedElements != null && change.movedElements.Count > 0) schedule.Execute(SaveMoved);
            return change;
        }
        /// <summary>선 편집을 그래프에 옮긴다(규칙은 DialogueGraphEdits.ApplyLinks — 이은 선은 편집 전 그래프로 검사하고, 막히면 밀려난 옛 선도 그대로 둠, 한 번의 Ctrl+Z). 잇지 못한 이유는 알림으로 보인다.</summary>
        private void ApplyEdges(List<(Port output, Port input)> removed, List<(Port output, Port input)> created)
        {
            string problem = null;
            EditGraph(created.Count > 0 ? "그래프: 선 잇기" : "그래프: 선 지우기", g => problem = DialogueGraphEdits.ApplyLinks(g, Links(removed), Links(created)));
            if (problem != null) Notify(problem);
        }
        private static List<DialogueGraphEdits.Link> Links(List<(Port output, Port input)> ports)
        {
            var list = new List<DialogueGraphEdits.Link>();
            foreach (var (output, input) in ports)
            {
                if (!(output?.userData is OutSlot o) || !(input?.userData is InSlot i)) continue;
                var kind = o.Kind == OutKind.First ? DialogueGraphEdits.LinkKind.First : o.Kind == OutKind.Next ? DialogueGraphEdits.LinkKind.Next : o.Kind == OutKind.Seen ? DialogueGraphEdits.LinkKind.Seen : DialogueGraphEdits.LinkKind.Clear;
                list.Add(new DialogueGraphEdits.Link { Kind = kind, ObjectId = o.ObjectId, From = o.Script, To = i.Script, Slot = i.Index == PrevIndex ? DialogueGraphEdits.PrevSlot : i.Index == AddIndex ? DialogueGraphEdits.AddSlot : i.Index });
            }
            return list;
        }
        /// <summary>그래프를 고치고(Undo, 저장) 다시 그린다.</summary>
        private void EditGraph(string undoName, Action<DialogueGraph> change)
        {
            if (graph == null) return;
            DialogueGraphEdits.Edit(graph, undoName, change);
            Redraw();
        }
        private void Notify(string message) { var window = EditorWindow.focusedWindow; if (window != null) window.ShowNotification(new GUIContent(message)); else Debug.LogWarning("[IBIIIS] " + message); }
        // 옮긴 노드를 격자에 맞춰 그래프에 저장한다(Ctrl+Z).
        private void SaveMoved()
        {
            if (graph == null) return;
            Vector2 Snap(Vector2 p) => new Vector2(Mathf.Round(p.x / Grid) * Grid, Mathf.Round(p.y / Grid) * Grid);
            var moved = new List<(Node node, Vector2 at)>();
            foreach (var n in objectNodes.Values) { var o = graph.ObjectOf(n.ObjectId); var p = Snap(n.GetPosition().position); if (o != null && (o.position - p).sqrMagnitude > .01f) moved.Add((n, p)); }
            foreach (var kv in scriptNodes) { var e = graph.EntryOf(kv.Key); var p = Snap(kv.Value.GetPosition().position); if (e == null || (e.position - p).sqrMagnitude > .01f) moved.Add((kv.Value, p)); }
            if (moved.Count == 0) return;
            DialogueGraphEdits.Edit(graph, "그래프: 노드 옮기기", g =>
            {
                foreach (var (node, at) in moved)
                {
                    if (node is ObjectNode on) { var o = g.ObjectOf(on.ObjectId); if (o != null) o.position = at; }
                    else if (node is ScriptNode sn) g.Entry(sn.Script).position = at;
                }
            });
            foreach (var (node, at) in moved) node.SetPosition(new Rect(at, node.GetPosition().size));
        }

        // ── 대상 추가 ──
        /// <summary>씬 오브젝트를 대상 노드로 그래프에 넣는다. 대상 컴포넌트가 없으면 NPC로 만들지 묻는다. 이미 있으면 그 노드를 고른다.</summary>
        public void AddTarget(GameObject go, Vector2 at)
        {
            if (go == null) return;
            if (graph == null) { Notify("대화 그래프가 없습니다. Overworld Settings의 Dialogue Graph 슬롯을 확인하세요."); return; }
            var target = DialogueGraphTargets.Of(go);
            if (target.Kind == DialogueGraphTargets.Kind.None)
            {
                if (!EditorUtility.DisplayDialog("대화 대상 만들기", $"'{go.name}'에 대화 대상 컴포넌트가 없습니다.\nNPC(NpcSpeaker)로 만들고 ID를 붙일까요? (F로 말을 걸면 대본을 보여 줍니다)", "붙이기", "취소")) return;
                target = DialogueGraphTargets.EnsureTarget(go, graph);
            }
            else if (string.IsNullOrEmpty(target.Id))
            {
                if (target.Kind == DialogueGraphTargets.Kind.Enemy) { Notify($"'{go.name}'의 Stage Id가 비어 있습니다. 적 Inspector에서 먼저 정하세요."); return; }
                target = DialogueGraphTargets.EnsureTarget(go, graph);
            }
            if (graph.HasObject(target.Id)) { Notify($"'{target.Id}'은(는) 이미 그래프에 있습니다."); Load(graph); Select(target.Id); return; }
            var id = target.Id; var snapped = new Vector2(Mathf.Round(at.x / Grid) * Grid, Mathf.Round(at.y / Grid) * Grid);
            DialogueGraphEdits.Edit(graph, "그래프: 대상 넣기", g => g.SetFirst(id, null).position = snapped);
            Load(graph); Select(id);
        }
        /// <summary>새 대화 대본을 만들어 at에 놓는다(저장 창). 만들었으면 그 대본.</summary>
        public ScenarioScript CreateTalk(Vector2 at)
        {
            var path = EditorUtility.SaveFilePanelInProject("새 대화 대본", "NewTalk", "asset", "대화 대본을 저장할 위치를 고르세요.", AssetPaths.Scenarios);
            if (string.IsNullOrEmpty(path)) return null;
            var id = DialogueScriptAssets.SuggestId(System.IO.Path.GetFileNameWithoutExtension(path));
            var script = DialogueScriptAssets.Create(path, id, new[] { new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate("") } }, out var problem);
            if (script == null) { EditorUtility.DisplayDialog("새 대화 대본", problem, "확인"); return null; }
            var options = script.Options; options.use = ScenarioUse.Talk; script.SetOptions(options);
            EditorUtility.SetDirty(script); AssetDatabase.SaveAssetIfDirty(script);
            var snapped = new Vector2(Mathf.Round(at.x / Grid) * Grid, Mathf.Round(at.y / Grid) * Grid);
            if (graph != null) DialogueGraphEdits.Edit(graph, "그래프: 새 대화 대본", g => g.Entry(script).position = snapped);
            Load(graph);
            return script;
        }

        // ── 고르기·보기 ──
        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            if (!rebuilding && (selectable is ScriptNode || selectable is ObjectNode)) SyncSelectionSoon();
        }
        // 그래프에서 고른 대본은 Project 창, 대상은 Hierarchy에서도 고르고 보여 준다(한 프레임에 한 번).
        private void SyncSelectionSoon()
        {
            if (selectionQueued) return;
            selectionQueued = true;
            schedule.Execute(() =>
            {
                selectionQueued = false;
                var picked = new List<UnityEngine.Object>();
                foreach (var n in selection.OfType<ScriptNode>()) picked.Add(n.Script);
                foreach (var n in selection.OfType<ObjectNode>()) if (targets.TryGetValue(n.ObjectId, out var t) && t.GameObject != null) picked.Add(t.GameObject);
                if (picked.Count == 0) return;
                Selection.objects = picked.ToArray(); EditorGUIUtility.PingObject(picked[picked.Count - 1]);
            });
        }
        /// <summary>대상 노드를 고르고 가운데로.</summary>
        public void Select(string objectId) { if (objectNodes.TryGetValue(objectId, out var n)) Focus(n); }
        public void Select(ScenarioScript script) { if (script != null && scriptNodes.TryGetValue(script, out var n)) Focus(n); }
        private void Focus(Node node)
        {
            ClearSelection(); AddToSelection(node);
            schedule.Execute(() =>
            {
                var view = layout.size; if (float.IsNaN(view.x)) return;
                UpdateViewTransform(view * .5f - node.GetPosition().center * Zoom, ViewScale);
            });
        }
        public void SetView(float zoom, Vector2 pan) => UpdateViewTransform(pan, Vector3.one * DialogueZoom.Clamp(zoom));
        public void ZoomAt(float zoom) { var anchor = layout.size * .5f; var at = (anchor - Pan) / Zoom; float next = DialogueZoom.Clamp(zoom); UpdateViewTransform(anchor - at * next, Vector3.one * next); }
        /// <summary>검색어·종류(all·talk·battle·object)로 거른다. 맞지 않는 노드는 흐리게, 검색에 맞는 노드는 제목 줄을 노랗게.</summary>
        public void SetFilter(string text, string kind) { query = text ?? ""; filter = kind ?? "all"; ApplyFilter(); }
        private void ApplyFilter()
        {
            string q = query.Trim(); bool searching = q.Length > 0;
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var kv in scriptNodes)
            {
                var s = kv.Key;
                bool kindOk = filter == "all" || (filter == "talk" && s.IsTalk) || (filter == "battle" && !s.IsTalk);
                bool textOk = !searching || Has(s.name) || Has(s.Id) || Enumerable.Range(0, s.VersionCount).SelectMany(v => s.VersionSteps(v)).Any(st => Has(st.line.text));
                Style(kv.Value, kindOk && textOk, searching && kindOk && textOk);
            }
            foreach (var kv in objectNodes)
            {
                bool kindOk = filter == "all" || filter == "object";
                bool textOk = !searching || Has(kv.Key) || Has(kv.Value.title);
                Style(kv.Value, kindOk && textOk, searching && kindOk && textOk);
            }
        }
        private static void Style(Node node, bool match, bool hit)
        {
            node.style.opacity = match ? 1 : .3f;
            node.titleContainer.style.backgroundColor = hit ? new StyleColor(new Color(.45f, .36f, .12f)) : new StyleColor(StyleKeyword.Null);
        }
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is ScriptNode sn)
            {
                var s = sn.Script;
                evt.menu.AppendAction(expanded == s ? "접기" : "펼쳐서 편집", _ => { if (expanded == s) Collapse(); else Expand(s, false); });
                evt.menu.AppendAction("Project 창에서 보기", _ => { Selection.activeObject = s; EditorGUIUtility.PingObject(s); });
                return;
            }
            if (evt.target is ObjectNode on)
            {
                var id = on.ObjectId;
                if (targets.TryGetValue(id, out var t) && t.GameObject != null) evt.menu.AppendAction("Hierarchy에서 보기", _ => { Selection.activeGameObject = t.GameObject; EditorGUIUtility.PingObject(t.GameObject); });
                evt.menu.AppendAction("그래프에서 빼기", _ => EditGraph("그래프: 대상 빼기", g => g.RemoveObject(id)));
                return;
            }
            if (evt.target is Edge edge) { evt.menu.AppendAction("이 선 지우기", _ => DeleteElements(new GraphElement[] { edge })); return; }
            if (evt.target == this)
            {
                var at = contentViewContainer.WorldToLocal(evt.mousePosition);
                evt.menu.AppendAction("새 대화 대본…", _ => { var s = CreateTalk(at); if (s != null) Select(s); });
                evt.menu.AppendAction("전체 맞춤", _ => FrameAll());
                // 지운 대본을 가리키던 연결 정리(열 때 저절로 하지 않음 — 대본이 잠깐 없을 때 연결을 잃지 않게)
                evt.menu.AppendAction("지운 대본 연결 정리", _ => EditGraph("그래프: 지운 대본 연결 정리", g => g.RemoveMissing()), graph != null && graph.HasMissing ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
    }
}
