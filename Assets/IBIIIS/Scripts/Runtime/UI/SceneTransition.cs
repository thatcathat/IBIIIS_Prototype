using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBIIIS
{
    /// <summary>씬 전환 단계. Out: 지금 씬을 가림, Loading: 불러오는 중(완전히 가려짐), In: 새 씬을 드러냄.</summary>
    public enum SceneTransitionPhase { Out, Loading, In }

    /// <summary>씬 전환 화면(표시 전용). 기본은 검은 페이드(FadeTransitionView)이고, Scene Transition Settings의 View Prefab에
    /// 이 인터페이스를 구현한 컴포넌트가 있는 프리팹(예: 로딩 화면)을 넣으면 그것을 쓴다. 전환이 끝나면 오브젝트째 지워진다.</summary>
    public interface ISceneTransitionView
    {
        /// <param name="cover">화면을 가리는 정도(0 = 다 보임, 1 = 다 가림).</param>
        /// <param name="loadProgress">불러오기 진행(0~1, Loading 단계에서 의미가 있음).</param>
        void Show(SceneTransitionPhase phase, float cover, float loadProgress);
    }

    /// <summary>씬 전환 진행기: 전환 화면으로 지금 씬을 가린 뒤 다음 씬을 불러오고, 불러온 뒤 다시 드러낸다(StageFlow의 모든 씬 이동이 거친다).
    /// 씬이 바뀌어도 남도록 Play 중에는 DontDestroyOnLoad로 두며, 시간 정지(전투 입력 대기)와 무관하게 실제 시간으로 진행한다.
    /// 전환 중에는 다른 전환 요청을 받지 않는다. 시간·색·전환 화면은 Scene Transition Settings(Resources/IBIIIS)에서 바꾼다.</summary>
    public sealed class SceneTransition : MonoBehaviour
    {
        private static SceneTransition current;
        /// <summary>씬을 실제로 불러오는 함수(테스트에서 바꿔 끼운다). 기본은 StageFlow의 씬 불러오기.</summary>
        public static Action<string> Loader { get; set; }
        private SceneTransitionSettings settings;
        private ISceneTransitionView view;
        private GameObject viewObject;
        private string scenePath;
        private float time;
        // 씬을 불러온 직후 몇 프레임은 초기화로 한 프레임이 길어(수백 ms) 드러내기가 한 번에 끝나 버리므로 건너뛴다.
        private int settleFrames;
        /// <summary>한 프레임에 진행하는 최대 시간(초). 멈칫한 프레임 때문에 가리기·드러내기가 건너뛰어지지 않게 한다.</summary>
        public const float MaxStep = 1f / 30;
        /// <summary>불러온 뒤 드러내기 전에 건너뛰는 프레임 수.</summary>
        public const int SettleFrames = 2;
        public static SceneTransition Current => current;
        /// <summary>전환 중이면 true(이동 요청을 받지 않음).</summary>
        public static bool Busy => current != null;
        public SceneTransitionPhase Phase { get; private set; }
        public string ScenePath => scenePath;
        public ISceneTransitionView View => view;
        /// <summary>scenePath로 전환을 시작한다. 이미 전환 중이면 false.</summary>
        public static bool Begin(string scenePath, SceneTransitionSettings settingsOverride = null)
        {
            if (Busy) return false;
            var go = new GameObject("Scene Transition");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            current = go.AddComponent<SceneTransition>();
            current.Setup(scenePath, settingsOverride != null ? settingsOverride : SceneTransitionSettings.Load());
            return true;
        }
        private void Setup(string path, SceneTransitionSettings shared)
        {
            scenePath = path; settings = shared;
            var prefab = settings != null ? settings.ViewPrefab : null;
            if (prefab != null)
            {
                viewObject = Instantiate(prefab, transform);
                view = viewObject.GetComponentInChildren<ISceneTransitionView>(true);
                if (view == null) { Debug.LogWarning($"[IBIIIS] 전환 화면 프리팹 '{prefab.name}'에 ISceneTransitionView 컴포넌트가 없어 기본 페이드를 씁니다.", prefab); Destroy(viewObject); viewObject = null; }
            }
            if (view == null) { var fade = gameObject.AddComponent<FadeTransitionView>(); fade.Color = settings != null ? settings.FadeColor : Color.black; view = fade; }
            Phase = SceneTransitionPhase.Out; time = 0;
            Tick(0);
        }
        private float OutSeconds => settings != null ? settings.OutSeconds : SceneTransitionSettings.DefaultSeconds;
        private float InSeconds => settings != null ? settings.InSeconds : SceneTransitionSettings.DefaultSeconds;
        private void Update() => Tick(Time.unscaledDeltaTime);
        /// <summary>진행한다(Play 중 매 프레임, 테스트에서는 직접 호출).</summary>
        public void Tick(float seconds)
        {
            seconds = Mathf.Min(seconds, MaxStep);
            if (Phase == SceneTransitionPhase.In && settleFrames > 0) { settleFrames--; seconds = 0; }
            switch (Phase)
            {
                case SceneTransitionPhase.Out:
                    time += seconds;
                    view.Show(SceneTransitionPhase.Out, OutSeconds > 0 ? Mathf.Clamp01(time / OutSeconds) : 1, 0);
                    if (time >= OutSeconds) StartLoading();
                    break;
                case SceneTransitionPhase.Loading:
                    view.Show(SceneTransitionPhase.Loading, 1, 0);
                    break;
                case SceneTransitionPhase.In:
                    time += seconds;
                    view.Show(SceneTransitionPhase.In, InSeconds > 0 ? 1 - Mathf.Clamp01(time / InSeconds) : 0, 1);
                    if (time >= InSeconds) Finish();
                    break;
            }
        }
        private void StartLoading()
        {
            Phase = SceneTransitionPhase.Loading; time = 0;
            view.Show(SceneTransitionPhase.Loading, 1, 0);
            SceneManager.sceneLoaded += OnSceneLoaded;
            try { (Loader ?? StageFlow.LoadImmediately)(scenePath); }
            catch (Exception e) { Debug.LogException(e); Finish(); }
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (scene.path == scenePath || string.IsNullOrEmpty(scenePath)) Loaded(); }
        /// <summary>다음 씬을 다 불러왔다(씬 불러오기 이벤트가 부른다. 테스트에서는 직접 호출). 새 씬을 드러내기 시작한다.</summary>
        public void Loaded()
        {
            if (Phase != SceneTransitionPhase.Loading) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Phase = SceneTransitionPhase.In; time = 0; settleFrames = SettleFrames;
            view.Show(SceneTransitionPhase.In, 1, 1);
        }
        private void Finish()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (current == this) current = null;
            RuntimeObjects.Release(gameObject);
        }
        /// <summary>진행 중인 전환을 바로 지운다(테스트·오류 정리용).</summary>
        public static void Cancel() { if (current != null) current.Finish(); }
        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; if (current == this) current = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { current = null; Loader = null; }
    }
}
