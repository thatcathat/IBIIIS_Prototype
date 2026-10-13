using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace IBIIIS
{
    /// <summary>번역 글을 찾는 곳. 기본은 Unity Localization 문자열 표이고, 테스트는 다른 구현으로 바꿀 수 있다.</summary>
    public interface ITextSource
    {
        /// <summary>표(table)의 키(key)에 대한 language 언어 글. 없거나 비어 있으면 null.</summary>
        string Find(string table, string key, string language);
    }

    /// <summary>화면 글자를 지금 언어로 돌려준다. 한국어(원문)면 원문 그대로, 다른 언어면 번역 표에서 찾고, 번역이 비어 있으면
    /// 원문 앞에 언어 표시(예: "[EN] ")를 붙여 보인다(번역 전임을 알 수 있게). 자리 표시 {이름}은 Fill로 채운다.</summary>
    public static class GameText
    {
        /// <summary>표 이름. 대본 표는 Scenario(scriptId)로 만든다.</summary>
        public const string UiTable = "UI", CharactersTable = "Characters", StagesTable = "Stages", BattleDialogueTable = "BattleDialogue";
        public static string Scenario(string scriptId) => "Scenario_" + scriptId;
        public static ITextSource Source { get; set; } = new LocalizationTableSource();
        /// <summary>임시: 번역이 없는 글 앞에 붙는 표시. 정식 번역이 갖춰지면 비우거나 없앤다.</summary>
        public static string UntranslatedMark(string language) => $"[{language.ToUpperInvariant()}] ";

        /// <summary>원문(source)의 지금 언어 글. key가 비면 원문.</summary>
        public static string Get(string table, string key, string source) => Get(table, key, source, true);
        /// <param name="markUntranslated">false면 번역이 없을 때 표시 없이 원문(키 표시·입력 칸 기본값처럼 표시가 끼면 깨지는 칸).</param>
        public static string Get(string table, string key, string source, bool markUntranslated)
        {
            if (GameLanguage.IsSource || string.IsNullOrEmpty(key)) return source;
            string language = GameLanguage.Current, found = null;
            try { found = Source?.Find(table, key, language); }
            catch (Exception e) { Debug.LogException(e); }
            if (!string.IsNullOrEmpty(found)) return found;
            return string.IsNullOrEmpty(source) || !markUntranslated ? source : UntranslatedMark(language) + source;
        }
        /// <summary>키 표시·기본값처럼 짧은 칸은 영문·숫자·기호만 있으면(예: "W A S D") 번역하지 않는다. 한글 등이 있으면 번역 대상.</summary>
        public static bool NeedsTranslation(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text) if (c > 127) return true;
            return false;
        }
        /// <summary>짧은 칸용(키 표시·입력 칸 기본값): 번역 대상일 때만 표에서 찾고, 번역이 없으면 표시 없이 원문.</summary>
        public static string GetShort(string table, string key, string source) => NeedsTranslation(source) ? Get(table, key, source, false) : source;
        /// <summary>{이름} 자리 표시를 값으로 바꾼다. 없는 이름은 그대로 둔다.</summary>
        public static string Fill(string text, params (string name, object value)[] values)
        {
            if (string.IsNullOrEmpty(text) || values == null) return text;
            foreach (var (name, value) in values) text = text.Replace("{" + name + "}", value?.ToString() ?? "");
            return text;
        }
    }

    /// <summary>Unity Localization 문자열 표에서 찾는다. 설정·언어·표가 없으면 null(원문으로 대신 보임). 없는 표는 한 번만 찾는다.</summary>
    public sealed class LocalizationTableSource : ITextSource
    {
        private readonly HashSet<string> missing = new HashSet<string>();
        public string Find(string table, string key, string language)
        {
            if (!LocalizationSettings.HasSettings || missing.Contains(table + "|" + language)) return null;
            var locale = LocalizationSettings.AvailableLocales?.GetLocale(new LocaleIdentifier(language));
            if (locale == null) { missing.Add(table + "|" + language); return null; }
            StringTable strings = null;
            try { strings = LocalizationSettings.StringDatabase.GetTable(table, locale); } catch (Exception) { }
            if (strings == null) { missing.Add(table + "|" + language); return null; }
            var entry = strings.GetEntry(key);
            return entry == null || string.IsNullOrEmpty(entry.Value) ? null : entry.Value;
        }
    }
}
