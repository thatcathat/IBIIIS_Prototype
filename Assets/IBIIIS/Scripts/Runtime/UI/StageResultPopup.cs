using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>미니맵에서 들어간 전투의 결과를 보고 팝업을 띄운다. 승리하면 클리어 기록을 저장하고, 확인을 누르면 미니맵으로 돌아간다.
    /// 전투 씬을 넘나들도록 StageFlow.Enter가 만들며, 미니맵으로 돌아가면 사라진다. 전투 규칙·GridMapPlayer는 수정하지 않는다.</summary>
    public sealed class StageResultPopup : MonoBehaviour
    {
        private StageRun run;
        private GridMapPlayer battle;
        private bool clearSaved, closing;
        // 클리어 기록 저장에 실패했는지, 확인 버튼으로 다시 시도했는지
        private bool saveFailed, retried;
        private string problem;
        // 정식 팝업 화면(UI Toolkit). Game UI 설정이 없으면 null이고 OnGUI의 임시 표시를 쓴다.
        private VisualElement view;
        private Label title, message;
        /// <param name="ui">선택. 팝업 화면 구성을 가진 Game UI 설정(미니맵 Overworld Settings의 것).</param>
        public static StageResultPopup Create(StageRun run, GameUiSettings ui = null)
        {
            var go = new GameObject("Stage Result Popup");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var popup = go.AddComponent<StageResultPopup>(); popup.run = run;
            var document = ui != null ? ui.CreateDocument("Result Popup UI", ui.ResultPopup, go.transform, 100) : null;
            if (document != null) popup.Bind(document.rootVisualElement);
            return popup;
        }
        private void Bind(VisualElement root)
        {
            const string screen = "결과 팝업";
            view = root;
            title = GameUiSettings.Find<Label>(root, "title", screen);
            message = GameUiSettings.Find<Label>(root, "message", screen);
            var confirm = GameUiSettings.Find<Button>(root, "confirm", screen);
            if (confirm != null) confirm.clicked += Confirm;
            view.style.display = DisplayStyle.None;
        }
        /// <summary>지금 보여 줄 팝업 내용. 보일 것이 없으면(결과 전, 닫는 중) false.</summary>
        public bool TryGetView(out string heading, out string text, out BattlePhase result)
        {
            heading = text = null; result = Result;
            if (run == null || closing) return false;
            var shown = Problem;
            if (shown == null && result != BattlePhase.Won && result != BattlePhase.Lost) return false;
            heading = result == BattlePhase.Won ? "클리어!" : result == BattlePhase.Lost ? "패배" : "확인 필요";
            text = result == BattlePhase.Won ? $"{run.DisplayName}을(를) 클리어했습니다." : result == BattlePhase.Lost ? $"{run.DisplayName}에서 패배했습니다." : "";
            if (shown != null) text = (text.Length > 0 ? text + "\n" : "") + shown;
            return true;
        }
        /// <summary>정식 팝업 화면을 쓰고 있으면 true(테스트·디버그용).</summary>
        public bool UsesGameUi => view != null;
        private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (run == null) return;
            if (closing && scene.path == run.ReturnScene) { Destroy(gameObject); return; }
            if (scene.path != run.BattleScene) return;
            GridMapPlayer found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                found = root.GetComponentInChildren<GridMapPlayer>(true);
                if (found != null) break;
            }
            if (found == null) problem = $"전투 씬 '{scene.path}'에 Grid Map Player가 없습니다.";
            else Watch(found);
        }
        /// <summary>결과를 지켜볼 전투를 정한다(씬을 불러온 뒤 자동으로 호출).</summary>
        public void Watch(GridMapPlayer value) { battle = value; }
        /// <summary>팝업에 보여 줄 문제. 맵 설정 오류 등으로 전투를 시작하지 못했으면(GridMapPlayer가 꺼지고 세션 없음) 복귀를 안내한다.</summary>
        public string Problem => problem ?? (battle != null && battle.Session == null && !battle.enabled
            ? "전투를 시작하지 못했습니다. Console의 [IBIIIS] 오류(맵 설정)를 확인하세요." : null);
        /// <summary>표시할 결과. 충돌·패배 연출이 끝난 뒤에만 Won/Lost를 돌려준다. 되돌리기·재시작하면 다시 Waiting이 된다.</summary>
        public BattlePhase Result => battle != null && battle.Session != null && !battle.IsPresenting ? battle.Session.Phase : BattlePhase.Waiting;
        private void Update()
        {
            CheckResult();
            if (view == null) return;
            bool visible = TryGetView(out var heading, out var text, out var result);
            view.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            if (title != null) { title.text = heading; title.EnableInClassList("won", result == BattlePhase.Won); title.EnableInClassList("lost", result == BattlePhase.Lost); }
            if (message != null) message.text = text;
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Confirm();
        }
        /// <summary>승리가 확정되었으면 클리어 기록을 한 번 저장한다(매 프레임 호출).</summary>
        internal void CheckResult()
        {
            if (clearSaved || Result != BattlePhase.Won) return;
            clearSaved = true; TrySaveClear();
        }
        // 승리 때 한 번 저장한다. 실패하면 매 프레임 다시 시도하지 않고, 확인 버튼을 누를 때 한 번 더 시도한다.
        private bool TrySaveClear()
        {
            try { ProgressStore.MarkCleared(run.StageId); saveFailed = false; return true; }
            catch (Exception e) { saveFailed = true; problem = $"클리어 기록을 저장하지 못했습니다: {e.Message}"; Debug.LogException(e); return false; }
        }
        // Game UI 설정이 없을 때만 쓰는 임시 표시(IMGUI).
        private void OnGUI()
        {
            if (view != null || !TryGetView(out var heading, out var message, out _)) return;
            const float width = 420, height = 220;
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.Box(rect, GUIContent.none); GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x, rect.y + 20, width, 44), heading, OverworldGui.Title);
            GUI.Label(new Rect(rect.x + 20, rect.y + 70, width - 40, 80), message, OverworldGui.Body);
            bool confirm = GUI.Button(new Rect(rect.x + (width - 160) / 2, rect.yMax - 60, 160, 40), "확인", OverworldGui.Button);
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { confirm = true; e.Use(); }
            if (confirm) Confirm();
        }
        /// <summary>확인(버튼·Enter). 저장 실패 뒤 첫 확인은 다시 저장을 시도한다. 또 실패하면 안내하고, 다음 확인에서는 저장 없이 돌아간다.</summary>
        public void Confirm()
        {
            if (!TryGetView(out _, out _, out _)) return; // 보이지 않을 때(결과 전·닫는 중)는 무시해 중복 처리를 막는다
            if (saveFailed && !retried)
            {
                retried = true;
                if (!TrySaveClear()) { problem += "\n다시 시도했지만 실패했습니다. 확인을 누르면 저장하지 않고 미니맵으로 돌아갑니다."; return; }
                problem = null;
            }
            Close();
        }
        private void Close()
        {
            closing = true;
            try { StageFlow.Return(); }
            catch (Exception e) { closing = false; problem = $"미니맵 씬을 불러오지 못했습니다: {e.Message}"; Debug.LogException(e); }
        }
    }
}
