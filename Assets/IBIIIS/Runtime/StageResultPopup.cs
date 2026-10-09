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
            if (Application.isPlaying) DontDestroyOnLoad(go);
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
            if (clearSaved || Result != BattlePhase.Won) return;
            clearSaved = true;
            try { ProgressStore.MarkCleared(run.StageId); }
            catch (Exception e) { problem = $"클리어 기록을 저장하지 못했습니다: {e.Message}"; Debug.LogException(e); }
        }
        private void OnGUI()
        {
            if (run == null || closing) return;
            var result = Result;
            var shown = Problem;
            if (shown == null && result != BattlePhase.Won && result != BattlePhase.Lost) return;
            string heading = result == BattlePhase.Won ? "클리어!" : result == BattlePhase.Lost ? "패배" : "확인 필요";
            string message = result == BattlePhase.Won ? $"{run.DisplayName}을(를) 클리어했습니다." : result == BattlePhase.Lost ? $"{run.DisplayName}에서 패배했습니다." : "";
            if (shown != null) message = (message.Length > 0 ? message + "\n" : "") + shown;
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
