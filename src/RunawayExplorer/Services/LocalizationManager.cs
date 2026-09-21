using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;

namespace RunawayExplorer.Services;

/// <summary>
/// Manages active application UI language and dynamic resource dictionaries.
/// </summary>
public sealed class LocalizationManager
{
    public static LocalizationManager Instance { get; } = new();

    public const string LanguageEnglish = "en";
    public const string LanguageSpanish = "es";

    private ResourceDictionary? _currentLanguageDictionary;

    public string CurrentLanguage { get; private set; } = LanguageEnglish;

    public event EventHandler<string>? LanguageChanged;

    public void Initialize(string? initialLanguage)
    {
        string lang = NormalizeLanguage(initialLanguage);
        SetLanguage(lang, force: true);
    }

    public void SetLanguage(string? language, bool force = false)
    {
        string lang = NormalizeLanguage(language);
        if (!force && string.Equals(CurrentLanguage, lang, StringComparison.OrdinalIgnoreCase))
            return;

        CurrentLanguage = lang;

        if (Application.Current is not null)
        {
            var uri = new Uri($"avares://RunawayExplorer/Assets/Localization/Strings.{lang}.axaml");
            var newDict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

            // Find application root dictionary
            var rootResources = Application.Current.Resources;
            var merged = rootResources.MergedDictionaries;

            if (_currentLanguageDictionary is not null)
            {
                merged.Remove(_currentLanguageDictionary);
            }
            else
            {
                // Remove any pre-existing language dictionary included in XAML
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i] is ResourceInclude inc && inc.Source?.ToString().Contains("Strings.") == true)
                    {
                        merged.RemoveAt(i);
                    }
                }
            }

            merged.Add(newDict);
            _currentLanguageDictionary = newDict;
        }

        LanguageChanged?.Invoke(this, lang);
    }

    public string GetString(string key, string? fallback = null)
    {
        if (Application.Current is not null &&
            Application.Current.TryGetResource(key, null, out object? resource) &&
            resource is string str)
        {
            return str;
        }

        return fallback ?? key;
    }

    public string Format(string key, params object[] args)
    {
        string pattern = GetString(key);
        try
        {
            return string.Format(pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, LanguageSpanish, StringComparison.OrdinalIgnoreCase))
            return LanguageSpanish;
        return LanguageEnglish;
    }
}
