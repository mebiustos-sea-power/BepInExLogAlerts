using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInExLogAlerts;

internal static class LocalizationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(Assembly game, string package)
    {
        Type loader = game.GetType("SeaPower.LocalizationLoader", true)!;
        MethodInfo load = loader.GetMethod("LoadFileAsData", new[] { typeof(string), typeof(string) })!;
        IDictionary<string, string> Read(string code)
        {
            string path = Path.Combine(package, "language_" + code, "bepinexlogalerts.json");
            var sections = (IDictionary<string, Dictionary<string, string>>)load.Invoke(null, new object[] { path, "bepinexlogalerts" })!;
            var result = new Dictionary<string, string>();
            foreach (var section in sections)
                foreach (var entry in section.Value)
                    result.Add((section.Key + entry.Key).ToLowerInvariant(), entry.Value);
            return result;
        }
        var english = Read("en"); var japanese = Read("ja");
        foreach (var resources in new[] { english, japanese })
        {
            foreach (TextKey key in Enum.GetValues(typeof(TextKey)))
            {
                Check(resources.TryGetValue(("BepInExLogAlerts" + key).ToLowerInvariant(), out string value), "A shipped language file cannot render " + key);
                _ = string.Format(value, 4, 7);
            }
            Check(resources["bepinexlogalertsclearhistoryconfirmation"].Contains("\\n") ||
                resources["bepinexlogalertsclearhistoryconfirmation"].Contains("\n"), "Game parsing lost the confirmation's line break.");
        }
        Console.WriteLine("PASS Both shipped language files load through Sea Power's actual localization loader and format every overlay resource");
        string close = "bepinexlogalertsclose";
        var partial = new Dictionary<string, string>(japanese); partial.Remove(close);
        Type dictionary = game.GetType("SeaPower.DictionaryWithFallbacks\u00602", true)!.MakeGenericType(typeof(string), typeof(string));
        var fallback = (IDictionary<string, string>)Activator.CreateInstance(dictionary, new object[] { "MISSING TEXT", partial, english })!;
        Check(fallback.TryGetValue(close, out string inherited) && inherited == english[close], "Game English fallback did not cover a missing translation.");
        Check(fallback["bepinexlogalertscancel"] == japanese["bepinexlogalertscancel"], "Game fallback overrode an existing translation.");
        Console.WriteLine("PASS Actual game dictionary keeps translated values and falls back to English for missing keys");
        string path = Path.Combine(Path.GetTempPath(), "LogAlerts-legacy-language-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            File.WriteAllText(path, "[Display]\nLanguage = no-longer-a-valid-language\nWidth = 750\n[Input]\nHistoryKey = F9\n");
            var settings = new AlertSettings(path);
            Check(settings.Width == 750 && settings.HistoryKey.ToString() == "F9", "Retired Language setting prevented unrelated settings from loading.");
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS Released configs with a retired or invalid Language value still load through actual BepInEx configuration");
    }
}
