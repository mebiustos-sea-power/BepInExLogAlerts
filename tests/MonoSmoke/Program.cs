using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInExLogAlerts;
using BepInExLogAlerts.Core;

internal static class Program
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class BadMessage { public override string ToString() => throw new InvalidOperationException("Expected conversion failure"); }
    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "BepInExLogAlerts-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DiskLogListener? disk = null;
        BufferedLogListener? listener = null;
        ManualLogSource? source = null;
        try
        {
            typeof(Paths).GetProperty("BepInExRootPath")!.GetSetMethod(true)!.Invoke(null, new object[] { root });
            disk = new DiskLogListener("LogOutput.log", LogLevel.Warning | LogLevel.Error | LogLevel.Fatal);
            Logger.Listeners.Add(disk);
            source = Logger.CreateLogSource("Integration Test");
            source.LogWarning("Repeated warning");
            var box = new LogMailbox(128, new NotificationRules(new NotificationRuleSet(Array.Empty<string>()), _ => { }));
            listener = new BufferedLogListener(box, false);
            Logger.Listeners.Add(listener);
            source.LogWarning("Repeated warning");
            source.LogError("Failed\n  at Integration.Run()");
            var access = new DiskLogAccess(disk.LogWriter);
            FileSnapshot snapshot = box.SealStartup(() => access.Snapshot(1024 * 1024), out var captured);
            var parsed = LogParser.Parse(new StringReader(Encoding.UTF8.GetString(snapshot.Bytes)), false);
            var merged = StartupMerge.Merge(parsed, captured);
            Check(merged.Count == 2 && merged[0].Count == 2 && merged[1].Message.Contains("at Integration.Run()"), "Buffered BepInEx startup recovery failed.");
            source.LogWarning("Repeated warning");
            Check(box.Drain(10).Single().Startup == false, "Post-startup warning was lost or treated as historical.");
            Console.WriteLine("PASS Actual BepInEx writer flush, replay overlap, multiline exception, and live subscription");
            using (var other = new DiskLogListener("LogOutput.log", LogLevel.Warning))
            {
                other.LogEvent(null!, new LogEventArgs("other", LogLevel.Warning, source));
                var alternate = new DiskLogAccess(other.LogWriter).Snapshot(1024);
                GC.SuppressFinalize(other);
                Check(alternate.Path.EndsWith("LogOutput.log.1", StringComparison.Ordinal), "Did not follow the active writer's alternate filename.");
            }
            Console.WriteLine("PASS Active log filename discovery (multiple log files)");
            listener.LogEvent(null!, new LogEventArgs(new BadMessage(), LogLevel.Error, source));
            Check(listener.TakeFailure() != null, "Listener conversion failure was hidden.");
            source.LogError("The logging pipeline still works");
            Console.WriteLine("PASS Callback failures are isolated and available for reporting without recursion");
            Assembly game = Assembly.LoadFrom(Path.Combine(args[0], "Sea Power_Data", "Managed", "Seapower-Scripts.dll"));
            _ = new StartupScreen(game);
            _ = new UiText(game, UiLanguage.Auto);
            Console.WriteLine("PASS Actual game menu/loading/language member contracts resolve without creating singletons");
            string rulePath = Path.Combine(root, "notifications.mutedlogs");
            var store = new NotificationRuleStore(rulePath);
            var rules = new NotificationRules(store.Load(), store.Save);
            var a = new LogRecord(Severity.Error, "BepInEx", "writer", true);
            var b = new LogRecord(Severity.Warning, "日本語MOD", "message\nstack", false);
            rules.SetSuppressed(a, true); rules.SetSuppressed(b, true);
            var reloaded = new NotificationRules(store.Load(), store.Save);
            Check(reloaded.Count == 2 && reloaded.IsSuppressed(a) && reloaded.IsSuppressed(b), "Mono did not preserve suppression rules after durable replacement.");
            File.WriteAllText(rulePath, "BepInExLogAlerts muted logs / 1\n" + a.NotificationId + "\n" + b.NotificationId + "\n");
            reloaded = new NotificationRules(store.Load(), store.Save);
            Check(reloaded.IsLevelEnabled(Severity.Warning) && reloaded.Count == 2, "Legacy settings did not preserve defaults and individual rules on Mono.");
            reloaded.SetLevelEnabled(Severity.Warning, false);
            reloaded.SetLevelEnabled(Severity.Error, false);
            reloaded.SetLevelEnabled(Severity.Fatal, false);
            reloaded = new NotificationRules(store.Load(), store.Save);
            Check(new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(level => !reloaded.IsLevelEnabled(level)) && reloaded.Count == 2 && reloaded.IsIndividuallySuppressed(a) && reloaded.IsIndividuallySuppressed(b), "Mono did not persist all three level choices during the released-format upgrade.");
            byte[] saved = File.ReadAllBytes(rulePath);
            using (var held = new FileStream(rulePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { reloaded.Clear(); } catch (IOException) { failed = true; }
                Check(failed && reloaded.Count == 2 && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(level => !reloaded.IsLevelEnabled(level)) && File.ReadAllBytes(rulePath).SequenceEqual(saved), "Failed replacement lost the previous settings.");
            }
            Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed persistence left an uncommitted temporary file.");
            reloaded.Clear();
            reloaded = new NotificationRules(store.Load(), store.Save);
            Check(reloaded.Count == 0 && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(level => !reloaded.IsLevelEnabled(level)) && !reloaded.IsIndividuallySuppressed(a) && !reloaded.IsIndividuallySuppressed(b), "Mono did not preserve level choices while persisting bulk unmute after releasing the file lock.");
            Console.WriteLine("PASS Released settings, independent levels, atomic replacement, failed-save rollback, and bulk unmute preserving levels on game Mono");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (listener != null) { listener.Dispose(); Logger.Listeners.Remove(listener); }
            if (disk != null) { Logger.Listeners.Remove(disk); disk.Dispose(); GC.SuppressFinalize(disk); }
            if (source != null) { Logger.Sources.Remove(source); source.Dispose(); }
            Directory.Delete(root, true);
        }
    }
}
