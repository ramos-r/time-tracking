using System.Security;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TimeTracking.Services;

/// <summary>
/// Aplica e persiste a preferência de tema (Seção 57). O estado Running/Paused/Stopped do
/// timer é derivado do banco; aqui, de forma parecida, o tema "efetivo" é sempre derivado
/// da preferência (Dark/Light/Sistema) + do registro do Windows quando "Sistema".
/// </summary>
public class ThemeService : IThemeService
{
    private readonly AppSettingsStore _settingsStore;

    public ThemeService(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public AppTheme EffectiveTheme { get; private set; } = AppTheme.Dark;

    public event Action<AppTheme>? EffectiveThemeChanged;

    public void Initialize()
    {
        CurrentTheme = LoadSavedTheme();
        ApplyEffectiveTheme(GetEffectiveTheme(CurrentTheme));
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SaveTheme(theme);
        ApplyEffectiveTheme(GetEffectiveTheme(theme));
    }

    private void ApplyEffectiveTheme(AppTheme effective)
    {
        EffectiveTheme = effective;
        ApplyResourceDictionary(effective);
        EffectiveThemeChanged?.Invoke(effective);
    }

    private AppTheme GetEffectiveTheme(AppTheme theme) => theme == AppTheme.System ? GetSystemTheme() : theme;

    /// <summary>Lê HKCU\...\Personalize\AppsUseLightTheme (item 9 da nota de revisão).
    /// Indisponível/inacessível → assume Dark como padrão seguro.</summary>
    private static AppTheme GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int useLightTheme)
            {
                return useLightTheme != 0 ? AppTheme.Light : AppTheme.Dark;
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            // Registro indisponível/sem permissão — cai no padrão abaixo.
        }

        return AppTheme.Dark;
    }

    private static void ApplyResourceDictionary(AppTheme effective)
    {
        var dictName = effective == AppTheme.Light ? "Light" : "Dark";
        var newDictionary = new ResourceDictionary
        {
            Source = new Uri($"/TimeTracking;component/Resources/Themes/{dictName}.xaml", UriKind.Relative)
        };

        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = mergedDictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Resources/Themes/"));

        if (existing is not null)
        {
            mergedDictionaries[mergedDictionaries.IndexOf(existing)] = newDictionary;
        }
        else
        {
            mergedDictionaries.Add(newDictionary);
        }
    }

    private AppTheme LoadSavedTheme()
    {
        var data = _settingsStore.Load();
        return Enum.TryParse<AppTheme>(data.Theme, out var theme) ? theme : AppTheme.System;
    }

    private void SaveTheme(AppTheme theme) =>
        _settingsStore.Save(data => data.Theme = theme.ToString());
}
