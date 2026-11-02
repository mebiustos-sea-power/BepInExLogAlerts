using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInExLogAlerts.Core;

internal static class Program
{
    private static int count;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static LogRecord Record(string message, bool startup = false, Severity level = Severity.Warning) => new LogRecord(level, "Test Mod", message, startup);
    private static void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
    private static NotificationRules Rules() => new NotificationRules(new NotificationRuleSet(Array.Empty<string>()), _ => { });
    private static AlertJournal HistoryJournal(int count)
    {
        var journal = new AlertJournal(count + 5, 10);
        for (int i = 0; i < count; i++) journal.Add(Record("message " + i), 0);
        return journal;
    }
    private static void Send(HistoryInteraction history, HistoryLayout layout, float x, float y,
        bool pressed, bool released, bool held, float wheel, Action<string> copy) =>
        history.Process(new PointerFrame(x * layout.Scale, layout.PixelHeight - y * layout.Scale, pressed, released, held, wheel), layout, copy);
    private static void Click(HistoryInteraction history, HistoryLayout layout, UiRect target, Action<string> copy)
    {
        float x = target.X + Math.Min(20, target.Width / 2), y = target.Y + Math.Min(20, target.Height / 2);
        Send(history, layout, x, y, true, false, true, 0, copy);
        Send(history, layout, x, y, false, true, false, 0, copy);
    }
    private static int Main()
    {
        try
        {
            Test("BepInEx multiline exceptions keep their stack, Info starts a new record", () =>
            {
                var records = LogParser.Parse(new StringReader("[Info   :   BepInEx] start\r\n[Warning: Test Mod] warning\r\n[Error  : Test Mod] failed\r\n  at Test.Run()\r\n[Info   : Test Mod] ok\r\nunrelated\r\n[Fatal  : Another] stopped\r\n"), false);
                Check(records.Count == 3 && records[1].Message == "failed\n  at Test.Run()" && records[2].Level == Severity.Fatal, "Exception parsing or level filtering failed.");
            });
            Test("Flagged BepInEx severity uses the highest warning/error level", () =>
            {
                var records = LogParser.Parse(new StringReader("[Error, Warning: M] combined\n[Info, Debug: M] normal\n"), false);
                Check(records.Count == 1 && records[0].Level == Severity.Error, "Combined severity was omitted or joined to another record.");
            });
            Test("Unity filter and own diagnostics do not recurse into replay", () =>
            {
                string log = "[Error  : Unity Log] engine error\n[Warning:BepInEx Log Alerts] own warning\n[Warning: Test Mod] message <b>raw</b>\n";
                Check(LogParser.Parse(new StringReader(log), false).Count == 1, "Source filter failed.");
                Check(LogParser.Parse(new StringReader(log), true).Count == 2, "Unity opt-in failed.");
            });
            Test("Startup overlap preserves repeats before registration and unseen sources", () =>
            {
                var a = Record("repeat", true); var b = new LogRecord(Severity.Error, "Unity Log", "exception", true);
                var records = StartupMerge.Merge(new[] { a, a, a }, new[] { a, a, b });
                Check(records.Count == 2 && records[0].Count == 3 && records[1].Count == 1, "Overlap incorrectly removed or doubled occurrences.");
            });
            Test("The same error after startup remains a new live alert", () =>
            {
                var journal = new AlertJournal(20, 10);
                journal.Add(Record("repeat", true), 1);
                journal.Add(Record("repeat"), 2);
                journal.Add(Record("repeat"), 3);
                Check(journal.Items.Count == 2 && journal.Items[1].Count == 2, "Live error was suppressed by startup recovery.");
            });
            Test("Floods stay bounded and report how many events were omitted", () =>
            {
                var box = new LogMailbox(4, Rules());
                for (int i = 0; i < 10; i++) box.Post(Severity.Warning, "M", "x", DateTimeOffset.Now);
                box.SealStartup(() => 0, out var boot);
                Check(boot.Count == 4 && box.TakeDropped() == 6 && box.TakeDropped() == 0, "Overflow accounting failed.");
            });
            Test("Failed startup snapshots keep captured events and allow explicit recovery", () =>
            {
                var box = new LogMailbox(4, Rules()); box.Post(Severity.Error, "M", "x", DateTimeOffset.Now);
                try { box.SealStartup<int>(() => throw new IOException("test"), out _); } catch (IOException) { }
                box.SealStartup(() => 0, out var boot);
                Check(boot.Count == 1, "Failed snapshot lost captured events.");
            });
            Test("Concurrent logging is delivered once across the startup phase boundary", () =>
            {
                var box = new LogMailbox(5000, Rules());
                Parallel.For(0, 1000, i => box.Post(Severity.Warning, "M", i.ToString(), DateTimeOffset.Now));
                box.SealStartup(() => 0, out var boot);
                Parallel.For(0, 1000, i => box.Post(Severity.Error, "M", i.ToString(), DateTimeOffset.Now));
                var live = box.Drain(5000);
                Check(boot.Count == 1000 && boot.All(r => r.Startup) && live.Count == 1000 && live.All(r => !r.Startup), "Phase boundary lost or mislabeled events.");
            });
            Test("A stalled screen does not consume a notification's display duration", () =>
            {
                var toasts = new Toasts(6, 10, Rules()); toasts.Show(new AlertItem(Record("x"), 0));
                toasts.Tick(300); Check(toasts.Visible.Count == 1, "Notification expired before drawing.");
                toasts.Rendered(toasts.Visible[0], 300); toasts.Tick(305); Check(toasts.Visible.Count == 1, "Notification expired too early.");
                toasts.Tick(306); Check(toasts.Visible.Count == 0, "Notification did not expire.");
            });
            Test("History bounds do not prevent later alerts, and expiry does not remove history", () =>
            {
                var journal = new AlertJournal(2, 10);
                journal.Add(Record("1"), 0); journal.Add(Record("2"), 0); var item = journal.Add(Record("3"), 0);
                var toasts = new Toasts(6, 10, Rules()); toasts.Show(item); toasts.Rendered(toasts.Visible[0], 0); toasts.Tick(20);
                Check(journal.Items.Count == 2 && journal.Evicted == 1 && journal.Items[1].Record.Message == "3" && toasts.Visible.Count == 0, "History retention failed.");
            });
            Test("Repeated live alerts reuse a card without keeping it on screen forever", () =>
            {
                var journal = new AlertJournal(20, 10); var toasts = new Toasts(6, 10, Rules());
                var item = journal.Add(Record("x"), 0); toasts.Show(item); toasts.Rendered(toasts.Visible[0], 0);
                toasts.Show(journal.Add(Record("x"), 5)); Check(toasts.Visible.Count == 1 && item.Count == 2, "Repeat grouping failed.");
                toasts.Tick(6); Check(toasts.Visible.Count == 0, "Repeated log extended the card indefinitely.");
                for (int i = 0; i < 10; i++) toasts.Show(journal.Add(Record(i.ToString()), i));
                Check(toasts.Visible.Count == 2, "Screen occupancy is unbounded.");
            });
            Test("Input System pointer frames select every row without IMGUI input, at multiple resolutions", () =>
            {
                foreach (var screen in new[] { (1920f, 1080f, 1f), (3840f, 2160f, 1f), (2560f, 1440f, 1.4f), (1280f, 720f, 1f) })
                {
                    var journal = HistoryJournal(3);
                    var history = new HistoryInteraction(journal, Rules()) { Open = true };
                    var layout = new HistoryLayout(screen.Item1, screen.Item2, screen.Item3);
                    for (int display = 0; display < 3; display++)
                    {
                        Click(history, layout, layout.Row(display, 0), _ => { });
                        Check(ReferenceEquals(history.Selected, journal.Items[2 - display]), "Detail selection did not follow the clicked row at this resolution.");
                    }
                }
            });
            Test("Copy receives the newly selected level, source, count and message without IMGUI events", () =>
            {
                var journal = HistoryJournal(3);
                journal.Add(Record("message 0"), 1);
                var history = new HistoryInteraction(journal, Rules()) { Open = true };
                var layout = new HistoryLayout(3840, 2160, 1);
                string clipboard = "";
                Click(history, layout, layout.Row(2, 0), value => clipboard = value);
                Click(history, layout, layout.Copy, value => clipboard = value);
                Check(clipboard == "Warning | Test Mod | ×2\nmessage 0", "Copy used the old selection or omitted diagnostic content.");
                Click(history, layout, layout.Row(1, 0), value => clipboard = value);
                Click(history, layout, layout.Copy, value => clipboard = value);
                Check(clipboard.EndsWith("message 1", StringComparison.Ordinal), "Copy was not refreshed after changing selection.");
            });
            Test("Close responds to a pointer click and history can reopen", () =>
            {
                var history = new HistoryInteraction(HistoryJournal(3), Rules()) { Open = true };
                var layout = new HistoryLayout(3840, 2160, 1);
                Click(history, layout, layout.Close, _ => { });
                Check(!history.Open, "Close did not hide history.");
                history.Open = true;
                Click(history, layout, layout.Row(2, 0), _ => { });
                Check(history.Open && history.Selected!.Record.Message == "message 0", "Reopening retained a stale mouse capture.");
            });
            Test("Press inside and release outside cancels clicks; focus loss cancels a pending copy", () =>
            {
                var history = new HistoryInteraction(HistoryJournal(3), Rules()) { Open = true };
                var layout = new HistoryLayout(1920, 1080, 1);
                int copies = 0;
                var close = layout.Close;
                Send(history, layout, close.X + 5, close.Y + 5, true, false, true, 0, _ => copies++);
                Send(history, layout, 0, 0, false, true, false, 0, _ => copies++);
                Check(history.Open, "Releasing outside unexpectedly closed history.");
                var copy = layout.Copy;
                Send(history, layout, copy.X + 5, copy.Y + 5, true, false, true, 0, _ => copies++);
                history.CancelPointer();
                Send(history, layout, copy.X + 5, copy.Y + 5, false, true, false, 0, _ => copies++);
                Check(copies == 0, "A canceled mouse press still copied diagnostic content.");
            });
            Test("History and detail wheels scroll independently and clamp to their content", () =>
            {
                var history = new HistoryInteraction(HistoryJournal(100), Rules()) { Open = true, DetailContentHeight = 3000 };
                var layout = new HistoryLayout(3840, 2160, 1);
                Send(history, layout, layout.List.X + 20, layout.List.Y + 20, false, false, false, -120, _ => { });
                Check(history.ListScroll > 0 && history.DetailScroll == 0, "List wheel scrolled the wrong pane.");
                float listScroll = history.ListScroll;
                Send(history, layout, layout.Detail.X + 20, layout.Detail.Y + 20, false, false, false, -1, _ => { });
                Check(history.DetailScroll > 0 && history.ListScroll == listScroll, "Normalized detail wheel scrolled the wrong pane.");
                Send(history, layout, layout.Detail.X + 20, layout.Detail.Y + 20, false, false, false, -120000, _ => { });
                Check(history.DetailScroll <= 3000 - layout.Detail.Height, "Detail scrolled beyond its content.");
            });
            Test("Dragging the list scrollbar reaches the last row and hit testing follows the scrolled content", () =>
            {
                var journal = HistoryJournal(100);
                var history = new HistoryInteraction(journal, Rules()) { Open = true };
                var layout = new HistoryLayout(3840, 2160, 1.2f);
                UiRect thumb = HistoryLayout.Thumb(layout.ListTrack, 100 * HistoryLayout.RowStep, 0);
                float x = thumb.X + 5;
                Send(history, layout, x, thumb.Y + 5, true, false, true, 0, _ => { });
                Send(history, layout, x, layout.ListTrack.Bottom - 1, false, false, true, 0, _ => { });
                Send(history, layout, x, layout.ListTrack.Bottom - 1, false, true, false, 0, _ => { });
                Check(history.ListScroll > 6000, "Scrollbar drag did not reach the end.");
                UiRect oldest = layout.Row(99, history.ListScroll);
                Click(history, layout, oldest, _ => { });
                Check(ReferenceEquals(history.Selected, journal.Items[0]), "Scrolled row click selected the wrong diagnostic entry.");
            });
            Test("Passive notifications do not activate hidden history controls", () =>
            {
                var history = new HistoryInteraction(HistoryJournal(3), Rules());
                var layout = new HistoryLayout(1920, 1080, 1);
                int copies = 0;
                Click(history, layout, layout.Copy, _ => copies++);
                Check(copies == 0 && !history.Open, "A hidden control consumed a gameplay click.");
            });
            Test("Incoming logs between press and release do not select a different row", () =>
            {
                var journal = HistoryJournal(3);
                var history = new HistoryInteraction(journal, Rules()) { Open = true };
                var layout = new HistoryLayout(1920, 1080, 1);
                AlertItem initial = history.Selected!;
                UiRect target = layout.Row(1, 0);
                float x = target.X + 20, y = target.Y + 20;
                Send(history, layout, x, y, true, false, true, 0, _ => { });
                journal.Add(Record("new message"), 2);
                Send(history, layout, x, y, false, true, false, 0, _ => { });
                Check(ReferenceEquals(history.Selected, initial), "A shifted row was unexpectedly selected.");
            });
            Test("Suppression matches level/source/message, across startup, time and repeat counts", () =>
            {
                var rules = Rules();
                var record = new LogRecord(Severity.Error, "BepInEx", "failure\r\n  at A()", false);
                rules.SetSuppressed(record, true);
                Check(rules.IsSuppressed(new LogRecord(Severity.Error, "BepInEx", "failure\n  at A()", true, DateTimeOffset.Now, 9)), "Identical startup/live log was not matched.");
                Check(!rules.IsSuppressed(new LogRecord(Severity.Warning, "BepInEx", record.Message, false)), "Different severity was suppressed.");
                Check(!rules.IsSuppressed(new LogRecord(Severity.Error, "Other mod", record.Message, false)), "Different source was suppressed.");
                Check(!rules.IsSuppressed(new LogRecord(Severity.Error, "BepInEx", record.Message + " changed", false)), "Different error was suppressed.");
                var split = new LogRecord(Severity.Error, "AB", "C", false);
                rules.SetSuppressed(split, true);
                Check(!rules.IsSuppressed(new LogRecord(Severity.Error, "A", "BC", false)), "Source/message boundaries collided.");
            });
            Test("Suppression keeps history, removes active cards and restores only future occurrences", () =>
            {
                var rules = Rules();
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                var record = Record("repeat");
                center.Receive(record, 0);
                rules.SetSuppressed(record, true); center.Tick(1);
                center.Receive(record, 2);
                Check(center.Journal.Items.Single().Count == 2 && center.Toasts.Visible.Count == 0, "Suppression erased history or left a card visible.");
                rules.SetSuppressed(record, false); center.Tick(3);
                Check(center.Toasts.Visible.Count == 0, "Unmuting replayed old alerts.");
                center.Receive(record, 4);
                Check(center.Journal.Items.Single().Count == 3 && center.Toasts.Visible.Single().Count == 1, "Old suppressed occurrences were included in the new card count.");
                center.Receive(record, 5);
                Check(center.Toasts.Visible.Single().Count == 2, "New repeats did not aggregate after resuming.");
            });
            Test("Startup counts obey the same suppression rules and retain every historical entry", () =>
            {
                var rules = Rules();
                var hidden = new LogRecord(Severity.Error, "BepInEx", "writer", true, count: 2);
                rules.SetSuppressed(hidden, true);
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                center.PublishStartup(new[] { hidden, new LogRecord(Severity.Warning, "Mod", "warning", true, count: 3), new LogRecord(Severity.Error, "Mod", "error", true) }, 0);
                Toast summary = center.Toasts.Visible.Single();
                Check(summary.StartupSummary && summary.Warnings == 3 && summary.Errors == 1 && center.Journal.Items.Count == 3, "Startup counters or retained history ignored the rule.");
                var allMuted = new NotificationCenter(20, 10, 6, 10, rules);
                allMuted.PublishStartup(new[] { hidden }, 0);
                Check(allMuted.Toasts.Visible.Count == 0 && allMuted.Journal.Items.Single().Count == 2, "An all-muted startup still produced a popup or lost history.");
                var defaults = new NotificationCenter(20, 10, 6, 10, Rules());
                defaults.PublishStartup(new[] { hidden }, 0);
                Check(defaults.Toasts.Visible.Single().Errors == 2, "BepInEx errors stopped being included by default.");
            });
            Test("Changing rules cannot resurrect old startup contributions or expired cards", () =>
            {
                var rules = Rules();
                var core = new LogRecord(Severity.Error, "BepInEx", "writer", true);
                var mod = Record("startup warning", true);
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                center.PublishStartup(new[] { core, mod }, 0);
                rules.SetSuppressed(core, true); center.Tick(1);
                var summary = center.Toasts.Visible.Single();
                Check(summary.Errors == 0 && summary.Warnings == 1 && summary.Level == Severity.Warning, "A muted startup contribution still affected count or color.");
                rules.Clear(); center.Tick(2);
                Check(summary.Errors == 0 && summary.Warnings == 1, "Reset re-added historical errors to an existing popup.");
                rules.SetSuppressed(mod, true); center.Tick(3);
                Check(center.Toasts.Visible.Count == 0, "An empty startup summary remained visible.");
                rules.Clear(); center.Tick(4);
                Check(center.Toasts.Visible.Count == 0, "Reset replayed the removed startup summary.");
                center.Receive(new LogRecord(Severity.Error, "BepInEx", "writer", false), 5);
                Check(!center.Toasts.Visible.Single().StartupSummary, "A new error after reset was not delivered normally.");
            });
            Test("Persisted rules survive relaunch, selective unmute and bulk reset", () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "BepInExLogAlerts-rules-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var store = new NotificationRuleStore(Path.Combine(directory, "rules"));
                    var rules = new NotificationRules(store.Load(), store.Save);
                    var a = new LogRecord(Severity.Error, "日本語MOD", "first\nstack \"quoted\"\t\u001b", false);
                    var b = Record("other");
                    rules.SetSuppressed(a, true); rules.SetSuppressed(b, true);
                    var next = new NotificationRules(store.Load(), store.Save);
                    Check(next.Count == 2 && next.IsSuppressed(a) && next.IsSuppressed(b), "A saved rule did not survive reload.");
                    next.SetSuppressed(a, false);
                    next = new NotificationRules(store.Load(), store.Save);
                    Check(!next.IsSuppressed(a) && next.IsSuppressed(b), "Individual unmute changed unrelated rules.");
                    next.Clear();
                    Check(store.Load().Count == 0, "Bulk reset was not persisted.");
                }
                finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            });
            Test("Failed rule saves leave checkbox state and previous settings intact", () =>
            {
                var record = Record("x");
                var rules = new NotificationRules(new NotificationRuleSet(Array.Empty<string>()), _ => throw new IOException("disk unavailable"));
                bool failed = false;
                try { rules.SetSuppressed(record, true); } catch (IOException) { failed = true; }
                Check(failed && rules.Count == 0 && !rules.IsSuppressed(record) && rules.Revision == 0, "Failed save silently changed the live policy.");
                rules = new NotificationRules(new NotificationRuleSet(new[] { record.NotificationId }), _ => throw new IOException("disk unavailable"));
                failed = false;
                try { rules.Clear(); } catch (IOException) { failed = true; }
                Check(failed && rules.IsSuppressed(record) && rules.Count == 1, "Failed bulk reset discarded the previous policy.");
            });
            Test("Corrupt persisted rules fail explicitly and are not overwritten with empty settings", () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "BepInExLogAlerts-corrupt-" + Guid.NewGuid().ToString("N"));
                try
                {
                    string path = Path.Combine(directory, "rules");
                    var store = new NotificationRuleStore(path);
                    var rules = new NotificationRules(store.Load(), store.Save);
                    rules.SetSuppressed(Record("x"), true);
                    File.AppendAllText(path, "invalid rule\n");
                    byte[] previous = File.ReadAllBytes(path);
                    bool failed = false;
                    try { store.Load(); } catch (InvalidDataException) { failed = true; }
                    Check(failed && File.ReadAllBytes(path).SequenceEqual(previous), "Corrupt settings were silently reset or rewritten.");
                }
                finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            });
            Test("The history checkbox toggles suppression for the selected entry at several scales", () =>
            {
                foreach (var screen in new[] { (1920f, 1080f, 1f), (3840f, 2160f, 1f), (2560f, 1440f, 1.4f) })
                {
                    var rules = Rules(); var journal = HistoryJournal(3);
                    var history = new HistoryInteraction(journal, rules) { Open = true };
                    var layout = new HistoryLayout(screen.Item1, screen.Item2, screen.Item3);
                    Click(history, layout, layout.Row(1, 0), _ => { });
                    Click(history, layout, layout.Suppress, _ => { });
                    Check(rules.IsSuppressed(journal.Items[1].Record) && !rules.IsSuppressed(journal.Items[2].Record), "Checkbox changed the wrong entry.");
                    Click(history, layout, layout.Suppress, _ => { });
                    Check(rules.Count == 0 && journal.Items.Count == 3, "Checkbox unmute failed or removed history.");
                }
            });
            Test("Bulk reset works when the muted entry is absent from this session's history", () =>
            {
                var rules = Rules(); rules.SetSuppressed(Record("from a previous launch"), true);
                var journal = new AlertJournal(20, 10);
                var history = new HistoryInteraction(journal, rules) { Open = true };
                var layout = new HistoryLayout(3840, 2160, 1);
                Click(history, layout, layout.ClearSuppression, _ => { });
                Check(rules.Count == 0 && history.Selected == null && journal.Items.Count == 0, "Reset required a currently selected or retained log.");
            });
            Test("Releasing outside or evicting the selected entry cancels a pending suppression change", () =>
            {
                var rules = Rules(); var journal = new AlertJournal(1, 10);
                journal.Add(Record("old"), 0);
                var history = new HistoryInteraction(journal, rules) { Open = true };
                var layout = new HistoryLayout(1920, 1080, 1);
                float x = layout.Suppress.X + 10, y = layout.Suppress.Y + 10;
                Send(history, layout, x, y, true, false, true, 0, _ => { });
                Send(history, layout, 0, 0, false, true, false, 0, _ => { });
                Check(rules.Count == 0, "Releasing outside muted the log.");
                Send(history, layout, x, y, true, false, true, 0, _ => { });
                journal.Add(Record("new"), 1);
                Send(history, layout, x, y, false, true, false, 0, _ => { });
                Check(rules.Count == 0, "A newly selected replacement was muted by the previous entry's click.");
            });
            Test("Resuming notifications does not replay logs queued while muted", () =>
            {
                var rules = Rules();
                var record = Record("queued while muted");
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                var mailbox = new LogMailbox(4, rules);
                mailbox.SealStartup(() => 0, out _);
                rules.SetSuppressed(record, true);
                mailbox.Post(record.Level, record.Source, record.Message, DateTimeOffset.Now);
                rules.Clear();
                foreach (LogRecord pending in mailbox.Drain(4)) center.Receive(pending, 1);
                Check(center.Journal.Items.Count == 1 && center.Toasts.Visible.Count == 0, "An old queued occurrence was replayed after resuming.");
                mailbox.Post(record.Level, record.Source, record.Message, DateTimeOffset.Now);
                foreach (LogRecord pending in mailbox.Drain(4)) center.Receive(pending, 2);
                Check(center.Journal.Items.Single().Count == 2 && center.Toasts.Visible.Single().Count == 1, "A newly received event was lost after resuming.");
            });
            Test("Global Warning suppression keeps history and leaves Error/Fatal and individual rules intact", () =>
            {
                var rules = Rules();
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                var warning = Record("global warning");
                center.Receive(warning, 0);
                Check(rules.IsLevelEnabled(Severity.Warning) && center.Toasts.Visible.Single().Level == Severity.Warning, "Warnings were not enabled by default.");
                rules.SetLevelEnabled(Severity.Warning, false); center.Tick(1);
                Check(center.Toasts.Visible.Count == 0, "An already visible Warning survived global suppression.");
                center.Receive(new LogRecord(Severity.Warning, "BepInEx", "another warning", false), 2);
                var error = Record("error", level: Severity.Error);
                center.Receive(error, 2);
                center.Receive(Record("fatal", level: Severity.Fatal), 2);
                Check(center.Journal.Items.Count == 4 && center.Toasts.Visible.Count == 2 && center.Toasts.Visible.All(t => t.Level != Severity.Warning), "History was lost or other severities changed.");
                rules.SetSuppressed(error, true); center.Tick(3);
                Check(center.Toasts.Visible.Single().Level == Severity.Fatal, "Global suppression bypassed the individual Error mute.");
            });
            Test("Global Warning suppression updates startup summaries without restoring removed warnings", () =>
            {
                var rules = Rules();
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                var warnings = new LogRecord(Severity.Warning, "Mod", "startup", true, count: 3);
                var error = new LogRecord(Severity.Error, "Mod", "error", true, count: 2);
                center.PublishStartup(new[] { warnings, error }, 0);
                Toast summary = center.Toasts.Visible.Single();
                rules.SetLevelEnabled(Severity.Warning, false); center.Tick(1);
                Check(summary.Warnings == 0 && summary.Errors == 2 && center.Journal.Items.Count == 2, "Startup Warning contributions remained or history was removed.");
                rules.SetLevelEnabled(Severity.Warning, true); center.Tick(2);
                Check(summary.Warnings == 0 && summary.Errors == 2, "Disabling the option restored past startup warnings.");
                rules.SetLevelEnabled(Severity.Warning, false);
                var onlyWarnings = new NotificationCenter(20, 10, 6, 10, rules);
                onlyWarnings.PublishStartup(new[] { warnings }, 0);
                Check(onlyWarnings.Toasts.Visible.Count == 0 && onlyWarnings.Journal.Items.Single().Count == 3, "Muted Warning-only startup still produced a summary.");
            });
            Test("Resuming global Warning notifications preserves individual mutes and only notifies future events", () =>
            {
                var rules = Rules();
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                LogRecord a = Record("individually muted"), b = Record("globally muted");
                rules.SetLevelEnabled(Severity.Warning, false);
                Check(rules.IsSuppressed(a) && !rules.IsIndividuallySuppressed(a), "Global state was confused with an individual rule.");
                rules.SetSuppressed(a, true);
                center.Receive(a, 0); center.Receive(b, 0);
                rules.SetLevelEnabled(Severity.Warning, true); center.Tick(1);
                Check(rules.IsSuppressed(a) && !rules.IsSuppressed(b) && center.Toasts.Visible.Count == 0, "Resume lost an individual mute or replayed past notifications.");
                center.Receive(a, 2); center.Receive(b, 2);
                Check(center.Journal.Items.Sum(i => i.Count) == 4 && center.Toasts.Visible.Single().Item!.Record.Message == b.Message && center.Toasts.Visible.Single().Count == 1, "A muted message appeared or old counts entered the resumed popup.");
            });
            Test("Logs queued while each level is disabled stay silent after reset", () =>
            {
                foreach (Severity level in new[] { Severity.Warning, Severity.Error, Severity.Fatal })
                {
                    var rules = Rules();
                    var mailbox = new LogMailbox(4, rules);
                    mailbox.SealStartup(() => 0, out _);
                    rules.SetLevelEnabled(level, false);
                    mailbox.Post(level, "Mod", "queued", DateTimeOffset.Now);
                    rules.Clear();
                    var center = new NotificationCenter(20, 10, 6, 10, rules);
                    foreach (LogRecord record in mailbox.Drain(4)) center.Receive(record, 1);
                    Check(center.Journal.Items.Count == 1 && center.Toasts.Visible.Count == 0, "Reset replayed an event received while its level was disabled.");
                    mailbox.Post(level, "Mod", "queued", DateTimeOffset.Now);
                    foreach (LogRecord record in mailbox.Drain(4)) center.Receive(record, 2);
                    Check(center.Journal.Items.Single().Count == 2 && center.Toasts.Visible.Single().Count == 1, "A new event failed to resume with a fresh popup count.");
                }
            });
            Test("Released individual rules survive upgrade and per-level preferences persist independently", () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "BepInExLogAlerts-released-rules-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    string path = Path.Combine(directory, "rules");
                    LogRecord a = Record("saved Warning"), b = Record("saved Error", level: Severity.Error);
                    File.WriteAllText(path, "BepInExLogAlerts muted logs / 1\n" + a.NotificationId + "\n" + b.NotificationId + "\n");
                    byte[] previous = File.ReadAllBytes(path);
                    var store = new NotificationRuleStore(path);
                    var rules = new NotificationRules(store.Load(), store.Save);
                    Check(rules.Count == 2 && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(rules.IsLevelEnabled) && rules.IsSuppressed(a) && rules.IsSuppressed(b) && File.ReadAllBytes(path).SequenceEqual(previous), "Loading the released format changed it or lost settings.");
                    rules.SetLevelEnabled(Severity.Warning, false);
                    rules.SetLevelEnabled(Severity.Error, false);
                    rules.SetLevelEnabled(Severity.Fatal, false);
                    rules = new NotificationRules(store.Load(), store.Save);
                    Check(rules.Count == 5 && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(level => !rules.IsLevelEnabled(level)) && rules.IsIndividuallySuppressed(a) && rules.IsIndividuallySuppressed(b), "Reload lost a level or an individual setting.");
                    rules.SetLevelEnabled(Severity.Error, true);
                    rules = new NotificationRules(store.Load(), store.Save);
                    Check(rules.IsLevelEnabled(Severity.Error) && !rules.IsLevelEnabled(Severity.Warning) && !rules.IsLevelEnabled(Severity.Fatal) && rules.IsIndividuallySuppressed(b), "Changing Error modified another level or erased an individual setting.");
                    rules.Clear();
                    rules = new NotificationRules(store.Load(), store.Save);
                    Check(rules.Count == 0 && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(rules.IsLevelEnabled) && !rules.IsSuppressed(a) && !rules.IsSuppressed(b), "Reset did not persist all enabled levels and cleared individual settings.");
                }
                finally { Directory.Delete(directory, true); }
            });
            Test("Failed level changes and reset preserve every level and individual setting", () =>
            {
                var error = Record("preserved", level: Severity.Error);
                foreach (Severity level in new[] { Severity.Warning, Severity.Error, Severity.Fatal })
                {
                    var rules = new NotificationRules(new NotificationRuleSet(new[] { error.NotificationId }), _ => throw new IOException("disk unavailable"));
                    bool failed = false;
                    try { rules.SetLevelEnabled(level, false); } catch (IOException) { failed = true; }
                    Check(failed && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(rules.IsLevelEnabled) && rules.IsSuppressed(error) && rules.Count == 1 && rules.Revision == 0, "A failed save changed a live level or individual setting.");
                }
                var muted = new NotificationRules(new NotificationRuleSet(new[] { error.NotificationId }, false, false, false), _ => throw new IOException("disk unavailable"));
                bool resetFailed = false;
                try { muted.Clear(); } catch (IOException) { resetFailed = true; }
                Check(resetFailed && new[] { Severity.Warning, Severity.Error, Severity.Fatal }.All(level => !muted.IsLevelEnabled(level)) && muted.IsIndividuallySuppressed(error) && muted.Count == 4 && muted.Revision == 0, "A failed reset partially cleared the policy.");
            });
            Test("Invalid per-level settings and the unsupported development format are not silently reset", () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "BepInExLogAlerts-invalid-levels-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    string path = Path.Combine(directory, "rules");
                    foreach (string options in new[] { "Warning=maybe\nError=true\nFatal=true", "Warning=true\nError=maybe\nFatal=true", "Warning=true\nError=true\nFatal=maybe", "Warning=true\nError=true", "Warning=true\nError=true\nFatal=true\ninvalid rule", "SuppressWarnings=true" })
                    {
                        File.WriteAllText(path, "BepInExLogAlerts muted logs / 2\n" + options + "\n");
                        byte[] original = File.ReadAllBytes(path);
                        bool failed = false;
                        try { new NotificationRuleStore(path).Load(); } catch (InvalidDataException) { failed = true; }
                        Check(failed && File.ReadAllBytes(path).SequenceEqual(original), "An invalid or unsupported setting was silently accepted or overwritten.");
                    }
                }
                finally { Directory.Delete(directory, true); }
            });
            Test("Three positive level checkboxes and reset work independently with empty history at several scales", () =>
            {
                foreach (var screen in new[] { (1920f, 1080f, 1f), (3840f, 2160f, 1f), (2560f, 1440f, 1.4f), (1280f, 720f, 2f) })
                {
                    var rules = Rules();
                    var history = new HistoryInteraction(new AlertJournal(20, 10), rules) { Open = true };
                    var layout = new HistoryLayout(screen.Item1, screen.Item2, screen.Item3);
                    var controls = new[] { (Severity.Warning, layout.NotifyWarning), (Severity.Error, layout.NotifyError), (Severity.Fatal, layout.NotifyFatal) };
                    Check(controls.All(c => rules.IsLevelEnabled(c.Item1)), "Level checkboxes were not all enabled by default.");
                    int disabled = 0;
                    foreach (var control in controls)
                    {
                        Click(history, layout, control.Item2, _ => throw new Exception("A level checkbox copied a log."));
                        disabled++;
                        Check(!rules.IsLevelEnabled(control.Item1) && rules.Count == disabled && history.Selected == null, "A checkbox toggled another level or required a selected row.");
                    }
                    history.Open = false; history.Open = true;
                    Check(controls.All(c => !rules.IsLevelEnabled(c.Item1)), "Closing history lost the level choices.");
                    Click(history, layout, layout.ClearSuppression, _ => { });
                    Check(controls.All(c => rules.IsLevelEnabled(c.Item1)) && rules.Count == 0, "Reset did not enable all three levels with empty history.");
                    foreach (var control in controls)
                    {
                        float x = control.Item2.X + 20, y = control.Item2.Y + 15;
                        Send(history, layout, x, y, true, false, true, 0, _ => { });
                        Send(history, layout, layout.Detail.X, layout.Detail.Y, false, true, false, 0, _ => { });
                        Check(rules.IsLevelEnabled(control.Item1), "Releasing outside toggled a level.");
                    }
                }
            });
            Test("The individual history checkbox remains editable independently of global Warning suppression", () =>
            {
                var rules = Rules();
                var journal = HistoryJournal(3);
                var history = new HistoryInteraction(journal, rules) { Open = true };
                var layout = new HistoryLayout(1920, 1080, 1);
                LogRecord selected = history.Selected!.Record;
                Click(history, layout, layout.NotifyWarning, _ => { });
                Check(rules.IsSuppressed(selected) && !rules.IsIndividuallySuppressed(selected), "Global toggle created an individual rule.");
                Click(history, layout, layout.Suppress, _ => { });
                Check(rules.IsIndividuallySuppressed(selected), "Individual checkbox could not add a rule while globally muted.");
                Click(history, layout, layout.NotifyWarning, _ => { });
                Check(rules.IsSuppressed(selected), "Global resume erased the individual setting.");
                Click(history, layout, layout.NotifyWarning, _ => { });
                Click(history, layout, layout.Suppress, _ => { });
                Check(!rules.IsIndividuallySuppressed(selected) && rules.IsSuppressed(selected), "Individual unmute incorrectly bypassed the global setting.");
                Click(history, layout, layout.ClearSuppression, _ => { });
                Check(rules.IsLevelEnabled(Severity.Warning) && !rules.IsSuppressed(selected), "Combined reset failed from the history UI.");
            });
            Test("All eight level combinations affect popups independently while retaining history", () =>
            {
                var levels = new[] { Severity.Warning, Severity.Error, Severity.Fatal };
                for (int mask = 0; mask < 8; mask++)
                {
                    var rules = Rules();
                    for (int i = 0; i < levels.Length; i++) rules.SetLevelEnabled(levels[i], (mask & (1 << i)) != 0);
                    for (int i = 0; i < levels.Length; i++)
                    {
                        var center = new NotificationCenter(20, 10, 6, 10, rules);
                        center.Receive(Record("level choice", level: levels[i]), 0);
                        Check(center.Journal.Items.Count == 1 && center.Toasts.Visible.Count == ((mask & (1 << i)) != 0 ? 1 : 0), "A level combination changed another level or lost history.");
                    }
                }
            });
            Test("Startup summaries honor all eight independent level combinations", () =>
            {
                var levels = new[] { Severity.Warning, Severity.Error, Severity.Fatal };
                for (int mask = 0; mask < 8; mask++)
                {
                    var rules = Rules();
                    for (int i = 0; i < levels.Length; i++) rules.SetLevelEnabled(levels[i], (mask & (1 << i)) != 0);
                    var center = new NotificationCenter(20, 10, 6, 10, rules);
                    center.PublishStartup(new[] { new LogRecord(Severity.Warning, "Mod", "w", true, count: 2), new LogRecord(Severity.Error, "Mod", "e", true, count: 3), new LogRecord(Severity.Fatal, "Mod", "f", true, count: 5) }, 0);
                    Check(center.Journal.Items.Count == 3, "Level choices removed startup history.");
                    if (mask == 0) Check(center.Toasts.Visible.Count == 0, "Disabling all levels left a startup popup.");
                    else
                    {
                        Toast summary = center.Toasts.Visible.Single();
                        Check(summary.Warnings == ((mask & 1) != 0 ? 2 : 0) && summary.Errors == (((mask & 2) != 0 ? 3 : 0) + ((mask & 4) != 0 ? 5 : 0)), "Startup summary counts conflated Error and Fatal eligibility.");
                    }
                }
            });
            Test("Error and Fatal popups can be removed and resumed independently without replay", () =>
            {
                var rules = Rules();
                var center = new NotificationCenter(20, 10, 6, 10, rules);
                var error = Record("error card", level: Severity.Error);
                var fatal = Record("fatal card", level: Severity.Fatal);
                center.Receive(error, 0); center.Receive(fatal, 0);
                rules.SetLevelEnabled(Severity.Error, false); center.Tick(1);
                Check(center.Toasts.Visible.Single().Level == Severity.Fatal, "Disabling Error removed Fatal or left Error visible.");
                rules.SetLevelEnabled(Severity.Fatal, false); center.Tick(2);
                Check(center.Toasts.Visible.Count == 0 && center.Journal.Items.Count == 2, "Disabling Fatal lost history or left a popup.");
                rules.SetLevelEnabled(Severity.Error, true); rules.SetLevelEnabled(Severity.Fatal, true); center.Tick(3);
                Check(center.Toasts.Visible.Count == 0, "Re-enabling levels replayed old popups.");
                center.Receive(error, 4); center.Receive(fatal, 4);
                Check(center.Toasts.Visible.Count == 2 && center.Toasts.Visible.All(t => t.Count == 1), "New Error/Fatal occurrences did not resume with fresh popup counts.");
            });
            Console.WriteLine("Passed " + count + " behavior checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
