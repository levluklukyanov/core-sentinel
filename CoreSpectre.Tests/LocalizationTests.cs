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

        string dashTitleEn = LocalizationService.Get("Dash.Title");
        Assert.Equal("System Overview", dashTitleEn);

        LocalizationService.CurrentLanguage = "RU";
        Assert.True(LocalizationService.IsRussian);
        Assert.False(LocalizationService.IsEnglish);

        string dashTitleRu = LocalizationService.Get("Dash.Title");
        Assert.Equal("Обзор системы", dashTitleRu);
    }

    [Fact]
    public void Localization_AllKeysHaveValidEnglishAndRussianTranslations()
    {
        Assert.NotEmpty(LocalizationService.Keys);
        foreach (string key in LocalizationService.Keys)
        {
            LocalizationService.CurrentLanguage = "EN";
            string en = LocalizationService.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(en), $"Empty EN translation for {key}");
            Assert.NotEqual(key, en);

            LocalizationService.CurrentLanguage = "RU";
            string ru = LocalizationService.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(ru), $"Empty RU translation for {key}");
            Assert.NotEqual(key, ru);
        }
        LocalizationService.CurrentLanguage = "EN";
    }

    [Fact]
    public void Localization_UnknownKey_ReturnsKeyItselfAsFallback()
    {
        const string unknownKey = "NonExistent.Key.ForTesting";
        string result = LocalizationService.Get(unknownKey);
        Assert.Equal(unknownKey, result);
    }
}
