using System.Collections.Generic;
using System.IO;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace IBIIIS.Tests
{
    public sealed class SetupToolTests
    {
        private readonly List<string> paths = new List<string>();
        private string NewPath()
        {
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/SetupCheck.inputactions"); paths.Add(path);
            return path;
        }
        [TearDown] public void TearDown()
        {
            foreach (var path in paths) { AssetDatabase.DeleteAsset(path); if (File.Exists(path)) File.Delete(path); }
            paths.Clear();
        }
        [Test] public void BrokenInputFileIsKeptInsteadOfOverwritten()
        {
            var path = NewPath(); const string broken = "{ \"name\": \"Broken\", \"maps\": [ ";
            File.WriteAllText(path, broken);
            LogAssert.ignoreFailingMessages = true; // 입력 임포터와 이 도구가 남기는 가져오기 오류는 의도한 결과다
            Assert.IsNull(InputSetup.LoadOrCreate(path, OverworldInput.CreateDefaultAsset));
            Assert.AreEqual(broken, File.ReadAllText(path));
        }
        [Test] public void MissingInputFileIsCreatedWithDefaults()
        {
            var path = NewPath();
            var asset = InputSetup.LoadOrCreate(path, OverworldInput.CreateDefaultAsset);
            Assert.IsNotNull(asset); Assert.IsNotNull(asset.FindActionMap(OverworldInput.MapName));
            Assert.AreSame(asset, InputSetup.LoadOrCreate(path, () => { Assert.Fail("이미 있는 파일을 다시 만들면 안 된다."); return null; }));
        }
    }
}
