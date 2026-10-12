using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>전투 씬에서 대본(TutorialScript)을 위에서부터 실행한다. 대사는 미연시 대화창으로 보이고,
    /// 입력 대기 단계에서는 허용한 행동만 전투에 통과시키며(나머지는 무시) 안내 칸을 점멸·키를 표시한다.
    /// 대본이 도는 동안 되돌리기·다시 시작·범위 표시는 막는다. 판정은 바꾸지 않고 입력만 걸러 낸다.</summary>
    public sealed class TutorialDirector : MonoBehaviour
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
        private sealed class Emote { public Transform Target; public float Height, Left; public bool UntilLine; public string Text; public VisualElement View; }

        [SerializeField, Tooltip("필수. 대본을 진행할 전투(Grid Map Player).")] private GridMapPlayer battle;
        [SerializeField, Tooltip("필수. 실행할 대본.")] private TutorialScript script;
        [SerializeField, Tooltip("등장 오브젝트. 대본의 Target ID로 찾습니다.")] private Actor[] actors = Array.Empty<Actor>();
        [SerializeField, Min(0), Tooltip("플레이어 감정 표시 높이(칸). 임시 1.6.")] private float playerEmoteHeight = 1.6f;
        [SerializeField, Tooltip("체크하면 전투가 준비되는 대로 대본을 시작합니다.")] private bool playOnStart = true;
        private int index = -1;
        private bool waitingFirstMove, clicked;
        private int baselineActions;
        private float timer;
        private DialogueController dialogue;
        private readonly List<Emote> emotes = new List<Emote>();
        private readonly List<Thrown> thrown = new List<Thrown>();
        private float warningLeft;
        /// <summary>자유 진행에서 막은(패배할) 행동 수. 테스트·디버그용.</summary>
        public int BlockedDangerousMoves { get; private set; }
        public bool IsRunning { get; private set; }
        public bool Finished { get; private set; }
        public int StepIndex => index;
        public TutorialStep Current => IsRunning && index >= 0 && index < script.Steps.Length ? script.Steps[index] : default;
        /// <summary>이름 입력 단계에서 받은 값(대사는 바꾸지 않는다).</summary>
        public string EnteredName { get; private set; }
        /// <summary>칸 누르기 단계에서 정답 칸을 누른 순간(연출용, 예: 칼이 날아가는 연출).</summary>
        public event Action<TutorialStep> CellClicked;
        public DialogueController Dialogue => dialogue;
        public IReadOnlyList<string> ActiveEmotes { get { var list = new List<string>(); foreach (var e in emotes) list.Add(e.Text); return list; } }
        private GameUiSettings Ui => battle != null && battle.SharedPlayerSettings != null ? battle.SharedPlayerSettings.GameUi : null;
        public void Configure(GridMapPlayer value, TutorialScript tutorial, Actor[] cast = null) { battle = value; script = tutorial; actors = cast ?? Array.Empty<Actor>(); }

        private void Update()
        {
            if (!IsRunning && !Finished && playOnStart && battle != null && battle.Session != null && script != null) Begin();
            Tick(Time.unscaledDeltaTime);
            if (IsRunning && Current.type == TutorialStepType.WaitClickCell && !clicked)
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
                case TutorialStepType.WaitAction:
                    if (acted > 0 && ready) Next();
                    break;
                case TutorialStepType.WaitFreeMove:
                    if (waitingFirstMove && acted > 0) { waitingFirstMove = false; timer = 0; }
                    if (!waitingFirstMove) timer += seconds;
                    if (!waitingFirstMove && timer >= step.seconds && ready) Next();
                    break;
                case TutorialStepType.WaitReachCell:
                    if (ready && session.Position == step.cell) Next();
                    break;
                case TutorialStepType.WaitClickCell:
                    if (clicked) { timer += seconds; if (timer >= step.seconds) Next(); }
                    break;
                case TutorialStepType.Wait:
                    timer += seconds; if (timer >= step.seconds) Next();
                    break;
                case TutorialStepType.FreePlay:
                    if (warningLeft > 0) { warningLeft -= seconds; if (warningLeft <= 0) EnsureDialogue().Hide(); }
                    if (ready && session.Phase == BattlePhase.Won) Next();
                    break;
            }
        }
        // 자유 진행의 입력 판정: 허용한 행동이고, 했을 때 패배하지 않아야 한다. 패배할 행동이면 막고 경고를 보인다.
        private bool AllowSafe(PlayerAction action, Vector2Int direction)
        {
            var step = Current;
            if (!step.Allows(action, direction)) return false;
            if (battle.Session.Predict(action, direction) != BattlePhase.Lost) return true;
            BlockedDangerousMoves++;
            if (!string.IsNullOrEmpty(step.line.text)) { EnsureDialogue().Prompt(Ui, step.line, null); warningLeft = step.seconds > 0 ? step.seconds : 1.5f; }
            return false;
        }
        /// <summary>칸을 눌렀다(마우스 왼쪽 버튼과 같은 경로). 칸 누르기 단계의 정답 칸이면 연출을 시작한다.</summary>
        public void ClickCell(Vector2Int cell)
        {
            if (!IsRunning || clicked || Current.type != TutorialStepType.WaitClickCell || cell != Current.cell) return;
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
            clicked = false; waitingFirstMove = false; timer = 0;
        }
        private static bool DenyAll(PlayerAction action, Vector2Int direction) => false;
        private void StartStep(TutorialStep step)
        {
            baselineActions = battle.Session != null ? battle.Session.UndoCount : 0;
            switch (step.type)
            {
                case TutorialStepType.Line:
                    // 대본은 한 줄씩 대화창을 다시 여므로, 좌우에 선 인물이 매 줄 사라지지 않게 이어 붙인다.
                    EnsureDialogue().Open(Ui, new[] { step.line }, OnLineDone, true);
                    break;
                case TutorialStepType.Emote:
                    AddEmote(step); Next();
                    break;
                case TutorialStepType.WaitAction:
                    Prompt(step);
                    battle.CommandFilter = step.Allows;
                    if (step.direction != Vector2Int.zero && battle.Session != null)
                        battle.SetGuideCells(new[] { battle.Session.Position + step.direction * ((step.actions & TutorialActions.Dash) != 0 ? 2 : 1) });
                    break;
                case TutorialStepType.WaitFreeMove:
                    Prompt(step); battle.CommandFilter = step.Allows; waitingFirstMove = true;
                    break;
                case TutorialStepType.WaitReachCell:
                    Prompt(step); battle.CommandFilter = step.Allows; battle.SetGuideCells(new[] { step.cell });
                    break;
                case TutorialStepType.WaitClickCell:
                    Prompt(step); battle.SetGuideCells(new[] { step.cell });
                    break;
                case TutorialStepType.NameInput:
                    EnsureDialogue().AskName(Ui, step.line, step.text, value => { EnteredName = value; Next(); });
                    break;
                case TutorialStepType.SetActive:
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
                case TutorialStepType.SpawnEnemy:
                    if (!battle.SpawnEnemy(step.prefab, step.cell, step.direction))
                        Debug.LogWarning($"[IBIIIS] {name}: 대본의 적 합류({(step.prefab != null ? step.prefab.name : "프리팹 없음")}, {step.cell})에 실패했습니다(빈 이동 칸·상하좌우 방향·유효한 적 프리팹 필요).", this);
                    Next();
                    break;
                case TutorialStepType.FreePlay:
                    EnsureDialogue().Hide(); warningLeft = 0;
                    battle.CommandFilter = AllowSafe;
                    break;
                case TutorialStepType.Wait:
                    break;
                case TutorialStepType.LoadScene:
                    IsRunning = false; Finished = true; Restore();
                    if (!StageFlow.LoadScene(step.scenePath)) Debug.LogError($"[IBIIIS] {name}: 대본의 다음 씬 '{step.scenePath}'을 불러오지 못했습니다.", this);
                    break;
            }
        }
        private void OnLineDone()
        {
            // 다 본 대사와 함께 "다음 대사까지" 감정 표시를 지운다.
            emotes.RemoveAll(e => { if (e.UntilLine) { e.View?.RemoveFromHierarchy(); return true; } return false; });
            Next();
        }
        private void Prompt(TutorialStep step)
        {
            if (string.IsNullOrEmpty(step.line.text) && step.KeyCaps.Length == 0) { EnsureDialogue().Hide(); return; }
            EnsureDialogue().Prompt(Ui, step.line, step.KeyCaps);
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
        private void Restore() { battle.CommandFilter = null; battle.SetGuideCells(null); battle.AllowUndoRestart = true; battle.AllowRangeToggle = true; }
        private Actor FindActor(string id)
        {
            foreach (var actor in actors) if (actor.id == id) return actor;
            return default;
        }

        private void AddEmote(TutorialStep step)
        {
            Transform target; float height;
            if (step.target == "player") { target = battle.PlayerAnchor; height = playerEmoteHeight; }
            else { var actor = FindActor(step.target); target = actor.target != null ? actor.target.transform : null; height = actor.height > 0 ? actor.height : playerEmoteHeight; }
            if (target == null) { Debug.LogWarning($"[IBIIIS] {name}: 감정 표시 대상 '{step.target}'을 찾지 못했습니다.", this); return; }
            emotes.RemoveAll(e => { if (e.Target == target) { e.View?.RemoveFromHierarchy(); return true; } return false; });
            emotes.Add(new Emote { Target = target, Height = height, Text = step.text, Left = step.seconds, UntilLine = step.seconds <= 0 });
        }
        private void TickEmotes(float seconds)
        {
            for (int i = emotes.Count - 1; i >= 0; i--)
            {
                var e = emotes[i];
                if (e.UntilLine) continue;
                e.Left -= seconds;
                if (e.Left <= 0 || e.Target == null) { e.View?.RemoveFromHierarchy(); emotes.RemoveAt(i); }
            }
        }
        // 감정 표시는 전투 HUD의 말풍선 층에 말풍선 화면 구성(emote 클래스)으로 그린다. 화면이 없으면 그리지 않는다.
        private void RenderEmotes()
        {
            var layer = battle != null && battle.Hud != null ? battle.Hud.Root.Q("bubbles") : null;
            var ui = Ui; var camera = battle != null ? battle.ViewCamera : null;
            if (layer == null || layer.panel == null || ui == null || ui.SpeechBubble == null || camera == null) return;
            foreach (var e in emotes)
            {
                if (e.Target == null) continue;
                if (e.View == null)
                {
                    e.View = ui.SpeechBubble.Instantiate().Q("bubble");
                    if (e.View == null) continue;
                    e.View.AddToClassList("emote"); e.View.pickingMode = PickingMode.Ignore;
                    var label = e.View.Q<Label>("text"); if (label != null) label.text = e.Text;
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
