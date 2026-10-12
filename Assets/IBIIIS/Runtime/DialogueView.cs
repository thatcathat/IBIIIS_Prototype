using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>미연시 대화창 화면(표시 전용). 하단 대화 상자(이름표·본문·다음 표시)와 그 뒤 좌우 초상화에 DialogueRunner 상태를 그린다.
    /// 말하는 쪽 초상화는 밝게(.speaking), 듣는 쪽은 어둡게(.listening) 표시한다. 대화 상자를 클릭하면 onClick을 부른다.
    /// 화면 구성에 없는 요소는 경고 후 건너뛴다.</summary>
    public sealed class DialogueView
    {
        private const string Screen = "대화창";
        private readonly VisualElement root, left, right, box;
        private readonly Label nameLabel, text, next;
        public VisualElement Root => root;
        public DialogueView(VisualElement root, Action onClick)
        {
            this.root = root;
            left = GameUiSettings.Find<VisualElement>(root, "portrait-left", Screen);
            right = GameUiSettings.Find<VisualElement>(root, "portrait-right", Screen);
            box = GameUiSettings.Find<VisualElement>(root, "box", Screen);
            nameLabel = GameUiSettings.Find<Label>(root, "name", Screen);
            text = GameUiSettings.Find<Label>(root, "text", Screen);
            next = root?.Q<Label>("next");
            box?.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            Hide();
        }
        public void Hide() { if (root != null) root.style.display = DisplayStyle.None; }
        /// <summary>현재 상태를 그린다(매 프레임). time은 다음 표시 깜빡임용.</summary>
        public void Show(DialogueRunner runner, float time)
        {
            if (root == null) return;
            if (runner == null || !runner.IsOpen) { Hide(); return; }
            root.style.display = DisplayStyle.Flex;
            Portrait(left, runner.Left, runner.LeftExpression, runner.ActiveSide == DialogueSide.Left);
            Portrait(right, runner.Right, runner.RightExpression, runner.ActiveSide == DialogueSide.Right);
            if (nameLabel != null)
            {
                nameLabel.text = runner.SpeakerName;
                nameLabel.style.display = runner.IsNarration ? DisplayStyle.None : DisplayStyle.Flex;
                if (!runner.IsNarration) nameLabel.style.color = runner.Current.speaker.NameColor;
            }
            if (text != null) { text.text = runner.ShownText; text.EnableInClassList("narration", runner.IsNarration); }
            if (next != null) next.style.visibility = runner.LineComplete && Mathf.Repeat(time, 1f) < .6f ? Visibility.Visible : Visibility.Hidden;
        }
        private static void Portrait(VisualElement element, DialogueCharacter character, string expression, bool speaking)
        {
            if (element == null) return;
            var sprite = character != null ? character.Portrait(expression) : null;
            element.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (sprite == null) return;
            element.style.backgroundImage = new StyleBackground(sprite);
            element.EnableInClassList("speaking", speaking); element.EnableInClassList("listening", !speaking);
        }
    }
}
