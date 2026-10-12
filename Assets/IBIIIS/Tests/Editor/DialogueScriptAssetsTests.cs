using System.Linq;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>IBIIIS Dialogue Editor의 새 대본·대본 복제 규칙.</summary>
    public sealed class DialogueScriptAssetsTests
    {
        private string folder;
        [SetUp] public void Setup()
        {
            folder = "Assets/IBIIIS/Tests/Temp_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            AssetDatabase.CreateFolder("Assets/IBIIIS/Tests", folder.Substring(folder.LastIndexOf('/') + 1));
        }
        [TearDown] public void Cleanup() { AssetDatabase.DeleteAsset(folder); }

        [TestCase("Tutorial3Script", "tutorial_3")]
        [TestCase("BossIntro.asset", "boss_intro")]
        [TestCase("Stage 2 시작Script", "stage_2")]
        [TestCase("Script", "script")]
        public void SuggestsIdFromFileName(string fileName, string id) => Assert.AreEqual(id, DialogueScriptAssets.SuggestId(fileName));

        [Test] public void RejectsEmptyInvalidOrTakenIds()
        {
            Assert.IsNotNull(DialogueScriptAssets.IdProblem(""), "빈 ID");
            Assert.IsNotNull(DialogueScriptAssets.IdProblem("Boss Intro"), "대문자·공백");
            var tutorial = AssetDatabase.LoadAssetAtPath<ScenarioScript>(TutorialSetup.Stage1Script);
            StringAssert.Contains("Tutorial1Script", DialogueScriptAssets.IdProblem(tutorial.Id), "다른 대본이 쓰는 ID");
            Assert.IsNull(DialogueScriptAssets.IdProblem(tutorial.Id, tutorial), "자기 자신은 제외");
            Assert.IsNull(DialogueScriptAssets.IdProblem("test_unused_id_" + System.Guid.NewGuid().ToString("N").Substring(0, 6)));
        }
        [Test] public void CreatesABlankScriptWithOneNarrationLine()
        {
            var created = DialogueScriptAssets.Create(folder + "/New.asset", "test_new_blank", DialogueScriptAssets.BlankSteps(), out string problem);
            Assert.IsNull(problem); Assert.IsNotNull(created);
            Assert.AreEqual("test_new_blank", created.Id);
            Assert.AreEqual(1, created.Steps.Length); Assert.AreEqual(ScenarioStepType.Line, created.Steps[0].type);
            Assert.IsFalse(string.IsNullOrEmpty(created.Steps[0].id), "단계 ID가 채워짐");
            Assert.AreSame(created, AssetDatabase.LoadAssetAtPath<ScenarioScript>(folder + "/New.asset"), "파일로 저장됨");
            Assert.IsNull(DialogueScriptAssets.Create(folder + "/New.asset", "test_other", null, out problem), "있는 파일은 덮어쓰지 않음"); Assert.IsNotNull(problem);
            Assert.IsNull(DialogueScriptAssets.Create(folder + "/Other.asset", "test_new_blank", null, out problem), "겹친 ID는 만들지 않음"); Assert.IsNotNull(problem);
        }
        [Test] public void DuplicatesContentWithNewStepIdsAndLeavesTheOriginal()
        {
            var original = AssetDatabase.LoadAssetAtPath<ScenarioScript>(TutorialSetup.Stage2Script);
            var originalIds = original.Steps.Select(s => s.id).ToArray(); string originalId = original.Id;
            var copy = DialogueScriptAssets.Create(folder + "/Copy.asset", "test_copy", original.Steps, out string problem);
            Assert.IsNull(problem);
            Assert.AreEqual(original.Steps.Length, copy.Steps.Length);
            Assert.IsTrue(copy.Steps.Select(s => s.type).SequenceEqual(original.Steps.Select(s => s.type)), "같은 단계");
            Assert.AreEqual(original.Steps[5].line.text, copy.Steps[5].line.text);
            Assert.IsEmpty(copy.Steps.Select(s => s.id).Intersect(originalIds), "단계 ID는 새로 받음");
            CollectionAssert.AreEqual(originalIds, original.Steps.Select(s => s.id).ToArray(), "원본 단계 ID 그대로"); Assert.AreEqual(originalId, original.Id, "원본 대본 ID 그대로");
        }
    }
}
