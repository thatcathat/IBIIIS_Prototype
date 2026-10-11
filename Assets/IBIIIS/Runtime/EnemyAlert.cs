using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>머리 위에 뜬 말풍선 하나. Speaker가 -1이면 플레이어, 아니면 GridSession.Enemies 인덱스.</summary>
    public sealed class SpeechBubble
    {
        public const int Player = -1;
        public int Speaker { get; internal set; }
        public string Text { get; internal set; }
        /// <summary>뜨기까지 남은 시간(초). 0보다 크면 아직 보이지 않는다(적의 대답 대기).</summary>
        public float Delay { get; internal set; }
        /// <summary>뜬 뒤 지난 시간(초).</summary>
        public float Time { get; internal set; }
        public float Duration { get; internal set; }
        public bool Shown => Delay <= 0 && Time < Duration;
        internal VisualElement View;
    }
    /// <summary>전투 인식 말풍선(표시 전용). 입력 대기·승패 상태에서 적이 플레이어를 새로 인식하면 대사 쌍 하나를 골라
    /// 플레이어 말풍선을 띄우고, 잠시 뒤 새로 인식한 적 모두가 대답한다. 인식에서 벗어나면 그 적이 혼잣말을 한다.
    /// 행동 진행 중에는 판정이 확정되지 않았으므로 말풍선을 모두 지우고, 행동이 끝난 뒤 행동 전 인식 상태와 비교해 반응한다. 판정에는 관여하지 않는다.
    /// 화면이 연결되지 않으면(AttachView 전·Game UI 없음) 말풍선 상태만 계산하고 그리지 않는다.</summary>
    public sealed class EnemyAlert : IDisposable
    {
        private readonly EnemyAlertSettings settings;
        private readonly Transform parent, player;
        private readonly Camera camera;
        private readonly float cellSize;
        private readonly System.Random random;
        private readonly Dictionary<int, bool> recognizing = new Dictionary<int, bool>();
        private readonly List<SpeechBubble> bubbles = new List<SpeechBubble>();
        private readonly List<int> spotted = new List<int>(), lost = new List<int>();
        // 대사 목록마다 바로 직전에 고른 번호(실행 중 상태. 에셋은 바꾸지 않는다). 키: (대사 에셋, 0=인식 1=놓침)
        private readonly Dictionary<(BattleDialogue, int), int> lastPick = new Dictionary<(BattleDialogue, int), int>();
        private VisualElement layer;
        private VisualTreeAsset template;
        private AudioSource audio;

        /// <param name="player">플레이어 말풍선 기준점(발밑). 없으면 플레이어 말은 생략한다.</param>
        /// <param name="random">선택. 대사 고르기용(테스트에서 고정).</param>
        public EnemyAlert(EnemyAlertSettings settings, Transform parent, Camera camera, float cellSize, Transform player = null, System.Random random = null)
        {
            this.settings = settings; this.parent = parent; this.camera = camera; this.cellSize = cellSize; this.player = player;
            this.random = random ?? new System.Random();
        }
        private bool Active => settings != null && settings.Enabled;
        /// <summary>지금 살아 있는 말풍선(대기 중인 대답 포함).</summary>
        public IReadOnlyList<SpeechBubble> Bubbles => bubbles;
        /// <summary>새로 인식해 대화가 시작된 횟수(효과음 횟수와 같음, 테스트·디버그용).</summary>
        public int ExchangesPlayed { get; private set; }
        /// <summary>말풍선을 그릴 화면 층과 말풍선 한 개의 화면 구성. 없으면 그리지 않는다.</summary>
        public void AttachView(VisualElement bubbleLayer, VisualTreeAsset bubbleTemplate)
        {
            ClearBubbles(); layer = bubbleLayer; template = bubbleTemplate;
        }

        /// <summary>매 프레임 호출한다. show가 false(행동 진행 중 등)면 말풍선을 모두 지우고 인식 상태는 유지한다. skip이 true인 적(충돌 연출 중)과 죽은 적의 말풍선도 지운다.</summary>
        public void Update(float seconds, GridSession session, IReadOnlyList<EnemyDefinition> views, bool show, Func<int, bool> skip)
        {
            if (!Active || session == null || views == null) { ClearBubbles(); return; }
            if (!show) { ClearBubbles(); ForgetDead(session); return; }
            Advance(seconds);
            spotted.Clear(); lost.Clear();
            for (int i = 0; i < views.Count && i < session.Enemies.Count; i++)
            {
                var state = session.Enemies[i];
                if (views[i] == null) continue;
                if (!state.Alive || (skip != null && skip(i))) { RemoveBubbles(i); if (!state.Alive) recognizing[i] = false; continue; }
                bool now = session.IsRecognizing(state);
                recognizing.TryGetValue(i, out bool before);
                if (now && !before) spotted.Add(i); else if (!now && before) lost.Add(i);
                recognizing[i] = now;
            }
            if (spotted.Count > 0) StartExchange(views);
            foreach (var i in lost)
            {
                var line = Pick(DialogueFor(views, i), 1);
                RemoveBubbles(i);
                if (!string.IsNullOrEmpty(line)) Add(i, line, 0, settings.LostLineTime);
            }
            Render(views);
        }
        // 대사 쌍은 첫 번째로 새로 인식한 적의 목록에서 고른다. 같은 목록을 쓰는 적은 같은 대답을, 다른 목록을 쓰는 적은 자기 목록의 대답을 한다.
        private void StartExchange(IReadOnlyList<EnemyDefinition> views)
        {
            var firstDialogue = DialogueFor(views, spotted[0]);
            var exchange = PickExchange(firstDialogue);
            bool playerSpeaks = player != null && !string.IsNullOrEmpty(exchange.player);
            RemoveBubbles(SpeechBubble.Player);
            if (playerSpeaks) Add(SpeechBubble.Player, exchange.player, 0, settings.SpottedTime);
            float delay = playerSpeaks ? settings.ReplyDelay : 0;
            foreach (var i in spotted)
            {
                var dialogue = DialogueFor(views, i);
                var reply = dialogue == firstDialogue ? exchange.enemy : PickExchange(dialogue).enemy;
                RemoveBubbles(i);
                if (!string.IsNullOrEmpty(reply)) Add(i, reply, delay, settings.SpottedTime);
            }
            ExchangesPlayed++;
            RuntimeObjects.PlayOneShot(ref audio, parent, "Alert Audio", settings.AlertSound, settings.AlertVolume);
        }
        private BattleDialogue DialogueFor(IReadOnlyList<EnemyDefinition> views, int i)
            => views[i] != null && views[i].Dialogue != null ? views[i].Dialogue : settings.Dialogue;
        private DialogueExchange PickExchange(BattleDialogue dialogue)
        {
            if (dialogue == null || dialogue.Spotted.Length == 0) return default;
            return dialogue.Spotted[PickIndex(dialogue, 0, dialogue.Spotted.Length)];
        }
        private string Pick(BattleDialogue dialogue, int kind)
        {
            if (dialogue == null || dialogue.Lost.Length == 0) return null;
            return dialogue.Lost[PickIndex(dialogue, kind, dialogue.Lost.Length)];
        }
        // 무작위로 고르되, 체크되어 있으면 바로 직전 번호는 빼고 고른다.
        private int PickIndex(BattleDialogue dialogue, int kind, int count)
        {
            var key = (dialogue, kind);
            bool hasLast = lastPick.TryGetValue(key, out int last);
            int index;
            if (dialogue.NoImmediateRepeat && hasLast && count > 1 && last < count) { index = random.Next(count - 1); if (index >= last) index++; }
            else index = random.Next(count);
            lastPick[key] = index; return index;
        }
        private void Add(int speaker, string text, float delay, float duration)
            => bubbles.Add(new SpeechBubble { Speaker = speaker, Text = text, Delay = delay, Duration = duration });
        private void Advance(float seconds)
        {
            for (int b = bubbles.Count - 1; b >= 0; b--)
            {
                var bubble = bubbles[b];
                if (bubble.Delay > 0) { bubble.Delay -= seconds; if (bubble.Delay < 0) { bubble.Time = -bubble.Delay; bubble.Delay = 0; } }
                else bubble.Time += seconds;
                if (bubble.Delay <= 0 && bubble.Time >= bubble.Duration) { Release(bubble); bubbles.RemoveAt(b); }
            }
        }
        private void RemoveBubbles(int speaker)
        {
            for (int b = bubbles.Count - 1; b >= 0; b--) if (bubbles[b].Speaker == speaker) { Release(bubbles[b]); bubbles.RemoveAt(b); }
        }
        private void ClearBubbles() { foreach (var bubble in bubbles) Release(bubble); bubbles.Clear(); }
        private static void Release(SpeechBubble bubble) { bubble.View?.RemoveFromHierarchy(); bubble.View = null; }
        private void ForgetDead(GridSession session) { for (int i = 0; i < session.Enemies.Count; i++) if (!session.Enemies[i].Alive) recognizing[i] = false; }

        // 말풍선을 말하는 이의 머리 위(화면 위쪽 방향)에 붙인다. 꼬리 끝이 기준점에 오도록 USS에서 아래 가운데를 기준으로 둔다.
        private void Render(IReadOnlyList<EnemyDefinition> views)
        {
            if (layer == null || template == null || layer.panel == null || camera == null) return;
            var up = camera.transform.up;
            foreach (var bubble in bubbles)
            {
                Transform anchor = bubble.Speaker == SpeechBubble.Player ? player : bubble.Speaker < views.Count && views[bubble.Speaker] != null ? views[bubble.Speaker].transform : null;
                float height = bubble.Speaker == SpeechBubble.Player ? settings.PlayerHeight : settings.Height;
                var world = anchor != null ? anchor.position + up * height * cellSize : Vector3.zero;
                bool visible = bubble.Shown && anchor != null && camera.WorldToScreenPoint(world).z > 0;
                if (!visible) { if (bubble.View != null) bubble.View.style.display = DisplayStyle.None; continue; }
                if (bubble.View == null) bubble.View = CreateView(bubble);
                if (bubble.View == null) continue;
                bubble.View.style.display = DisplayStyle.Flex;
                var point = RuntimePanelUtils.CameraTransformWorldToPanel(layer.panel, world, camera);
                bubble.View.style.left = point.x; bubble.View.style.top = point.y;
                bubble.View.style.scale = new Scale(Vector3.one * PopScale(bubble.Time));
                bubble.View.style.opacity = Mathf.Clamp01((1 - bubble.Time / bubble.Duration) / .25f);
            }
        }
        private VisualElement CreateView(SpeechBubble bubble)
        {
            var view = template.Instantiate().Q("bubble");
            if (view == null) { Debug.LogWarning("[IBIIIS] 말풍선 화면 구성에 'bubble' 요소가 없어 말풍선을 그리지 않습니다."); template = null; return null; }
            var label = view.Q<Label>("text"); if (label != null) label.text = bubble.Text;
            view.EnableInClassList("player", bubble.Speaker == SpeechBubble.Player);
            view.pickingMode = PickingMode.Ignore;
            layer.Add(view); return view;
        }
        // 튀어나옴: 앞 절반에 0에서 최대 배율까지 커지고 뒤 절반에 1로 줄어든다. 끝나면 1.
        private float PopScale(float time)
        {
            if (time < 0 || time >= settings.PopTime) return 1;
            float u = time / settings.PopTime;
            return u < .5f ? Mathf.Lerp(0, settings.PopScale, u / .5f) : Mathf.Lerp(settings.PopScale, 1, (u - .5f) / .5f);
        }
        /// <summary>되돌리기 등으로 상태가 순간 바뀌었을 때, 말풍선·효과음 없이 현재 인식 상태로 맞춘다.</summary>
        public void Resync(GridSession session)
        {
            ClearBubbles(); recognizing.Clear();
            if (session != null) for (int i = 0; i < session.Enemies.Count; i++) recognizing[i] = session.Enemies[i].Alive && session.IsRecognizing(session.Enemies[i]);
        }
        public void Dispose()
        {
            ClearBubbles();
            if (audio != null) { MotionEffects.Release(audio.gameObject); audio = null; }
        }
    }
}
