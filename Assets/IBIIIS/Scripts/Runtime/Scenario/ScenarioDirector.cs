using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>전투 씬에서 대본(ScenarioScript)을 위에서부터 실행한다. 대사는 미연시 대화창으로 보이고,
    /// 입력 대기 단계에서는 허용한 행동만 전투에 통과시키며(나머지는 무시) 안내 칸을 점멸·키를 표시한다.
    /// 대본이 도는 동안 되돌리기·다시 시작·범위 표시는 막는다. 판정은 바꾸지 않고 입력만 걸러 낸다.</summary>
    public sealed class ScenarioDirector : MonoBehaviour
    {
        /// <summary>대본에서 ID로 부르는 등장 오브젝트(켜고 끄기·감정 표시 대상).</summary>
        [Serializable]
        public struct Actor
        {
            [Tooltip("대본의 Target에 적는 ID. \"player\"는 플레이어로 예약되어 있습니다.")] public string id;
            public GameObject target;
            [Tooltip("감정 표시를 띄울 높이(칸, 발밑 기준 화면 위쪽).")] public float height;
            [Tooltip("체크하면 켤 때 Cell 칸으로 옮기고 그 칸을 막습니다(허수아비처럼 칸만 막는 오브젝트). 끄면 풉니다.")] public bool blocksCell;
            [Tooltip("막을 칸(맵 좌표).")] public Vector2Int cell;
        }
        private sealed class Thrown { public Transform View; public SpriteRenderer Renderer; public Vector3 Start; public float Time, Duration; }
        private sealed class Emote { public Transform Target; public float Height, Left; public bool UntilLine, UntilEnd; public string Text; public Sprite Icon; public VisualElement View; }

        [SerializeField, Tooltip("필수. 대본을 진행할 전투(Grid Map Player).")] private GridMapPlayer battle;
        [SerializeField, Tooltip("필수. 실행할 대본.")] private ScenarioScript script;
        [SerializeField, Tooltip("등장 오브젝트. 대본의 Target ID로 찾습니다.")] private Actor[] actors = Array.Empty<Actor>();
        [SerializeField, Min(0), Tooltip("플레이어 감정 표시 높이(칸). 임시 1.6.")] private float playerEmoteHeight = 1.6f;
        [SerializeField, Tooltip("체크하면 전투가 준비되는 대로 대본을 시작합니다.")] private bool playOnStart = true;
        [SerializeField, Tooltip("체크하면 대본이 도는 동안 적 인식 말풍선(대사)을 띄우지 않습니다. 대본 대사와 겹치지 않게 튜토리얼에서 켭니다.")] private bool hideEnemyAlerts = true;
        [SerializeField, Tooltip("체크하면 대본이 도는 동안 적 인식 범위 안의 위험 그림을 자동으로 보이지 않습니다. 위험 자세는 대본의 Pose 단계로 정합니다.")] private bool scriptControlsDanger = true;
        private int index = -1;
        private bool waitingFirstMove, clicked;
        private int baselineActions;
        private float timer;
        private DialogueController dialogue;
        private readonly List<Emote> emotes = new List<Emote>();
        private readonly List<Thrown> thrown = new List<Thrown>();
        // 자유 진행 경고 대사가 떠 있는 동안 전투 입력을 막는다(경고를 넘기는 키가 대기 등으로 읽혀 턴이 넘어가지 않게).
        private bool warningOpen;
        // 대사를 닫은 프레임. 대사를 넘긴 키(Space 등)가 같은 프레임에 전투 명령으로 읽히지 않게 그 프레임은 입력을 막는다(Play 중).
        private int closedFrame = -1;
        // "다음 대사가 끝날 때까지" 자세를 보이는 중.
        private bool poseUntilLine;
        private readonly HashSet<Vector2Int> triedDirections = new HashSet<Vector2Int>();
        private static readonly Vector2Int[] Cardinal = { Vector2Int.up, Vector2Int.left, Vector2Int.down, Vector2Int.right };
        /// <summary>자유 진행에서 막은(패배할) 행동 수. 테스트·디버그용.</summary>
        public int BlockedDangerousMoves { get; private set; }
        public bool IsRunning { get; private set; }
        /// <summary>마지막 씬 전환 단계가 가리킨 씬(테스트·디버그용).</summary>
        public string LoadedScene { get; private set; }
        public bool Finished { get; private set; }
        public int StepIndex => index;
        public ScenarioStep Current => IsRunning && index >= 0 && index < script.Steps.Length ? script.Steps[index] : default;
        /// <summary>이름 입력 단계에서 받은 값(대사는 바꾸지 않는다).</summary>
        public string EnteredName { get; private set; }
        /// <summary>칸 누르기 단계에서 정답 칸을 누른 순간(연출용, 예: 칼이 날아가는 연출).</summary>
        public event Action<ScenarioStep> CellClicked;
        public DialogueController Dialogue => dialogue;
        public IReadOnlyList<string> ActiveEmotes { get { var list = new List<string>(); foreach (var e in emotes) list.Add(e.Text); return list; } }
        private GameUiSettings Ui => battle != null && battle.SharedPlayerSettings != null ? battle.SharedPlayerSettings.GameUi : null;
        public void Configure(GridMapPlayer value, ScenarioScript tutorial, Actor[] cast = null) { battle = value; script = tutorial; actors = cast ?? Array.Empty<Actor>(); }

        private void Update()
        {
            if (!IsRunning && !Finished && playOnStart && battle != null && battle.Session != null && script != null) Begin();
            Tick(Time.unscaledDeltaTime);
            if (IsRunning && Current.type == ScenarioStepType.WaitClickCell && !clicked)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && battle.TryGetCell(mouse.position.ReadValue(), out var cell)) ClickCell(cell);
            }
        }
        private void LateUpdate() => RenderEmotes();

        /// <summary>대본을 처음부터 시작한다.</summary>
        public void Begin()
        {
            if (battle == null || script == null) { Debug.LogError($"[IBIIIS] {name}: 대본 진행기에 전투(Battle) 또는 대본(Script)이 연결되지 않았습니다.", this); return; }
            IsRunning = true; Finished = false; index = -1;
            battle.AllowUndoRestart = false; battle.AllowRangeToggle = false;
            if (hideEnemyAlerts) battle.AlertsEnabled = false;
            if (scriptControlsDanger) battle.DangerIdleEnabled = false;
            Next();
        }
        /// <summary>시간을 진행하고 현재 단계의 끝 조건을 확인한다(매 프레임, 테스트에서는 직접 호출).</summary>
        public void Tick(float seconds)
        {
            TickEmotes(seconds); TickThrown(seconds);
            if (!IsRunning) return;
            var step = Current; var session = battle.Session;
            bool ready = session != null && !session.IsBusy && !battle.IsPresenting;
            int acted = session != null ? session.UndoCount - baselineActions : 0;
            switch (step.type)
            {
                case ScenarioStepType.WaitAction:
                    if (acted > 0 && ready) Next();
                    break;
                case ScenarioStepType.WaitFreeMove:
                    if (waitingFirstMove && acted > 0) { waitingFirstMove = false; timer = 0; }
                    if (!waitingFirstMove) timer += seconds;
                    if (!waitingFirstMove && timer >= step.seconds && ready) Next();
                    break;
                case ScenarioStepType.WaitReachCell:
                    if (ready && session.Position == step.cell) Next();
                    break;
                case ScenarioStepType.WaitClickCell:
                    if (clicked) { timer += seconds; if (timer >= step.seconds) Next(); }
                    break;
                case ScenarioStepType.Wait:
                    timer += seconds; if (timer >= step.seconds) Next();
                    break;
                case ScenarioStepType.FreePlay:
                    if (!warningOpen && ready && session.Phase == BattlePhase.Won) Next();
                    break;
                case ScenarioStepType.WaitAllDirections:
                    if (triedDirections.Count >= Cardinal.Length && ready) Next();
                    break;
            }
        }
        private bool JustClosed => Application.isPlaying && Time.frameCount == closedFrame;
        // 입력 대기 단계의 판정을 감싼다: 경고가 떠 있거나 방금 대사를 닫은 프레임이면 막는다.
        private Func<PlayerAction, Vector2Int, bool> Guard(Func<PlayerAction, Vector2Int, bool> inner) => (action, direction) => !warningOpen && !JustClosed && inner(action, direction);
        // 자유 진행의 입력 판정: 허용한 행동이고, 했을 때 패배하지 않아야 한다. 패배할 행동이면 막고 경고 대사를 띄운다.
        // 경고는 넘겨야 닫히며(F·Enter·Space·클릭), 그동안과 닫은 프레임에는 어떤 행동도 받지 않아 턴이 넘어가지 않는다.
        private bool AllowSafe(PlayerAction action, Vector2Int direction)
        {
            var step = Current;
            if (!step.Allows(action, direction)) return false;
            if (battle.Session.Predict(action, direction) != BattlePhase.Lost) return true;
            BlockedDangerousMoves++;
            if (!string.IsNullOrEmpty(step.line.text))
            {
                warningOpen = true;
                EnsureDialogue().Open(Ui, new[] { Line(step) }, () => { warningOpen = false; closedFrame = Time.frameCount; }, true);
            }
            return false;
        }
        // 네 방향 누르기: 이동이면서 상하좌우 중 하나면 받아들이고(벽에 막혀 부딪혀도) 누른 방향을 기록한다. 남은 키만 표시한다.
        private bool AllowDirection(PlayerAction action, Vector2Int direction)
        {
            if (action != PlayerAction.Move || Array.IndexOf(Cardinal, direction) < 0) return false;
            if (triedDirections.Add(direction)) ShowRemainingKeys(Current);
            return true;
        }
        private void ShowRemainingKeys(ScenarioStep step)
        {
            var caps = Keys(step); var remaining = new List<string>();
            for (int i = 0; i < Cardinal.Length; i++) if (!triedDirections.Contains(Cardinal[i])) remaining.Add(caps.Length == Cardinal.Length ? caps[i] : Cardinal[i].ToString());
            EnsureDialogue().Prompt(Ui, Line(step), remaining);
        }
        /// <summary>네 방향 누르기 단계에서 지금까지 누른 방향 수(테스트·디버그용).</summary>
        public int TriedDirections => triedDirections.Count;
        /// <summary>칸을 눌렀다(마우스 왼쪽 버튼과 같은 경로). 칸 누르기 단계의 정답 칸이면 연출을 시작한다.</summary>
        public void ClickCell(Vector2Int cell)
        {
            if (!IsRunning || clicked || Current.type != ScenarioStepType.WaitClickCell || cell != Current.cell) return;
            clicked = true; timer = 0; battle.SetGuideCells(null);
            if (Current.sprite != null) Throw(Current.sprite, Current.seconds);
            CellClicked?.Invoke(Current);
        }

        private void Next()
        {
            EndStep();
            index++;
            if (index >= script.Steps.Length) { Finish(); return; }
            StartStep(script.Steps[index]);
        }
        private void EndStep()
        {
            battle.SetGuideCells(null); battle.CommandFilter = DenyAll;
            clicked = false; warningOpen = false; triedDirections.Clear(); waitingFirstMove = false; timer = 0;
        }
        private static bool DenyAll(PlayerAction action, Vector2Int direction) => false;
        private void StartStep(ScenarioStep step)
        {
            baselineActions = battle.Session != null ? battle.Session.UndoCount : 0;
            switch (step.type)
            {
                case ScenarioStepType.Line:
                    // 대본은 한 줄씩 대화창을 다시 여므로, 좌우에 선 인물이 매 줄 사라지지 않게 이어 붙인다.
                    EnsureDialogue().Open(Ui, new[] { Line(step) }, OnLineDone, true);
                    break;
                case ScenarioStepType.Emote:
                    AddEmote(step); Next();
                    break;
                case ScenarioStepType.WaitAction:
                    Prompt(step);
                    battle.CommandFilter = Guard(step.Allows);
                    if (step.direction != Vector2Int.zero && battle.Session != null)
                        battle.SetGuideCells(new[] { battle.Session.Position + step.direction * ((step.actions & ScenarioActions.Dash) != 0 ? 2 : 1) });
                    break;
                case ScenarioStepType.WaitFreeMove:
                    Prompt(step); battle.CommandFilter = Guard(step.Allows); waitingFirstMove = true;
                    break;
                case ScenarioStepType.WaitReachCell:
                    Prompt(step); battle.CommandFilter = Guard(step.Allows); battle.SetGuideCells(new[] { step.cell });
                    break;
                case ScenarioStepType.WaitClickCell:
                    Prompt(step); battle.SetGuideCells(new[] { step.cell });
                    break;
                case ScenarioStepType.NameInput:
                    EnsureDialogue().AskName(Ui, Line(step), GameText.GetShort(Table, step.id + ".default", step.text), value => { EnteredName = value; Next(); });
                    break;
                case ScenarioStepType.SetActive:
                    var actor = FindActor(step.target);
                    if (actor.target != null)
                    {
                        if (actor.blocksCell)
                        {
                            if (step.active) { actor.target.transform.position = battle.CellWorldPosition(actor.cell); if (!battle.SetCellBlocked(actor.cell, true)) Debug.LogWarning($"[IBIIIS] {name}: '{step.target}'이 막을 칸 {actor.cell}을 막지 못했습니다(이동 불가·플레이어·적이 있는 칸).", this); }
                            else battle.SetCellBlocked(actor.cell, false);
                        }
                        actor.target.SetActive(step.active);
                    }
                    else Debug.LogWarning($"[IBIIIS] {name}: 대본의 켜고 끄기 대상 '{step.target}'을 Actors에서 찾지 못했습니다.", this);
                    Next();
                    break;
                case ScenarioStepType.SpawnEnemy:
                    if (!battle.SpawnEnemy(step.prefab, step.cell, step.direction))
                        Debug.LogWarning($"[IBIIIS] {name}: 대본의 적 합류({(step.prefab != null ? step.prefab.name : "프리팹 없음")}, {step.cell})에 실패했습니다(빈 이동 칸·상하좌우 방향·유효한 적 프리팹 필요).", this);
                    Next();
                    break;
                case ScenarioStepType.FreePlay:
                    EnsureDialogue().Hide();
                    battle.CommandFilter = Guard(AllowSafe);
                    break;
                case ScenarioStepType.WaitAllDirections:
                    ShowRemainingKeys(step); battle.CommandFilter = Guard(AllowDirection);
                    break;
                case ScenarioStepType.Pose:
                    if (step.sprite == null) Debug.LogWarning($"[IBIIIS] {name}: 대본의 자세 단계에 그림이 없습니다.", this);
                    else if (step.untilEnd) { battle.PlayPose(step.sprite, step.motion, 0, step.endMotion); poseUntilLine = false; } // 표시 끝 단계가 끝낸다
                    else { battle.PlayPose(step.sprite, step.motion, step.untilAction ? 0 : step.seconds, step.endMotion, step.untilAction); poseUntilLine = !step.untilAction && step.seconds <= 0; }
                    Next();
                    break;
                case ScenarioStepType.EndDisplay:
                    if (step.display == ScenarioDisplay.Pose) { poseUntilLine = false; battle.EndPose(); }
                    else
                    {
                        var ended = EmoteTarget(step.target);
                        emotes.RemoveAll(e => { if (e.Target == ended) { e.View?.RemoveFromHierarchy(); return true; } return false; });
                    }
                    Next();
                    break;
                case ScenarioStepType.Wait:
                    break;
                case ScenarioStepType.LoadScene:
                    IsRunning = false; Finished = true; LoadedScene = step.scenePath;
                    // 씬 전환은 Play 중에만 한다(에디터 테스트에서는 기록만).
                    if (Application.isPlaying && !StageFlow.LoadScene(step.scenePath)) { Debug.LogError($"[IBIIIS] {name}: 대본의 다음 씬 '{step.scenePath}'을 불러오지 못했습니다.", this); Restore(); }
                    break;
                case ScenarioStepType.RecordClear:
                    try { ProgressStore.MarkCleared(step.text); }
                    catch (Exception e) { Debug.LogException(e); }
                    Next();
                    break;
            }
        }
        private void OnLineDone()
        {
            closedFrame = Time.frameCount;
            if (poseUntilLine) { poseUntilLine = false; battle.EndPose(); }
            // 다 본 대사와 함께 "다음 대사까지" 감정 표시를 지운다.
            emotes.RemoveAll(e => { if (e.UntilLine) { e.View?.RemoveFromHierarchy(); return true; } return false; });
            Next();
        }
        private void Prompt(ScenarioStep step)
        {
            var caps = Keys(step);
            if (string.IsNullOrEmpty(step.line.text) && caps.Length == 0) { EnsureDialogue().Hide(); return; }
            EnsureDialogue().Prompt(Ui, Line(step), caps);
        }
        // 번역: 대본 표(Scenario_대본 ID)에서 단계 ID로 찾는다. 키 표시는 한글 등이 있을 때만(예: "좌클릭") 번역한다.
        private string Table => GameText.Scenario(script.Id);
        private DialogueLine Line(ScenarioStep step) => step.line.Localized(Table, step.id);
        private string[] Keys(ScenarioStep step)
        {
            var text = GameText.GetShort(Table, step.id + ".keys", step.keys);
            return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
        private DialogueController EnsureDialogue()
        {
            if (dialogue == null) dialogue = DialogueController.Play(Ui, null);
            return dialogue;
        }
        private void Finish()
        {
            IsRunning = false; Finished = true;
            EnsureDialogue().Hide();
            Restore();
        }
        private void Restore() { battle.CommandFilter = null; battle.SetGuideCells(null); battle.AllowUndoRestart = true; battle.AllowRangeToggle = true; battle.AlertsEnabled = true; battle.DangerIdleEnabled = true; }
        private Actor FindActor(string id)
        {
            foreach (var actor in actors) if (actor.id == id) return actor;
            return default;
        }

        private Transform EmoteTarget(string id)
        {
            if (id == "player") return battle.PlayerAnchor;
            var actor = FindActor(id); return actor.target != null ? actor.target.transform : null;
        }
        private void AddEmote(ScenarioStep step)
        {
            Transform target; float height;
            if (step.target == "player") { target = battle.PlayerAnchor; height = playerEmoteHeight; }
            else { var actor = FindActor(step.target); target = actor.target != null ? actor.target.transform : null; height = actor.height > 0 ? actor.height : playerEmoteHeight; }
            if (target == null) { Debug.LogWarning($"[IBIIIS] {name}: 감정 표시 대상 '{step.target}'을 찾지 못했습니다.", this); return; }
            emotes.RemoveAll(e => { if (e.Target == target) { e.View?.RemoveFromHierarchy(); return true; } return false; });
            emotes.Add(new Emote { Target = target, Height = height, Text = step.text, Icon = step.sprite, Left = step.seconds, UntilEnd = step.untilEnd, UntilLine = !step.untilEnd && step.seconds <= 0 });
        }
        private void TickEmotes(float seconds)
        {
            for (int i = emotes.Count - 1; i >= 0; i--)
            {
                var e = emotes[i];
                if (e.UntilLine || e.UntilEnd) continue;
                e.Left -= seconds;
                if (e.Left <= 0 || e.Target == null) { e.View?.RemoveFromHierarchy(); emotes.RemoveAt(i); }
            }
        }
        // 감정 표시는 말풍선 없이 아이콘만(그림이 있으면 그림, 없으면 글자) 전투 HUD의 말풍선 층에 그린다. 모양은 GameUi.uss의 emote-icon. 화면이 없으면 그리지 않는다.
        private void RenderEmotes()
        {
            var layer = battle != null && battle.Hud != null ? battle.Hud.Root.Q("bubbles") : null;
            var camera = battle != null ? battle.ViewCamera : null;
            if (layer == null || layer.panel == null || camera == null) return;
            foreach (var e in emotes)
            {
                if (e.Target == null) continue;
                if (e.View == null)
                {
                    if (e.Icon != null) { e.View = new VisualElement(); e.View.AddToClassList("image"); e.View.style.backgroundImage = new StyleBackground(e.Icon); }
                    else e.View = new Label(e.Text);
                    e.View.AddToClassList("emote-icon"); e.View.pickingMode = PickingMode.Ignore;
                    layer.Add(e.View);
                }
                var world = e.Target.position + camera.transform.up * e.Height * battle.CellSize;
                bool visible = camera.WorldToScreenPoint(world).z > 0;
                e.View.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                var point = RuntimePanelUtils.CameraTransformWorldToPanel(layer.panel, world, camera);
                e.View.style.left = point.x; e.View.style.top = point.y;
            }
        }
        // 칸 누르기 연출: 그림이 플레이어에게서 화면 오른쪽 위로 빙글빙글 돌며 날아가 흐려진다(표시 전용, 예: 공격 실패로 칼이 날아감).
        private void Throw(Sprite sprite, float duration)
        {
            var anchor = battle.PlayerAnchor;
            if (anchor == null) return;
            var go = new GameObject("Thrown " + sprite.name); go.transform.SetParent(battle.Generated, true);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 5;
            var start = anchor.position + (battle.ViewCamera != null ? battle.ViewCamera.transform.up : Vector3.up) * .6f * battle.CellSize;
            go.transform.position = start; go.transform.localScale = Vector3.one * .6f * battle.CellSize;
            thrown.Add(new Thrown { View = go.transform, Renderer = renderer, Start = start, Duration = Mathf.Max(.1f, duration) });
        }
        private void TickThrown(float seconds)
        {
            var camera = battle != null ? battle.ViewCamera : null;
            for (int i = thrown.Count - 1; i >= 0; i--)
            {
                var t = thrown[i];
                if (t.View == null) { thrown.RemoveAt(i); continue; }
                t.Time += seconds; float u = Mathf.Clamp01(t.Time / t.Duration);
                Vector3 right = camera != null ? camera.transform.right : Vector3.right, up = camera != null ? camera.transform.up : Vector3.up;
                t.View.position = t.Start + (right * 1.4f * u + up * (2.2f * u - 1.2f * u * u)) * battle.CellSize;
                t.View.rotation = (camera != null ? camera.transform.rotation : Quaternion.identity) * Quaternion.Euler(0, 0, -720 * u);
                t.Renderer.color = new Color(1, 1, 1, 1 - Mathf.Clamp01((u - .6f) / .4f));
                if (u >= 1) { RuntimeObjects.Release(t.View.gameObject); thrown.RemoveAt(i); }
            }
        }
        /// <summary>지금 날아가는 연출 수(테스트·디버그용).</summary>
        public int ThrownCount => thrown.Count;
        private void OnDisable()
        {
            foreach (var e in emotes) e.View?.RemoveFromHierarchy(); emotes.Clear();
            foreach (var t in thrown) if (t.View != null) RuntimeObjects.Release(t.View.gameObject); thrown.Clear();
        }
    }
}
