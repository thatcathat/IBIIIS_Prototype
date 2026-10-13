using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace IBIIIS
{
    /// <summary>처음 시작 타이틀 화면. '게임 시작' 버튼(또는 Enter)을 누르면 Start Scene(지금은 튜토리얼 1단계)으로 넘어간다.
    /// '언어' 버튼은 지원 언어를 차례로 바꾸고(GameLanguage, 저장됨) 버튼 글자를 바로 다시 채운다.
    /// 시작 전 컷신은 나중에 Start Scene 앞에 넣는다(예정). 화면 구성은 같은 오브젝트의 UIDocument(Title.uxml)가 그린다.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TitleScreen : MonoBehaviour
    {
        [SerializeField, Tooltip("필수. '게임 시작'으로 넘어갈 씬 경로(Assets/...unity). 빌드에서는 빌드 씬 목록에 있어야 합니다.")] private string startScenePath;
        private bool starting;
        private Button start, language;
        public string StartScenePath => startScenePath;
        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            start = GameUiSettings.Find<Button>(root, "start", "타이틀");
            language = GameUiSettings.Find<Button>(root, "language", "타이틀");
            if (start != null) { start.clicked += StartGame; start.Focus(); }
            if (language != null) language.clicked += () => GameLanguage.Set(GameLanguage.Next);
            GameLanguage.Changed += Relabel;
            Relabel();
        }
        private void OnDisable() { GameLanguage.Changed -= Relabel; }
        private void Relabel()
        {
            if (start != null) start.text = UiText.TitleStart.Text;
            if (language != null) language.text = UiText.TitleLanguage.Format(("language", GameLanguage.DisplayName(GameLanguage.Current)));
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) StartGame();
        }
        /// <summary>게임 시작(버튼·Enter와 같은 경로). 한 번만 넘어간다.</summary>
        public void StartGame()
        {
            if (starting) return;
            if (string.IsNullOrEmpty(startScenePath)) { Debug.LogError($"[IBIIIS] {name}: 타이틀의 Start Scene Path가 비어 있습니다.", this); return; }
            starting = StageFlow.LoadScene(startScenePath);
        }
    }
}
