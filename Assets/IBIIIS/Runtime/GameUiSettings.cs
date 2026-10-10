using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>게임 화면 UI(UI Toolkit) 공용 설정: 화면 배율·테마(Panel Settings), 전투 HUD·결과 팝업·미니맵 안내의 화면 구성(UXML), 회피기 아이콘.
    /// 전투(Player Settings)와 미니맵(Overworld Settings)이 같은 에셋을 슬롯으로 참조한다. `IBIIIS > Create Default Game UI`로 만든다.
    /// 이 에셋이나 필요한 화면 구성이 비어 있으면 해당 화면만 임시 IMGUI 표시로 대신하고 게임 진행은 막지 않는다.</summary>
    [CreateAssetMenu(menuName = "IBIIIS/Game UI Settings")]
    public sealed class GameUiSettings : ScriptableObject
    {
        [SerializeField, Tooltip("필수. 화면 배율(기준 해상도)·테마·정렬. 기본 GamePanelSettings(1920×1080 기준, 가로세로 절반씩 맞춤).")] private PanelSettings panelSettings;
        [SerializeField, Tooltip("전투 HUD 화면 구성. 요소 이름(name)은 코드가 찾는 키이므로 유지하고, 배치·스타일은 GameUi.uss에서 바꿉니다.")] private VisualTreeAsset battleHud;
        [SerializeField, Tooltip("스테이지 결과(클리어·패배) 팝업 화면 구성.")] private VisualTreeAsset resultPopup;
        [SerializeField, Tooltip("미니맵 조작 안내·상호작용 안내 화면 구성.")] private VisualTreeAsset overworldHud;
        [SerializeField, Tooltip("선택. 회피기(대시·구르기) 아이콘. 비우면 원형 표시만 보입니다. 쿨타임 중에는 회색으로 바뀌고 남은 턴 수가 표시됩니다.")] private Sprite evasionIcon;
        [SerializeField, Tooltip("개발용 정보(전투 단계·현재 칸 좌표)를 전투 HUD 왼쪽 아래에 함께 표시합니다.")] private bool showDebugInfo;
        public PanelSettings PanelSettings => panelSettings;
        public VisualTreeAsset BattleHud => battleHud;
        public VisualTreeAsset ResultPopup => resultPopup;
        public VisualTreeAsset OverworldHud => overworldHud;
        public Sprite EvasionIcon => evasionIcon;
        public bool ShowDebugInfo => showDebugInfo;

        /// <summary>tree 화면을 띄울 UIDocument 오브젝트를 만든다. Panel Settings나 tree가 없으면 null(호출하는 쪽이 임시 표시로 대신한다).
        /// sortingOrder가 클수록 위에 그린다(팝업 > HUD).</summary>
        public UIDocument CreateDocument(string name, VisualTreeAsset tree, Transform parent, int sortingOrder)
        {
            if (panelSettings == null || tree == null) return null;
            var go = new GameObject(name); go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings; document.visualTreeAsset = tree; document.sortingOrder = sortingOrder;
            go.SetActive(true);
            return document;
        }
        /// <summary>필수 요소를 찾는다. 없으면 어떤 화면의 어떤 이름이 빠졌는지 한 번 경고하고 null.</summary>
        public static T Find<T>(VisualElement root, string elementName, string screen) where T : VisualElement
        {
            var element = root?.Q<T>(elementName);
            if (element == null) Debug.LogWarning($"[IBIIIS] {screen} 화면 구성에 '{elementName}'({typeof(T).Name}) 요소가 없어 그 표시를 생략합니다. UXML의 name을 확인하세요.");
            return element;
        }
    }
}
