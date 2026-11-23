using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace BepInExLogAlerts;

internal enum TextKey
{
    StartupHeading, StartupCounts, HistoryHint, HistoryTitle, Close, HistoryCount,
    CollectingStartup, StatusWithEvictions, ClearHistory, PopupHeading, ResetIndividualMutes,
    StartupStamp, MutedTag, NoHistory, Copied, Copy, SuppressMessage, ResetMutesTitle,
    ClearHistoryConfirmation, ResetMutesConfirmation, Cancel, ConfirmClearHistory,
    ConfirmResetMutes, ClearHistoryTooltip, ResetMutesTooltip, PendingLogLimit,
    StartupLogLimit, MonitoringProblem
}

internal sealed class UiText
{
    private const string Section = "BepInExLogAlerts";
    private readonly MethodInfo exists;
    private readonly PropertyInfo instance, dictionary;
    private Dictionary<TextKey, string>? resources;
    internal bool Ready => resources != null;

    internal UiText(Assembly game)
    {
        Type handler = game.GetType("SeaPower.LanguageResourceHandler", true)!;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        exists = handler.GetMethod("InstanceExists", flags, null, new[] { typeof(bool) }, null)
            ?? throw new MissingMethodException(handler.FullName, "InstanceExists(bool)");
        instance = handler.GetProperty("Instance", flags) ?? throw new MissingMemberException(handler.FullName, "Instance");
        dictionary = handler.GetProperty("LanguageDictionary") ?? throw new MissingMemberException(handler.FullName, "LanguageDictionary");
    }

    internal void Refresh()
    {
        // Keep only text snapshots while a scene's handler is absent or initializing.
        // No handler, game dictionary or event subscription survives this call.
        if (!(bool)exists.Invoke(null, new object[] { false })!) return;
        object handler = instance.GetValue(null) ?? throw new InvalidOperationException("The language handler is unavailable.");
        object? value = dictionary.GetValue(handler);
        if (value == null) return;
        var current = value as IDictionary<string, string>
            ?? throw new InvalidOperationException("Unsupported game language dictionary.");
        var next = new Dictionary<TextKey, string>();
        foreach (TextKey key in Enum.GetValues(typeof(TextKey)))
        {
            string id = (Section + key).ToLowerInvariant();
            if (!current.TryGetValue(id, out string? text) || text == null)
                throw new KeyNotFoundException("Missing Log Alerts text resource: " + id);
            next.Add(key, text.Replace("\\n", "\n").Replace("\\r", "\r"));
        }
        resources = next;
    }

    internal string T(TextKey key, params object[] arguments)
    {
        if (resources == null) throw new InvalidOperationException("Log Alerts text resources are not initialized.");
        try { return string.Format(CultureInfo.CurrentCulture, resources[key], arguments); }
        catch (FormatException error) { throw new FormatException("Invalid Log Alerts text format: " + key, error); }
    }
}
