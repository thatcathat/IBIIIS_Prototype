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
        public void Open(GameUiSettings ui, IEnumerable<DialogueLine> lines, Action finished = null)
        {
            if (view == null && ui != null)
            {
                var document = ui.CreateDocument("Dialogue UI", ui.DialogueBox, transform, 50);
                if (document != null) view = new DialogueView(document.rootVisualElement, Advance);
            }
            runner.CharactersPerSecond = ui != null ? ui.DialogueTextSpeed : GameUiSettings.DefaultDialogueTextSpeed;
            openedFrame = Time.frameCount; time = 0;
            runner.Start(lines, finished);
            view?.Show(runner, time);
        }
        /// <summary>넘기기(키·클릭과 같은 경로). 대화를 연 프레임에는 무시한다.</summary>
        public void Advance() { if (Time.frameCount != openedFrame) runner.Advance(); }
        private void Update()
        {
            if (!runner.IsOpen) { view?.Hide(); return; }
            time += Time.unscaledDeltaTime;
            runner.Tick(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.fKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) Advance();
            view?.Show(runner, time);
        }
        // Game UI 설정이 없을 때만 쓰는 임시 표시.
        private void OnGUI()
        {
            if (view != null || !runner.IsOpen || Event.current.type != EventType.Repaint) return;
            var text = runner.IsNarration ? runner.ShownText : $"{runner.SpeakerName}\n{runner.ShownText}";
            GUI.Box(new Rect(40, Screen.height - 170, Screen.width - 80, 140), text, OverworldGui.Body);
        }
        private void OnDestroy() { runner.Close(); }
    }
}
