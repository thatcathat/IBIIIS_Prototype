using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS
{
    /// <summary>미니맵에서 들어간 전투의 결과를 보고 팝업을 띄운다. 승리하면 클리어 기록을 저장하고, 확인을 누르면 미니맵으로 돌아간다.
    /// 전투 씬을 넘나들도록 StageFlow.Enter가 만들며, 미니맵으로 돌아가면 사라진다. 전투 규칙·GridMapPlayer는 수정하지 않는다.</summary>
    public sealed class StageResultPopup : MonoBehaviour
    {
        private StageRun run;
        private GridMapPlayer battle;
        private bool clearSaved, closing;
        private string problem;
        public static StageResultPopup Create(StageRun run)
        {
            var go = new GameObject("Stage Result Popup");
            DontDestroyOnLoad(go);
            var popup = go.AddComponent<StageResultPopup>(); popup.run = run;
            return popup;
        }
        private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (run == null) return;
            if (closing && scene.path == run.ReturnScene) { Destroy(gameObject); return; }
            if (scene.path != run.BattleScene) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                battle = root.GetComponentInChildren<GridMapPlayer>(true);
                if (battle != null) break;
            }
            if (battle == null) problem = $"전투 씬 '{scene.path}'에 Grid Map Player가 없습니다.";
        }
        /// <summary>표시할 결과. 충돌·패배 연출이 끝난 뒤에만 Won/Lost를 돌려준다. 되돌리기·재시작하면 다시 Waiting이 된다.</summary>
        public BattlePhase Result => battle != null && battle.Session != null && !battle.IsPresenting ? battle.Session.Phase : BattlePhase.Waiting;
        private void Update()
        {
            if (clearSaved || Result != BattlePhase.Won) return;
            clearSaved = true;
            try { ProgressStore.MarkCleared(run.StageId); }
            catch (Exception e) { problem = $"클리어 기록을 저장하지 못했습니다: {e.Message}"; Debug.LogException(e); }
        }
        private void OnGUI()
        {
            if (run == null || closing) return;
            var result = Result;
            if (problem == null && result != BattlePhase.Won && result != BattlePhase.Lost) return;
            string heading = result == BattlePhase.Won ? "클리어!" : result == BattlePhase.Lost ? "패배" : "확인 필요";
            string message = result == BattlePhase.Won ? $"{run.DisplayName}을(를) 클리어했습니다." : result == BattlePhase.Lost ? $"{run.DisplayName}에서 패배했습니다." : "";
            if (problem != null) message = (message.Length > 0 ? message + "\n" : "") + problem;
            const float width = 420, height = 220;
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.Box(rect, GUIContent.none); GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x, rect.y + 20, width, 44), heading, OverworldGui.Title);
            GUI.Label(new Rect(rect.x + 20, rect.y + 70, width - 40, 80), message, OverworldGui.Body);
            bool confirm = GUI.Button(new Rect(rect.x + (width - 160) / 2, rect.yMax - 60, 160, 40), "확인", OverworldGui.Button);
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { confirm = true; e.Use(); }
            if (confirm) Close();
        }
        private void Close()
        {
            closing = true;
            try { StageFlow.Return(); }
            catch (Exception e) { closing = false; problem = $"미니맵 씬을 불러오지 못했습니다: {e.Message}"; Debug.LogException(e); }
        }
    }
}
