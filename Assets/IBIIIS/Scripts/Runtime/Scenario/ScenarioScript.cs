using System;
using System.Collections.Generic;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>대본 단계 종류. 프리팹·에셋에 정수로 저장되므로 기존 값을 바꾸거나 재사용하지 말고, 새 종류는 맨 끝에 새 값으로 추가한다.</summary>
    public enum ScenarioStepType
    {
        /// <summary>대사·지문 한 줄. 넘기면 다음 단계.</summary>
        Line = 0,
        /// <summary>인물·오브젝트 머리 위 감정 표시(예: !, ?, 땀). 기다리지 않고 바로 다음 단계.</summary>
        Emote = 1,
        /// <summary>지정한 행동(종류·방향)을 한 번 하면 다음 단계. 그 외 입력은 무시.</summary>
        WaitAction = 2,
        /// <summary>허용한 행동으로 자유롭게 움직이다가, 처음 움직인 때부터 Seconds가 지나면 다음 단계.</summary>
        WaitFreeMove = 3,
        /// <summary>허용한 행동으로 움직여 Cell에 도착하면 다음 단계(그 칸이 점멸).</summary>
        WaitReachCell = 4,
        /// <summary>Cell을 마우스 왼쪽 버튼으로 누르면 연출 시간(Seconds) 뒤 다음 단계(그 칸이 점멸, 이동 입력은 무시).</summary>
        WaitClickCell = 5,
        /// <summary>이름 입력 창. 확인하면 다음 단계(입력값은 기록만 하고 대사는 바꾸지 않음).</summary>
        NameInput = 6,
        /// <summary>등장 오브젝트(Target)를 켜거나(Active) 끈다. 바로 다음 단계.</summary>
        SetActive = 7,
        /// <summary>Seconds 동안 기다린다(입력 무시).</summary>
        Wait = 8,
        /// <summary>다른 씬(Scene Path)으로 넘어간다.</summary>
        LoadScene = 9,
        /// <summary>적(Prefab)을 Cell에 Direction 방향으로 합류시킨다. 바로 다음 단계.</summary>
        SpawnEnemy = 10,
        /// <summary>허용한 행동으로 자유롭게 싸운다. 패배할 행동은 막고 경고 대사(Line)를 띄운다(넘겨야 닫히고 그동안 턴이 넘어가지 않음). 적이 모두 사라지면 다음 단계.</summary>
        FreePlay = 11,
        /// <summary>클리어 기록에 Text(기록 ID)를 남긴다(예: 튜토리얼 클리어). 바로 다음 단계.</summary>
        RecordClear = 12,
        /// <summary>플레이어 그림을 자세 그림(Sprite)으로 바꾸고 움직임(Motion)을 준다. Seconds 동안(0이면 다음 대사가 끝날 때까지). 바로 다음 단계.</summary>
        Pose = 13,
        /// <summary>네 방향(위·왼쪽·아래·오른쪽)을 모두 한 번씩 눌러야 다음 단계. 키 표시는 Keys에 위 왼쪽 아래 오른쪽 순서로 적고, 누른 키는 표시에서 빠진다.</summary>
        WaitAllDirections = 14,
        /// <summary>'표시 끝 단계까지 유지'로 띄운 감정 표시(Target 대상) 또는 자세를 끝낸다. 바로 다음 단계. IBIIIS Dialogue Editor에서는 막대의 끝으로 보인다.</summary>
        EndDisplay = 15,
        /// <summary>'클리어 후' 구간 시작(대본마다 하나). 이 뒤는 전투에서 이겨 어드벤처씬으로 돌아온 뒤 실행한다. 경계로만 쓰고 실행하지 않는다.
        /// IBIIIS Dialogue Editor에서는 '▶ 클리어 후' 시작 노드로 보인다.</summary>
        ClearSection = 16,
        /// <summary>'입장 후' 구간 시작(대본마다 하나). 앞은 어드벤처씬에서 전투씬으로 들어가기 전에 실행하는 '입장 전' 구간, 뒤는 전투가 준비되면 전투씬에서 실행하는 '입장 후' 구간이다.
        /// 경계로만 쓰고 실행하지 않는다. IBIIIS Dialogue Editor에서는 '▶ 입장 후' 시작 노드로 보인다.</summary>
        BattleSection = 17,
    }
    /// <summary>대본 구간. 입장 전·클리어 후는 어드벤처씬(AdventureDialogue), 입장 후는 전투씬(ScenarioDirector)이 실행한다.</summary>
    public enum ScenarioSection { Before = 0, Battle = 1, Clear = 2 }
    /// <summary>예전 대본의 시작 시점(클리어 구간 단계가 없는 대본에서만 쓴다). 값은 대본 에셋에 정수로 저장되므로 끝에만 추가한다.</summary>
    public enum ScenarioStartTiming
    {
        /// <summary>전투가 준비되는 대로(튜토리얼처럼 전투 전체를 대본이 이끈다).</summary>
        [InspectorName("전투 준비 시 (Battle Ready)")] BattleReady = 0,
        /// <summary>적을 모두 물리치고 충돌 연출이 끝난 뒤. 그때까지 전투는 평소대로 진행되고, 결과 팝업은 대본이 끝난 뒤 뜬다.</summary>
        [InspectorName("클리어 시 (Battle Won)")] BattleWon = 1,
    }
    /// <summary>대본 용도. 값은 에셋에 정수로 저장되므로 끝에만 추가한다(예전 대본은 모두 전투 진입 = 0).</summary>
    public enum ScenarioUse
    {
        /// <summary>전투 진입 대본: 입장 전·입장 후·클리어 후 세 구간(적·전투씬 진행기).</summary>
        [InspectorName("전투 진입 (Battle)")] Battle = 0,
        /// <summary>대화 대본: 구간 없이 대사만. 어드벤처씬의 NPC·근접 대화 구역·적이 대화 그래프의 순서대로 보여 준다. 버전을 여러 개 둘 수 있다.</summary>
        [InspectorName("대화 (Talk)")] Talk = 1,
    }
    /// <summary>대화 대본의 버전을 고르는 순서. 값은 에셋에 정수로 저장되므로 끝에만 추가한다.</summary>
    public enum ScenarioVersionOrder { [InspectorName("차례대로")] InOrder = 0, [InspectorName("무작위")] Random = 1 }
    /// <summary>대화 대본의 추가 버전(버전 2부터). 버전 1은 대본의 단계 목록이다.</summary>
    [Serializable]
    public struct ScenarioVersion
    {
        public ScenarioStep[] steps;
        public ScenarioStep[] Steps => steps ?? Array.Empty<ScenarioStep>();
    }
    /// <summary>표시 끝 단계가 끝낼 표시 종류.</summary>
    public enum ScenarioDisplay { Emote = 0, Pose = 1 }
    /// <summary>자세 연출의 움직임. 표시 전용. 값은 에셋에 정수로 저장되므로 끝에만 추가한다.</summary>
    public enum PoseMotion { None = 0, Hop = 1, Shake = 2, Sigh = 3, Proud = 4 }
    /// <summary>대본에서 허용할 플레이어 행동(여러 개 선택 가능).</summary>
    [Flags]
    public enum ScenarioActions { None = 0, Move = 1, Wait = 2, Dash = 4, Roll = 8 }
    /// <summary>대본 한 단계. 종류(Type)에 따라 쓰는 칸만 의미가 있다(Inspector는 쓰는 칸만 보여 준다).</summary>
    [Serializable]
    public struct ScenarioStep
    {
        [HideInInspector, Tooltip("단계 고유 ID(자동). 편집기에서 선택·복사·연결에 쓴다. 비거나 겹치면 대본이 새로 채운다.")] public string id;
        public ScenarioStepType type;
        [Tooltip("Line: 보여 줄 대사. 입력 대기·이름 입력: 그동안 보일 안내 줄(인물을 비우면 지문). 자유 진행: 위험한 이동을 막을 때 보일 경고 대사.")] public DialogueLine line;
        [Tooltip("입력 대기에서 허용할 행동.")] public ScenarioActions actions;
        [Tooltip("허용할 방향 또는 합류하는 적의 방향(위=(0,1), 왼쪽=(-1,0)). 입력 대기에서 (0,0)이면 모든 방향.")] public Vector2Int direction;
        [Tooltip("도착하거나 누를 칸(열, 행). 맵 좌표 기준(0,0 = 왼쪽 아래).")] public Vector2Int cell;
        [Tooltip("초. 자유 이동: 처음 움직인 뒤 기다릴 시간. 칸 누르기: 누른 뒤 연출 시간. 기다리기: 기다릴 시간. 감정 표시: 보이는 시간(0이면 다음 대사가 끝날 때까지).")] public float seconds;
        [Tooltip("화면에 깜빡일 키 표시. 공백으로 나눔(예: \"Shift W\" → Shift + W).")] public string keys;
        [Tooltip("감정 표시·켜고 끄기 대상: \"player\" 또는 진행기(ScenarioDirector) Actors의 ID.")] public string target;
        [Tooltip("감정 표시 글자(예: !, ?) 또는 이름 입력 기본값.")] public string text;
        [Tooltip("켜고 끄기: 켤지(체크) 끌지.")] public bool active;
        [Tooltip("넘어갈 씬 경로(Assets/...unity).")] public string scenePath;
        [Tooltip("합류할 적 프리팹(EnemyDefinition 필요).")] public GameObject prefab;
        [Tooltip("칸 누르기: 누른 뒤 날아가는 그림(예: 칼). 감정 표시: 글자 대신 보일 아이콘. 자세: 플레이어가 잠시 바꿔 보일 자세 그림.")] public Sprite sprite;
        [Tooltip("자세: 그림을 바꾸며 주는 움직임(Hop 펄쩍, Shake 좌우 떨림, Sigh 한숨처럼 납작, Proud 으쓱하며 늘어남).")] public PoseMotion motion;
        [Tooltip("자세: 자세가 끝나 기본 그림으로 돌아올 때 주는 움직임. None이면 움직임 없이 바로 돌아옵니다.")] public PoseMotion endMotion;
        [Tooltip("자세: 체크하면 시간·대사와 관계없이 플레이어가 다음 행동을 시작할 때까지 유지합니다.")] public bool untilAction;
        [Tooltip("감정 표시·자세: 체크하면 시간·대사와 관계없이 뒤에 오는 '표시 끝' 단계까지 유지합니다(다른 끝 조건보다 우선). IBIIIS Dialogue Editor에서 막대 끝을 끌면 자동으로 켜집니다.")] public bool untilEnd;
        [Tooltip("표시 끝: 끝낼 표시 종류(감정 표시는 대상 ID도 맞아야 함).")] public ScenarioDisplay display;
        public string[] KeyCaps => string.IsNullOrWhiteSpace(keys) ? Array.Empty<string>() : keys.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        /// <summary>이 행동이 허용되는지(입력 대기 단계).</summary>
        public bool Allows(PlayerAction action, Vector2Int dir)
        {
            var flag = action == PlayerAction.Move ? ScenarioActions.Move : action == PlayerAction.Wait ? ScenarioActions.Wait : action == PlayerAction.Dash ? ScenarioActions.Dash : ScenarioActions.Roll;
            return (actions & flag) != 0 && (direction == Vector2Int.zero || direction == dir);
        }
    }
    /// <summary>대본(단계 목록). 용도가 전투 진입이면 전투 씬의 ScenarioDirector와 어드벤처씬 적이 구간별로 실행하고(튜토리얼 등),
    /// 대화면 어드벤처씬의 NPC·근접 대화 구역·적이 대화 그래프(DialogueGraph)의 순서대로 보여 준다(기획서 7.6). 대사·입력 제한·조건·연출을 Inspector에서 편집한다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Scenario Script")]
    public sealed class ScenarioScript : ScriptableObject
    {
        [SerializeField, Tooltip("이 대본의 고정 ID. 기록 등이 이 값으로 연결되므로 정한 뒤 바꾸지 않습니다.")] private string scriptId = "tutorial";
        [SerializeField, Tooltip("대본 전체 설정: 시작 시점·한 번만·건너뛰기·전투 연출. IBIIIS Dialogue Editor의 시작 노드에서 편집합니다.")] private Settings settings = Settings.Default;
        [SerializeField] private ScenarioStep[] steps = Array.Empty<ScenarioStep>();
        [SerializeField, Tooltip("대화 대본의 버전 2부터(버전 1은 위 단계 목록). 보여 줄 때마다 대본 설정의 Version Order대로 하나를 고릅니다. 전투 진입 대본은 쓰지 않습니다.")]
        private ScenarioVersion[] moreVersions = Array.Empty<ScenarioVersion>();
        public string Id => scriptId;
        public Settings Options => settings;
        public ScenarioUse Use => settings.use;
        public bool IsTalk => settings.use == ScenarioUse.Talk;
        /// <summary>버전 수. 전투 진입 대본은 항상 1.</summary>
        public int VersionCount => IsTalk ? 1 + (moreVersions?.Length ?? 0) : 1;
        /// <summary>버전의 단계 목록(0 = 버전 1 = Steps). 범위 밖이면 버전 1.</summary>
        public ScenarioStep[] VersionSteps(int index) => index <= 0 || index >= VersionCount ? Steps : moreVersions[index - 1].Steps;
        /// <summary>대화 대본을 끝까지 본 기록 ID(어느 버전이든 같음).</summary>
        public string TalkSeenId => scriptId;
        /// <summary>대본 전체 설정. 씬 연결(전투·등장 오브젝트)은 진행기(ScenarioDirector)에 있다.</summary>
        [Serializable]
        public struct Settings
        {
            [HideInInspector, Tooltip("예전 대본 호환용. 클리어 구간 단계가 없는 대본에서만 쓴다: Battle Won이면 단계 전체가 클리어 구간, 아니면 전투 시작 구간.")] public ScenarioStartTiming startTiming;
            [Tooltip("체크하면 전투 시작·클리어 구간을 각각 한 번 끝까지 본 뒤로는 그 구간을 자동으로 시작하지 않습니다(같은 맵을 다시 깨도 대화가 다시 나오지 않음). 본 기록은 구간별로 클리어 기록 파일에 남습니다.")] public bool onlyOnce;
            [Tooltip("체크하면 대사가 보이는 동안 Esc를 길게 눌러 다음 입력 대기 단계(행동·칸 누르기·이름 입력·자유 전투)나 대본 끝까지 대사를 건너뜁니다. 대화 상자에 안내가 보입니다.")] public bool allowSkip;
            [Min(0.1f), Tooltip("건너뛰기에 필요한 Esc 누르는 시간(초). 임시 0.6.")] public float skipHoldSeconds;
            [Tooltip("체크하면 대본이 도는 동안 적 인식 말풍선(대사)을 띄우지 않습니다. 대본 대사와 겹치지 않게 튜토리얼에서 켭니다.")] public bool hideEnemyAlerts;
            [Tooltip("체크하면 대본이 도는 동안 적 인식 범위 안의 위험 그림을 자동으로 보이지 않습니다. 위험 자세는 대본의 Pose 단계로 정합니다.")] public bool scriptControlsDanger;
            [Tooltip("전투 진입: 적·전투씬에 연결하는 세 구간 대본. 대화: 어드벤처씬 NPC·근접 대화 구역·적이 대화 그래프 순서대로 보여 주는 대본(구간 없음, 버전 여러 개 가능).")] public ScenarioUse use;
            [Tooltip("대화 대본: 버전이 여럿일 때 보여 줄 때마다 고르는 순서(차례대로·무작위). 어느 버전 차례인지는 저장하지 않습니다(다시 실행하면 버전 1부터).")] public ScenarioVersionOrder versionOrder;
            /// <summary>건너뛰기 누르는 시간(0 이하로 저장된 값은 0.1초).</summary>
            public float SkipHold => Mathf.Max(.1f, skipHoldSeconds);
            /// <summary>새 대본의 기본값(이전에 진행기에 있던 기본값과 같다).</summary>
            public static Settings Default => new Settings { startTiming = ScenarioStartTiming.BattleReady, allowSkip = true, skipHoldSeconds = .6f, hideEnemyAlerts = true, scriptControlsDanger = true };
        }
        public ScenarioStep[] Steps => steps ?? Array.Empty<ScenarioStep>();
        /// <summary>구간 경계 단계의 위치(없으면 -1).</summary>
        public int BattleMarker => IndexOf(Steps, ScenarioStepType.BattleSection);
        public int ClearMarker => IndexOf(Steps, ScenarioStepType.ClearSection);
        public static int IndexOf(ScenarioStep[] list, ScenarioStepType type) { for (int i = 0; i < list.Length; i++) if (list[i].type == type) return i; return -1; }
        public static int IndexOfClearSection(ScenarioStep[] list) => IndexOf(list, ScenarioStepType.ClearSection);
        /// <summary>단계가 구간 경계(입장 후 구간 시작·클리어 후 구간 시작)인지.</summary>
        public static bool IsSectionMarker(ScenarioStepType type) => type == ScenarioStepType.BattleSection || type == ScenarioStepType.ClearSection;
        /// <summary>입장 후 구간의 첫 위치(입장 후 구간 시작 단계 바로 뒤, 없으면 0 — 입장 전 구간 없음).</summary>
        public int BattleStart => BattleMarker + 1;
        /// <summary>클리어 후 구간 시작 위치(경계). 입장 후 구간은 [BattleStart, 경계), 클리어 후 구간은 (경계, 끝]. 클리어 후 구간 시작 단계가 없는 예전 대본은
        /// 시작 시점이 Battle Won이면 BattleStart - 1(입장 후 구간 없음, 나머지 전체가 클리어 후 구간), 아니면 단계 수(클리어 후 구간 없음)다.</summary>
        public int ClearBoundary { get { int marker = ClearMarker; return marker >= 0 ? marker : settings.startTiming == ScenarioStartTiming.BattleWon ? BattleStart - 1 : Steps.Length; } }
        public bool HasBeforePart => BattleMarker > 0;
        public bool HasStartPart => ClearBoundary > BattleStart;
        public bool HasClearPart => ClearBoundary + 1 < Steps.Length;
        /// <summary>구간의 단계 범위 [from, to).</summary>
        public void Range(ScenarioSection section, out int from, out int to)
        {
            switch (section)
            {
                case ScenarioSection.Before: from = 0; to = Mathf.Max(0, BattleMarker); break;
                case ScenarioSection.Battle: from = BattleStart; to = Mathf.Max(BattleStart, ClearBoundary); break;
                default: from = ClearBoundary + 1; to = Steps.Length; break;
            }
        }
        public bool Has(ScenarioSection section) => section == ScenarioSection.Before ? HasBeforePart : section == ScenarioSection.Battle ? HasStartPart : HasClearPart;
        /// <summary>단계가 속한 구간(경계 단계는 그 뒤 구간).</summary>
        public static ScenarioSection SectionOf(IReadOnlyList<ScenarioStep> list, int index)
        {
            var section = ScenarioSection.Battle; bool battleMarker = false;
            for (int i = 0; i < list.Count; i++) if (list[i].type == ScenarioStepType.BattleSection) { battleMarker = true; break; }
            if (battleMarker) section = ScenarioSection.Before;
            for (int i = 0; i < index && i < list.Count; i++)
            {
                if (list[i].type == ScenarioStepType.BattleSection) section = ScenarioSection.Battle;
                else if (list[i].type == ScenarioStepType.ClearSection) section = ScenarioSection.Clear;
            }
            return section;
        }
        /// <summary>구간별 '한 번만' 기록 ID. 입장 후 구간은 대본 ID, 입장 전은 '대본 ID:before', 클리어 후는 '대본 ID:clear'
        /// (클리어 후 구간만 있는 대본은 대본 ID: 예전 클리어 대본과 같은 기록).</summary>
        public string SeenId(ScenarioSection section)
            => section == ScenarioSection.Before ? scriptId + ":before" : section == ScenarioSection.Battle ? scriptId : HasStartPart || HasBeforePart ? scriptId + ":clear" : scriptId;
        /// <summary>구간 경계를 맞춘다(편집기용): 입장 후 구간 시작 단계가 없으면 맨 앞에(입장 전 구간 비움), 클리어 후 구간 시작 단계가 없으면
        /// 예전 시작 시점대로 입장 후 구간 시작 바로 뒤(Battle Won)나 맨 뒤에 넣고, 같은 경계가 두 개 이상이면 첫 번째만 남기며, 순서가 거꾸로면 클리어 경계를 입장 후 경계 뒤로 옮긴다. 바뀌었으면 true.</summary>
        public bool EnsureSections()
        {
            if (IsTalk) return false; // 대화 대본은 구간이 없다
            var list = new System.Collections.Generic.List<ScenarioStep>(Steps);
            bool changed = false;
            bool legacyWon = settings.startTiming == ScenarioStartTiming.BattleWon;
            void Dedupe(ScenarioStepType type)
            {
                int first = list.FindIndex(s => s.type == type);
                for (int i = list.Count - 1; i > first && first >= 0; i--) if (list[i].type == type) { list.RemoveAt(i); changed = true; }
            }
            Dedupe(ScenarioStepType.BattleSection); Dedupe(ScenarioStepType.ClearSection);
            if (list.FindIndex(s => s.type == ScenarioStepType.BattleSection) < 0) { list.Insert(0, new ScenarioStep { type = ScenarioStepType.BattleSection }); changed = true; }
            int battle = list.FindIndex(s => s.type == ScenarioStepType.BattleSection), clear = list.FindIndex(s => s.type == ScenarioStepType.ClearSection);
            if (clear < 0) { list.Insert(legacyWon ? battle + 1 : list.Count, new ScenarioStep { type = ScenarioStepType.ClearSection }); changed = true; }
            else if (clear < battle) { var marker = list[clear]; list.RemoveAt(clear); list.Insert(list.FindIndex(s => s.type == ScenarioStepType.BattleSection) + 1, marker); changed = true; }
            if (settings.startTiming != ScenarioStartTiming.BattleReady) { settings.startTiming = ScenarioStartTiming.BattleReady; changed = true; }
            if (changed) { steps = list.ToArray(); EnsureStepIds(); }
            return changed;
        }
        public void Set(string id, ScenarioStep[] value) { scriptId = id; steps = value ?? Array.Empty<ScenarioStep>(); EnsureStepIds(); }
        /// <summary>버전의 단계 목록을 바꾼다(0 = 버전 1 = Steps). 범위 밖이면 아무것도 하지 않는다. 편집기(노드 안 타임라인)가 Undo 기록 뒤 부른다.</summary>
        public void SetVersionSteps(int index, ScenarioStep[] value)
        {
            if (index <= 0) { steps = value ?? Array.Empty<ScenarioStep>(); EnsureStepIds(); return; }
            if (index >= VersionCount) return;
            moreVersions[index - 1].steps = value ?? Array.Empty<ScenarioStep>();
            EnsureStepIds();
        }
        /// <summary>대화 대본에 빈 지문 한 줄의 버전을 맨 뒤에 더한다. 새 버전 번호(0부터)를 돌려준다. 전투 진입 대본이면 -1.</summary>
        public int AddVersion()
        {
            if (!IsTalk) return -1;
            var list = new System.Collections.Generic.List<ScenarioVersion>(moreVersions ?? Array.Empty<ScenarioVersion>())
                { new ScenarioVersion { steps = new[] { new ScenarioStep { type = ScenarioStepType.Line, line = DialogueLine.Narrate("") } } } };
            moreVersions = list.ToArray(); EnsureStepIds();
            return VersionCount - 1;
        }
        /// <summary>버전을 지운다(버전이 하나뿐이면 지우지 않음). 버전 1을 지우면 버전 2가 버전 1이 된다. 지웠으면 true.</summary>
        public bool RemoveVersion(int index)
        {
            if (VersionCount <= 1 || index < 0 || index >= VersionCount) return false;
            var list = new System.Collections.Generic.List<ScenarioVersion>(moreVersions);
            if (index == 0) { steps = list[0].Steps; list.RemoveAt(0); }
            else list.RemoveAt(index - 1);
            moreVersions = list.ToArray();
            return true;
        }
        /// <summary>대화 대본의 버전 2부터를 바꾼다(코드·테스트용. 편집기는 직렬화 속성으로 고친다).</summary>
        public void SetMoreVersions(params ScenarioStep[][] versions)
        {
            moreVersions = Array.ConvertAll(versions ?? Array.Empty<ScenarioStep[]>(), v => new ScenarioVersion { steps = v ?? Array.Empty<ScenarioStep>() });
            EnsureStepIds();
        }
        /// <summary>대본 설정을 바꾼다(코드·테스트용. 편집기는 직렬화 속성으로 고친다).</summary>
        public void SetOptions(Settings value) => settings = value;
        /// <summary>비었거나 앞 단계와 겹치는 단계 ID를 새로 채운다(복사·추가된 단계). 바뀌었으면 true. 진행에는 쓰지 않고 편집기만 쓴다.</summary>
        public bool EnsureStepIds()
        {
            // 버전 사이에서도 겹치지 않게 한다(단계 ID가 번역 표의 키).
            var seen = new System.Collections.Generic.HashSet<string>(); bool changed = false;
            void Fix(ScenarioStep[] list)
            {
                for (int i = 0; i < list.Length; i++)
                    if (string.IsNullOrEmpty(list[i].id) || !seen.Add(list[i].id)) { list[i].id = Guid.NewGuid().ToString("N"); seen.Add(list[i].id); changed = true; }
            }
            Fix(Steps);
            if (moreVersions != null) for (int v = 0; v < moreVersions.Length; v++) { if (moreVersions[v].steps == null) moreVersions[v].steps = Array.Empty<ScenarioStep>(); Fix(moreVersions[v].steps); }
            return changed;
        }
        // Inspector에서 단계를 복제하면 ID가 겹치므로 고친다.
        private void OnValidate() => EnsureStepIds();
    }
}
