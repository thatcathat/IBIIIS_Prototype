using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대화 한 줄. 말하는 인물이 비어 있으면 지문(상황 설명)으로 보인다.</summary>
    [Serializable]
    public struct DialogueLine
    {
        [Tooltip("말하는 인물. 비우면 지문(이름표 없이 기울어진 글씨)으로 보입니다.")] public DialogueCharacter speaker;
        [Tooltip("선택. 표정 ID. 비우면 기본 초상화.")] public string expression;
        [Tooltip("선택. 이름표에 인물 이름 대신 보일 이름(예: 정체를 모를 때 '???(어씨파리)').")] public string nameOverride;
        [TextArea(1, 4)] public string text;
        public static DialogueLine Say(DialogueCharacter speaker, string text, string expression = null) => new DialogueLine { speaker = speaker, text = text, expression = expression };
        public static DialogueLine Narrate(string text) => new DialogueLine { text = text };
    }
    /// <summary>대화 하나(대사 목록). NPC 등에 연결해 미연시 대화창으로 보여 준다(표시 전용).</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Dialogue Script")]
    public sealed class DialogueScript : ScriptableObject
    {
        [SerializeField, Tooltip("이 대화의 고정 ID. '본 대화' 기록 등이 이 값으로 연결되므로 정한 뒤 바꾸지 않습니다.")] private string scriptId = "dialogue";
        [SerializeField, Tooltip("위에서부터 한 줄씩 보여 줍니다.")] private DialogueLine[] lines = Array.Empty<DialogueLine>();
        public string Id => scriptId;
        public DialogueLine[] Lines => lines ?? Array.Empty<DialogueLine>();
        public void Set(string id, DialogueLine[] value) { scriptId = id; lines = value ?? Array.Empty<DialogueLine>(); }
    }
}
