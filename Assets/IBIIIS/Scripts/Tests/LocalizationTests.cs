using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IBIIIS.Editor;
using NUnit.Framework;

namespace IBIIIS.Tests
{
    /// <summary>화면 글자 번역 규칙(GameText): 한국어는 원문, 다른 언어는 표에서 찾고 없으면 언어 표시를 붙인 원문.</summary>
    public sealed class LocalizationTests
    {
        private sealed class FakeSource : ITextSource
        {
            public readonly Dictionary<string, string> Texts = new Dictionary<string, string>();
            public int Calls;
            public string Find(string table, string key, string language) { Calls++; return Texts.TryGetValue($"{table}/{key}/{language}", out var text) ? text : null; }
        }
        private ITextSource saved;
        private FakeSource fake;
        [SetUp] public void Setup() { saved = GameText.Source; GameText.Source = fake = new FakeSource(); }
        [TearDown] public void Cleanup() { GameText.Source = saved; GameLanguage.UseForTests(GameLanguage.Source); }

        [Test] public void KoreanUsesTheSourceWithoutLookingUpTables()
        {
            GameLanguage.UseForTests("ko");
            Assert.AreEqual("남은 적 2/3", UiText.HudEnemies.Format(("alive", 2), ("total", 3)));
            Assert.AreEqual(0, fake.Calls, "원문 언어는 표를 보지 않음");
        }
        [Test] public void OtherLanguagesUseTheTableOrMarkUntranslatedText()
        {
            GameLanguage.UseForTests("en");
            fake.Texts["UI/hud.enemies/en"] = "Foes {alive}/{total}";
            Assert.AreEqual("Foes 2/3", UiText.HudEnemies.Format(("alive", 2), ("total", 3)));
            Assert.AreEqual("[EN] 되돌리기", UiText.HudUndo.Text, "번역이 없으면 언어 표시 + 원문");
            Assert.AreEqual("원문", GameText.Get(GameText.UiTable, "", "원문"), "키가 없으면 원문");
            Assert.AreEqual("", GameText.Get(GameText.UiTable, "x", ""), "빈 원문에는 표시를 붙이지 않음");
        }
        [Test] public void FillReplacesOnlyNamedPlaceholders()
        {
            Assert.AreEqual("Enter Stage {other}", GameText.Fill("Enter {stage} {other}", ("stage", "Stage")));
        }
        [Test] public void UiTextKeysAreUniqueAndBothLanguagesUseTheSamePlaceholders()
        {
            var strings = typeof(UiText).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (GameString)f.GetValue(null)).ToList();
            CollectionAssert.AllItemsAreUnique(strings.Select(s => s.Key));
            foreach (var s in strings)
            {
                var ko = Regex.Matches(s.Korean, @"\{(\w+)\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
                var en = Regex.Matches(s.English, @"\{(\w+)\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x);
                CollectionAssert.AreEqual(ko, en, s.Key);
            }
            CollectionAssert.IsSubsetOf(strings.Select(s => s.Key), LocalizationTables.CollectEntries().Where(e => e.Table == GameText.UiTable).Select(e => e.Key), "동기화가 모든 화면 문구를 모음");
        }
        [Test] public void DialogueDataUsesTablesByStableIds()
        {
            GameLanguage.UseForTests("en");
            var fly = UnityEngine.ScriptableObject.CreateInstance<DialogueCharacter>(); fly.Set("assi_fly", "어시파리", DialogueSide.Right, null, UnityEngine.Color.white);
            var battle = UnityEngine.ScriptableObject.CreateInstance<BattleDialogue>(); battle.Set(new[] { new DialogueExchange { player = "들켰다!", enemy = "거기 서!" } }, new[] { "어디 갔지?" }, false);
            try
            {
                fake.Texts["Characters/assi_fly/en"] = "AssiFly";
                fake.Texts["Scenario_t/s1/en"] = "Hi!";
                fake.Texts["BattleDialogue/battle.spotted.0.enemy/en"] = "Stop!";
                Assert.AreEqual("AssiFly", fly.LocalizedName);
                var line = new DialogueLine { speaker = fly, text = "안녕!", nameOverride = "???(어시파리)" }.Localized("Scenario_t", "s1");
                Assert.AreEqual("Hi!", line.text); Assert.AreEqual("[EN] ???(어시파리)", line.nameOverride, "이름표 덮어쓰기는 단계 ID.name, 없으면 표시");
                Assert.AreEqual("[EN] 들켰다!", battle.Spotted[0].player); Assert.AreEqual("Stop!", battle.Spotted[0].enemy);
                Assert.AreEqual("[EN] 어디 갔지?", battle.Lost[0]);
                Assert.AreEqual("W A S D", GameText.GetShort("Scenario_t", "s1.keys", "W A S D"), "영문 키 표시는 번역하지 않음");
                Assert.AreEqual("좌클릭", GameText.GetShort("Scenario_t", "s2.keys", "좌클릭"), "번역 없는 짧은 칸은 표시 없이 원문");
                GameLanguage.UseForTests("ko");
                Assert.AreEqual("어시파리", fly.LocalizedName); Assert.AreEqual("들켰다!", battle.Spotted[0].player);
            }
            finally { UnityEngine.Object.DestroyImmediate(fly); UnityEngine.Object.DestroyImmediate(battle); }
        }
        [Test] public void BattleHudShowsTheCurrentLanguage()
        {
            GameLanguage.UseForTests("en");
            fake.Texts["UI/hud.actions/en"] = "Moves {count}";
            var root = new UnityEngine.UIElements.VisualElement();
            var actions = new UnityEngine.UIElements.Label { name = "actions" }; root.Add(actions);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // 이 시험 화면에 없는 요소 경고는 무시
            var hud = new BattleHud(root, "", "", "", "", null, false, null, null, null);
            hud.Refresh(new BattleHudState { Actions = 4 });
            Assert.AreEqual("Moves 4", actions.text);
        }
    }
}
