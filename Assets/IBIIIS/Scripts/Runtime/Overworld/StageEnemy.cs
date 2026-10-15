using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>전투 스테이지로 이어지는 연결(어드벤처씬의 적·입구 공통). 편집기가 ID 중복·씬 설정을 함께 검사한다.</summary>
    public interface IStageLink
    {
        string StageId { get; }
        string BattleScenePath { get; }
        bool IsCleared { get; }
        /// <summary>설정 오류. 정상이면 null.</summary>
        string Problem { get; }
    }

    /// <summary>어드벤처씬에 서 있는 적. 플레이어가 닿으면 대본의 '입장 전' 구간을 보여 준 뒤 연결된 전투 씬으로 들어간다(기획서 7.2).
    /// 달리기 중에 닿아도 들어가고, 구르기 중에는 들어가지 않는다(구르기 뒤 회복 중에는 들어감). 클리어한 적은 사라진다.
    /// 전투에서 돌아오면 적과 겹치지 않는 자리에 서고, 잠깐 동안(Overworld Settings의 접촉 무시 시간) 다시 닿아도 들어가지 않는다.
    /// 닿음은 바닥 평면(XZ)의 거리로 판정하며 Collider와 무관하다. 표시는 자식 오브젝트(그림 등)가 맡는다.
    /// 닿은 순간 부딪힘 연출(충격 이펙트·효과음·화면 흔들림·플레이어가 부딪혔다 돌아오는 반응)을 보이고 잠시 멈춘 뒤,
    /// 입장 전 대화를 거쳐 전투씬으로 넘어간다(화면 전환은 SceneTransition. 수치·에셋은 Overworld Settings의 적 접촉 연출, 표시 전용).
    /// 대본은 대화 그래프(DialogueProgress.Graph)에서 Stage Id(오브젝트 ID)의 대본 순서로 연결한다(기획서 7.6). 지금 대본이 대화 대본이면 전투에 들어가지 않고
    /// 대화를 보여 준 뒤 플레이어가 적에게서 조금 물러나고, 전투 진입 대본이면 그 대본의 입장 전 구간을 거쳐 들어간다(입장 후 구간은 전투 씬 진행기, 클리어 후 구간은 돌아온 뒤).
    /// 그래프에 없거나 순서가 비었으면 대화 없이 들어간다.</summary>
    public sealed class StageEnemy : MonoBehaviour, IStageLink
    {
        private static readonly List<StageEnemy> active = new List<StageEnemy>();
        [SerializeField, Tooltip("필수. 클리어 기록에 쓰는 고유 ID. 어드벤처씬 전체(입구 포함)에서 겹치면 안 되며, 정한 뒤에는 바꾸지 않습니다.")] private string stageId;
        [SerializeField, Tooltip("결과 팝업에 표시할 스테이지 이름")] private string displayName = "스테이지";
        // 에디터에서는 씬 에셋 참조가 기준이고, 경로는 빌드에서 불러오기 위해 함께 저장한다. Inspector는 StageLinkEditor가 그린다.
        [SerializeField] private Object battleSceneAsset;
        [SerializeField] private string battleScenePath;
        [SerializeField, Min(.1f), Tooltip("닿음 판정 거리(칸). 플레이어 발밑과 이 오브젝트 위치의 바닥 평면 거리가 이보다 가까우면 닿은 것입니다. 임시 0.8.")] private float contactRadius = .8f;
        private bool engaging;
        // 대화 뒤 물러나기가 끝났지만 플레이어가 아직 닿음 거리 안이면(벽 등으로 막힘) 거리 밖으로 나갈 때까지 다시 닿지 않는다.
        private bool waitForLeave;
        // 닿은 뒤 진행: 부딪힘 연출 뒤 멈춤 → 입장 전 대화 → 화면 어두워짐 → 전투씬. 대화 대본이면 멈춤 → 대화 → 물러나기.
        private enum Phase { None, Hold, Dialogue, StepBack }
        // 이번 접촉에 쓰는 대본(그래프의 지금 대본).
        private ScenarioScript engagedScript;
        private Phase phase;
        private float phaseLeft;
        private OverworldPlayer engagedPlayer;
        private static AudioSource contactAudio;
        /// <summary>이 시각(Time.time)까지는 어떤 적에 닿아도 들어가지 않는다(전투에서 돌아온 직후).</summary>
        public static float IgnoreContactUntil { get; private set; } = -1;
        public static IReadOnlyList<StageEnemy> Active => active;
        public string StageId => stageId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? stageId : GameText.Get(GameText.StagesTable, stageId, displayName);
        public string SourceName => displayName;
        /// <summary>대화 그래프에 이 적(Stage Id)이 있으면 true.</summary>
        public bool UsesGraph => !string.IsNullOrEmpty(stageId) && DialogueProgress.Graph != null && DialogueProgress.Graph.HasObject(stageId);
        /// <summary>지금 닿으면 쓸 대본(그래프의 대본 순서에서 지금 대본). 없으면 null(대화 없이 전투).</summary>
        public ScenarioScript CurrentScript => UsesGraph ? DialogueProgress.Current(DialogueProgress.Graph, stageId) : null;
        public float ContactRadius => Mathf.Max(.1f, contactRadius);
        public bool IsCleared => !string.IsNullOrEmpty(stageId) && ProgressStore.IsCleared(stageId);
        public bool Engaging => engaging;
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
        public string Problem => string.IsNullOrWhiteSpace(stageId) ? "Stage Id가 비어 있습니다." : string.IsNullOrEmpty(BattleScenePath) ? "Battle Scene이 지정되지 않았습니다." : null;
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() { active.Remove(this); }
        private void Start()
        {
            // 클리어한 적은 어드벤처씬에서 사라진다(다시 도전할 수 없음).
            if (IsCleared) { gameObject.SetActive(false); return; }
            if (Problem != null) Debug.LogError($"[IBIIIS] 어드벤처씬 적 '{name}': {Problem} 닿아도 전투에 들어가지 않습니다.", this);
        }
        /// <summary>전투에서 돌아온 직후 seconds 동안 적과 닿아도 들어가지 않게 한다.</summary>
        public static void IgnoreContactFor(float seconds) => IgnoreContactUntil = Time.time + Mathf.Max(0, seconds);
        /// <summary>position에 닿은 적(가장 가까운 것). 닿을 수 없는 상태(클리어·설정 오류·진입 중·접촉 무시 시간)면 null.</summary>
        public static StageEnemy FindTouching(Vector3 position)
        {
            if (Time.time < IgnoreContactUntil) return null;
            StageEnemy best = null; float bestDistance = float.PositiveInfinity;
            foreach (var enemy in active)
            {
                if (enemy == null || !enemy.isActiveAndEnabled || enemy.engaging || enemy.Problem != null || enemy.IsCleared) continue;
                float distance = OverworldInteractable.FlatDistance(position, enemy.transform.position);
                if (enemy.waitForLeave) { if (distance > enemy.ContactRadius) enemy.waitForLeave = false; continue; }
                if (distance <= enemy.ContactRadius && distance < bestDistance) { best = enemy; bestDistance = distance; }
            }
            return best;
        }
        /// <summary>전투에서 돌아올 자리: 적에서 플레이어 쪽으로 닿음 거리 + clearance만큼 떨어진 곳(같은 자리면 적의 앞(-Z) 쪽). 높이는 플레이어 그대로.</summary>
        public static Vector3 ReturnPoint(Vector3 enemy, Vector3 player, float contactRadius, float clearance)
        {
            var away = new Vector3(player.x - enemy.x, 0, player.z - enemy.z);
            if (away.sqrMagnitude < 1e-6f) away = Vector3.back;
            var point = enemy + away.normalized * (contactRadius + Mathf.Max(0, clearance));
            point.y = player.y;
            return point;
        }
        /// <summary>지금 진행 단계(테스트·디버그용): 0 없음, 1 멈춤, 2 대화, 3 물러나기.</summary>
        public int EngagePhase => (int)phase;
        /// <summary>플레이어가 닿았다: 부딪힘 연출 뒤 멈추고, 입장 전 대화가 있으면 보여 준 뒤 전투 씬으로 들어간다(전환 화면을 거침). 진입 중에는 다시 반응하지 않는다.</summary>
        public void Engage(OverworldPlayer player)
        {
            if (engaging || player == null) return;
            if (Problem != null) { Debug.LogError($"[IBIIIS] 어드벤처씬 적 '{name}': {Problem}", this); return; }
            engaging = true; engagedPlayer = player; engagedScript = CurrentScript;
            player.SetInputLocked(true); // 연출 동안 제자리에 선다
            var s = player.Settings;
            var toEnemy = transform.position - player.transform.position; toEnemy.y = 0;
            var between = Vector3.Lerp(player.transform.position, transform.position, .5f) + Vector3.up * .9f;
            ImpactPop.Spawn(s != null ? s.ContactImpactPrefab : null, between, s != null ? s.ContactImpactScale : OverworldSettings.DefaultContactImpactScale, s != null ? s.ContactImpactTime : OverworldSettings.DefaultContactImpactTime);
            if (s != null && s.ContactSound != null) RuntimeObjects.PlayOneShot(ref contactAudio, transform, "Contact Audio", s.ContactSound, s.ContactVolume);
            var camera = FindAnyObjectByType<OverworldCamera>();
            if (camera != null) camera.Shake(s != null ? s.ContactShakeAmplitude : OverworldSettings.DefaultContactShakeAmplitude, s != null ? s.ContactShakeTime : OverworldSettings.DefaultContactShakeTime);
            player.PlayContactBump(toEnemy);
            phase = Phase.Hold; phaseLeft = s != null ? s.ContactHoldSeconds : OverworldSettings.DefaultContactHoldSeconds;
            Advance(0);
        }
        private void Update() { if (phase != Phase.None) Advance(Time.deltaTime); }
        /// <summary>닿은 뒤 진행을 seconds만큼 진행한다(Play 중 매 프레임, 테스트에서는 직접 호출).</summary>
        public void Advance(float seconds)
        {
            var player = engagedPlayer;
            if (player == null) { phase = Phase.None; engaging = false; return; }
            switch (phase)
            {
                case Phase.Hold:
                    phaseLeft -= seconds;
                    if (phaseLeft > 0) return;
                    player.SetInputLocked(false);
                    phase = Phase.Dialogue;
                    if (engagedScript != null && engagedScript.IsTalk)
                    {
                        // 대화 대본: 전투에 들어가지 않고 대화 뒤 물러난다.
                        if (AdventureDialogue.PlayTalk(engagedScript, player, StepBack) == null) StepBack();
                        break;
                    }
                    if (!AdventureDialogue.Play(engagedScript, ScenarioSection.Before, player, EnterAfterDialogue)) EnterAfterDialogue();
                    break;
            }
        }
        // 대화 뒤 플레이어를 닿음 거리 바깥(전투에서 돌아올 때와 같은 자리)으로 걸어 물러나게 하고, 다 물러나면 다시 닿을 수 있게 한다.
        private void StepBack()
        {
            var player = engagedPlayer;
            if (player == null) { phase = Phase.None; engaging = false; return; }
            phase = Phase.StepBack;
            var settings = player.Settings;
            var back = ReturnPoint(transform.position, player.transform.position, ContactRadius, settings != null ? settings.ReturnClearance : OverworldSettings.DefaultReturnClearance);
            player.WalkTo(back, () =>
            {
                phase = Phase.None; engaging = false; engagedPlayer = null; engagedScript = null;
                waitForLeave = player != null && OverworldInteractable.FlatDistance(player.transform.position, transform.position) <= ContactRadius;
            });
        }
        private void EnterAfterDialogue()
        {
            phase = Phase.None;
            if (engagedPlayer != null) Enter(engagedPlayer);
        }
        private void Enter(OverworldPlayer player)
        {
            var settings = player.Settings;
            var back = ReturnPoint(transform.position, player.transform.position, ContactRadius, settings != null ? settings.ReturnClearance : OverworldSettings.DefaultReturnClearance);
            var run = new StageRun(stageId, DisplayName, BattleScenePath, gameObject.scene.path, back, player.Facing, engagedScript);
            if (StageFlow.Enter(run, settings != null ? settings.GameUi : null)) player.Freeze();
            else { engaging = false; engagedPlayer = null; player.SetInputLocked(false); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { IgnoreContactUntil = -1; }
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (battleSceneAsset != null && !(battleSceneAsset is UnityEditor.SceneAsset)) battleSceneAsset = null;
            battleScenePath = battleSceneAsset != null ? UnityEditor.AssetDatabase.GetAssetPath(battleSceneAsset) : "";
        }
        private void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.color = new Color(1f, .4f, .3f, .9f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, ContactRadius);
        }
#endif
    }
}
