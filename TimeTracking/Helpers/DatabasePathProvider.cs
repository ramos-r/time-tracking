using System.IO;

namespace TimeTracking.Helpers;

public static class DatabasePathProvider
{
    public static string GetDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeTracking");

        Directory.CreateDirectory(folder);

        return Path.Combine(folder, "timetracking.db");
    }
}
