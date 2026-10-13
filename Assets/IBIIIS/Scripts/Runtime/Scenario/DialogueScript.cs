using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대화 한 줄. 말하는 인물이 비어 있으면 지문(상황 설명)으로 보인다.</summary>
    [Serializable]
    public struct DialogueLine
    {
        [HideInInspector, Tooltip("대화 에셋 안 줄의 고유 ID(자동). 번역 표의 키. 대본 단계 안의 줄은 단계 ID를 쓰므로 비어 있다.")] public string id;
        [Tooltip("말하는 인물. 비우면 지문(이름표 없이 기울어진 글씨)으로 보입니다.")] public DialogueCharacter speaker;
        [Tooltip("선택. 표정 ID. 비우면 기본 초상화.")] public string expression;
        [Tooltip("선택. 이름표에 인물 이름 대신 보일 이름(예: 정체를 모를 때 '???(어씨파리)').")] public string nameOverride;
        [TextArea(1, 4)] public string text;
        public static DialogueLine Say(DialogueCharacter speaker, string text, string expression = null) => new DialogueLine { speaker = speaker, text = text, expression = expression };
        public static DialogueLine Narrate(string text) => new DialogueLine { text = text };
        /// <summary>지금 언어로 바꾼 사본. 본문은 table의 key, 이름표 덮어쓰기는 key.name(있을 때만). 인물 이름은 인물 표에서 따로 찾는다.</summary>
        public DialogueLine Localized(string table, string key)
        {
            var copy = this;
            copy.text = GameText.Get(table, key, text);
            if (!string.IsNullOrEmpty(nameOverride)) copy.nameOverride = GameText.Get(table, key + ".name", nameOverride);
            return copy;
        }
    }
    /// <summary>대화 하나(대사 목록). NPC 등에 연결해 미연시 대화창으로 보여 준다(표시 전용).</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Dialogue Script")]
    public sealed class DialogueScript : ScriptableObject
    {
        [SerializeField, Tooltip("이 대화의 고정 ID. '본 대화' 기록 등이 이 값으로 연결되므로 정한 뒤 바꾸지 않습니다.")] private string scriptId = "dialogue";
        [SerializeField, Tooltip("위에서부터 한 줄씩 보여 줍니다.")] private DialogueLine[] lines = Array.Empty<DialogueLine>();
        public string Id => scriptId;
        public DialogueLine[] Lines => lines ?? Array.Empty<DialogueLine>();
        /// <summary>번역 표 이름(Dialogue_대화 ID).</summary>
        public string TableName => "Dialogue_" + scriptId;
        /// <summary>지금 언어로 바꾼 줄들(줄 ID가 키).</summary>
        public DialogueLine[] LocalizedLines() => Array.ConvertAll(Lines, l => l.Localized(TableName, l.id));
        public void Set(string id, DialogueLine[] value) { scriptId = id; lines = value ?? Array.Empty<DialogueLine>(); EnsureLineIds(); }
        /// <summary>비었거나 앞 줄과 겹치는 줄 ID를 새로 채운다. 바뀌었으면 true.</summary>
        public bool EnsureLineIds()
        {
            var seen = new System.Collections.Generic.HashSet<string>(); bool changed = false;
            for (int i = 0; i < Lines.Length; i++)
                if (string.IsNullOrEmpty(lines[i].id) || !seen.Add(lines[i].id)) { lines[i].id = Guid.NewGuid().ToString("N"); seen.Add(lines[i].id); changed = true; }
            return changed;
        }
        // Inspector에서 줄을 추가·복제하면 ID가 비거나 겹치므로 고친다.
        private void OnValidate() => EnsureLineIds();
    }
}
