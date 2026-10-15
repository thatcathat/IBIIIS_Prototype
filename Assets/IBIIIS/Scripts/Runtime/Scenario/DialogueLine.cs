using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대사 한 줄(대본 단계의 줄). 말하는 인물이 비어 있으면 지문(상황 설명)으로 보인다. 번역 키는 그 단계의 ID다.</summary>
    [Serializable]
    public struct DialogueLine
    {
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
}
