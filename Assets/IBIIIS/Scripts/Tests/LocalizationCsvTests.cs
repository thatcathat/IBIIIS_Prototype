using System.Collections.Generic;
using System.Linq;
using IBIIIS.Editor;
using NUnit.Framework;

namespace IBIIIS.Tests
{
    /// <summary>번역 CSV 내보내기·가져오기 규칙(표 대신 메모리 저장소로).</summary>
    public sealed class LocalizationCsvTests
    {
        private sealed class MemoryStore : LocalizationCsv.IStore
        {
            public readonly Dictionary<(string, string, string), string> Values = new Dictionary<(string, string, string), string>();
            public readonly Dictionary<(string, string, string), string> Records = new Dictionary<(string, string, string), string>();
            public int Saves;
            public IEnumerable<(string table, string key)> Keys => Values.Keys.Select(k => (k.Item1, k.Item2)).Distinct();
            public string Get(string table, string key, string language) => Values.TryGetValue((table, key, language), out var v) ? v : null;
            public void Set(string table, string key, string language, string value) => Values[(table, key, language)] = value;
            public string Recorded(string table, string key, string language) => Records.TryGetValue((table, key, language), out var v) ? v : null;
            public void Record(string table, string key, string language, string korean) => Records[(table, key, language)] = korean;
            public void Save() => Saves++;
            public void Add(string table, string key, string ko, string en = "") { Values[(table, key, "ko")] = ko; Values[(table, key, "en")] = en; }
        }
        private static MemoryStore Sample()
        {
            var store = new MemoryStore();
            store.Add("UI", "hud.enemies", "남은 적 {alive}/{total}", "Enemies {alive}/{total}");
            store.Add("Scenario_t", "a1", "안녕, \"플레이어\"!\n반가워");
            store.Add("Scenario_t", "a2", "...", "...");
            return store;
        }
        private static string Csv(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

        [Test] public void ExportRoundTripsTrickyTextAndMarksMissingTranslations()
        {
            var store = Sample();
            var csv = LocalizationCsv.Export(store);
            var rows = LocalizationCsv.Parse(csv);
            CollectionAssert.AreEqual(new[] { "table", "key", "context", "ko", "en", "status" }, rows[0]);
            var a1 = rows.First(r => r[1] == "a1");
            Assert.AreEqual("안녕, \"플레이어\"!\n반가워", a1[3], "쉼표·큰따옴표·줄바꿈이 그대로");
            StringAssert.Contains(LocalizationCsv.StatusMissing, a1[5]);
            Assert.AreEqual("", rows.First(r => r[1] == "a2")[5], "번역이 있으면 표시 없음");
        }
        [Test] public void ImportChangesOnlyTranslationsAndRecordsTheSourceItWasTranslatedFrom()
        {
            var store = Sample();
            var plan = LocalizationCsv.PlanImport(store, Csv("table,key,ko,en", "Scenario_t,a1,바뀐 원문,Hello!", "Scenario_t,a2,...,", "UI,hud.enemies,x,Enemies {alive}/{total}"));
            Assert.IsTrue(plan.Ok, plan.Summary());
            Assert.AreEqual(2, plan.Changes.Count, "a1 새 번역, a2 지움"); Assert.AreEqual(1, plan.Unchanged);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("ko 칸")), "ko 칸이 원문과 다르면 알리기만 함");
            Assert.IsTrue(LocalizationCsv.Apply(store, plan)); Assert.AreEqual(1, store.Saves);
            Assert.AreEqual("Hello!", store.Get("Scenario_t", "a1", "en"));
            Assert.AreEqual("", store.Get("Scenario_t", "a2", "en"), "빈칸은 번역 없음으로 지움");
            Assert.AreEqual("안녕, \"플레이어\"!\n반가워", store.Get("Scenario_t", "a1", "ko"), "한국어 원문은 바꾸지 않음");
            // 번역한 뒤 원문이 바뀌면 내보낼 때 표시
            store.Values[("Scenario_t", "a1", "ko")] = "고친 원문";
            var a1 = LocalizationCsv.Parse(LocalizationCsv.Export(store)).First(r => r[1] == "a1");
            StringAssert.Contains(LocalizationCsv.StatusStale, a1[5]);
        }
        [Test] public void AnyErrorBlocksTheWholeImport()
        {
            var store = Sample();
            var plan = LocalizationCsv.PlanImport(store, Csv("table,key,en", "Scenario_t,a1,Hello!", "Scenario_t,missing,x", "Scenario_t,a1,again", "UI,hud.enemies,Enemies {alive}"));
            Assert.IsFalse(plan.Ok);
            Assert.AreEqual(3, plan.Errors.Count, string.Join("\n", plan.Errors)); // 없는 키·중복·자리 표시
            Assert.IsFalse(LocalizationCsv.Apply(store, plan));
            Assert.AreEqual("", store.Get("Scenario_t", "a1", "en"), "오류가 있으면 정상 줄도 반영하지 않음"); Assert.AreEqual(0, store.Saves);
        }
        [Test] public void BadFilesAreRejected()
        {
            Assert.IsFalse(LocalizationCsv.PlanImport(Sample(), "").Ok);
            Assert.IsFalse(LocalizationCsv.PlanImport(Sample(), Csv("a,b,c", "1,2,3")).Ok, "table·key 칸 없음");
            Assert.IsFalse(LocalizationCsv.PlanImport(Sample(), Csv("table,key,ko", "UI,hud.enemies,x")).Ok, "번역 칸 없음");
            Assert.IsFalse(LocalizationCsv.PlanImport(Sample(), "table,key,en\r\nUI,\"hud.enemies,x\r\n").Ok, "닫히지 않은 큰따옴표");
        }
    }
}
