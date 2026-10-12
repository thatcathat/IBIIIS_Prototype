using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IBIIIS
{
    /// <summary>장면에서 미연시 대화를 띄운다. 처음 쓸 때 자동으로 만들어지고(씬마다 하나), Game UI 설정의 대화창 화면 구성으로 그린다.
    /// 넘기기: F·Enter·Space 또는 대화 상자 클릭. 대화를 연 프레임의 입력은 무시해 말 거는 키(F)가 첫 줄을 바로 넘기지 않게 한다.
    /// Game UI 설정이 없으면 화면 아래 임시 IMGUI 상자로 대신하고 진행은 막지 않는다.</summary>
    public sealed class DialogueController : MonoBehaviour
    {
        private readonly DialogueRunner runner = new DialogueRunner();
        private DialogueView view;
        private float time;
        private int openedFrame = -1;
        public DialogueRunner Runner => runner;
        private string[] keys = Array.Empty<string>();
        private Action<string> nameDone;
        private string nameDefault;
        /// <summary>지금 보이는 키 안내(예: Shift, W).</summary>
        public IReadOnlyList<string> Keys => keys;
        /// <summary>이름 입력을 기다리는 중이면 true.</summary>
        public bool AskingName => nameDone != null;
        public bool IsOpen => runner.IsOpen;
        public bool UsesGameUi => view != null;

        /// <summary>대화를 시작한다. ui는 화면 구성·글자 속도를 가진 Game UI 설정(없으면 임시 표시). finished는 대화가 끝나면 한 번 불린다.</summary>
        public static DialogueController Play(GameUiSettings ui, IEnumerable<DialogueLine> lines, Action finished = null)
        {
            var controller = FindAnyObjectByType<DialogueController>();
            if (controller == null) controller = new GameObject("Dialogue").AddComponent<DialogueController>();
            controller.Open(ui, lines, finished);
            return controller;
        }
        public void Open(GameUiSettings ui, IEnumerable<DialogueLine> lines, Action finished = null, bool keepCast = false)
        {
            EnsureView(ui);
            runner.CharactersPerSecond = ui != null ? ui.DialogueTextSpeed : GameUiSettings.DefaultDialogueTextSpeed;
            openedFrame = Time.frameCount; time = 0; keys = Array.Empty<string>(); nameDone = null;
            runner.Start(lines, finished, keepCast);
            Draw();
        }
        /// <summary>넘길 수 없는 안내 줄을 띄운다(대본이 입력을 기다릴 때). keys는 화면에 깜빡이는 키 표시(예: "Shift", "W"). 좌우 인물은 유지한다.</summary>
        public void Prompt(GameUiSettings ui, DialogueLine line, IEnumerable<string> keyCaps)
        {
            Open(ui, new[] { line }, null, true);
            runner.Locked = true; keys = keyCaps != null ? new List<string>(keyCaps).ToArray() : Array.Empty<string>();
            Draw();
        }
        /// <summary>이름 입력 창을 띄운다. 확인(버튼·Enter)하면 입력값으로 done을 부른다. 안내 줄은 prompt.</summary>
        public void AskName(GameUiSettings ui, DialogueLine prompt, string defaultValue, Action<string> done)
        {
            Open(ui, new[] { prompt }, null, true);
            runner.Locked = true; nameDefault = defaultValue ?? ""; nameDone = done;
            view?.ShowNameInput(nameDefault);
            Draw();
        }
        /// <summary>이름 입력을 확정한다(확인 버튼·Enter와 같은 경로). 입력을 기다리지 않으면 무시한다.</summary>
        public void SubmitName(string value)
        {
            if (nameDone == null) return;
            var done = nameDone; nameDone = null; view?.HideNameInput();
            runner.Locked = false; runner.Close();
            done(string.IsNullOrEmpty(value) ? nameDefault : value);
        }
        /// <summary>대화창을 닫는다(끝 알림은 부르지 않는다).</summary>
        public void Hide() { nameDone = null; keys = Array.Empty<string>(); view?.HideNameInput(); runner.Start(null); Draw(); }
        private void EnsureView(GameUiSettings ui)
        {
            if (view != null || ui == null) return;
            var document = ui.CreateDocument("Dialogue UI", ui.DialogueBox, transform, 50);
            if (document != null) view = new DialogueView(document.rootVisualElement, Advance, SubmitName);
        }
        private void Draw() { view?.Show(runner, time, keys); }
        /// <summary>넘기기(키·클릭과 같은 경로). 대화를 연 프레임에는 무시한다.</summary>
        public void Advance() { if (Time.frameCount != openedFrame) runner.Advance(); }
        private void Update()
        {
            if (!runner.IsOpen) { view?.Hide(); return; }
            time += Time.unscaledDeltaTime;
            runner.Tick(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            // 이름 입력 중에는 글자 입력과 겹치지 않게 넘기기 키를 쓰지 않는다(Enter는 입력 칸이 확정에 쓴다).
            if (nameDone == null && keyboard != null && (keyboard.fKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) Advance();
            Draw();
        }
        // Game UI 설정이 없을 때만 쓰는 임시 표시.
        private void OnGUI()
        {
            if (view != null || !runner.IsOpen) return;
            var text = runner.IsNarration ? runner.ShownText : $"{runner.SpeakerName}\n{runner.ShownText}";
            if (keys.Length > 0) text += "\n[" + string.Join(" + ", keys) + "]";
            if (Event.current.type == EventType.Repaint) GUI.Box(new Rect(40, Screen.height - 170, Screen.width - 80, 140), text, OverworldGui.Body);
            if (nameDone != null && GUI.Button(new Rect(Screen.width / 2 - 60, Screen.height - 60, 120, 32), "확인")) SubmitName(nameDefault);
        }
        private void OnDestroy() { runner.Close(); }
    }
}
