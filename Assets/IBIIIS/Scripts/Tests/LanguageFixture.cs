using NUnit.Framework;

namespace IBIIIS.Tests
{
    /// <summary>모든 테스트를 원문(한국어)으로 돌린다. 이 PC에 저장된 게임 언어(PlayerPrefs)에 결과가 좌우되지 않게 한다.</summary>
    [SetUpFixture]
    public sealed class LanguageFixture
    {
        [OneTimeSetUp] public void UseKorean() => GameLanguage.UseForTests(GameLanguage.Source);
        [OneTimeTearDown] public void Restore() => GameLanguage.UseForTests(null);
    }
}
