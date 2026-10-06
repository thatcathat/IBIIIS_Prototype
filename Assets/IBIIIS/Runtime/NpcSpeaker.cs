using UnityEngine;

namespace IBIIIS
{
    /// <summary>말을 걸면 머리 위 말풍선에 대사를 한 줄씩 보여 주는 NPC(임시 말풍선, 대화창은 미구현).
    /// 상호작용 키를 누를 때마다 다음 줄로 넘어가고, 마지막 줄 다음에 닫힌다. 플레이어가 범위를 벗어나도 닫힌다.</summary>
    public sealed class NpcSpeaker : OverworldInteractable
    {
        [SerializeField, Tooltip("말풍선 위에 표시할 이름. 비우면 이름 없이 대사만 표시합니다.")] private string displayName = "NPC";
        [SerializeField, TextArea(1, 3), Tooltip("말풍선 대사. 위에서부터 한 줄(항목)씩 표시합니다.")] private string[] lines = { "안녕!" };
        [SerializeField, Min(80), Tooltip("말풍선 최대 너비(화면 픽셀)")] private float bubbleWidth = 320;
        private int lineIndex = -1;
        /// <summary>지금 보여 주는 대사 위치. 닫혀 있으면 -1.</summary>
        public int LineIndex => lineIndex;
        public bool IsTalking => lineIndex >= 0 && lines != null && lineIndex < lines.Length;
        public string CurrentLine => IsTalking ? lines[lineIndex] : null;
        public string DisplayName => displayName;
        public override string PromptVerb => lines == null || lines.Length == 0 ? null : !IsTalking ? "말 걸기" : lineIndex < lines.Length - 1 ? "다음" : "닫기";
        public override void Interact(OverworldPlayer player)
        {
            if (lines == null || lines.Length == 0) return;
            lineIndex = lineIndex + 1 < lines.Length ? lineIndex + 1 : -1;
        }
        public override void OnLeft() { lineIndex = -1; }
        private void OnGUI()
        {
            if (!IsTalking || !OverworldGui.ToGui(Camera.main, LabelPosition, out var anchor)) return;
            var text = string.IsNullOrEmpty(displayName) ? CurrentLine : $"<b>{displayName}</b>\n{CurrentLine}";
            OverworldGui.Bubble.richText = true;
            OverworldGui.BoxAbove(anchor, text, OverworldGui.Bubble, bubbleWidth);
        }
    }
}
