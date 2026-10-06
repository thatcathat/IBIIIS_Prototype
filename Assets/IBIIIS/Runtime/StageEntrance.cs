using UnityEngine;

namespace IBIIIS
{
    /// <summary>미니맵의 전투 스테이지 입구. 범위 안에서 상호작용 키를 누르면 연결된 전투 씬으로 들어간다.
    /// 클리어 기록은 Stage Id로 저장하므로, 한 번 정한 ID는 표시 이름·씬 경로가 바뀌어도 유지한다.</summary>
    public sealed class StageEntrance : OverworldInteractable
    {
        [SerializeField, Tooltip("필수. 클리어 기록에 쓰는 고유 ID. 미니맵 전체에서 겹치면 안 되며, 정한 뒤에는 바꾸지 않습니다.")] private string stageId;
        [SerializeField, Tooltip("안내 문구·결과 팝업에 표시할 스테이지 이름")] private string displayName = "스테이지";
        // 에디터에서는 씬 에셋 참조(battleSceneAsset)가 기준이고, 경로는 빌드에서 불러오기 위해 함께 저장한다. Inspector는 StageEntranceEditor가 그린다.
        [SerializeField] private Object battleSceneAsset;
        [SerializeField] private string battleScenePath;
        [SerializeField, Tooltip("선택. 클리어한 스테이지에서만 켜 둘 표시 오브젝트(깃발 등).")] private GameObject clearedIndicator;
        public string StageId => stageId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? stageId : displayName;
        public bool IsCleared => ProgressStore.IsCleared(stageId);
        public string BattleScenePath
        {
            get
            {
#if UNITY_EDITOR
                if (battleSceneAsset != null) return UnityEditor.AssetDatabase.GetAssetPath(battleSceneAsset);
#endif
                return battleScenePath;
            }
        }
        /// <summary>설정 오류. 정상이면 null.</summary>
        public string Problem => string.IsNullOrWhiteSpace(stageId) ? "Stage Id가 비어 있습니다." : string.IsNullOrEmpty(BattleScenePath) ? "Battle Scene이 지정되지 않았습니다." : null;
        public override string PromptVerb => Problem != null ? null : IsCleared ? "입장 (클리어)" : "입장";
        protected override void OnEnable() { base.OnEnable(); RefreshIndicator(); }
        public void RefreshIndicator() { if (clearedIndicator != null) clearedIndicator.SetActive(IsCleared); }
        public override void Interact(OverworldPlayer player)
        {
            if (Problem != null) { Debug.LogError($"[IBIIIS] 스테이지 입구 '{name}': {Problem}", this); return; }
            if (StageFlow.Enter(new StageRun(stageId, DisplayName, BattleScenePath, gameObject.scene.path, player.transform.position, player.Facing))) player.Freeze();
        }
        private void Start()
        {
            if (Problem != null) Debug.LogError($"[IBIIIS] 스테이지 입구 '{name}': {Problem} 이 입구는 상호작용하지 않습니다.", this);
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (battleSceneAsset != null && !(battleSceneAsset is UnityEditor.SceneAsset)) battleSceneAsset = null;
            battleScenePath = battleSceneAsset != null ? UnityEditor.AssetDatabase.GetAssetPath(battleSceneAsset) : "";
        }
#endif
    }
}
