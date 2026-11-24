using CoreSpectre.Services;
using Xunit;

namespace CoreSpectre.Tests;

public class LocalizationTests
{
    [Fact]
    public void Localization_EnglishAndRussianStrings_ReturnNonEmptyValues()
    {
        LocalizationService.CurrentLanguage = "EN";
        Assert.True(LocalizationService.IsEnglish);
        Assert.False(LocalizationService.IsRussian);

        string navOverviewEn = LocalizationService.Get("Nav.Overview");
        Assert.Equal("Overview", navOverviewEn);

        LocalizationService.CurrentLanguage = "RU";
        Assert.True(LocalizationService.IsRussian);
        Assert.False(LocalizationService.IsEnglish);

        string navOverviewRu = LocalizationService.Get("Nav.Overview");
        Assert.Equal("Обзор", navOverviewRu);
    }

    [Fact]
    public void Localization_UnknownKey_ReturnsKeyItselfAsFallback()
    {
        const string unknownKey = "NonExistent.Key.ForTesting";
        string result = LocalizationService.Get(unknownKey);
        Assert.Equal(unknownKey, result);
    }
}
