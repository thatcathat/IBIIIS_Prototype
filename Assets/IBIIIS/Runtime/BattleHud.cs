using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>전투 HUD에 보일 값. GridMapPlayer가 매 프레임 만들고, 바뀌었을 때만 화면을 고친다. 표시 전용이며 판정에 쓰지 않는다.</summary>
    public struct BattleHudState : IEquatable<BattleHudState>
    {
        public string StageName;
        public int AliveEnemies, TotalEnemies, Actions;
        /// <summary>회피기(대시·구르기)를 다시 쓸 수 있을 때까지 남은 일반 행동 수. 0이면 사용 가능.</summary>
        public int EvasionCooldown;
        /// <summary>입력을 받는 중(행동·연출 중이 아님)이면 true. 버튼은 이때만 누를 수 있다.</summary>
        public bool Ready;
        public bool CanUndo, RangesOn;
        public BattlePhase Phase;
        public Vector2Int Cell;
        public bool Equals(BattleHudState o) => StageName == o.StageName && AliveEnemies == o.AliveEnemies && TotalEnemies == o.TotalEnemies && Actions == o.Actions &&
            EvasionCooldown == o.EvasionCooldown && Ready == o.Ready && CanUndo == o.CanUndo && RangesOn == o.RangesOn && Phase == o.Phase && Cell == o.Cell;
        public override bool Equals(object obj) => obj is BattleHudState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(AliveEnemies, Actions, EvasionCooldown, Ready, CanUndo, RangesOn, Phase, Cell);
    }
    /// <summary>전투 HUD 화면(UXML)에 값을 채우고 버튼을 연결한다. 버튼은 키보드 입력과 같은 GridMapPlayer 기능을 부른다.
    /// 화면 구성에 없는 요소는 경고 후 건너뛴다.</summary>
    public sealed class BattleHud
    {
        private const string Screen = "전투 HUD";
        private readonly Label stageName, enemies, actions, cooldown, caption, controls, debug;
        private readonly VisualElement evasionIcon;
        private readonly Button undo, restart, ranges;
        private readonly Action onUndo, onRestart, onToggleRanges;
        private readonly string rangesKey;
        private readonly bool showDebug;
        private BattleHudState shown; private bool hasShown;
        public VisualElement Root { get; }

        /// <param name="keys">조작 안내 문구(현재 키 배치 반영). undoKey 등은 버튼 글자 옆에 붙일 키 이름.</param>
        public BattleHud(VisualElement root, string keys, string undoKey, string restartKey, string rangesKey, Sprite evasionSprite, bool showDebug,
            Action onUndo, Action onRestart, Action onToggleRanges)
        {
            Root = root; this.rangesKey = rangesKey; this.showDebug = showDebug;
            this.onUndo = onUndo; this.onRestart = onRestart; this.onToggleRanges = onToggleRanges;
            stageName = GameUiSettings.Find<Label>(root, "stage-name", Screen);
            enemies = GameUiSettings.Find<Label>(root, "enemies", Screen);
            actions = GameUiSettings.Find<Label>(root, "actions", Screen);
            evasionIcon = GameUiSettings.Find<VisualElement>(root, "evasion-icon", Screen);
            cooldown = GameUiSettings.Find<Label>(root, "evasion-cooldown", Screen);
            caption = root?.Q<Label>("evasion-caption");
            controls = GameUiSettings.Find<Label>(root, "controls", Screen);
            debug = root?.Q<Label>("debug");
            undo = GameUiSettings.Find<Button>(root, "undo", Screen);
            restart = GameUiSettings.Find<Button>(root, "restart", Screen);
            ranges = GameUiSettings.Find<Button>(root, "ranges", Screen);
            if (evasionIcon != null && evasionSprite != null) evasionIcon.style.backgroundImage = new StyleBackground(evasionSprite);
            if (caption != null) caption.text = "회피";
            if (controls != null) controls.text = keys;
            if (debug != null) debug.style.display = showDebug ? DisplayStyle.Flex : DisplayStyle.None;
            if (undo != null) { undo.text = Labelled("되돌리기", undoKey); undo.clicked += () => Press("undo"); }
            if (restart != null) { restart.text = Labelled("다시 시작", restartKey); restart.clicked += () => Press("restart"); }
            if (ranges != null) ranges.clicked += () => Press("ranges");
        }
        private static string Labelled(string text, string key) => string.IsNullOrEmpty(key) ? text : $"{text} [{key}]";
        /// <summary>버튼 기능을 실행한다(마우스 클릭과 같은 경로). 누를 수 없는 상태면 무시한다.</summary>
        public void Press(string button)
        {
            if (!hasShown || !shown.Ready) return;
            switch (button)
            {
                case "undo": if (shown.CanUndo) onUndo?.Invoke(); break;
                case "restart": onRestart?.Invoke(); break;
                case "ranges": onToggleRanges?.Invoke(); break;
            }
        }
        /// <summary>값이 바뀌었을 때만 화면을 고친다.</summary>
        public void Refresh(in BattleHudState s)
        {
            if (hasShown && s.Equals(shown)) return;
            shown = s; hasShown = true;
            if (stageName != null) stageName.text = s.StageName;
            if (enemies != null) enemies.text = $"남은 적 {s.AliveEnemies}/{s.TotalEnemies}";
            if (actions != null) actions.text = $"행동 {s.Actions}";
            bool locked = s.EvasionCooldown > 0;
            evasionIcon?.EnableInClassList("locked", locked);
            if (cooldown != null) { cooldown.text = locked ? s.EvasionCooldown.ToString() : ""; cooldown.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None; }
            undo?.SetEnabled(s.Ready && s.CanUndo);
            restart?.SetEnabled(s.Ready);
            if (ranges != null) { ranges.SetEnabled(s.Ready); ranges.text = Labelled(s.RangesOn ? "범위 표시 켬" : "범위 표시 끔", rangesKey); ranges.EnableInClassList("on", s.RangesOn); }
            if (debug != null && showDebug) debug.text = $"{s.Phase} | 칸 ({s.Cell.x}, {s.Cell.y})";
        }
    }
}
