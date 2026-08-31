using System.IO;
using System.Security;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using TimeTracking.Helpers;

namespace TimeTracking.Services;

/// <summary>
/// Aplica e persiste a preferência de tema (Seção 57). O estado Running/Paused/Stopped do
/// timer é derivado do banco; aqui, de forma parecida, o tema "efetivo" é sempre derivado
/// da preferência (Dark/Light/Sistema) + do registro do Windows quando "Sistema".
/// </summary>
public class ThemeService : IThemeService
{
    private readonly string _settingsPath = SettingsFilePathProvider.GetSettingsFilePath();

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public void Initialize()
    {
        CurrentTheme = LoadSavedTheme();
        ApplyResourceDictionary(GetEffectiveTheme(CurrentTheme));
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SaveTheme(theme);
        ApplyResourceDictionary(GetEffectiveTheme(theme));
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
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (settings is not null && Enum.TryParse<AppTheme>(settings.Theme, out var theme))
                {
                    return theme;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo de configuração ausente/corrompido — usa o padrão (Sistema).
        }

        return AppTheme.System;
    }

    private void SaveTheme(AppTheme theme)
    {
        try
        {
            var json = JsonSerializer.Serialize(new AppSettingsData { Theme = theme.ToString() });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao persistir preferência não deve impedir a troca de tema na sessão atual.
        }
    }

    private class AppSettingsData
    {
        public string Theme { get; set; } = nameof(AppTheme.System);
    }
}
