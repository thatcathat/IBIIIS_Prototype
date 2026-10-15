using UnityEngine;

namespace IBIIIS
{
    /// <summary>말을 거는 NPC(대화 대상). Npc Id가 대화 그래프(DialogueProgress.Graph)의 오브젝트 ID이며, 상호작용 키를 누르면
    /// 그래프의 대본 순서에서 지금 대본(기획서 7.6)을 미연시 대화창으로 보여 준다(그동안 플레이어 이동을 막음). 대본은 이 컴포넌트가 아니라 그래프에서 연결한다.
    /// 그래프에 이 ID가 없거나 보여 줄 대본이 없으면 반응하지 않는다.</summary>
    public sealed class NpcSpeaker : OverworldInteractable
    {
        [SerializeField, Tooltip("이 NPC의 고유 ID(대화 그래프의 오브젝트 ID, 번역 표 NPC의 키). 어드벤처씬 전체에서 겹치면 안 되며 정한 뒤 바꾸지 않습니다.")] private string npcId;
        [SerializeField, Tooltip("NPC 이름(번역 표 NPC의 'ID.name'). 대사의 이름표는 대본의 인물·이름표 덮어쓰기가 정합니다.")] private string displayName = "NPC";
        private DialogueController talking;
        public const string NpcTable = "NPC";
        public string NpcId => npcId;
        public string DisplayName => string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(displayName) ? displayName : GameText.Get(NpcTable, npcId + ".name", displayName);
        /// <summary>번역 표 동기화용 원문.</summary>
        public string SourceName => displayName;
        /// <summary>대화 그래프에 이 NPC(Npc Id)가 있으면 true.</summary>
        public bool UsesGraph => !string.IsNullOrEmpty(npcId) && DialogueProgress.Graph != null && DialogueProgress.Graph.HasObject(npcId);
        /// <summary>지금 말할 대본(그래프에 없거나 순서가 비었으면 null).</summary>
        public ScenarioScript CurrentScript => UsesGraph ? DialogueProgress.Current(DialogueProgress.Graph, npcId) : null;
        /// <summary>대화창으로 대화 중이면 true.</summary>
        public bool InDialogue => talking != null && talking.IsOpen;
        // 대화 대본이 아닌 대본이 지금 대본이면 말 걸기 안내를 띄우지 않는다(대화로 보여 줄 수 없음 — 그래프 검사가 알림).
        public override string PromptVerb => InDialogue || CurrentScript == null || !CurrentScript.IsTalk ? null : UiText.PromptTalk.Text;
        public override void Interact(OverworldPlayer player)
        {
            if (InDialogue) return;
            var script = CurrentScript;
            if (script != null) talking = AdventureDialogue.PlayTalk(script, player, null);
        }
    }
}
