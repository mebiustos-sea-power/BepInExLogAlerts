using System;
using System.IO;
using System.Linq;
using AnchorChain;
using BepInEx.Logging;
using BepInExLogAlerts.Core;
using UnityEngine;

namespace BepInExLogAlerts;

[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    public const string ModId = ModInfo.Id;
    internal static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModInfo.Name);
    private static bool initialized;
    public void TriggerEntryPoint()
    {
        if (initialized) return;
        initialized = true;
        BufferedLogListener? listener = null;
        GameObject? host = null;
        try
        {
            var game = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Seapower-Scripts");
            string package = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
                ?? throw new InvalidOperationException("The mod package directory is unavailable.");
            if (!ModActivation.IsEnabled(game.GetType("SeaPower.FileManager", true)!, package))
            { Log.LogInfo("Disabled in the mod manager."); return; }
            var settings = new AlertSettings(Path.Combine(BepInEx.Paths.ConfigPath, ModId + ".cfg"));
            var store = new NotificationRuleStore(Path.Combine(BepInEx.Paths.ConfigPath, ModId + ".mutedlogs"));
            var rules = new NotificationRules(store.Load(), store.Save);
            var mailbox = new LogMailbox(settings.MailboxLimit, rules);
            listener = new BufferedLogListener(mailbox, settings.IncludeUnity);
            BepInEx.Logging.Logger.Listeners.Add(listener);
            host = new GameObject("BepInExLogAlerts");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<AlertHost>().Initialize(game, settings, mailbox, listener, rules);
            Log.LogInfo("Initialized " + ModInfo.Version + ". Startup alerts wait for the main menu; history: " + settings.HistoryKey + ".");
        }
        catch (Exception error)
        {
            if (listener != null) { listener.Dispose(); BepInEx.Logging.Logger.Listeners.Remove(listener); }
            if (host != null) UnityEngine.Object.Destroy(host);
            Log.LogError("Initialization failed: " + error);
        }
    }
}
