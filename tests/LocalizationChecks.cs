using System;
using System.Collections.Generic;
using BepInExLogAlerts;

internal static class LocalizationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static Dictionary<string, string> Resources(string prefix)
    {
        var result = new Dictionary<string, string>();
        foreach (TextKey key in Enum.GetValues(typeof(TextKey))) result.Add(Id(key), prefix + key);
        return result;
    }
    private static string Id(TextKey key) => ("BepInExLogAlerts" + key).ToLowerInvariant();
    internal static void Run(Action<string, Action> test)
    {
        test("Localization waits for the handler and its dictionary without creating a singleton", () =>
        {
            SeaPower.LanguageResourceHandler.Current = null;
            var text = new UiText(typeof(LocalizationChecks).Assembly);
            text.Refresh(); Check(!text.Ready, "Text became ready without a handler.");
            SeaPower.LanguageResourceHandler.Current = new SeaPower.LanguageResourceHandler();
            text.Refresh(); Check(!text.Ready, "Text became ready without a dictionary.");
            SeaPower.LanguageResourceHandler.Current.Resources = Resources("ready ");
            text.Refresh(); Check(text.Ready && text.T(TextKey.Close) == "ready Close", "Initialized resources were not applied.");
        });
        test("Localization follows 20 handler replacements and changes within a handler", () =>
        {
            var text = new UiText(typeof(LocalizationChecks).Assembly);
            for (int i = 0; i < 20; i++)
            {
                if (SeaPower.LanguageResourceHandler.Current != null) SeaPower.LanguageResourceHandler.Current.Retired = true;
                var handler = new SeaPower.LanguageResourceHandler { Resources = Resources(i + " ") };
                SeaPower.LanguageResourceHandler.Current = handler;
                text.Refresh(); Check(text.T(TextKey.Close) == i + " Close", "An old handler's resources survived replacement.");
                handler.Resources = Resources("changed ");
                text.Refresh(); Check(text.T(TextKey.Close) == "changed Close", "Language change within a handler was ignored.");
            }
        });
        test("Scene reload gaps retain only the last text snapshot and resume translation", () =>
        {
            var data = Resources("old ");
            var old = new SeaPower.LanguageResourceHandler { Resources = data };
            SeaPower.LanguageResourceHandler.Current = old;
            var text = new UiText(typeof(LocalizationChecks).Assembly); text.Refresh();
            old.Retired = true; data[Id(TextKey.Close)] = "mutated old data";
            SeaPower.LanguageResourceHandler.Current = null; text.Refresh();
            Check(text.T(TextKey.Close) == "old Close", "An old game dictionary was retained instead of a text snapshot.");
            SeaPower.LanguageResourceHandler.Current = new SeaPower.LanguageResourceHandler(); text.Refresh();
            Check(text.T(TextKey.Close) == "old Close", "A reload gap broke existing overlay controls.");
            SeaPower.LanguageResourceHandler.Current.Resources = Resources("new "); text.Refresh();
            Check(text.T(TextKey.Close) == "new Close", "Replacement initialization did not restore translation updates.");
        });
        test("Translation templates reorder values and decode game-style escaped line breaks", () =>
        {
            var data = Resources("text ");
            data[Id(TextKey.StartupCounts)] = "{1} errors; {0} warnings";
            data[Id(TextKey.ClearHistoryConfirmation)] = "first\\nsecond";
            SeaPower.LanguageResourceHandler.Current = new SeaPower.LanguageResourceHandler { Resources = data };
            var text = new UiText(typeof(LocalizationChecks).Assembly); text.Refresh();
            Check(text.T(TextKey.StartupCounts, 4, 7) == "7 errors; 4 warnings", "Translation word order was constrained by code.");
            Check(text.T(TextKey.ClearHistoryConfirmation) == "first\nsecond", "Escaped line breaks were drawn literally.");
        });
        test("Missing resources and malformed translation formats are reported", () =>
        {
            var data = Resources("text ");
            data.Remove(Id(TextKey.Close));
            SeaPower.LanguageResourceHandler.Current = new SeaPower.LanguageResourceHandler { Resources = data };
            var text = new UiText(typeof(LocalizationChecks).Assembly);
            bool missing = false; try { text.Refresh(); } catch (KeyNotFoundException) { missing = true; }
            Check(missing, "A missing resource was silently hidden.");
            data[Id(TextKey.Close)] = "{bad}"; text.Refresh();
            bool malformed = false; try { text.T(TextKey.Close); } catch (FormatException error) { malformed = error.Message.Contains("Close"); }
            Check(malformed, "A malformed translation did not identify the failing key.");
        });
    }
}

// Models the game API boundary; retired instances fail if the product reuses them.
namespace SeaPower
{
    public sealed class LanguageResourceHandler
    {
        public static LanguageResourceHandler? Current;
        public static bool InstanceExists(bool unused) => Current != null;
        public static LanguageResourceHandler Instance => Current!;
        public IDictionary<string, string>? Resources;
        public bool Retired;
        public IDictionary<string, string>? LanguageDictionary =>
            Retired ? throw new InvalidOperationException("Retired language handler was accessed.") : Resources;
    }
}
