using System;
using System.Collections.Generic;
using System.Linq;

namespace GanttChart
{
    // ===== MODELLI DATI =====
    public class GanttTask
    {
        public string Id { get; set; }
        public string ResourceId { get; set; }  // Operatore/Risorsa
        public string GroupId { get; set; }     // Progetto/Gruppo
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool IsCompleted { get; set; }
        public int Priority { get; set; } = 0;  // Per ordinamento
        public Dictionary<string, object> CustomData { get; set; } = new();

        public TimeSpan Duration => End - Start;
    }

    public class GanttGroup
    {
        public string Id { get; set; }
        public string ResourceId { get; set; }
        public string Name { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<GanttTask> Tasks { get; set; } = new();
        public Dictionary<string, object> CustomData { get; set; } = new();

        public DateTime? StartDate => Tasks.Count > 0 ? Tasks.Min(t => t.Start) : null;
        public DateTime? EndDate => Tasks.Count > 0 ? Tasks.Max(t => t.End) : null;
        public TimeSpan TotalDuration => TimeSpan.FromTicks(Tasks.Sum(t => t.Duration.Ticks));
    }

    public class GanttResource
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<GanttGroup> Groups { get; set; } = new();
        public Dictionary<string, object> CustomData { get; set; } = new();
    }

    // ===== EVENTI =====
    public class GanttTaskChangedEventArgs : EventArgs
    {
        public GanttTask Task { get; set; }
        public string ChangeType { get; set; } // "Move", "Resize", "Create", "Delete"
    }

    public class GanttSelectionChangedEventArgs : EventArgs
    {
        public string ResourceId { get; set; }
        public string GroupId { get; set; }
        public string TaskId { get; set; }
    }

    public class GanttValidationEventArgs : EventArgs
    {
        public GanttTask Task { get; set; }
        public DateTime ProposedStart { get; set; }
        public DateTime ProposedEnd { get; set; }
        public bool IsValid { get; set; } = true;
        public string ErrorMessage { get; set; }
    }

    // ===== CONFIGURAZIONE =====
    public class GanttConfig
    {
        public int RowHeight { get; set; } = 26;
        public int RowGap { get; set; } = 4;
        public int LeftPanelWidth { get; set; } = 200;
        public int HeaderHeight { get; set; } = 28;
        public int MinorHeaderHeight { get; set; } = 18;
        public int SnapMinutes { get; set; } = 5;
        public bool AllowResize { get; set; } = true;
        public bool AllowDrag { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowNowLine { get; set; } = true;
    }

    // ===== ZOOM LEVELS =====
    public enum GanttZoomLevel
    {
        Hour,
        Day,
        Week,
        Month,
        Year
    }

    // ===== INTERFACCE PER ESTENSIBILITÀ =====
    public interface IGanttTaskValidator
    {
        bool ValidateTaskPlacement(GanttTask task, DateTime start, DateTime end, out string errorMessage);
    }

    public interface IGanttRenderer
    {
        void DrawLeftPanel(System.Drawing.Graphics g, System.Drawing.Rectangle bounds);
        void DrawHeader(System.Drawing.Graphics g, System.Drawing.Rectangle bounds);
        void DrawBody(System.Drawing.Graphics g, System.Drawing.Rectangle bounds);
    }

    public interface IGanttDataProvider
    {
        List<GanttResource> GetResources();
        void SaveTask(GanttTask task);
        void DeleteTask(string taskId);
    }
}
