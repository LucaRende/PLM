using System.Globalization;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace GanttChart
{
    internal class GanttRenderer
    {
        private readonly GanttControl _control;
        private readonly GanttConfig _config;

        // Colori tema
        private static readonly Color BackgroundColor = Color.FromArgb(36, 36, 36);
        private static readonly Color PanelColor = Color.FromArgb(50, 50, 50);
        private static readonly Color GridColor = Color.FromArgb(55, 55, 55);
        private static readonly Color TextColor = Color.Gainsboro;
        private static readonly Color TaskColor = Color.FromArgb(255, 222, 89);
        private static readonly Color TaskCompletedColor = Color.FromArgb(255, 204, 77);
        private static readonly Color SelectedColor = Color.FromArgb(110, 150, 240);
        private static readonly Color GroupColor = Color.FromArgb(160, 160, 160);

        public GanttRenderer(GanttControl control)
        {
            _control = control;
            _config = control.Configuration;
        }

        public void Render(Graphics g, Rectangle bounds)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackgroundColor);

            var leftPanel = new Rectangle(0, 0, _config.LeftPanelWidth, bounds.Height);
            var header = new Rectangle(_config.LeftPanelWidth, 0,
                                       bounds.Width - _config.LeftPanelWidth,
                                       _config.HeaderHeight + _config.MinorHeaderHeight);
            var body = new Rectangle(_config.LeftPanelWidth, header.Bottom,
                                     header.Width, bounds.Height - header.Bottom);

            DrawLeftPanel(g, leftPanel);
            DrawHeader(g, header);
            DrawBody(g, body);
        }

        public void DrawLeftPanel(Graphics g, Rectangle bounds)
        {
            using var bg = new SolidBrush(PanelColor);
            using var text = new SolidBrush(TextColor);
            using var font = new Font("Segoe UI", 9);
            using var border = new Pen(GridColor);

            g.FillRectangle(bg, bounds);
            g.DrawLine(border, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom);

            int y = _config.HeaderHeight + _config.MinorHeaderHeight - _control.ScrollOffset;
            int rowIndex = 0;

            foreach (var resource in _control.Resources)
            {
                var rect = new Rectangle(bounds.Left, y, bounds.Width, _config.RowHeight);
                if (rect.Bottom >= 0 && rect.Top <= bounds.Bottom)
                {
                    // Zebra striping
                    using var zebra = new SolidBrush(rowIndex % 2 == 0
                        ? Color.FromArgb(58, 58, 58)
                        : Color.FromArgb(52, 52, 52));
                    g.FillRectangle(zebra, rect);

                    // Expansion caret
                    var caret = resource.IsExpanded ? "▼" : "▶";
                    g.DrawString($"{caret}  {resource.Name}", font, text,
                               new PointF(rect.Left + 8, rect.Top + 5));

                    // Area cliccabile per espansione (schema: Type|Id|ResourceId|GroupId|TaskId)
                    _control.RegisterHitArea($"Resource|{resource.Id}|{resource.Id}||", rect);
                }
                y += _config.RowHeight + _config.RowGap;
                rowIndex++;

                if (!resource.IsExpanded) continue;

                // Gruppi
                foreach (var group in resource.Groups)
                {
                    var groupRect = new Rectangle(bounds.Left, y, bounds.Width, _config.RowHeight);
                    if (groupRect.Bottom >= 0 && groupRect.Top <= bounds.Bottom)
                    {
                        using var zebra = new SolidBrush(rowIndex % 2 == 0
                            ? Color.FromArgb(56, 56, 56)
                            : Color.FromArgb(50, 50, 50));
                        g.FillRectangle(zebra, groupRect);

                        var caret = group.IsExpanded ? "▼" : "▶";
                        var duration = group.TotalDuration;
                        var durationText = $"[{(int)duration.TotalHours:D2}:{duration.Minutes:D2}]";

                        g.DrawString($"    {caret}  {group.Name}  {durationText}",
                                   font, text, new PointF(groupRect.Left + 8, groupRect.Top + 5));

                        _control.RegisterHitArea($"Group|{group.Id}|{resource.Id}|{group.Id}|", groupRect);
                    }
                    y += _config.RowHeight + _config.RowGap;
                    rowIndex++;

                    if (!group.IsExpanded) continue;

                    // Tasks
                    foreach (var task in group.Tasks.OrderBy(t => t.Priority).ThenBy(t => t.Start))
                    {
                        var taskRect = new Rectangle(bounds.Left, y, bounds.Width, _config.RowHeight);
                        if (taskRect.Bottom >= 0 && taskRect.Top <= bounds.Bottom)
                        {
                            using var zebra = new SolidBrush(rowIndex % 2 == 0
                                ? Color.FromArgb(54, 54, 54)
                                : Color.FromArgb(48, 48, 48));
                            g.FillRectangle(zebra, taskRect);

                            var duration = task.Duration;
                            var durationText = $"[{(int)duration.TotalHours:D2}:{duration.Minutes:D2}]";

                            g.DrawString($"        • {task.Name}  {durationText}",
                                       font, text, new PointF(taskRect.Left + 8, taskRect.Top + 5));

                            _control.RegisterHitArea($"Task|{task.Id}|{resource.Id}|{group.Id}|{task.Id}", taskRect);
                        }
                        y += _config.RowHeight + _config.RowGap;
                        rowIndex++;
                    }
                }
            }
        }

        public void DrawHeader(Graphics g, Rectangle bounds)
        {
            using var bg = new SolidBrush(Color.FromArgb(45, 45, 45));
            using var pen = new Pen(GridColor);
            using var text = new SolidBrush(TextColor);
            using var font = new Font("Segoe UI", 9);
            using var boldFont = new Font("Segoe UI", 9, FontStyle.Bold);

            g.FillRectangle(bg, bounds);
            g.DrawRectangle(pen, bounds);

            // Titolo principale
            var title = GetHeaderTitle();
            g.DrawString(title, boldFont, text, new PointF(bounds.Left + 6, bounds.Top + 4));

            // Header secondario con scala temporale
            var minorHeader = new Rectangle(bounds.Left, bounds.Bottom - _config.MinorHeaderHeight,
                                          bounds.Width, _config.MinorHeaderHeight);
            g.DrawRectangle(pen, minorHeader);
            DrawTimeScale(g, minorHeader);
        }

        public void DrawBody(Graphics g, Rectangle bounds)
        {
            if (_config.ShowGrid)
                DrawGrid(g, bounds);

            DrawTasks(g, bounds);
            DrawGroups(g, bounds);

            if (_config.ShowNowLine)
                DrawNowLine(g, bounds);
        }

        private void DrawTimeScale(Graphics g, Rectangle bounds)
        {
            using var text = new SolidBrush(TextColor);
            using var font = new Font("Segoe UI", 8);
            using var pen = new Pen(GridColor);

            var (start, _) = GetTimeWindow();

            switch (_control.ZoomLevel)
            {
                case GanttZoomLevel.Hour:
                    DrawHourScale(g, bounds, start, pen, font, text);
                    break;
                case GanttZoomLevel.Day:
                    DrawDayScale(g, bounds, start, pen, font, text);
                    break;
                case GanttZoomLevel.Week:
                    DrawWeekScale(g, bounds, start, pen, font, text);
                    break;
                case GanttZoomLevel.Month:
                    DrawMonthScale(g, bounds, start, pen, font, text);
                    break;
                case GanttZoomLevel.Year:
                    DrawYearScale(g, bounds, start, pen, font, text);
                    break;
            }
        }

        private void DrawGrid(Graphics g, Rectangle bounds)
        {
            using var pen = new Pen(GridColor);
            var (start, end) = GetTimeWindow();

            // Linee verticali basate sullo zoom
            var intervals = GetGridIntervals();
            foreach (var time in intervals)
            {
                if (time >= start && time < end)
                {
                    var x = GetXFromTime(time, bounds, start, end);
                    g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                }
            }
        }

        private void DrawTasks(Graphics g, Rectangle bounds)
        {
            using var taskBrush = new SolidBrush(TaskColor);
            using var completedBrush = new SolidBrush(TaskCompletedColor);
            using var selectedBrush = new SolidBrush(SelectedColor);
            using var pen = new Pen(Color.FromArgb(30, 30, 30));

            int y = bounds.Top - _control.ScrollOffset;
            var (start, end) = GetTimeWindow();

            foreach (var resource in _control.Resources)
            {
                y += _config.RowHeight + _config.RowGap;
                if (!resource.IsExpanded) continue;

                foreach (var group in resource.Groups)
                {
                    y += _config.RowHeight + _config.RowGap;
                    if (!group.IsExpanded) continue;

                    foreach (var task in group.Tasks.OrderBy(t => t.Priority).ThenBy(t => t.Start))
                    {
                        if (task.End > start && task.Start < end)
                        {
                            var taskRect = GetTaskRect(task, bounds, start, end, y);
                            var brush = _control.IsTaskSelected(task) ? selectedBrush :
                                       task.IsCompleted ? completedBrush : taskBrush;

                            DrawRoundedRectangle(g, brush, pen, taskRect, 6);

                            // Area cliccabile coerente
                            _control.RegisterHitArea($"Task|{task.Id}|{resource.Id}|{group.Id}|{task.Id}", taskRect);
                        }
                        y += _config.RowHeight + _config.RowGap;
                    }
                }
            }
        }

        private void DrawGroups(Graphics g, Rectangle bounds)
        {
            using var groupBrush = new SolidBrush(GroupColor);
            using var selectedBrush = new SolidBrush(SelectedColor);
            using var pen = new Pen(Color.FromArgb(30, 30, 30));

            int y = bounds.Top - _control.ScrollOffset;
            var (start, end) = GetTimeWindow();

            foreach (var resource in _control.Resources)
            {
                y += _config.RowHeight + _config.RowGap;
                if (!resource.IsExpanded) continue;

                foreach (var group in resource.Groups)
                {
                    if (group.StartDate.HasValue && group.EndDate.HasValue &&
                        group.EndDate > start && group.StartDate < end)
                    {
                        var groupRect = GetGroupRect(group, bounds, start, end, y);
                        var brush = _control.IsGroupSelected(group, resource) ? selectedBrush : groupBrush;

                        DrawRoundedRectangle(g, brush, pen, groupRect, 4);

                        _control.RegisterHitArea($"Group|{group.Id}|{resource.Id}|{group.Id}|", groupRect);
                    }

                    y += _config.RowHeight + _config.RowGap;
                    if (group.IsExpanded)
                        y += group.Tasks.Count * (_config.RowHeight + _config.RowGap);
                }
            }
        }

        private void DrawNowLine(Graphics g, Rectangle bounds)
        {
            var now = DateTime.Now;
            var (start, end) = GetTimeWindow();

            if (now >= start && now < end)
            {
                var x = GetXFromTime(now, bounds, start, end);
                using var pen = new Pen(Color.FromArgb(60, 220, 120), 2f)
                {
                    DashPattern = new float[] { 4, 4 }
                };
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
            }
        }

        #region Helper Methods (renderer)

        private void DrawHourScale(Graphics g, Rectangle bounds, DateTime start, Pen pen, Font font, Brush text)
        {
            for (int m = 0; m <= 60; m += 5)
            {
                var time = start.AddMinutes(m);
                var x = GetXFromTime(time, bounds, start, start.AddHours(1));
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                if (m < 60)
                    g.DrawString(time.ToString("HH:mm"), font, text, new PointF(x + 2, bounds.Top + 2));
            }
        }

        private void DrawDayScale(Graphics g, Rectangle bounds, DateTime start, Pen pen, Font font, Brush text)
        {
            for (int h = 0; h <= 24; h++)
            {
                var time = start.AddHours(h);
                var x = GetXFromTime(time, bounds, start, start.AddDays(1));
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                if (h < 24)
                    g.DrawString(h.ToString("00"), font, text, new PointF(x + 2, bounds.Top + 2));
            }
        }

        private void DrawWeekScale(Graphics g, Rectangle bounds, DateTime start, Pen pen, Font font, Brush text)
        {
            for (int d = 0; d <= 7; d++)
            {
                var time = start.AddDays(d);
                var x = GetXFromTime(time, bounds, start, start.AddDays(7));
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                if (d < 7)
                    g.DrawString(time.ToString("ddd d", CultureInfo.GetCultureInfo("it-IT")),
                               font, text, new PointF(x + 2, bounds.Top + 2));
            }
        }

        private void DrawMonthScale(Graphics g, Rectangle bounds, DateTime start, Pen pen, Font font, Brush text)
        {
            var days = DateTime.DaysInMonth(start.Year, start.Month);
            var step = Math.Max(1, days / 15);

            for (int d = 0; d <= days; d += step)
            {
                var time = start.AddDays(d);
                var x = GetXFromTime(time, bounds, start, start.AddMonths(1));
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                if (d < days)
                    g.DrawString((d + 1).ToString(), font, text, new PointF(x + 2, bounds.Top + 2));
            }
        }

        private void DrawYearScale(Graphics g, Rectangle bounds, DateTime start, Pen pen, Font font, Brush text)
        {
            for (int m = 0; m <= 12; m++)
            {
                var time = start.AddMonths(m);
                var x = GetXFromTime(time, bounds, start, start.AddYears(1));
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
                if (m < 12)
                    g.DrawString(new DateTime(2000, m + 1, 1).ToString("MMM", CultureInfo.GetCultureInfo("it-IT")),
                               font, text, new PointF(x + 2, bounds.Top + 2));
            }
        }

        private string GetHeaderTitle()
        {
            return _control.ZoomLevel switch
            {
                GanttZoomLevel.Hour => _control.ViewStart.ToString("dddd d MMMM yyyy HH:mm"),
                GanttZoomLevel.Day => _control.ViewStart.ToString("dddd d MMMM yyyy"),
                GanttZoomLevel.Week => $"Settimana {GetWeekNumber(_control.ViewStart)} ({_control.ViewStart:dd MMM} – {_control.ViewStart.AddDays(6):dd MMM})",
                GanttZoomLevel.Month => _control.ViewStart.ToString("MMMM yyyy"),
                GanttZoomLevel.Year => _control.ViewStart.ToString("yyyy"),
                _ => _control.ViewStart.ToString("MMMM yyyy")
            };
        }

        private (DateTime start, DateTime end) GetTimeWindow()
        {
            return _control.ZoomLevel switch
            {
                GanttZoomLevel.Hour => (_control.ViewStart, _control.ViewStart.AddHours(1)),
                GanttZoomLevel.Day => (_control.ViewStart, _control.ViewStart.AddDays(1)),
                GanttZoomLevel.Week => (_control.ViewStart, _control.ViewStart.AddDays(7)),
                GanttZoomLevel.Month => (_control.ViewStart, _control.ViewStart.AddMonths(1)),
                GanttZoomLevel.Year => (_control.ViewStart, _control.ViewStart.AddYears(1)),
                _ => (_control.ViewStart, _control.ViewStart.AddDays(7))
            };
        }

        private DateTime[] GetGridIntervals()
        {
            var (start, end) = GetTimeWindow();
            var intervals = new System.Collections.Generic.List<DateTime>();

            switch (_control.ZoomLevel)
            {
                case GanttZoomLevel.Hour:
                    for (int m = 0; m <= 60; m += 5)
                        intervals.Add(start.AddMinutes(m));
                    break;
                case GanttZoomLevel.Day:
                    for (int h = 0; h <= 24; h++)
                        intervals.Add(start.AddHours(h));
                    break;
                case GanttZoomLevel.Week:
                    for (int d = 0; d <= 7; d++)
                        intervals.Add(start.AddDays(d));
                    break;
                case GanttZoomLevel.Month:
                    var days = (end - start).Days;
                    for (int d = 0; d <= days; d++)
                        intervals.Add(start.AddDays(d));
                    break;
                case GanttZoomLevel.Year:
                    for (int m = 0; m <= 12; m++)
                        intervals.Add(start.AddMonths(m));
                    break;
            }

            return intervals.ToArray();
        }

        private int GetXFromTime(DateTime time, Rectangle bounds, DateTime start, DateTime end)
        {
            if (time <= start) return bounds.Left;
            if (time >= end) return bounds.Right;

            var ratio = (time - start).TotalMilliseconds / (end - start).TotalMilliseconds;
            return bounds.Left + (int)(ratio * bounds.Width);
        }

        private Rectangle GetTaskRect(GanttTask task, Rectangle bounds, DateTime start, DateTime end, int y)
        {
            var taskStart = task.Start < start ? start : task.Start;
            var taskEnd = task.End > end ? end : task.End;

            var x = GetXFromTime(taskStart, bounds, start, end);
            var width = Math.Max(6, GetXFromTime(taskEnd, bounds, start, end) - x);

            return new Rectangle(x, y + 4, width, _config.RowHeight - 8);
        }

        private Rectangle GetGroupRect(GanttGroup group, Rectangle bounds, DateTime start, DateTime end, int y)
        {
            var groupStart = group.StartDate.Value < start ? start : group.StartDate.Value;
            var groupEnd = group.EndDate.Value > end ? end : group.EndDate.Value;

            var x = GetXFromTime(groupStart, bounds, start, end);
            var width = Math.Max(8, GetXFromTime(groupEnd, bounds, start, end) - x);

            return new Rectangle(x, y + 6, width, _config.RowHeight - 12);
        }

        private void DrawRoundedRectangle(Graphics g, Brush brush, Pen pen, Rectangle rect, float radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
            path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();

            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        private int GetWeekNumber(DateTime date)
        {
            var culture = CultureInfo.GetCultureInfo("it-IT");
            return culture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        #endregion
    }
}