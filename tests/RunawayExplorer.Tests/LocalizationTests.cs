using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using RunawayExplorer.Services;
using Xunit;

namespace RunawayExplorer.Tests;

public class LocalizationTests
{
    [AvaloniaFact]
    public void EnglishAndSpanishDictionaries_ContainIdenticalKeys()
    {
        var enDict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://RunawayExplorer/Assets/Localization/Strings.en.axaml"));
        var esDict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://RunawayExplorer/Assets/Localization/Strings.es.axaml"));

        var enKeys = enDict.Keys.Cast<string>().OrderBy(k => k).ToList();
        var esKeys = esDict.Keys.Cast<string>().OrderBy(k => k).ToList();

        Assert.NotEmpty(enKeys);
        Assert.Equal(enKeys, esKeys);

        foreach (string key in enKeys)
        {
            Assert.True(enDict[key] is string enVal && !string.IsNullOrWhiteSpace(enVal));
            Assert.True(esDict[key] is string esVal && !string.IsNullOrWhiteSpace(esVal));
        }
    }

    [AvaloniaFact]
    public void LocalizationManager_CanSwitchLanguageAndResolveStrings()
    {
        LocalizationManager.Instance.SetLanguage("en", force: true);
        Assert.Equal(LocalizationManager.LanguageEnglish, LocalizationManager.Instance.CurrentLanguage);
        Assert.Equal("_File", LocalizationManager.Instance.GetString("Menu_File"));

        LocalizationManager.Instance.SetLanguage("es");
        Assert.Equal(LocalizationManager.LanguageSpanish, LocalizationManager.Instance.CurrentLanguage);
        Assert.Equal("_Archivo", LocalizationManager.Instance.GetString("Menu_File"));

        // Reset to English
        LocalizationManager.Instance.SetLanguage("en");
        Assert.Equal("_File", LocalizationManager.Instance.GetString("Menu_File"));
    }
}
