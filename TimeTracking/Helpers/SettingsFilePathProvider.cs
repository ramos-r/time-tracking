using System.IO;

namespace TimeTracking.Helpers;

public static class SettingsFilePathProvider
{
    public static string GetSettingsFilePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeTracking");

        Directory.CreateDirectory(folder);

        return Path.Combine(folder, "settings.json");
    }
}
