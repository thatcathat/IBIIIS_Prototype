using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace IBIIIS.Editor
{
    /// <summary>번역 CSV 내보내기·가져오기(번역가가 엑셀·Google 시트에서 고친다).
    /// 칸: table, key, context(맥락·참고), ko(원문·참고), 언어별 번역(en …), status(번역 필요/원문 바뀜, 참고).
    /// 가져오기는 번역 칸만 바꾸고 한국어 원문(코드·에셋)은 바꾸지 않는다. 반영 전에 바뀌는 칸과 오류를 보여 주고, 오류가 하나라도 있으면 아무것도 반영하지 않는다.
    /// 빈칸은 "번역 없음"으로 지운다(게임에서는 [EN] 원문으로 보임). CSV에 없는 줄은 그대로 둔다.</summary>
    public static class LocalizationCsv
    {
        /// <summary>번역 표 읽기·쓰기(실제 표 또는 테스트용).</summary>
        public interface IStore
        {
            IEnumerable<(string table, string key)> Keys { get; }
            string Get(string table, string key, string language);
            void Set(string table, string key, string language, string value);
            string Recorded(string table, string key, string language);
            void Record(string table, string key, string language, string korean);
            void Save();
        }
        public static string[] TargetLanguages => GameLanguage.Supported.Where(l => l != GameLanguage.Source).ToArray();
        public const string StatusMissing = "번역 필요", StatusStale = "원문 바뀜";

        // ── 내보내기 ──
        /// <param name="order">원문 순서(대본 단계 순 등)와 맥락. 여기 없는 키(원문에서 사라진 키)는 뒤에 이름순으로 붙인다.</param>
        public static string Export(IStore store, IReadOnlyList<LocalizedEntry> order = null)
        {
            var contexts = new Dictionary<(string, string), string>(); var keys = new List<(string table, string key)>();
            foreach (var e in order ?? new LocalizedEntry[0]) if (!contexts.ContainsKey((e.Table, e.Key))) { contexts[(e.Table, e.Key)] = e.Context ?? ""; keys.Add((e.Table, e.Key)); }
            var present = new HashSet<(string, string)>(store.Keys);
            keys.RemoveAll(k => !present.Contains(k));
            keys.AddRange(store.Keys.Where(k => !contexts.ContainsKey(k)).OrderBy(k => k.table).ThenBy(k => k.key, new KeyOrder()));
            var sb = new StringBuilder();
            var header = new List<string> { "table", "key", "context", GameLanguage.Source }; header.AddRange(TargetLanguages); header.Add("status");
            sb.Append(Row(header));
            foreach (var (table, key) in keys)
            {
                string korean = store.Get(table, key, GameLanguage.Source) ?? "";
                var row = new List<string> { table, key, contexts.TryGetValue((table, key), out var c) ? c : "(원문에 없음)", korean };
                var status = new List<string>();
                foreach (var language in TargetLanguages)
                {
                    var value = store.Get(table, key, language) ?? ""; row.Add(value);
                    if (value.Length == 0) status.Add($"{language}: {StatusMissing}");
                    else { var seen = store.Recorded(table, key, language); if (seen != null && seen != korean) status.Add($"{language}: {StatusStale}"); }
                }
                row.Add(string.Join(", ", status));
                sb.Append(Row(row));
            }
            return sb.ToString();
        }
        // 대본 키는 단계 순서를 알 수 없으므로 이름순, 순번이 있는 키(예: npc.2)는 숫자 순.
        private sealed class KeyOrder : IComparer<string>
        {
            public int Compare(string a, string b) => string.CompareOrdinal(Regex.Replace(a ?? "", @"\d+", m => m.Value.PadLeft(6, '0')), Regex.Replace(b ?? "", @"\d+", m => m.Value.PadLeft(6, '0')));
        }

        // ── 가져오기 ──
        public sealed class Change { public string Table, Key, Language, Old, New; }
        public sealed class Plan
        {
            public readonly List<Change> Changes = new List<Change>();
            public readonly List<string> Errors = new List<string>(), Notes = new List<string>();
            public int Unchanged;
            public bool Ok => Errors.Count == 0;
            public string Summary()
            {
                int cleared = Changes.Count(c => c.New.Length == 0), added = Changes.Count(c => c.Old.Length == 0 && c.New.Length > 0);
                var sb = new StringBuilder($"바뀌는 칸 {Changes.Count}개(새 번역 {added}, 고침 {Changes.Count - added - cleared}, 지움 {cleared}), 그대로 {Unchanged}개");
                if (Errors.Count > 0) sb.Append($"\n\n오류 {Errors.Count}개 — 아무것도 반영하지 않습니다:\n- " + string.Join("\n- ", Errors.Take(15)) + (Errors.Count > 15 ? "\n- …" : ""));
                if (Notes.Count > 0) sb.Append($"\n\n참고 {Notes.Count}개:\n- " + string.Join("\n- ", Notes.Take(8)) + (Notes.Count > 8 ? "\n- …" : ""));
                foreach (var c in Changes.Take(8)) sb.Append($"\n{c.Table}/{c.Key} [{c.Language}]: \"{Short(c.Old)}\" → \"{Short(c.New)}\"");
                if (Changes.Count > 8) sb.Append("\n…");
                return sb.ToString();
            }
            private static string Short(string s) => s.Length > 30 ? s.Substring(0, 30) + "…" : s;
        }
        /// <summary>CSV를 읽어 반영할 내용과 오류를 만든다(표는 바꾸지 않음).</summary>
        public static Plan PlanImport(IStore store, string csv)
        {
            var plan = new Plan();
            List<List<string>> rows;
            try { rows = Parse(csv); } catch (System.FormatException e) { plan.Errors.Add(e.Message); return plan; }
            if (rows.Count == 0) { plan.Errors.Add("비어 있는 파일"); return plan; }
            var header = rows[0].Select(h => h.Trim()).ToList();
            int tableCol = header.IndexOf("table"), keyCol = header.IndexOf("key"), koCol = header.IndexOf(GameLanguage.Source);
            if (tableCol < 0 || keyCol < 0) { plan.Errors.Add("첫 줄에 table, key 칸이 있어야 합니다"); return plan; }
            var languages = TargetLanguages.Where(header.Contains).ToList();
            if (languages.Count == 0) { plan.Errors.Add($"번역 칸({string.Join(", ", TargetLanguages)})이 없습니다"); return plan; }
            foreach (var extra in header.Where(h => h.Length == 2 && h != GameLanguage.Source && !TargetLanguages.Contains(h))) plan.Notes.Add($"지원하지 않는 언어 칸 '{extra}'은 무시");
            var known = new HashSet<(string, string)>(store.Keys);
            var seen = new HashSet<(string, string)>();
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r]; int line = r + 1;
                if (row.All(string.IsNullOrWhiteSpace)) continue;
                string Cell(int col) => col >= 0 && col < row.Count ? row[col] : "";
                var id = (Cell(tableCol).Trim(), Cell(keyCol).Trim());
                if (!seen.Add(id)) { plan.Errors.Add($"{line}줄: {id.Item1}/{id.Item2}가 두 번 나옴"); continue; }
                if (!known.Contains(id)) { plan.Errors.Add($"{line}줄: 표에 없는 키 {id.Item1}/{id.Item2}(먼저 Sync Tables를 하거나 키를 확인)"); continue; }
                string korean = store.Get(id.Item1, id.Item2, GameLanguage.Source) ?? "";
                if (koCol >= 0 && Cell(koCol) != korean) plan.Notes.Add($"{line}줄: {id.Item1}/{id.Item2}의 ko 칸이 지금 원문과 다름(ko 칸은 반영하지 않음)");
                foreach (var language in languages)
                {
                    string value = Cell(header.IndexOf(language)), old = store.Get(id.Item1, id.Item2, language) ?? "";
                    if (value.Length > 0 && !SamePlaceholders(korean, value)) { plan.Errors.Add($"{line}줄: {id.Item1}/{id.Item2} [{language}] 자리 표시 {{…}}가 원문과 다름"); continue; }
                    if (value == old) { plan.Unchanged++; continue; }
                    plan.Changes.Add(new Change { Table = id.Item1, Key = id.Item2, Language = language, Old = old, New = value });
                }
            }
            return plan;
        }
        /// <summary>계획을 표에 반영한다(오류가 있으면 아무것도 하지 않음). 반영한 번역마다 지금 원문을 기록한다.</summary>
        public static bool Apply(IStore store, Plan plan)
        {
            if (!plan.Ok) return false;
            foreach (var c in plan.Changes)
            {
                store.Set(c.Table, c.Key, c.Language, c.New);
                store.Record(c.Table, c.Key, c.Language, c.New.Length == 0 ? null : store.Get(c.Table, c.Key, GameLanguage.Source));
            }
            store.Save();
            return true;
        }
        public static bool SamePlaceholders(string a, string b)
        {
            var pa = Regex.Matches(a ?? "", @"\{(\w+)\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
            var pb = Regex.Matches(b ?? "", @"\{(\w+)\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
            return pa.SequenceEqual(pb);
        }

        // ── CSV 형식(RFC 4180: 쉼표·큰따옴표·줄바꿈은 큰따옴표로 감싸고 큰따옴표는 두 번) ──
        private static string Row(IEnumerable<string> cells) => string.Join(",", cells.Select(Quote)) + "\r\n";
        private static string Quote(string s)
        {
            s ??= "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
            bool quoted = false, any = false; int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1; // BOM
            for (; i < text.Length; i++)
            {
                char c = text[i]; any = true;
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else cell.Append(c);
                    continue;
                }
                if (c == '"' && cell.Length == 0) quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); any = false; }
                else cell.Append(c);
            }
            if (quoted) throw new System.FormatException("닫히지 않은 큰따옴표가 있습니다");
            if (any || cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        // ── 실제 번역 표 ──
        public sealed class TableStore : IStore
        {
            private readonly TranslationRecord record;
            private readonly HashSet<Object> dirty = new HashSet<Object>();
            public TableStore() { record = LoadRecord(); }
            public IEnumerable<(string table, string key)> Keys
            {
                get
                {
                    foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
                        foreach (var entry in collection.SharedData.Entries) yield return (collection.TableCollectionName, entry.Key);
                }
            }
            public string Get(string table, string key, string language)
            {
                var collection = LocalizationEditorSettings.GetStringTableCollection(table); if (collection == null) return null;
                return LocalizationTables.Table(collection, language)?.GetEntry(key)?.Value;
            }
            public void Set(string table, string key, string language, string value)
            {
                var strings = LocalizationTables.Table(LocalizationEditorSettings.GetStringTableCollection(table), language);
                Undo.RecordObject(strings, "번역 CSV 가져오기");
                strings.AddEntry(key, value); dirty.Add(strings);
            }
            public string Recorded(string table, string key, string language) => record.Find(table, key, language);
            public void Record(string table, string key, string language, string korean) { Undo.RecordObject(record, "번역 CSV 가져오기"); record.Set(table, key, language, korean); dirty.Add(record); }
            public void Save() { foreach (var o in dirty) EditorUtility.SetDirty(o); AssetDatabase.SaveAssets(); dirty.Clear(); }
            private static TranslationRecord LoadRecord()
            {
                var record = AssetDatabase.LoadAssetAtPath<TranslationRecord>(AssetPaths.TranslationRecord);
                if (record != null) return record;
                AssetPaths.EnsureFolder(AssetPaths.Localization);
                record = ScriptableObject.CreateInstance<TranslationRecord>(); AssetDatabase.CreateAsset(record, AssetPaths.TranslationRecord);
                return record;
            }
        }

        // ── 메뉴 ──
        [MenuItem(LocalizationTables.MenuRoot + "Export CSV…")]
        private static void ExportMenu()
        {
            var sync = LocalizationTables.Sync(); // 원문을 먼저 맞춘다
            var path = EditorUtility.SaveFilePanel("번역 CSV 내보내기", DefaultFolder(), "IBIIIS_Translations.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, Export(new TableStore(), LocalizationTables.CollectEntries()), new UTF8Encoding(true)); // BOM: 엑셀에서 한글이 깨지지 않게
            Debug.Log($"[IBIIIS] 번역 CSV 내보냄: {path} (동기화: {sync})");
            EditorUtility.RevealInFinder(path);
        }
        [MenuItem(LocalizationTables.MenuRoot + "Import CSV…")]
        private static void ImportMenu()
        {
            var path = EditorUtility.OpenFilePanel("번역 CSV 가져오기", DefaultFolder(), "csv");
            if (string.IsNullOrEmpty(path)) return;
            var store = new TableStore();
            var plan = PlanImport(store, File.ReadAllText(path, Encoding.UTF8));
            if (!plan.Ok) { EditorUtility.DisplayDialog("번역 CSV 가져오기 — 반영하지 않음", plan.Summary(), "닫기"); return; }
            if (plan.Changes.Count == 0) { EditorUtility.DisplayDialog("번역 CSV 가져오기", "바뀌는 칸이 없습니다.\n\n" + plan.Summary(), "닫기"); return; }
            if (!EditorUtility.DisplayDialog("번역 CSV 가져오기 — 확인", plan.Summary(), "반영", "취소")) return;
            Apply(store, plan);
            Debug.Log($"[IBIIIS] 번역 CSV 가져옴: {path} — {plan.Changes.Count}칸 반영");
        }
        // 번역 CSV는 게임 에셋이 아니므로 Assets 밖 Translations 폴더를 기본으로 쓴다.
        private static string DefaultFolder()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Translations"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
