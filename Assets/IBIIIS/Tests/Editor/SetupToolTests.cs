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
        [Test] public void DefaultEnemiesUseSpriteVisualsAndLeaveExistingPrefabsAlone()
        {
            var folder = AssetDatabase.GenerateUniqueAssetPath("Assets/IBIIIS/Tests/GeneratedEnemies"); paths.Add(folder);
            var created = EnemyPrefabSetup.EnsureDefaults(folder);
            Assert.AreEqual(3, created.Count);
            foreach (var path in created)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                var definition = prefab.GetComponent<EnemyDefinition>(); Assert.IsTrue(definition.IsValid, path);
                var renderer = definition.Visual != null ? definition.Visual.GetComponent<UnityEngine.SpriteRenderer>() : null;
                Assert.IsNotNull(renderer, "Visual은 SpriteRenderer"); Assert.IsNotNull(renderer.sprite, "임시 그림 연결");
                Assert.AreEqual(new UnityEngine.Vector3(1.5f, 1.5f, 1), definition.Visual.localScale);
                Assert.IsEmpty(prefab.GetComponentsInChildren<UnityEngine.MeshRenderer>(true), "Quad·재질을 만들지 않음");
                Assert.IsNotNull(prefab.transform.Find(GroundMarkerSetup.ShadowName)); Assert.IsNotNull(prefab.transform.Find(GroundMarkerSetup.ArrowName));
            }
            Assert.IsEmpty(AssetDatabase.FindAssets("t:Material", new[] { folder }), "재질 에셋 없음");
            // 사용자가 화살표를 지운 프리팹은 다시 실행해도 그대로 둔다.
            var first = created[0]; var contents = PrefabUtility.LoadPrefabContents(first);
            try { UnityEngine.Object.DestroyImmediate(contents.transform.Find(GroundMarkerSetup.ArrowName).gameObject); PrefabUtility.SaveAsPrefabAsset(contents, first); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Assert.IsEmpty(EnemyPrefabSetup.EnsureDefaults(folder), "이미 있으면 만들지 않음");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(first).transform.Find(GroundMarkerSetup.ArrowName), "지운 화살표가 되살아나지 않음");
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
