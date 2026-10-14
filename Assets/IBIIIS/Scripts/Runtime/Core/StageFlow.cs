using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS
{
    /// <summary>미니맵에서 들어간 전투 한 번의 정보. 돌아올 씬과 위치를 함께 기억한다.</summary>
    public sealed class StageRun
    {
        public string StageId { get; }
        public string DisplayName { get; }
        public string BattleScene { get; }
        public string ReturnScene { get; }
        public Vector3 ReturnPosition { get; }
        public PlayerFacing ReturnFacing { get; }
        /// <summary>선택. 진입 대화 대본(돌아온 뒤 '클리어 후' 구간을 보여 줄 때 쓴다).</summary>
        public ScenarioScript Script { get; }
        /// <summary>전투 결과. 결과 팝업을 닫고 돌아갈 때 기록한다(그 전에는 Waiting).</summary>
        public BattlePhase Result { get; set; } = BattlePhase.Waiting;
        public StageRun(string stageId, string displayName, string battleScene, string returnScene, Vector3 returnPosition, PlayerFacing returnFacing, ScenarioScript script = null)
        { StageId = stageId; DisplayName = displayName; BattleScene = battleScene; ReturnScene = returnScene; ReturnPosition = returnPosition; ReturnFacing = returnFacing; Script = script; }
    }
    /// <summary>미니맵 ↔ 전투 씬 전환. 전투 씬과 GridMapPlayer는 이 흐름을 모른다. 미니맵을 거치지 않고 전투 씬을 바로 Play하면 기존과 같이 동작한다.
    /// 모든 씬 이동은 Play 중 SceneTransition(가리기 → 불러오기 → 드러내기)을 거친다.</summary>
    public static class StageFlow
    {
        /// <summary>진행 중인 전투. 미니맵으로 돌아가 위치를 복원하면 비운다.</summary>
        public static StageRun Current { get; private set; }
        private static bool returning;
        /// <summary>전투 정보를 기록한다. 씬 전환은 하지 않는다(테스트·내부용).</summary>
        public static void Begin(StageRun run) { Current = run; returning = false; }
        /// <summary>전투 씬으로 들어간다. 결과 팝업을 띄울 감시 오브젝트를 만들고 전투 씬을 불러온다.</summary>
        /// <param name="ui">선택. 결과 팝업 화면을 가진 Game UI 설정. 없으면 임시 표시를 쓴다.</param>
        public static bool Enter(StageRun run, GameUiSettings ui = null)
        {
            if (string.IsNullOrEmpty(run.BattleScene)) { Debug.LogError($"[IBIIIS] 스테이지 '{run.StageId}'에 전투 씬이 지정되지 않았습니다."); return false; }
            if (!CanLoad(run.BattleScene)) return false;
            if (SceneTransition.Busy) return false; // 다른 전환 중에는 들어가지 않는다
            Begin(run);
            StageResultPopup.Create(run, ui);
            Load(run.BattleScene);
            return true;
        }
        /// <summary>미니맵으로 돌아간다. 위치 복원은 미니맵의 플레이어가 TakeReturnPoint로 가져간다.</summary>
        public static void Return()
        {
            if (BeginReturn()) Load(Current.ReturnScene);
        }
        /// <summary>돌아가는 중으로 표시한다. 씬 전환은 하지 않는다(테스트·내부용). 진행 중인 전투가 없으면 false.</summary>
        public static bool BeginReturn()
        {
            if (Current == null) return false;
            returning = true; return true;
        }
        /// <summary>돌아온 씬이 맞으면 복원할 위치를 돌려주고 진행 정보를 비운다.</summary>
        public static bool TakeReturnPoint(string scenePath, out Vector3 position, out PlayerFacing facing)
        {
            bool taken = TakeReturn(scenePath, out var run);
            position = taken ? run.ReturnPosition : default; facing = taken ? run.ReturnFacing : PlayerFacing.Front;
            return taken;
        }
        /// <summary>돌아온 씬이 맞으면 끝난 전투 정보(위치·방향·결과·대본)를 돌려주고 진행 정보를 비운다.</summary>
        public static bool TakeReturn(string scenePath, out StageRun run)
        {
            run = null;
            if (Current == null || !returning || Current.ReturnScene != scenePath) return false;
            run = Current; Current = null; returning = false; return true;
        }
        private static bool CanLoad(string scenePath)
        {
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scenePath) != null) return true;
            Debug.LogError($"[IBIIIS] 씬 '{scenePath}'을(를) 찾을 수 없습니다. 스테이지 입구의 Battle Scene 연결을 확인하세요.");
            return false;
#else
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0) return true;
            Debug.LogError($"[IBIIIS] 씬 '{scenePath}'이(가) 빌드 씬 목록에 없어 불러올 수 없습니다.");
            return false;
#endif
        }
        // 에디터에서는 빌드 씬 목록에 없어도 Play 중에 불러올 수 있게 한다.
        /// <summary>씬을 불러온다(에디터 Play에서는 빌드 목록에 없어도 됨). 없는 씬이면 오류를 남기고 false.</summary>
        public static bool LoadScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || !CanLoad(scenePath)) return false;
            Load(scenePath); return true;
        }
        // Play 중에는 전환 화면을 거쳐 불러온다(에디터 테스트에서는 바로).
        private static void Load(string scenePath)
        {
            if (Application.isPlaying) { if (!SceneTransition.Begin(scenePath)) Debug.LogWarning($"[IBIIIS] 다른 씬 전환 중이라 '{scenePath}'(으)로 이동하지 않았습니다."); return; }
            LoadImmediately(scenePath);
        }
        /// <summary>전환 없이 바로 불러온다(SceneTransition이 가린 뒤 부른다).</summary>
        public static void LoadImmediately(string scenePath)
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
#endif
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Current = null; returning = false; }
    }
}
