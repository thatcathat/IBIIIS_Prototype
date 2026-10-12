using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IBIIIS.Tests
{
    /// <summary>Preset Manager의 기본 프리셋: 플레이어·적 그림 폴더에 새 PNG를 넣으면 같은 가져오기 설정(스프라이트, PPU, 발밑 기준점)이 자동으로 적용된다.</summary>
    public sealed class SpriteImportPresetTests
    {
        private string path;
        [TearDown] public void Cleanup() { if (path != null) AssetDatabase.DeleteAsset(path); path = null; }
        private TextureImporter ImportNew(string folder)
        {
            path = folder + "/__PresetTest_" + System.Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); } finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }
        [TestCase("Assets/IBIIIS/Characters/Player/Sprites", 500)]
        [TestCase("Assets/IBIIIS/Characters/Enemies/Enemy001", 600)]
        public void NewSpritesGetTheFolderPreset(string folder, float pixelsPerUnit)
        {
            var importer = ImportNew(folder);
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(pixelsPerUnit, importer.spritePixelsPerUnit);
            Assert.AreEqual((int)SpriteAlignment.BottomCenter, settings.spriteAlignment, "발밑 기준점");
            Assert.IsFalse(importer.mipmapEnabled); Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(path), "스프라이트로 가져와짐");
        }
    }
}
