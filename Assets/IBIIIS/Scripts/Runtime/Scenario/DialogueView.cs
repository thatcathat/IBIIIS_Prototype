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
        private readonly VisualElement keys, nameInput, skip, skipFill;
        private readonly Label skipText;
        private readonly TextField nameField;
        private string[] shownKeys = new string[0];
        public VisualElement Root => root;
        public DialogueView(VisualElement root, Action onClick, Action<string> onName = null)
        {
            this.root = root;
            left = GameUiSettings.Find<VisualElement>(root, "portrait-left", Screen);
            right = GameUiSettings.Find<VisualElement>(root, "portrait-right", Screen);
            box = GameUiSettings.Find<VisualElement>(root, "box", Screen);
            nameLabel = GameUiSettings.Find<Label>(root, "name", Screen);
            text = GameUiSettings.Find<Label>(root, "text", Screen);
            next = root?.Q<Label>("next");
            box?.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            keys = root?.Q("keys");
            nameInput = root?.Q("name-input");
            skip = root?.Q("skip"); skipText = root?.Q<Label>("skip-text"); skipFill = root?.Q("skip-fill");
            nameField = root?.Q<TextField>("name-field");
            var confirm = root?.Q<Button>("name-confirm");
            if (confirm != null) { confirm.text = UiText.Confirm.Text; confirm.clicked += () => onName?.Invoke(nameField?.value); }
            nameField?.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) onName?.Invoke(nameField.value); }, TrickleDown.TrickleDown);
            HideNameInput();
            Hide();
        }
        public void Hide() { if (root != null) root.style.display = DisplayStyle.None; }
        public void ShowNameInput(string value)
        {
            if (nameInput == null) return;
            nameInput.style.display = DisplayStyle.Flex;
            if (nameField != null) { nameField.value = value; nameField.Focus(); }
        }
        public void HideNameInput() { if (nameInput != null) nameInput.style.display = DisplayStyle.None; }
        /// <summary>현재 상태를 그린다(매 프레임). time은 다음 표시·키 표시 깜빡임용. keyCaps는 입력을 기다릴 때 보일 키.
        /// skipProgress는 건너뛰기 안내(0~1: 누른 비율, 음수: 숨김).</summary>
        public void Show(DialogueRunner runner, float time, string[] keyCaps = null, float skipProgress = -1)
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
            bool waitingForInput = runner.Locked;
            if (next != null) next.style.visibility = !waitingForInput && runner.LineComplete && Mathf.Repeat(time, 1f) < .6f ? Visibility.Visible : Visibility.Hidden;
            ShowKeys(keyCaps ?? new string[0], time);
            ShowSkip(skipProgress);
        }
        private void ShowSkip(float progress)
        {
            if (skip == null) return;
            skip.style.display = progress >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (progress < 0) return;
            if (skipText != null) skipText.text = UiText.DialogueSkip.Text;
            if (skipFill != null) skipFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100);
            skip.EnableInClassList("holding", progress > 0);
        }
        // 키 표시: 바뀌었을 때만 다시 만들고, 깜빡임은 투명도로.
        private void ShowKeys(string[] caps, float time)
        {
            if (keys == null) return;
            if (!Same(caps, shownKeys))
            {
                keys.Clear();
                for (int i = 0; i < caps.Length; i++)
                {
                    if (i > 0) keys.Add(new Label("+") { pickingMode = PickingMode.Ignore });
                    var cap = new Label(caps[i]) { pickingMode = PickingMode.Ignore }; cap.AddToClassList("key-cap"); keys.Add(cap);
                }
                shownKeys = caps;
            }
            keys.style.display = caps.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            keys.style.opacity = Mathf.Repeat(time, 1f) < .5f ? 1 : .45f;
        }
        private static bool Same(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
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
