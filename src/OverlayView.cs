using System;
using BepInExLogAlerts.Core;
using UnityEngine;

namespace BepInExLogAlerts;

internal sealed class OverlayView : IDisposable
{
    private readonly AlertSettings settings;
    private readonly UiText words;
    private readonly AlertJournal journal;
    private readonly Toasts toasts;
    private readonly HistoryInteraction history;
    private readonly NotificationRules rules;
    private GUIStyle? title, text, small, row, detail, button, toggle;
    private Font? font;
    private double copiedUntil;
    internal bool HistoryOpen { get => history.Open; set => history.Open = value; }
    internal OverlayView(AlertSettings settings, UiText words, NotificationCenter notifications)
    {
        this.settings = settings; this.words = words;
        journal = notifications.Journal; toasts = notifications.Toasts; rules = notifications.Rules;
        history = new HistoryInteraction(journal, rules);
    }

    internal void ProcessPointer(PointerFrame pointer, double now)
    {
        if (!HistoryOpen) return;
        history.Process(pointer, new HistoryLayout(Screen.width, Screen.height, settings.Scale), value =>
        {
            GUIUtility.systemCopyBuffer = value;
            copiedUntil = now + 2;
        });
    }
    internal void CancelPointer() => history.CancelPointer();

    internal void Draw(double now, bool ready)
    {
        // IMGUI is used only for rendering. Input System pointer frames drive every
        // history action, so mouse events need not be forwarded to OnGUI by the game.
        if (Event.current.type != EventType.Repaint || (!HistoryOpen && (!ready || toasts.Visible.Count == 0))) return;
        if (title == null) Initialize();
        Matrix4x4 matrix = GUI.matrix;
        int depth = GUI.depth;
        Color color = GUI.color;
        try
        {
            var layout = new HistoryLayout(Screen.width, Screen.height, settings.Scale);
            float scale = layout.Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.depth = -9999;
            float canvasWidth = Screen.width / scale, canvasHeight = Screen.height / scale;
            if (!HistoryOpen && ready)
            {
                float width = Mathf.Min(settings.Width, canvasWidth - 24);
                float top = Mathf.Min(settings.Top, Mathf.Max(0, canvasHeight - toasts.Visible.Count * 118 + 8));
                foreach (Toast toast in toasts.Visible)
                {
                    DrawToast(new Rect((canvasWidth - width) / 2, top, width, 110), toast);
                    toasts.Rendered(toast, now);
                    top += 118;
                }
            }
            if (HistoryOpen) History(layout, now, ready);
        }
        finally { GUI.matrix = matrix; GUI.depth = depth; GUI.color = color; }
    }

    private void DrawToast(Rect rect, Toast toast)
    {
        Fill(rect, new Color(0.025f, 0.035f, 0.05f, 0.96f));
        Fill(new Rect(rect.x, rect.y, 5, rect.height), SeverityColor(toast.Level));
        string heading, body;
        if (toast.StartupSummary)
        {
            heading = words.T("起動中のログ通知", "Startup log alerts");
            body = words.T("警告 ", "Warnings: ") + toast.Warnings + words.T("件 ・ エラー ", "  |  Errors: ") + toast.Errors + words.T("件", "");
        }
        else
        {
            LogRecord record = toast.Item!.Record;
            heading = record.Level + "  |  " + record.Source + (toast.Count > 1 ? "  ×" + toast.Count : "");
            body = record.Summary();
        }
        GUI.Label(new Rect(rect.x + 16, rect.y + 8, rect.width - 30, 26), heading, title!);
        GUI.Label(new Rect(rect.x + 16, rect.y + 37, rect.width - 30, 42), body, text!);
        GUI.Label(new Rect(rect.x + 16, rect.y + 84, rect.width - 30, 22), settings.HistoryKey + words.T("：履歴・詳細", ": history / details"), small!);
    }

    private void History(HistoryLayout layout, double now, bool ready)
    {
        Rect rect = Rect(layout.Window);
        Fill(rect, new Color(0.025f, 0.035f, 0.05f, 0.98f));
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 16, rect.y + 12, rect.width - 115, 30), ModInfo.Name + words.T(" — 履歴", " — History"), title!);
        DrawButton(layout.Close, words.T("閉じる", "Close"), HistoryHit.Close);
        string status = ready ? journal.Items.Count + words.T("件の履歴", " history groups") : words.T("起動中のログを収集中。メニュー表示後に取り込みます。", "Collecting startup logs. Recovery completes after the main menu is shown.");
        if (journal.Evicted != 0) status += words.T(" ／ 古い履歴を", " / Old groups removed: ") + journal.Evicted + words.T("件削除", "");
        GUI.Label(new Rect(rect.x + 16, rect.y + 46, rect.width - 32, 38), status, small!);
        GUI.Label(Rect(layout.PopupHeading), words.T("ポップアップ通知", "Popup notifications"), text!);
        DrawLevelToggle(layout.NotifyWarning, Severity.Warning, HistoryHit.NotifyWarning);
        DrawLevelToggle(layout.NotifyError, Severity.Error, HistoryHit.NotifyError);
        DrawLevelToggle(layout.NotifyFatal, Severity.Fatal, HistoryHit.NotifyFatal);
        GUI.Label(Rect(layout.PopupHint), words.T("チェックを外しても履歴には記録します。", "Unchecked levels are still recorded in history."), small!);
        DrawButton(layout.ClearSuppression, words.T("通知しない設定をすべて解除", "Reset all muted notifications") + " (" + rules.Count + ")", HistoryHit.ClearSuppression, rules.Count != 0);
        AlertItem? selected = history.Selected;
        history.ClampScroll(layout);
        GUI.BeginGroup(Rect(layout.List));
        try
        {
            int first = Math.Max(0, (int)(history.ListScroll / HistoryLayout.RowStep));
            int end = Math.Min(journal.Items.Count, first + (int)Math.Ceiling(layout.List.Height / HistoryLayout.RowStep) + 1);
            for (int display = first; display < end; display++)
            {
                AlertItem item = journal.Items[journal.Items.Count - 1 - display];
                string stamp = item.Record.Startup ? words.T("起動時", "Startup") : item.Record.Time?.ToLocalTime().ToString("HH:mm:ss") ?? "";
                string label = stamp + "  " + item.Record.Level + (item.Count > 1 ? " ×" + item.Count : "") + (rules.IsSuppressed(item.Record) ? words.T("  [通知しない]", "  [Muted]") : "") + "\n" + item.Record.Source + "\n" + item.Record.Summary(65);
                var rowRect = new Rect(0, display * HistoryLayout.RowStep - history.ListScroll, layout.List.Width - 24, HistoryLayout.RowHeight);
                bool hover = ReferenceEquals(history.HoverItem, item), active = ReferenceEquals(selected, item);
                row!.Draw(rowRect, new GUIContent(label), hover, hover && history.Pressed == HistoryHit.Row, active, false);
                Fill(new Rect(rowRect.x, rowRect.y, 3, rowRect.height), SeverityColor(item.Record.Level));
            }
        }
        finally { GUI.EndGroup(); }
        DrawScrollbar(layout.ListTrack, journal.Items.Count * HistoryLayout.RowStep, history.ListScroll, HistoryHit.ListScroll);
        if (selected == null)
        { GUI.Label(Rect(layout.Detail), words.T("警告・エラーはありません。", "No warnings or errors."), text!); return; }
        GUI.Label(Rect(layout.Heading), selected.Record.Level + "  ×" + selected.Count + "\n" + selected.Record.Source, text!);
        DrawButton(layout.Copy, now < copiedUntil ? words.T("コピー済", "Copied") : words.T("コピー", "Copy"), HistoryHit.Copy);
        bool suppressed = rules.IsIndividuallySuppressed(selected.Record);
        bool toggleHover = history.Hover == HistoryHit.Suppress;
        toggle!.Draw(Rect(layout.Suppress), new GUIContent(words.T("今後、このログを通知しない", "Do not notify me of this log again")),
            toggleHover, toggleHover && history.Pressed == HistoryHit.Suppress, suppressed, false);
        float contentHeight = Mathf.Max(layout.Detail.Height - 4, detail!.CalcHeight(new GUIContent(selected.Record.Message), layout.Detail.Width - 26));
        history.DetailContentHeight = contentHeight + 4;
        history.ClampScroll(layout);
        GUI.BeginGroup(Rect(layout.Detail));
        try { GUI.Label(new Rect(0, -history.DetailScroll, layout.Detail.Width - 24, contentHeight), selected.Record.Message, detail); }
        finally { GUI.EndGroup(); }
        DrawScrollbar(layout.DetailTrack, history.DetailContentHeight, history.DetailScroll, HistoryHit.DetailScroll);
    }

    private void DrawLevelToggle(UiRect rect, Severity level, HistoryHit kind)
    {
        bool hover = history.Hover == kind;
        toggle!.Draw(Rect(rect), new GUIContent(level.ToString()), hover,
            hover && history.Pressed == kind, rules.IsLevelEnabled(level), false);
    }

    private void DrawButton(UiRect rect, string label, HistoryHit kind, bool enabled = true)
    {
        bool hover = enabled && history.Hover == kind;
        Color previous = GUI.color;
        try
        {
            if (!enabled) GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * 0.45f);
            button!.Draw(Rect(rect), new GUIContent(label), hover, hover && history.Pressed == kind, false, false);
        }
        finally { GUI.color = previous; }
    }
    private void DrawScrollbar(UiRect track, float content, float scroll, HistoryHit kind)
    {
        if (content <= track.Height) return;
        Fill(Rect(track), new Color(0.10f, 0.13f, 0.17f));
        Fill(Rect(HistoryLayout.Thumb(track, content, scroll)), history.Hover == kind || history.Pressed == kind
            ? new Color(0.45f, 0.65f, 0.85f) : new Color(0.28f, 0.35f, 0.43f));
    }
    private static Color SeverityColor(Severity level) =>
        level == Severity.Warning ? new Color(1f, 0.72f, 0.2f) : new Color(1f, 0.3f, 0.3f);

    private static Rect Rect(UiRect rect) => new Rect(rect.X, rect.Y, rect.Width, rect.Height);

    private void Initialize()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "Arial" }, 18);
        if (font == null) throw new InvalidOperationException("A notification font could not be created.");
        title = Style(GUI.skin.label, 19, FontStyle.Bold);
        text = Style(GUI.skin.label, 16);
        small = Style(GUI.skin.label, 13);
        row = Style(GUI.skin.button, 13); row.alignment = TextAnchor.UpperLeft;
        detail = Style(GUI.skin.textArea, 15);
        button = Style(GUI.skin.button, 16); button.alignment = TextAnchor.MiddleCenter;
        toggle = Style(GUI.skin.toggle, 14);
        toggle.onNormal.textColor = toggle.hover.textColor = toggle.onHover.textColor = toggle.active.textColor = toggle.onActive.textColor = Color.white;
    }
    private GUIStyle Style(GUIStyle basis, int size, FontStyle weight = FontStyle.Normal)
    {
        var style = new GUIStyle(basis) { font = font, fontSize = size, fontStyle = weight, wordWrap = true, richText = false };
        style.normal.textColor = Color.white;
        return style;
    }
    private static void Fill(Rect rect, Color color)
    { Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous; }
    public void Dispose() { if (font != null) UnityEngine.Object.Destroy(font); font = null; }
}
