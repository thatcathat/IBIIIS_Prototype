using System;
using System.Collections.Generic;

namespace IBIIIS
{
    /// <summary>대화 진행(화면과 무관한 로직). 현재 줄, 한 글자씩 나오는 진행, 넘기기, 좌우에 선 인물과 말하는 쪽을 관리한다.
    /// 넘기기: 글자가 나오는 중이면 전체를 바로 보이고, 다 나왔으면 다음 줄. 마지막 줄 다음에 끝난다.</summary>
    public sealed class DialogueRunner
    {
        private readonly List<DialogueLine> lines = new List<DialogueLine>();
        private float elapsed;
        private Action onFinished;
        /// <summary>초당 나오는 글자 수. 0 이하면 바로 전체를 보인다.</summary>
        public float CharactersPerSecond { get; set; } = 30;
        public bool IsOpen { get; private set; }
        public int Index { get; private set; } = -1;
        public DialogueLine Current => IsOpen ? lines[Index] : default;
        /// <summary>지금 보이는 글자 수.</summary>
        public int Visible { get; private set; }
        public bool LineComplete => IsOpen && Visible >= Text.Length;
        public string Text => IsOpen ? Current.text ?? "" : "";
        public string ShownText => Text.Substring(0, Math.Min(Visible, Text.Length));
        /// <summary>지문이면 true(말하는 인물 없음).</summary>
        public bool IsNarration => IsOpen && Current.speaker == null;
        public string SpeakerName => !IsOpen || IsNarration ? "" : !string.IsNullOrEmpty(Current.nameOverride) ? Current.nameOverride : Current.speaker.LocalizedName;
        /// <summary>좌우에 서 있는 인물과 표정. 인물은 처음 말할 때 자기 쪽에 서고, 다른 인물이 그쪽에서 말하면 바뀐다.</summary>
        public DialogueCharacter Left { get; private set; }
        public DialogueCharacter Right { get; private set; }
        public string LeftExpression { get; private set; }
        public string RightExpression { get; private set; }
        /// <summary>true면 넘기기로 다음 줄에 가지 않는다(글자만 다 보임). 대본이 입력을 기다리는 안내 줄에 쓴다.</summary>
        public bool Locked { get; set; }
        /// <summary>지금 말하는 쪽. 지문이면 null.</summary>
        public DialogueSide? ActiveSide => IsOpen && !IsNarration ? Current.speaker.Side : (DialogueSide?)null;

        /// <summary>대화를 시작한다. 이미 열려 있으면 이전 대화를 끝내지 않고 바꾼다(끝 알림은 새 대화 것만).
        /// keepCast면 좌우에 서 있던 인물을 그대로 두고 이어 간다(대본이 한 줄씩 이어 보여 줄 때).</summary>
        public void Start(IEnumerable<DialogueLine> script, Action finished = null, bool keepCast = false)
        {
            lines.Clear(); if (script != null) lines.AddRange(script);
            onFinished = finished; Locked = false;
            if (!keepCast) { Left = Right = null; LeftExpression = RightExpression = null; }
            IsOpen = lines.Count > 0; Index = -1;
            if (IsOpen) Show(0); else finished?.Invoke();
        }
        /// <summary>시간을 진행해 글자를 더 보인다.</summary>
        public void Tick(float seconds)
        {
            if (!IsOpen || LineComplete) return;
            if (CharactersPerSecond <= 0) { Visible = Text.Length; return; }
            elapsed += Math.Max(0, seconds);
            Visible = Math.Min(Text.Length, (int)(elapsed * CharactersPerSecond));
        }
        /// <summary>넘기기(F·Enter·Space·클릭). 글자가 나오는 중이면 전체를 보이고, 다 나왔으면 다음 줄로 간다.</summary>
        public void Advance()
        {
            if (!IsOpen) return;
            if (!LineComplete) { Visible = Text.Length; return; }
            if (Locked) return;
            if (Index + 1 < lines.Count) { Show(Index + 1); return; }
            Close();
        }
        /// <summary>대화를 바로 끝낸다(끝 알림 포함).</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; Index = -1; Visible = 0; Left = Right = null;
            var finished = onFinished; onFinished = null; finished?.Invoke();
        }
        private void Show(int index)
        {
            Index = index; elapsed = 0; Visible = CharactersPerSecond <= 0 ? Text.Length : 0;
            var line = lines[index];
            if (line.speaker == null) return;
            if (line.speaker.Side == DialogueSide.Left) { Left = line.speaker; LeftExpression = line.expression; }
            else { Right = line.speaker; RightExpression = line.expression; }
        }
    }
}
