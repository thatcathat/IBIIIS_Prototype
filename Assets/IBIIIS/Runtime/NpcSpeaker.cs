using UnityEngine;

namespace IBIIIS
{
    /// <summary>말을 거는 NPC. Dialogue를 연결하면 미연시 대화창으로 대화하고(그동안 플레이어 이동을 막음),
    /// 비우면 머리 위 말풍선에 Lines를 한 줄씩 보여 준다(상호작용 키마다 다음 줄, 마지막 줄 다음·범위를 벗어나면 닫힘).</summary>
    public sealed class NpcSpeaker : OverworldInteractable
    {
        [SerializeField, Tooltip("말풍선 위에 표시할 이름. 비우면 이름 없이 대사만 표시합니다.")] private string displayName = "NPC";
        [SerializeField, TextArea(1, 3), Tooltip("말풍선 대사. 위에서부터 한 줄(항목)씩 표시합니다.")] private string[] lines = { "안녕!" };
        [SerializeField, Min(80), Tooltip("말풍선 최대 너비(화면 픽셀)")] private float bubbleWidth = 320;
        [SerializeField, Tooltip("선택. 미연시 대화창으로 보여 줄 대화. 연결하면 말풍선 대사(Lines) 대신 이것을 씁니다.")] private DialogueScript dialogue;
        private DialogueController talking;
        public DialogueScript Dialogue => dialogue;
        /// <summary>대화창으로 대화 중이면 true.</summary>
        public bool InDialogue => talking != null && talking.IsOpen;
        private int lineIndex = -1;
        /// <summary>지금 보여 주는 대사 위치. 닫혀 있으면 -1.</summary>
        public int LineIndex => lineIndex;
        public bool IsTalking => lineIndex >= 0 && lines != null && lineIndex < lines.Length;
        public string CurrentLine => IsTalking ? lines[lineIndex] : null;
        public string DisplayName => displayName;
        public override string PromptVerb => dialogue != null ? (InDialogue ? null : "말 걸기") : lines == null || lines.Length == 0 ? null : !IsTalking ? "말 걸기" : lineIndex < lines.Length - 1 ? "다음" : "닫기";
        public override void Interact(OverworldPlayer player)
        {
            if (dialogue != null)
            {
                if (InDialogue) return;
                player?.SetInputLocked(true);
                var ui = player != null && player.Settings != null ? player.Settings.GameUi : null;
                talking = DialogueController.Play(ui, dialogue.Lines, () => { if (player != null) player.SetInputLocked(false); });
                return;
            }
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
