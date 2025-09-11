using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GanttChart
{
    public partial class GanttControl : UserControl
    {
        // --- in GanttControl ---
        internal void RegisterHitArea(string key, Rectangle rect) => _clickableAreas[key] = rect;

        internal bool IsTaskSelected(GanttTask t) => _selectedTaskId == t.Id;

        internal bool IsGroupSelected(GanttGroup g, GanttResource r) =>
            _selectedResourceId == r.Id && _selectedGroupId == g.Id && _selectedTaskId == null;

        internal int ScrollOffset => _scrollY;


        private readonly GanttConfig _config = new();
        private readonly GanttRenderer _renderer;
        private readonly List<GanttResource> _resources = new();
        private readonly Dictionary<string, Rectangle> _clickableAreas = new();

        private DateTime _viewStart = DateTime.Today;
        private GanttZoomLevel _zoomLevel = GanttZoomLevel.Week;
        private int _scrollY;
        private Bitmap _backBuffer;

        // Selezione
        private string _selectedResourceId;
        private string _selectedGroupId;
        private string _selectedTaskId;

        // Drag & Drop
        private bool _isDragging;
        private Point _dragStart;
        private GanttTask _dragTask;
        private DateTime _originalStart, _originalEnd;

        // Resize
        private bool _isResizing;
        private bool _resizeLeft;
        private const int RESIZE_MARGIN = 6;

        // Validazione
        public IGanttTaskValidator TaskValidator { get; set; }

        // Eventi
        public event EventHandler<GanttTaskChangedEventArgs> TaskChanged;
        public event EventHandler<GanttSelectionChangedEventArgs> SelectionChanged;
        public event EventHandler<GanttValidationEventArgs> TaskValidating;

        public GanttControl()
        {
            InitializeComponent();
            _renderer = new GanttRenderer(this);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.DoubleBuffer, true);
        }

        #region Proprietà pubbliche
        public GanttConfig Configuration => _config;
        public DateTime ViewStart
        {
            get => _viewStart;
            set { _viewStart = value; Invalidate(); }
        }

        public GanttZoomLevel ZoomLevel
        {
            get => _zoomLevel;
            set { _zoomLevel = value; Invalidate(); }
        }

        public List<GanttResource> Resources => _resources;
        #endregion

        #region API pubbliche
        public void LoadData(List<GanttResource> resources)
        {
            _resources.Clear();
            _resources.AddRange(resources);
            Invalidate();
        }

        public void AddTask(string resourceId, string groupId, GanttTask task)
        {
            var resource = _resources.FirstOrDefault(r => r.Id == resourceId);
            if (resource == null)
            {
                resource = new GanttResource { Id = resourceId, Name = resourceId };
                _resources.Add(resource);
            }

            var group = resource.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null)
            {
                group = new GanttGroup { Id = groupId, Name = groupId, ResourceId = resourceId };
                resource.Groups.Add(group);
            }

            task.ResourceId = resourceId;
            task.GroupId = groupId;
            group.Tasks.Add(task);
            SortTasksInGroup(group);
            Invalidate();
        }

        public void RemoveTask(string taskId)
        {
            foreach (var resource in _resources)
            {
                foreach (var group in resource.Groups)
                {
                    var task = group.Tasks.FirstOrDefault(t => t.Id == taskId);
                    if (task != null)
                    {
                        group.Tasks.Remove(task);
                        TaskChanged?.Invoke(this, new GanttTaskChangedEventArgs
                        {
                            Task = task,
                            ChangeType = "Delete"
                        });
                        Invalidate();
                        return;
                    }
                }
            }
        }

        public GanttTask GetTask(string taskId)
        {
            return _resources
                .SelectMany(r => r.Groups)
                .SelectMany(g => g.Tasks)
                .FirstOrDefault(t => t.Id == taskId);
        }

        public void SetSelection(string resourceId, string groupId = null, string taskId = null)
        {
            _selectedResourceId = resourceId;
            _selectedGroupId = groupId;
            _selectedTaskId = taskId;

            SelectionChanged?.Invoke(this, new GanttSelectionChangedEventArgs
            {
                ResourceId = resourceId,
                GroupId = groupId,
                TaskId = taskId
            });

            Invalidate();
        }
        #endregion

        #region Override eventi
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_backBuffer?.Size != Size)
            {
                _backBuffer?.Dispose();
                if (Width > 0 && Height > 0)
                    _backBuffer = new Bitmap(Width, Height);
            }

            if (_backBuffer != null)
            {
                using (var g = Graphics.FromImage(_backBuffer))
                {
                    _clickableAreas.Clear();
                    _renderer.Render(g, new Rectangle(0, 0, Width, Height));
                }
                e.Graphics.DrawImageUnscaled(_backBuffer, 0, 0);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            var hit = GetHitTest(e.Location);
            if (hit == null) return;

            if (hit.Type == "Task")
            {
                var task = GetTask(hit.Id);
                if (task != null)
                {
                    var taskRect = GetTaskRectangle(task);
                    bool nearLeft = Math.Abs(e.X - taskRect.Left) <= RESIZE_MARGIN;
                    bool nearRight = Math.Abs(e.X - taskRect.Right) <= RESIZE_MARGIN;

                    if ((nearLeft || nearRight) && _config.AllowResize)
                    {
                        StartResize(task, nearLeft);
                    }
                    else if (_config.AllowDrag)
                    {
                        StartDrag(task, e.Location);
                    }
                }
            }
            else if (hit.Type == "Resource" || hit.Type == "Group")
            {
                ToggleExpansion(hit.Type, hit.Id);
            }

            SetSelection(hit.ResourceId, hit.GroupId, hit.TaskId);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isResizing)
            {
                HandleResize(e.Location);
            }
            else if (_isDragging)
            {
                HandleDrag(e.Location);
            }
            else
            {
                // Cambia cursore su hover
                Cursor = GetHoverCursor(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isResizing || _isDragging)
            {
                FinishEdit();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (ModifierKeys.HasFlag(Keys.Control))
            {
                // Zoom
                if (e.Delta > 0) ZoomIn();
                else ZoomOut();
            }
            else
            {
                // Scroll
                _scrollY = Math.Max(0, _scrollY - e.Delta);
                Invalidate();
            }
        }
        #endregion

        #region Metodi interni
        private void InitializeComponent()
        {
            SuspendLayout();
            Name = "GanttControl";
            Size = new Size(800, 600);
            ResumeLayout(false);
        }

        private HitTestResult GetHitTest(Point location)
        {
            foreach (var area in _clickableAreas)
            {
                if (area.Value.Contains(location))
                {
                    return ParseHitKey(area.Key);
                }
            }
            return null;
        }

        private HitTestResult ParseHitKey(string key)
        {
            var parts = key.Split('|');
            return new HitTestResult
            {
                Type = parts[0],
                Id = parts[1],
                ResourceId = parts.Length > 2 ? parts[2] : null,
                GroupId = parts.Length > 3 ? parts[3] : null,
                TaskId = parts.Length > 4 ? parts[4] : null
            };
        }

        private Rectangle GetTaskRectangle(GanttTask task)
        {
            // Calcola rettangolo della task basandosi su zoom e posizione
            var timelineRect = GetTimelineRectangle();
            var (start, end) = GetTimeWindow();

            if (task.End <= start || task.Start >= end)
                return Rectangle.Empty;

            var startRatio = (task.Start - start).TotalMilliseconds / (end - start).TotalMilliseconds;
            var endRatio = (task.End - start).TotalMilliseconds / (end - start).TotalMilliseconds;

            var x = timelineRect.Left + (int)(startRatio * timelineRect.Width);
            var width = Math.Max(6, (int)((endRatio - startRatio) * timelineRect.Width));
            var y = GetTaskY(task);

            return new Rectangle(x, y, width, _config.RowHeight - 4);
        }

        private void StartDrag(GanttTask task, Point location)
        {
            _isDragging = true;
            _dragTask = task;
            _dragStart = location;
            _originalStart = task.Start;
            _originalEnd = task.End;
        }

        private void StartResize(GanttTask task, bool resizeLeft)
        {
            _isResizing = true;
            _resizeLeft = resizeLeft;
            _dragTask = task;
            _originalStart = task.Start;
            _originalEnd = task.End;
            _dragStart = PointToClient(MousePosition); // <— AGGIUNGI QUESTO
        }

        private void HandleDrag(Point location)
        {
            if (_dragTask == null) return;

            var deltaX = location.X - _dragStart.X;
            var timeDelta = GetTimeDeltaFromPixels(deltaX);

            var newStart = _originalStart.Add(timeDelta);
            var newEnd = _originalEnd.Add(timeDelta);

            if (ValidateTaskPosition(_dragTask, newStart, newEnd))
            {
                _dragTask.Start = SnapToGrid(newStart);
                _dragTask.End = SnapToGrid(newEnd);
                Invalidate();
            }
        }

        private void HandleResize(Point location)
        {
            if (_dragTask == null) return;

            var deltaX = location.X - _dragStart.X;
            var timeDelta = GetTimeDeltaFromPixels(deltaX);

            DateTime newStart = _originalStart;
            DateTime newEnd = _originalEnd;

            if (_resizeLeft)
                newStart = _originalStart.Add(timeDelta);
            else
                newEnd = _originalEnd.Add(timeDelta);

            // Minimo 5 minuti
            if ((newEnd - newStart).TotalMinutes < 5)
            {
                if (_resizeLeft) newStart = newEnd.AddMinutes(-5);
                else newEnd = newStart.AddMinutes(5);
            }

            if (ValidateTaskPosition(_dragTask, newStart, newEnd))
            {
                _dragTask.Start = SnapToGrid(newStart);
                _dragTask.End = SnapToGrid(newEnd);
                Invalidate();
            }
        }

        private void FinishEdit()
        {
            if (_dragTask != null)
            {
                TaskChanged?.Invoke(this, new GanttTaskChangedEventArgs
                {
                    Task = _dragTask,
                    ChangeType = _isResizing ? "Resize" : "Move"
                });

                var group = _resources
                    .FirstOrDefault(r => r.Id == _dragTask.ResourceId)
                    ?.Groups.FirstOrDefault(g => g.Id == _dragTask.GroupId);

                if (group != null)
                    SortTasksInGroup(group);
            }

            _isDragging = false;
            _isResizing = false;
            _dragTask = null;
            Cursor = Cursors.Default;
        }

        private bool ValidateTaskPosition(GanttTask task, DateTime start, DateTime end)
        {
            var args = new GanttValidationEventArgs
            {
                Task = task,
                ProposedStart = start,
                ProposedEnd = end,
                IsValid = true
            };

            TaskValidating?.Invoke(this, args);

            if (!args.IsValid) return false;

            return TaskValidator?.ValidateTaskPlacement(task, start, end, out _) ?? true;
        }

        private void SortTasksInGroup(GanttGroup group)
        {
            group.Tasks.Sort((a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                return priorityCompare != 0 ? priorityCompare : a.Start.CompareTo(b.Start);
            });
        }

        private void ToggleExpansion(string type, string id)
        {
            if (type == "Resource")
            {
                var resource = _resources.FirstOrDefault(r => r.Id == id);
                if (resource != null)
                {
                    resource.IsExpanded = !resource.IsExpanded;
                    Invalidate();
                }
            }
            else if (type == "Group")
            {
                foreach (var resource in _resources)
                {
                    var group = resource.Groups.FirstOrDefault(g => g.Id == id);
                    if (group != null)
                    {
                        group.IsExpanded = !group.IsExpanded;
                        Invalidate();
                        break;
                    }
                }
            }
        }

        private void ZoomIn()
        {
            if (_zoomLevel > GanttZoomLevel.Hour)
            {
                _zoomLevel--;
                Invalidate();
            }
        }

        private void ZoomOut()
        {
            if (_zoomLevel < GanttZoomLevel.Year)
            {
                _zoomLevel++;
                Invalidate();
            }
        }

        private Cursor GetHoverCursor(Point location)
        {
            var hit = GetHitTest(location);
            if (hit?.Type == "Task")
            {
                var task = GetTask(hit.Id);
                if (task != null)
                {
                    var rect = GetTaskRectangle(task);
                    if (Math.Abs(location.X - rect.Left) <= RESIZE_MARGIN ||
                        Math.Abs(location.X - rect.Right) <= RESIZE_MARGIN)
                        return Cursors.SizeWE;
                }
            }
            return Cursors.Default;
        }

        private DateTime SnapToGrid(DateTime time)
        {
            var minutes = (time.Minute / _config.SnapMinutes) * _config.SnapMinutes;
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, minutes, 0);
        }

        private TimeSpan GetTimeDeltaFromPixels(int deltaX)
        {
            var timelineRect = GetTimelineRectangle();
            if (timelineRect.Width <= 0) return TimeSpan.Zero;

            var (start, end) = GetTimeWindow();
            var totalTime = end - start;
            var timePerPixel = totalTime.TotalMilliseconds / timelineRect.Width;
            return TimeSpan.FromMilliseconds(deltaX * timePerPixel);
        }

        private Rectangle GetTimelineRectangle()
        {
            return new Rectangle(_config.LeftPanelWidth, _config.HeaderHeight + _config.MinorHeaderHeight,
                               Width - _config.LeftPanelWidth, Height - _config.HeaderHeight - _config.MinorHeaderHeight);
        }

        private (DateTime start, DateTime end) GetTimeWindow()
        {
            return _zoomLevel switch
            {
                GanttZoomLevel.Hour => (_viewStart, _viewStart.AddHours(1)),
                GanttZoomLevel.Day => (_viewStart, _viewStart.AddDays(1)),
                GanttZoomLevel.Week => (_viewStart, _viewStart.AddDays(7)),
                GanttZoomLevel.Month => (_viewStart, _viewStart.AddMonths(1)),
                GanttZoomLevel.Year => (_viewStart, _viewStart.AddYears(1)),
                _ => (_viewStart, _viewStart.AddDays(7))
            };
        }

        private int GetTaskY(GanttTask task)
        {
            int y = _config.HeaderHeight + _config.MinorHeaderHeight - _scrollY;

            foreach (var resource in _resources)
            {
                y += _config.RowHeight + _config.RowGap;

                if (!resource.IsExpanded) continue;

                foreach (var group in resource.Groups)
                {
                    y += _config.RowHeight + _config.RowGap;

                    if (!group.IsExpanded) continue;

                    foreach (var t in group.Tasks)
                    {
                        if (t.Id == task.Id) return y;
                        y += _config.RowHeight + _config.RowGap;
                    }
                }
            }

            return y;
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _backBuffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class HitTestResult
    {
        public string Type { get; set; }
        public string Id { get; set; }
        public string ResourceId { get; set; }
        public string GroupId { get; set; }
        public string TaskId { get; set; }
    }
}