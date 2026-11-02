using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BepInExLogAlerts.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BepInExLogAlerts;

public sealed class AlertHost : MonoBehaviour
{
    private AlertSettings? settings;
    private LogMailbox? mailbox;
    private BufferedLogListener? listener;
    private StartupScreen? screen;
    private NotificationCenter? notifications;
    private UiText? words;
    private OverlayView? view;
    private Task<List<LogRecord>>? import;
    private List<LogRecord> startupCaptured = new List<LogRecord>();
    private bool menuDetected, menuRepainted, importStarted, ready, closed;
    private double nextSample;
    private readonly List<LogRecord> problems = new List<LogRecord>();

    internal void Initialize(Assembly game, AlertSettings settings, LogMailbox mailbox, BufferedLogListener listener, NotificationRules rules)
    {
        this.settings = settings; this.mailbox = mailbox; this.listener = listener;
        screen = new StartupScreen(game);
        words = new UiText(game, settings.Language);
        notifications = new NotificationCenter(settings.HistoryLimit, settings.RepeatSeconds, settings.WarningSeconds, settings.ErrorSeconds, rules);
        view = new OverlayView(settings, words, notifications);
    }

    private void Update()
    {
        if (closed || settings == null || view == null) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[settings.HistoryKey].wasPressedThisFrame) view.HistoryOpen = !view.HistoryOpen;
                if (keyboard.escapeKey.wasPressedThisFrame) view.Escape();
            }
            Mouse? mouse = Mouse.current;
            if (mouse != null && Application.isFocused)
            {
                Vector2 position = mouse.position.ReadValue();
                try
                {
                    view.ProcessPointer(new PointerFrame(position.x, position.y,
                        mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame,
                        mouse.leftButton.isPressed, mouse.scroll.ReadValue().y), now);
                }
                catch (Exception error)
                {
                    view.CancelPointer();
                    Problem("History action failed; notification settings were not changed if saving failed", error);
                }
            }
            else view.CancelPointer();
            if (now >= nextSample)
            {
                nextSample = now + 0.25;
                words!.Refresh();
                if (!menuDetected) menuDetected = screen!.MenuVisible();
            }
            Exception? failure = listener!.TakeFailure();
            if (failure != null)
            { BepInEx.Logging.Logger.Listeners.Remove(listener); Problem("Log reception stopped", failure); }
            if (menuRepainted && !importStarted) BeginImport();
            if (import != null && import.IsCompleted)
            {
                List<LogRecord> records;
                try { records = import.GetAwaiter().GetResult(); }
                catch (Exception error) { Problem("Startup log processing failed", error); records = StartupMerge.Merge(Array.Empty<LogRecord>(), startupCaptured); }
                PublishStartup(records, now);
                startupCaptured.Clear();
                import = null; ready = true;
            }
            if (ready)
            {
                foreach (LogRecord record in mailbox!.Drain(128)) notifications!.Receive(record, now);
                foreach (LogRecord record in problems) notifications!.Receive(record, now);
                problems.Clear();
                long lost = mailbox!.TakeDropped();
                if (lost != 0)
                {
                    var record = new LogRecord(Severity.Warning, ModInfo.Name, words!.T("ログが大量発生したため、受信待ちログを", "The pending log limit was reached; ") + lost + words.T("件省略しました。詳細は元のログを確認してください。", " events were omitted. Check the original log for details."), false, DateTimeOffset.Now);
                    notifications!.Receive(record, now);
                }
            }
            notifications!.Tick(now);
        }
        catch (Exception error) { Plugin.Log.LogError("Log alert monitoring stopped: " + error); Close(); }
    }

    private void BeginImport()
    {
        importStarted = true;
        DiskLogAccess? disk = null;
        try { disk = DiskLogAccess.FindCurrent(); }
        catch (Exception error) { Problem("Startup log recovery is unavailable", error); }
        FileSnapshot? snapshot = null;
        List<LogRecord> captured;
        try { snapshot = mailbox!.SealStartup(() => disk?.Snapshot(settings!.StartupMegabytes * 1024 * 1024), out captured); }
        catch (Exception error)
        {
            Problem("Startup log snapshot failed", error);
            mailbox!.SealStartup(() => 0, out captured);
        }
        startupCaptured = captured;
        bool includeUnity = settings!.IncludeUnity;
        if (snapshot?.Truncated == true)
            problems.Add(new LogRecord(Severity.Warning, ModInfo.Name, words!.T("起動ログが読込上限を超えたため、末尾の", "The startup log exceeded the read limit. Only the last ") + settings.StartupMegabytes + words.T(" MiBを取り込みました。", " MiB was recovered."), true));
        import = Task.Run(() =>
        {
            var records = new List<LogRecord>();
            if (snapshot != null)
            {
                using var reader = new StreamReader(new MemoryStream(snapshot.Bytes, false), new UTF8Encoding(false, true), true);
                records = LogParser.Parse(reader, includeUnity);
            }
            return StartupMerge.Merge(records, captured);
        });
    }

    private void PublishStartup(List<LogRecord> records, double now)
    {
        records.AddRange(problems);
        problems.Clear();
        notifications!.PublishStartup(records, now);
    }

    private void Problem(string area, Exception error)
    {
        Plugin.Log.LogError(area + ": " + error);
        problems.Add(new LogRecord(Severity.Error, ModInfo.Name, words!.T("ログ通知の一部を処理できませんでした。", "Part of log monitoring could not be completed. ") + "\n" + area + ": " + error, !ready, DateTimeOffset.Now));
    }

    private void OnGUI()
    {
        if (closed || view == null) return;
        try
        {
            // Same first-menu repaint boundary as Harmony Startup Optimizer.
            if (menuDetected && Event.current.type == EventType.Repaint) menuRepainted = true;
            view.Draw(Time.realtimeSinceStartupAsDouble, ready);
        }
        catch (Exception error) { Plugin.Log.LogError("Log alert display stopped: " + error); Close(); }
    }
    private void OnApplicationQuit() => Close();
    private void OnDestroy() => Close();
    private void Close()
    {
        if (closed) return;
        closed = true;
        if (listener != null) { listener.Dispose(); BepInEx.Logging.Logger.Listeners.Remove(listener); }
        view?.Dispose();
        enabled = false;
    }
}
