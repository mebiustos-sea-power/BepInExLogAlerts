using System.IO;
using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace BepInExLogAlerts;

internal enum UiLanguage { Auto, English, Japanese }

internal sealed class AlertSettings
{
    internal readonly bool IncludeUnity;
    internal readonly int HistoryLimit, MailboxLimit, StartupMegabytes;
    internal readonly float Width, Top, Scale, WarningSeconds, ErrorSeconds, RepeatSeconds;
    internal readonly Key HistoryKey;
    internal readonly UiLanguage Language;
    internal AlertSettings(string path)
    {
        var config = new ConfigFile(path, false) { SaveOnConfigSet = false };
        IncludeUnity = config.Bind("Logging", "IncludeUnityLogs", false, "Include Unity Log. It may contain game messages whose originating mod is unknown. Startup recovery also requires BepInEx WriteUnityLog.").Value;
        HistoryLimit = config.Bind("Limits", "HistoryEntries", 200, new ConfigDescription("Maximum history groups retained in memory.", new AcceptableValueRange<int>(20, 1000))).Value;
        MailboxLimit = config.Bind("Limits", "PendingEntries", 4096, new ConfigDescription("Maximum pending log events. Overflow is reported on screen.", new AcceptableValueRange<int>(128, 16384))).Value;
        StartupMegabytes = config.Bind("Limits", "StartupLogMegabytes", 8, new ConfigDescription("Maximum startup log bytes read from the end of the file. Truncation is reported.", new AcceptableValueRange<int>(1, 64))).Value;
        Width = config.Bind("Display", "Width", 680f, new ConfigDescription("Notification width at 1080p.", new AcceptableValueRange<float>(320, 1200))).Value;
        Top = config.Bind("Display", "Top", 20f, new ConfigDescription("Distance from the top at 1080p.", new AcceptableValueRange<float>(0, 800))).Value;
        Scale = config.Bind("Display", "Scale", 1f, new ConfigDescription("Text and notification scale, in addition to resolution scaling.", new AcceptableValueRange<float>(0.5f, 2f))).Value;
        WarningSeconds = config.Bind("Display", "WarningSeconds", 6f, new ConfigDescription("Warning duration after the first repaint.", new AcceptableValueRange<float>(2, 60))).Value;
        ErrorSeconds = config.Bind("Display", "ErrorSeconds", 10f, new ConfigDescription("Error/Fatal duration after the first repaint.", new AcceptableValueRange<float>(2, 120))).Value;
        RepeatSeconds = config.Bind("Display", "RepeatSeconds", 10f, new ConfigDescription("Combine identical live log messages within this many seconds.", new AcceptableValueRange<float>(1, 300))).Value;
        HistoryKey = config.Bind("Input", "HistoryKey", Key.F8, "Toggle history. Escape also closes history.").Value;
        Language = config.Bind("Display", "Language", UiLanguage.Auto, "Auto follows the game language.").Value;
        if (HistoryKey == Key.None) throw new InvalidDataException("HistoryKey must be a keyboard key.");
        config.Save();
    }
}
