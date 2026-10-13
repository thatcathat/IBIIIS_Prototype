using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace IBIIIS.Editor
{
    /// <summary>번역 표에 들어갈 글 하나: 표·키, 한국어 원문, 영어 처음 값(없으면 빈칸 = 번역 전), 번역가를 위한 맥락(화자·장면 등).</summary>
    public readonly struct LocalizedEntry
    {
        public readonly string Table, Key, Korean, SeedEnglish, Context;
        public LocalizedEntry(string table, string key, string korean, string seedEnglish = null, string context = null)
        { Table = table; Key = key; Korean = korean; SeedEnglish = seedEnglish; Context = context; }
    }

    /// <summary>번역 표(Unity Localization 문자열 표)를 만들고 원문과 맞춘다.
    /// 관리 기준: 한국어 칸은 원문(코드의 UiText, 대본·인물 등 에셋)을 그대로 비춘 사본이라 동기화 때마다 덮어쓴다(표에서 직접 고치지 않음).
    /// 영어 등 번역 칸은 표·CSV 가져오기가 기준이며, 동기화는 비어 있는 키를 새로 만들 때만 처음 값을 넣는다.</summary>
    public static class LocalizationTables
    {
        public const string MenuRoot = "IBIIIS/Localization/";

        [MenuItem(MenuRoot + "Sync Tables (원문 → 번역 표)")]
        private static void SyncMenu()
        {
            var report = Sync();
            Debug.Log($"[IBIIIS] 번역 표 동기화: {report}");
            EditorUtility.DisplayDialog("번역 표 동기화", report.ToString(), "확인");
        }

        // ── 설정·언어 ──
        /// <summary>Localization 설정과 지원 언어(Locale)를 없을 때만 만든다. 원문 언어(한국어)를 프로젝트 언어로 둔다.</summary>
        public static LocalizationSettings EnsureSettings()
        {
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                AssetPaths.EnsureFolder(AssetPaths.Localization);
                settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetPaths.LocalizationSettings);
                if (settings == null) { settings = ScriptableObject.CreateInstance<LocalizationSettings>(); AssetDatabase.CreateAsset(settings, AssetPaths.LocalizationSettings); }
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }
            foreach (var code in GameLanguage.Supported)
            {
                if (LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code)) != null) continue;
                AssetPaths.EnsureFolder(AssetPaths.Locales);
                var locale = Locale.CreateLocale(new LocaleIdentifier(code));
                string file = new System.Globalization.CultureInfo(code).EnglishName.Replace(" ", ""); // Korean, English(공백 없는 이름 규칙)
                locale.name = file;
                AssetDatabase.CreateAsset(locale, $"{AssetPaths.Locales}/{file}.asset");
                LocalizationEditorSettings.AddLocale(locale, false);
            }
            var source = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(GameLanguage.Source));
            if (source != null && LocalizationSettings.ProjectLocale != source) LocalizationSettings.ProjectLocale = source;
            // 빌드에 번역 표가 들어가도록 Addressables를 플레이어 빌드와 함께 항상 만든다(PC별 환경설정에 맡기지 않음).
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (addressables != null && addressables.BuildAddressablesWithPlayerBuild != UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            { addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer; EditorUtility.SetDirty(addressables); }
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            return settings;
        }
        /// <summary>표 묶음(StringTableCollection)을 없을 때만 만든다(지원 언어마다 표 하나).</summary>
        public static StringTableCollection EnsureCollection(string name)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(name);
            if (collection != null) return collection;
            AssetPaths.EnsureFolder(AssetPaths.StringTables);
            return LocalizationEditorSettings.CreateStringTableCollection(name, AssetPaths.StringTables);
        }
        public static StringTable Table(StringTableCollection collection, string language) => collection.GetTable(new LocaleIdentifier(language)) as StringTable;

        // ── 원문 모으기 ──
        /// <summary>번역할 모든 글. 화면 문구(UiText)와 등록된 원문 모음(대본·인물 등)을 합친다.</summary>
        public static List<LocalizedEntry> CollectEntries()
        {
            var list = new List<LocalizedEntry>();
            foreach (var field in typeof(UiText).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is GameString s) list.Add(new LocalizedEntry(GameText.UiTable, s.Key, s.Korean, s.English, "화면 문구"));
            LocalizationSources.Collect(list);
            return list;
        }

        // ── 동기화 ──
        public sealed class SyncReport
        {
            public int Added, KoreanUpdated, Unchanged; public readonly List<string> Stale = new List<string>(), Problems = new List<string>();
            public override string ToString() => $"새 키 {Added}개, 한국어 원문 갱신 {KoreanUpdated}개, 그대로 {Unchanged}개" +
                (Stale.Count > 0 ? $", 원문에 없는 키 {Stale.Count}개(남겨 둠: {string.Join(", ", Stale.Take(8))}{(Stale.Count > 8 ? " …" : "")})" : "") +
                (Problems.Count > 0 ? $"\n문제 {Problems.Count}개: {string.Join("; ", Problems.Take(8))}" : "");
        }
        /// <summary>원문을 번역 표에 맞춘다: 한국어 칸은 원문으로 덮어쓰고, 새 키는 다른 언어 칸에 처음 값(있으면)을 넣는다. 원문에 없는 키는 지우지 않고 알린다.</summary>
        public static SyncReport Sync()
        {
            var report = new SyncReport();
            EnsureSettings();
            var entries = CollectEntries();
            foreach (var dup in entries.GroupBy(e => e.Table + "/" + e.Key).Where(g => g.Count() > 1)) report.Problems.Add($"키 중복 {dup.Key}");
            foreach (var group in entries.Where(e => !string.IsNullOrEmpty(e.Key)).GroupBy(e => e.Table))
            {
                var collection = EnsureCollection(group.Key);
                var korean = Table(collection, GameLanguage.Source);
                if (korean == null) { report.Problems.Add($"{group.Key}: 한국어 표 없음"); continue; }
                var keys = new HashSet<string>();
                foreach (var entry in group)
                {
                    if (!keys.Add(entry.Key)) continue;
                    bool isNew = collection.SharedData.GetEntry(entry.Key) == null;
                    var existing = korean.GetEntry(entry.Key);
                    if (isNew) report.Added++;
                    if (existing == null || existing.Value != entry.Korean) { korean.AddEntry(entry.Key, entry.Korean); if (!isNew) report.KoreanUpdated++; }
                    else if (!isNew) report.Unchanged++;
                    if (!isNew) continue;
                    foreach (var code in GameLanguage.Supported)
                    {
                        if (code == GameLanguage.Source) continue;
                        var table = Table(collection, code);
                        if (table != null && table.GetEntry(entry.Key) == null) table.AddEntry(entry.Key, code == "en" ? entry.SeedEnglish ?? "" : "");
                    }
                }
                foreach (var shared in collection.SharedData.Entries) if (!keys.Contains(shared.Key)) report.Stale.Add($"{group.Key}/{shared.Key}");
                foreach (var table in collection.StringTables) EditorUtility.SetDirty(table);
                EditorUtility.SetDirty(collection.SharedData);
            }
            AssetDatabase.SaveAssets();
            return report;
        }
    }
}
