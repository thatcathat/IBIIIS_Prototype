using System;
using UnityEngine;

namespace IBIIIS
{
    public enum DialogueSide { Left, Right }
    /// <summary>표정 하나: 대사에서 부를 ID와 초상화.</summary>
    [Serializable]
    public struct DialogueExpression
    {
        [Tooltip("대사에서 부르는 표정 ID(예: angry, surprised). 바꾸면 그 ID를 쓰는 대사도 고쳐야 합니다.")] public string id;
        public Sprite portrait;
    }
    /// <summary>대화창에 나오는 인물(표시 전용). 이름표, 기본 초상화와 표정별 초상화, 대화창에서 서는 쪽을 정한다.
    /// 초상화 규격(임시 제안): 투명 PNG, 상반신, 높이 약 1024px, 기준점 아래 가운데. 대화창이 아래를 화면 하단에 맞춰 높이에 맞게 줄인다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Dialogue Character")]
    public sealed class DialogueCharacter : ScriptableObject
    {
        [SerializeField, Tooltip("이 인물의 고정 ID. 대사·기록이 이 값으로 연결되므로 정한 뒤 바꾸지 않습니다.")] private string characterId = "character";
        [SerializeField, Tooltip("이름표에 보일 이름.")] private string displayName = "이름";
        [SerializeField, Tooltip("이름표 글자색.")] private Color nameColor = Color.white;
        [SerializeField, Tooltip("대화창에서 서는 쪽. 플레이어는 왼쪽, 상대는 오른쪽이 기본입니다.")] private DialogueSide side = DialogueSide.Right;
        [SerializeField, Tooltip("선택. 기본 초상화. 비우면 초상화 없이 이름만 보입니다.")] private Sprite portrait;
        [SerializeField, Tooltip("선택. 표정별 초상화. 대사에서 표정 ID를 지정하면 이 목록에서 찾고, 없으면 기본 초상화를 씁니다.")] private DialogueExpression[] expressions = Array.Empty<DialogueExpression>();
        public string Id => characterId;
        public string DisplayName => displayName;
        /// <summary>지금 언어의 이름(인물 표, 키 = 인물 ID).</summary>
        public string LocalizedName => GameText.Get(GameText.CharactersTable, characterId, displayName);
        public Color NameColor => nameColor;
        public DialogueSide Side => side;
        /// <summary>표정 ID의 초상화. ID가 비었거나 없으면 기본 초상화.</summary>
        public Sprite Portrait(string expression)
        {
            if (!string.IsNullOrEmpty(expression) && expressions != null)
                foreach (var e in expressions) if (e.id == expression && e.portrait != null) return e.portrait;
            return portrait;
        }
        /// <summary>코드·테스트·기본 에셋 생성에서 내용을 채운다.</summary>
        public void Set(string id, string name, DialogueSide standSide, Sprite defaultPortrait, Color color, DialogueExpression[] faces = null)
        { characterId = id; displayName = name; side = standSide; portrait = defaultPortrait; nameColor = color; expressions = faces ?? Array.Empty<DialogueExpression>(); }
    }
}
