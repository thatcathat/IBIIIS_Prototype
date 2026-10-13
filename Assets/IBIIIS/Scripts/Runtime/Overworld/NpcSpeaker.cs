using UnityEngine;

namespace IBIIIS
{
    /// <summary>말을 거는 NPC. Dialogue를 연결하면 미연시 대화창으로 대화하고(그동안 플레이어 이동을 막음),
    /// 비우면 머리 위 말풍선에 Lines를 한 줄씩 보여 준다(상호작용 키마다 다음 줄, 마지막 줄 다음·범위를 벗어나면 닫힘).
    /// Cleared Stage Id의 클리어 기록이 있으면 '클리어 후' 대사·대화를 같은 규칙으로 쓴다(둘 다 비어 있으면 기본 대사).</summary>
    public sealed class NpcSpeaker : OverworldInteractable
    {
        [SerializeField, Tooltip("번역 표(NPC)의 키에 쓰는 고유 ID. 미니맵 전체에서 겹치면 안 되며 정한 뒤 바꾸지 않습니다. 비우면 말풍선 이름·대사를 번역하지 않습니다.")] private string npcId;
        [SerializeField, Tooltip("말풍선 위에 표시할 이름. 비우면 이름 없이 대사만 표시합니다.")] private string displayName = "NPC";
        [SerializeField, TextArea(1, 3), Tooltip("말풍선 대사. 위에서부터 한 줄(항목)씩 표시합니다.")] private string[] lines = { "안녕!" };
        [SerializeField, Min(80), Tooltip("말풍선 최대 너비(화면 픽셀)")] private float bubbleWidth = 320;
        [SerializeField, Tooltip("선택. 미연시 대화창으로 보여 줄 대화. 연결하면 말풍선 대사(Lines) 대신 이것을 씁니다.")] private DialogueScript dialogue;
        [Header("클리어 후")]
        [SerializeField, Tooltip("선택. 이 클리어 기록 ID(스테이지 입구의 Stage Id 또는 대본의 클리어 기록 ID)가 있으면 아래 클리어 후 대사·대화를 씁니다. 비우면 항상 기본 대사.")] private string clearedStageId;
        [SerializeField, TextArea(1, 3), Tooltip("클리어 후 말풍선 대사. 위에서부터 한 줄씩. 클리어 후 대화(Cleared Dialogue)를 연결하면 쓰지 않습니다.")] private string[] clearedLines = new string[0];
        [SerializeField, Tooltip("선택. 클리어 후 미연시 대화창으로 보여 줄 대화. 연결하면 클리어 후 말풍선 대사 대신 이것을 씁니다.")] private DialogueScript clearedDialogue;
        private DialogueController talking;
        /// <summary>지금 쓰는 대화(클리어 여부에 따라). 없으면 말풍선 대사를 쓴다.</summary>
        public DialogueScript Dialogue => UsesCleared ? clearedDialogue : dialogue;
        /// <summary>클리어 기록이 있어 클리어 후 대사·대화를 쓰는 중이면 true.</summary>
        public bool UsesCleared => !string.IsNullOrEmpty(clearedStageId) && (clearedDialogue != null || (clearedLines != null && clearedLines.Length > 0)) && ProgressStore.IsCleared(clearedStageId);
        public string ClearedStageId => clearedStageId;
        // 지금 쓰는 말풍선 대사와 번역 키 앞부분(기본: NPC ID.순번, 클리어 후: NPC ID.cleared.순번).
        private string[] Lines => UsesCleared ? clearedLines : lines;
        private string KeyPrefix => UsesCleared ? npcId + ".cleared" : npcId;
        /// <summary>대화창으로 대화 중이면 true.</summary>
        public bool InDialogue => talking != null && talking.IsOpen;
        private int lineIndex = -1;
        /// <summary>지금 보여 주는 대사 위치. 닫혀 있으면 -1.</summary>
        public int LineIndex => lineIndex;
        public bool IsTalking => lineIndex >= 0 && Lines != null && lineIndex < Lines.Length;
        /// <summary>지금 언어의 대사(NPC 표, 키 = NPC ID.순번). 순서를 바꾸면 그 자리 원문이 바뀌므로 번역도 다시 확인 대상이 된다.</summary>
        public string CurrentLine => IsTalking ? (string.IsNullOrEmpty(npcId) ? Lines[lineIndex] : GameText.Get(NpcTable, $"{KeyPrefix}.{lineIndex}", Lines[lineIndex])) : null;
        public string DisplayName => string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(displayName) ? displayName : GameText.Get(NpcTable, npcId + ".name", displayName);
        public const string NpcTable = "NPC";
        public string NpcId => npcId;
        /// <summary>번역 표 동기화용 원문.</summary>
        public string SourceName => displayName;
        public string[] SourceLines => lines ?? new string[0];
        public string[] SourceClearedLines => clearedLines ?? new string[0];
        public DialogueScript DefaultDialogue => dialogue;
        public DialogueScript ClearedDialogue => clearedDialogue;
        public override string PromptVerb => Dialogue != null ? (InDialogue ? null : UiText.PromptTalk.Text) : Lines == null || Lines.Length == 0 ? null : !IsTalking ? UiText.PromptTalk.Text : lineIndex < Lines.Length - 1 ? UiText.PromptNext.Text : UiText.PromptClose.Text;
        public override void Interact(OverworldPlayer player)
        {
            var current = Dialogue;
            if (current != null)
            {
                if (InDialogue) return;
                player?.SetInputLocked(true);
                var ui = player != null && player.Settings != null ? player.Settings.GameUi : null;
                talking = DialogueController.Play(ui, current.LocalizedLines(), () => { if (player != null) player.SetInputLocked(false); });
                return;
            }
            var shown = Lines;
            if (shown == null || shown.Length == 0) return;
            lineIndex = lineIndex + 1 < shown.Length ? lineIndex + 1 : -1;
        }
        public override void OnLeft() { lineIndex = -1; }
        private void OnGUI()
        {
            if (!IsTalking || !OverworldGui.ToGui(Camera.main, LabelPosition, out var anchor)) return;
            var text = string.IsNullOrEmpty(displayName) ? CurrentLine : $"<b>{DisplayName}</b>\n{CurrentLine}";
            OverworldGui.Bubble.richText = true;
            OverworldGui.BoxAbove(anchor, text, OverworldGui.Bubble, bubbleWidth);
        }
    }
}
