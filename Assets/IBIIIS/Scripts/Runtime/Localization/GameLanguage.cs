using System;
using UnityEngine;

namespace IBIIIS
{
    /// <summary>게임 언어. 원문은 한국어(ko)이며 한국어일 때는 번역 표를 보지 않고 원문을 그대로 쓴다.
    /// 고른 언어는 PlayerPrefs에 저장하고, 저장된 값이 없으면 시스템 언어가 한국어면 한국어, 아니면 영어로 시작한다.</summary>
    public static class GameLanguage
    {
        /// <summary>원문 언어 코드.</summary>
        public const string Source = "ko";
        /// <summary>지원 언어 코드(언어 바꾸기 버튼이 이 순서로 돈다). 새 언어는 Localization 설정의 Locale과 함께 추가한다.</summary>
        public static readonly string[] Supported = { "ko", "en" };
        private const string PrefKey = "IBIIIS.Language";
        private static string current;
        /// <summary>언어가 바뀌면 1씩 늘어난다. 화면은 이 값이 바뀌었을 때 글자를 다시 채운다.</summary>
        public static int Version { get; private set; }
        public static event Action Changed;
        public static string Current => current ??= Initial();
        public static bool IsSource => Current == Source;
        public static bool IsSupported(string code) => Array.IndexOf(Supported, code) >= 0;
        /// <summary>언어를 바꾸고 저장한다. 지원하지 않는 코드는 무시한다.</summary>
        public static void Set(string code)
        {
            if (!IsSupported(code) || code == Current) return;
            current = code; Version++;
            try { PlayerPrefs.SetString(PrefKey, code); PlayerPrefs.Save(); }
            catch (Exception e) { Debug.LogWarning($"[IBIIIS] 언어 설정을 저장하지 못했습니다(이번 실행에만 적용): {e.Message}"); }
            Changed?.Invoke();
        }
        /// <summary>지원 언어 목록에서 다음 언어.</summary>
        public static string Next => Supported[(Array.IndexOf(Supported, Current) + 1) % Supported.Length];
        /// <summary>언어 이름(그 언어로 쓴 이름).</summary>
        public static string DisplayName(string code) => code == "ko" ? "한국어" : code == "en" ? "English" : code;
        private static string Initial()
        {
            string saved = null;
            try { saved = PlayerPrefs.GetString(PrefKey, null); } catch (Exception) { }
            if (IsSupported(saved)) return saved;
            return Application.systemLanguage == SystemLanguage.Korean ? Source : "en";
        }
        /// <summary>테스트용: 저장하지 않고 언어를 정한다(null이면 처음 상태로).</summary>
        internal static void UseForTests(string code) { current = code; Version++; }
    }
}
