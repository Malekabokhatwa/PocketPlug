using System.IO;
using System.Text.RegularExpressions;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using MelonLoader.Utils;

namespace PocketPlug.Util;

/// <summary>
/// Where per-save PocketPlug data lives: <c>UserData/PocketPlug/&lt;kind&gt;/&lt;steamid&gt;_&lt;SaveGame_N&gt;_&lt;seed&gt;.&lt;ext&gt;</c>.
/// The seed (from the save's Game.json) keeps a new game in a reused save slot from inheriting old data.
/// </summary>
internal static class SaveKey
{
    /// <summary>The key for the loaded save, or null if no save is loaded.</summary>
    public static string Current()
    {
        if (!Singleton<LoadManager>.InstanceExists)
            return null;
        var folder = Singleton<LoadManager>.Instance.LoadedGameFolderPath;
        if (string.IsNullOrEmpty(folder))
            return null;

        folder = folder.TrimEnd('/', '\\');
        string legacy = $"{Path.GetFileName(Path.GetDirectoryName(folder))}_{Path.GetFileName(folder)}";
        string seed = ReadSeed(Path.Combine(folder, "Game.json"));
        return seed == null ? legacy : $"{legacy}_{seed}";
    }

    /// <summary>
    /// Full path for a per-save file. If an older file without the seed exists (PocketPlug 1.x), it's renamed so
    /// existing history carries over.
    /// </summary>
    public static string File(string kind, string extension)
    {
        string key = Current();
        if (key == null)
            return null;
        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug", kind);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, key + extension);

        int seedAt = key.LastIndexOf('_');
        if (!System.IO.File.Exists(path) && seedAt > 0 && Regex.IsMatch(key.Substring(seedAt + 1), "^-?\\d+$"))
        {
            string legacy = Path.Combine(dir, key.Substring(0, seedAt) + extension);
            if (System.IO.File.Exists(legacy))
                System.IO.File.Move(legacy, path);
        }
        return path;
    }

    private static string ReadSeed(string gameJson)
    {
        try
        {
            if (!System.IO.File.Exists(gameJson))
                return null;
            var m = Regex.Match(System.IO.File.ReadAllText(gameJson), "\"Seed\"\\s*:\\s*(-?\\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
