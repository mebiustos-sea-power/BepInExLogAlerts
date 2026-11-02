using System;

namespace BepInExLogAlerts.Core;

internal readonly struct UiRect
{
    internal readonly float X, Y, Width, Height;
    internal float Right => X + Width;
    internal float Bottom => Y + Height;
    internal UiRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    internal bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;
}

internal readonly struct PointerFrame
{
    // Input System screen coordinates use the bottom-left origin, in physical pixels.
    internal readonly float X, Y, Wheel;
    internal readonly bool Pressed, Released, Held;
    internal PointerFrame(float x, float y, bool pressed = false, bool released = false, bool held = false, float wheel = 0)
    { X = x; Y = y; Pressed = pressed; Released = released; Held = held; Wheel = wheel; }
}

internal sealed class HistoryLayout
{
    internal const float RowStep = 76, RowHeight = 72;
    internal readonly float Scale, PixelHeight;
    internal readonly UiRect Window, Close, List, Copy, Heading, Detail, ListTrack, DetailTrack, Suppress, ClearSuppression;
    internal HistoryLayout(float pixelWidth, float pixelHeight, float configuredScale)
    {
        PixelHeight = pixelHeight;
        Scale = Math.Min(2f, Math.Max(0.75f, pixelHeight / 1080f)) * configuredScale;
        float canvasWidth = pixelWidth / Scale, canvasHeight = pixelHeight / Scale;
        float width = Math.Min(1040, canvasWidth - 24), height = Math.Min(760, canvasHeight - 24);
        Window = new UiRect((canvasWidth - width) / 2, (canvasHeight - height) / 2, width, height);
        Close = new UiRect(Window.Right - 88, Window.Y + 12, 72, 28);
        ClearSuppression = new UiRect(Window.X + 16, Window.Y + 84, Math.Min(360, width - 32), 32);
        List = new UiRect(Window.X + 12, Window.Y + 130, Math.Min(390, width * 0.42f), height - 144);
        float rightX = List.Right + 14, rightWidth = Window.Right - List.Right - 28;
        Heading = new UiRect(rightX, List.Y, rightWidth - 92, 50);
        Copy = new UiRect(Window.Right - 96, List.Y, 82, 30);
        Suppress = new UiRect(rightX, List.Y + 58, rightWidth, 40);
        Detail = new UiRect(rightX, List.Y + 106, rightWidth, List.Height - 106);
        ListTrack = new UiRect(List.Right - 16, List.Y, 12, List.Height);
        DetailTrack = new UiRect(Detail.Right - 16, Detail.Y, 12, Detail.Height);
    }
    internal UiRect Row(int display, float scroll) => new UiRect(List.X, List.Y + display * RowStep - scroll, List.Width - 24, RowHeight);
    internal static UiRect Thumb(UiRect track, float contentHeight, float scroll)
    {
        float maximum = Math.Max(0, contentHeight - track.Height);
        float height = Math.Min(track.Height, Math.Max(24, track.Height * track.Height / Math.Max(track.Height, contentHeight)));
        return new UiRect(track.X, track.Y + (maximum == 0 ? 0 : scroll / maximum * (track.Height - height)), track.Width, height);
    }
}

internal enum HistoryHit { None, Close, Copy, Row, ListScroll, DetailScroll, Suppress, ClearSuppression }

internal sealed class HistoryInteraction
{
    private readonly AlertJournal journal;
    private readonly NotificationRules rules;
    private bool open;
    private HistoryHit pressed;
    private AlertItem? pressedItem, selected;
    private float grabOffset;
    internal float ListScroll { get; private set; }
    internal float DetailScroll { get; private set; }
    internal float DetailContentHeight { get; set; }
    internal float PointerX { get; private set; } = -1;
    internal float PointerY { get; private set; } = -1;
    internal HistoryHit Hover { get; private set; }
    internal AlertItem? HoverItem { get; private set; }
    internal HistoryHit Pressed => pressed;
    internal bool Open { get => open; set { open = value; CancelPointer(); } }
    internal AlertItem? Selected
    {
        get
        {
            if (selected == null || !journal.Items.Contains(selected))
            { selected = journal.Items.Count == 0 ? null : journal.Items[journal.Items.Count - 1]; DetailScroll = 0; }
            return selected;
        }
    }
    internal HistoryInteraction(AlertJournal journal, NotificationRules rules) { this.journal = journal; this.rules = rules; }
    internal void CancelPointer() { pressed = HistoryHit.None; pressedItem = null; Hover = HistoryHit.None; HoverItem = null; PointerX = PointerY = -1; }

    internal void Process(PointerFrame frame, HistoryLayout layout, Action<string> copy)
    {
        if (!open) { CancelPointer(); return; }
        PointerX = frame.X / layout.Scale;
        PointerY = (layout.PixelHeight - frame.Y) / layout.Scale;
        ClampScroll(layout);
        _ = Selected;
        if (frame.Wheel != 0)
        {
            // Windows mice report 120 units per notch; normalized devices report 1.
            float steps = Math.Abs(frame.Wheel) > 1 ? frame.Wheel / 120 : frame.Wheel;
            if (layout.List.Contains(PointerX, PointerY)) ListScroll -= steps * 48;
            else if (layout.Detail.Contains(PointerX, PointerY)) DetailScroll -= steps * 48;
            ClampScroll(layout);
        }
        Hover = Hit(layout, out AlertItem? hovered);
        HoverItem = hovered;
        if (frame.Pressed)
        {
            pressed = Hover;
            pressedItem = hovered ?? (pressed == HistoryHit.Copy || pressed == HistoryHit.Suppress ? Selected : null);
            if (pressed == HistoryHit.ListScroll || pressed == HistoryHit.DetailScroll)
            {
                UiRect track = pressed == HistoryHit.ListScroll ? layout.ListTrack : layout.DetailTrack;
                float content = pressed == HistoryHit.ListScroll ? journal.Items.Count * HistoryLayout.RowStep : DetailContentHeight;
                float scroll = pressed == HistoryHit.ListScroll ? ListScroll : DetailScroll;
                UiRect thumb = HistoryLayout.Thumb(track, content, scroll);
                grabOffset = thumb.Contains(PointerX, PointerY) ? PointerY - thumb.Y : thumb.Height / 2;
                Drag(layout);
            }
        }
        if (frame.Held && (pressed == HistoryHit.ListScroll || pressed == HistoryHit.DetailScroll)) Drag(layout);
        if (frame.Released)
        {
            HistoryHit action = pressed;
            AlertItem? item = pressedItem;
            pressed = HistoryHit.None; pressedItem = null;
            if (action == Hover)
            {
                if (action == HistoryHit.Close) Open = false;
                else if (action == HistoryHit.Copy && item != null && ReferenceEquals(Selected, item))
                    copy(item.Record.Level + " | " + item.Record.Source + " | ×" + item.Count + "\n" + item.Record.Message);
                else if (action == HistoryHit.Row && ReferenceEquals(item, hovered))
                { selected = hovered; DetailScroll = 0; }
                else if (action == HistoryHit.Suppress && item != null && ReferenceEquals(Selected, item))
                    rules.SetSuppressed(item.Record, !rules.IsSuppressed(item.Record));
                else if (action == HistoryHit.ClearSuppression) rules.Clear();
            }
        }
        else if (!frame.Held && !frame.Pressed) { pressed = HistoryHit.None; pressedItem = null; }
    }

    internal void ClampScroll(HistoryLayout layout)
    {
        ListScroll = Math.Max(0, Math.Min(ListScroll, journal.Items.Count * HistoryLayout.RowStep - layout.List.Height));
        DetailScroll = Math.Max(0, Math.Min(DetailScroll, DetailContentHeight - layout.Detail.Height));
    }
    private HistoryHit Hit(HistoryLayout layout, out AlertItem? item)
    {
        item = null;
        if (layout.Close.Contains(PointerX, PointerY)) return HistoryHit.Close;
        if (Selected != null && layout.Copy.Contains(PointerX, PointerY)) return HistoryHit.Copy;
        if (Selected != null && layout.Suppress.Contains(PointerX, PointerY)) return HistoryHit.Suppress;
        if (rules.Count != 0 && layout.ClearSuppression.Contains(PointerX, PointerY)) return HistoryHit.ClearSuppression;
        if (layout.ListTrack.Contains(PointerX, PointerY) && journal.Items.Count * HistoryLayout.RowStep > layout.List.Height) return HistoryHit.ListScroll;
        if (layout.DetailTrack.Contains(PointerX, PointerY) && DetailContentHeight > layout.Detail.Height) return HistoryHit.DetailScroll;
        if (layout.List.Contains(PointerX, PointerY) && PointerX < layout.List.Right - 24)
        {
            int display = (int)((PointerY - layout.List.Y + ListScroll) / HistoryLayout.RowStep);
            if (display >= 0 && display < journal.Items.Count && layout.Row(display, ListScroll).Contains(PointerX, PointerY))
            { item = journal.Items[journal.Items.Count - 1 - display]; return HistoryHit.Row; }
        }
        return HistoryHit.None;
    }
    private void Drag(HistoryLayout layout)
    {
        bool list = pressed == HistoryHit.ListScroll;
        UiRect track = list ? layout.ListTrack : layout.DetailTrack;
        float content = list ? journal.Items.Count * HistoryLayout.RowStep : DetailContentHeight;
        UiRect thumb = HistoryLayout.Thumb(track, content, list ? ListScroll : DetailScroll);
        float fraction = track.Height <= thumb.Height ? 0 : Math.Max(0, Math.Min(1, (PointerY - grabOffset - track.Y) / (track.Height - thumb.Height)));
        float scroll = fraction * Math.Max(0, content - track.Height);
        if (list) ListScroll = scroll; else DetailScroll = scroll;
    }
}
