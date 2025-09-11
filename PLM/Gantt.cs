using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms; // per Keys, Control.ModifierKeys
using static PLM.Classe;

namespace PLM
{
    // ======== Gantt ========
    public class Gantt
    {
        #region Modelli & stato
        private enum Level { Year = 0, Month = 1, Week = 2, Day = 3, Hour = 4 }
        private Level _zoomLevel = Level.Week;

        // Vista giornaliera/oraria e snap
        private int _dailySnapMinutes = 5;
        public void SetDailySnapMinutes(int minutes) => _dailySnapMinutes = Math.Max(1, minutes);

        // Wrapper pubblico: controlla se posso piazzare una fase nel progetto rispettando le precedenze
        public bool CanPlaceByPrecedence(string projectCode, string taskName, DateTime start, TimeSpan duration, out DateTime minStartAllowed)
        {
            var end = SnapEnd(start + duration);
            return IsPlacementValidByPrecedence(projectCode, taskName, start, end, out minStartAllowed);
        }


        // === ORDINAMENTO DI VISUALIZZAZIONE ===
        // Gruppi: 0=3D, 1=(2D|Distinta), 2=Stampare, 3=altro
        private static int DisplayOrderGroup(string taskName)
        {
            if (string.Equals(taskName, "Disegno 3D", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(taskName, "Disegno 2D", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(taskName, "Distinta", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(taskName, "Stampare", StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }

        private static IEnumerable<TaskItem> GetDisplayTasks(ProjectRow pr)
        {
            // Ordina PRIMA per gruppo (3D → 2D/Distinta → Stampare), POI per Start (timeline),
            // e infine per indice logico come tie-breaker stabile.
            return pr.Tasks
                     .OrderBy(t => DisplayOrderGroup(t.TaskName))
                     .ThenBy(t => t.Start)
                     .ThenBy(t => OrderIndex(t.TaskName))
                     .ThenBy(t => t.End);
        }

        // viewport
        public DateTime Reference { get; private set; } = DateTime.Today;
        public int LeftLabelWidth { get; set; } = 200;
        public int HeaderHeight { get; set; } = 28;
        public int MinorHeaderHeight { get; set; } = 18;
        public int RowHeight { get; set; } = 26;
        public int RowGap { get; set; } = 4;

        private Rectangle _panel, _left, _header, _body;
        private DateTime _winStart, _winEnd;
        private double _pxPerUnit;
        private int _vScroll, _contentHeight;

        // Inserimento esterno (drag da DGV nel Gantt)
        private bool _inserting;
        private string _insertOpId, _insertProj, _insertTaskName;
        private TimeSpan _insertDuration;

        // snapshot per la cascata durante il RESIZE (tutte le task dello stesso operatore che iniziano dopo l'end originale)
        private List<(TaskItem task, DateTime s0, DateTime e0)> _resizeOpTailSnapshot;

        // anti-flicker
        private Bitmap _backBuffer;

        // === Preview dragging/inserting (solo move/insert, NO resize) ===
        private Rectangle? _previewOkRect;     // area verde (consentita)
        private Rectangle? _previewNoRect;     // area rossa tratteggiata (non consentita)
        private Rectangle? _previewDurRect;    // area grigia (durata prevista)
        private bool _previewActive;           // true solo durante drag/insert (non resize)

        // ===== HIERARCHY =====
        private class TaskItem
        {
            public string Id;              // <-- tienilo come "ID esterno vero"
            public string ProjectCode;
            public string ProjectId;       // (opzionale, se hai un ID progetto reale)
            public string TaskName;
            public string OpId;
            public DateTime Start;
            public DateTime End;
            public bool Completed;
        }

        private class ProjectRow
        {
            public string Code;
            public bool Expanded;
            public readonly List<TaskItem> Tasks = new();
            public DateTime? SummaryStart => Tasks.Count == 0 ? (DateTime?)null : Tasks.Min(t => t.Start);
            public DateTime? SummaryEnd => Tasks.Count == 0 ? (DateTime?)null : Tasks.Max(t => t.End);

            // NEW: durata totale progetto
            public TimeSpan TotalDuration => TimeSpan.FromTicks(Tasks.Sum(t => (t.End - t.Start).Ticks));
        }

        private class OperatorRow
        {
            public string Id;
            public string Label;
            public bool Expanded;
            public readonly List<ProjectRow> Projects = new();
        }

        private readonly List<OperatorRow> _ops = new();

        private readonly Dictionary<string, Rectangle> _barRects = new();
        private readonly Dictionary<int, (string kind, string key, Rectangle rect)> _rowsLeft = new();

        private static readonly string[] TaskOrder = new[]
        {
            "Disegno 3D",
            "Distinta",
            "Disegno 2D",
            "Stampare"
        };

        // === Rank fasi: 3D = 0, 2D/Distinta = 1, Stampare = 2 ===
        private static int PhaseRank(string taskName)
        {
            if (string.Equals(taskName, "Disegno 3D", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(taskName, "Disegno 2D", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(taskName, "Distinta", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(taskName, "Stampare", StringComparison.OrdinalIgnoreCase)) return 2;
            return 99;
        }
        private static int OrderIndex(string taskName)
        {
            int i = Array.IndexOf(TaskOrder, taskName);
            return i < 0 ? int.MaxValue : i;
        }

        // ===== Drag stato =====
        private bool _dragging;
        private string _dragKey; // "TASK|op|proj|taskName" oppure "SUM|op|proj"
        private Point _dragStartMouse;
        private DateTime _dragOrigStart, _dragOrigEnd;

        // ==== RESIZE stato ====
        private bool _resizing;
        private bool _resizeLeft; // true = sto ridimensionando il bordo sinistro, false = destro
        private const int RESIZE_EDGE_PX = 6; // larghezza "hot zone" sui bordi per iniziare il resize
        private const int RESIZE_STEP_MINUTES = 5; // snap del resize a 5 minuti
        private static readonly TimeSpan MIN_RESIZE_DURATION = TimeSpan.FromMinutes(5); // durata minima barra

        // drag progetto intero
        private bool _dragProject;
        private List<(TaskItem task, DateTime s0, DateTime e0)> _dragProjSnapshot;

        // cascata per operatore (successive alla task)
        private List<(TaskItem task, DateTime s0, DateTime e0)> _dragOpTailSnapshot;

        // ==== catene agganciate e isteresi direzione ====
        private const int LINK_TOL_MINUTES = 1; // tolleranza per considerare "agganciati"
        private List<(TaskItem task, DateTime s0, DateTime e0)> _dragLinkedTailSnapshot;
        private List<(ProjectRow proj, List<(TaskItem task, DateTime s0, DateTime e0)> snapTasks)> _dragProjChainSnapshot;
        private int _dragDirSign; // isteresi: direzione stabilizzata del drag

        // magnet/aggancio (solo stesso operatore)
        private const int MAGNET_PX = 8;
        private (Rectangle edgeRect, DateTime t0)? _magnetFlash;

        // hard no-overlap helper
        private const int HARD_SNAP_MARGIN_MINUTES = 0;

        // anti-overlap più fluido
        private DateTime _lastAvoidOverlapAt;
        private const int AVOID_DELAY_MS = 120;

        // evento: modifica pianificazione
        public class PlanningChangedArgs
        {
            public string Id { get; set; }           // NEW: ID vero dell’attività
            public string ProjectId { get; set; }    // NEW: opzionale, ID progetto
            public string JobId { get; set; }        // Legacy (codice+fase) se ti serve ancora
            public string ProjectCode { get; set; }
            public string Operatore { get; set; }
            public string TaskName { get; set; }
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
        }

        public event Action<PlanningChangedArgs> PlanningChanged;

        // ======= Selezione & evento =======
        public class SelectionChangedArgs
        {
            public string SelectedOperatorId { get; set; }
            public string SelectedProjectCode { get; set; }
            public string SelectedTaskName { get; set; }
        }
        public event Action<SelectionChangedArgs> SelectionChanged;

        private string _selOpId = null;
        private string _selProj = null;
        private string _selTask = null;

        // ======= Selezione multipla & delete =======
        private readonly HashSet<string> _multiSel = new(StringComparer.Ordinal);

        public class ItemsDeletedArgs
        {
            public List<(string Id, string ProjectId, string JobId, string ProjectCode, string Operatore, string TaskName)> Items { get; set; } = new();
        }

        public event Action<ItemsDeletedArgs> ItemsDeleted;

        public SelectionChangedArgs CurrentSelection => new SelectionChangedArgs
        {
            SelectedOperatorId = _selOpId,
            SelectedProjectCode = _selProj,
            SelectedTaskName = _selTask
        };
        private void SetSelection(string opId, string proj, string task)
        {
            _selOpId = opId;
            _selProj = proj;
            _selTask = task;
            SelectionChanged?.Invoke(CurrentSelection);
        }
        private void ClearMultiSelection() => _multiSel.Clear();
        private void ToggleSelectionKey(string key)
        {
            if (_multiSel.Contains(key)) _multiSel.Remove(key);
            else _multiSel.Add(key);
        }
        public IReadOnlyCollection<string> GetSelectedKeys() => _multiSel;

        // === BOX SELECT (rettangolo di selezione)
        private bool _boxSelecting;
        private Point _boxStart;
        private Point _boxCurrent;
        #endregion

        #region Caricamento dati
        public void LoadFromUfficioTecnico(IEnumerable<Lavorazioni_UfficioTecnico> lavorazioniUT,
                                     IEnumerable<Operatore> listaOperatori)
        {
            _allTasks.Clear();
            _ops.Clear();

            string S(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj) as string;
            DateTime? D(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj) as DateTime?;
            double? F(object obj, string name)
            {
                var p = obj.GetType().GetProperty(name);
                if (p == null) return null;
                var v = p.GetValue(obj);
                return v is double d ? d : v as double?;
            }

            // === 1) Costruisci TUTTE le task con chiave PROGETTO COMPOSITA e ID reali ===
            foreach (var ut in lavorazioniUT ?? Enumerable.Empty<Lavorazioni_UfficioTecnico>())
            {
                var cod = string.IsNullOrWhiteSpace(ut.codiceProgetto) ? "N/D" : ut.codiceProgetto;
                string projectKey = $"{cod}__ATT:{ut.ID_Attivita}";
                string projectId = ut.ID_Attivita.ToString();

                TryAdd(projectKey, "3D", "Disegno 3D",
                    ut.Disegno_3D_Assegnato,
                    S(ut, "Operatore_Disegno_3D_Assegnato"),
                    D(ut, "Data_Disegno_3D_Assegnato_InizioPrevista"),
                    D(ut, "Data_Disegno_3D_Assegnato_FinePrevista"),
                    F(ut, "Tempo_Disegno_3D_Prevista"),
                    ut.Disegno_3D_Fatto,
                    realTaskId: $"{projectKey}-3D",
                    projectId: projectId);

                TryAdd(projectKey, "DST", "Distinta",
                    ut.Distinta_Assegnato,
                    S(ut, "Operatore_Distinta_Assegnato"),
                    D(ut, "Data_Distinta_Assegnato_InizioPrevista"),
                    D(ut, "Data_Distinta_Assegnato_FinePrevista"),
                    F(ut, "Tempo_Distinta_Prevista"),
                    ut.Distinta_Fatto,
                    realTaskId: $"{projectKey}-DST",
                    projectId: projectId);

                TryAdd(projectKey, "2D", "Disegno 2D",
                    ut.Disegno_2D_Assegnato,
                    ut.Operatore_Disegno_2D_Assegnato,
                    ut.Data_Disegno_2D_Assegnato_InizioPrevista,
                    ut.Data_Disegno_2D_Assegnato_FinePrevista,
                    ut.Tempo_Disegno_2D_Prevista,
                    ut.Disegno_2D_Fatto,
                    realTaskId: $"{projectKey}-2D",
                    projectId: projectId);

                // Stampare: compatibile con entrambe le versioni (nuova/legacy 2D)
                var prnAssigned = ut.Stampare_2D_Assegnato ||
                                  (bool)(ut.GetType().GetProperty("Stampare_Assegnato")?.GetValue(ut) ?? false);

                TryAdd(projectKey, "PRN", "Stampare",
                    prnAssigned,
                    S(ut, "Operatore_Stampare_Assegnato") ?? S(ut, "Operatore_Stampare_2D_Assegnato"),
                    D(ut, "Data_Stampare_Assegnato_InizioPrevista") ?? D(ut, "Data_Stampare_2D_Assegnato_InizioPrevista"),
                    D(ut, "Data_Stampare_Assegnato_FinePrevista") ?? D(ut, "Data_Stampare_2D_Assegnato_FinePrevista"),
                    F(ut, "Tempo_Stampare_Prevista") ?? F(ut, "Tempo_Stampare_2D_Prevista"),
                    ut.Stampare_2D_Fatto || (bool)(ut.GetType().GetProperty("Stampare_Fatto")?.GetValue(ut) ?? false),
                    realTaskId: $"{projectKey}-PRN",
                    projectId: projectId);
            }

            // === 2) Lista operatori = unione tra anagrafica e operatori presenti nelle task ===
            var opNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in (listaOperatori ?? Enumerable.Empty<Operatore>())
                             .Where(o => !string.IsNullOrWhiteSpace(o?.nomeCognome))
                             .Select(o => o.nomeCognome))
                opNames.Add(o);
            foreach (var n in _allTasks.Select(t => t.OpId).Where(s => !string.IsNullOrWhiteSpace(s)))
                opNames.Add(n);

            foreach (var name in opNames.OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
                _ops.Add(new OperatorRow { Id = name, Label = name });

            // === 3) Popola i progetti per ogni operatore a partire da _allTasks ===
            foreach (var op in _ops)
            {
                var tasks = _allTasks.Where(t => string.Equals(t.OpId, op.Id, StringComparison.OrdinalIgnoreCase))
                                     .GroupBy(t => t.ProjectCode, StringComparer.OrdinalIgnoreCase);

                foreach (var g in tasks)
                {
                    var pr = new ProjectRow { Code = g.Key };
                    pr.Tasks.AddRange(g.OrderBy(t => PhaseRank(t.TaskName)).ThenBy(t => t.Start));
                    op.Projects.Add(pr);
                }

                op.Projects.Sort((a, b) =>
                {
                    var k1 = a.SummaryStart ?? DateTime.MaxValue;
                    var k2 = b.SummaryStart ?? DateTime.MaxValue;
                    int cmp = k1.CompareTo(k2);
                    if (cmp != 0) return cmp;
                    return StringComparer.CurrentCultureIgnoreCase.Compare(a.Code, b.Code);
                });
            }
        }



        private readonly List<TaskItem> _allTasks = new();

        private void TryAdd(
      string cod,
      string suffix,
      string taskName,
      bool assigned,
      string op,
      DateTime? start,
      DateTime? end,
      double? ore,
      bool completed,
      string realTaskId = null,   // opzionale
      string projectId = null    // opzionale
  )
        {
            if (!assigned) return;
            if (string.IsNullOrWhiteSpace(op)) return;
            if (!start.HasValue && !end.HasValue && !ore.HasValue) return;

            var se = ResolveDatesStrict(start, end, ore);
            if (se == null) return;
            var (s, e) = se.Value;
            if (e <= s) return;

            _allTasks.Add(new TaskItem
            {
                Id = string.IsNullOrWhiteSpace(realTaskId) ? $"{cod}-{suffix}" : realTaskId,
                ProjectCode = cod,
                ProjectId = projectId,     // può rimanere null se non la usi
                TaskName = taskName,
                OpId = op,
                Start = s,
                End = e,
                Completed = completed
            });
        }


        private static (DateTime start, DateTime end)? ResolveDatesStrict(DateTime? start, DateTime? end, double? ore)
        {
            if (start.HasValue && end.HasValue) return (start.Value, end.Value);
            if (start.HasValue && ore.HasValue) return (start.Value, start.Value.AddHours(ore.Value));
            return null;
        }
        #endregion

        #region API vista
        public void SetReference(DateTime reference) { Reference = reference; RefreshLayout(); }

        public void SetViewFromCombo(string testoCombo)
        {
            switch ((testoCombo ?? "").Trim().ToLowerInvariant())
            {
                case "oraria":
                    _zoomLevel = Level.Hour;
                    Reference = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
                    break;
                case "giornaliera":
                    _zoomLevel = Level.Day;
                    break;
                case "settimanale":
                    _zoomLevel = Level.Week;
                    break;
                case "mensile":
                    _zoomLevel = Level.Month;
                    break;
                case "annuale":
                    _zoomLevel = Level.Year;
                    break;
                default:
                    _zoomLevel = Level.Week;
                    break;
            }
            RefreshLayout();
            NotifyViewChanged();
        }


        public void RefreshLayout()
        {
            if (_body.Width <= 0 || _body.Height <= 0) return;
            ComputeWindow();
            ComputeContentHeight();
        }

        public void GoNext()
        {
            Reference = _zoomLevel switch
            {
                Level.Hour => Reference.AddHours(1),
                Level.Day => Reference.AddDays(1),
                Level.Week => Reference.AddDays(7),
                Level.Month => Reference.AddMonths(1),
                Level.Year => Reference.AddYears(1),
                _ => Reference
            };
            RefreshLayout();
        }
        public void GoPrev()
        {
            Reference = _zoomLevel switch
            {
                Level.Hour => Reference.AddHours(-1),
                Level.Day => Reference.AddDays(-1),
                Level.Week => Reference.AddDays(-7),
                Level.Month => Reference.AddMonths(-1),
                Level.Year => Reference.AddYears(-1),
                _ => Reference
            };
            RefreshLayout();
        }

        // ► Sync con UI (ComboBox)
        public event Action<string> ViewChanged;
        private string CurrentViewName => _zoomLevel switch
        {
            Level.Hour => "Oraria",
            Level.Day => "Giornaliera",
            Level.Week => "Settimanale",
            Level.Month => "Mensile",
            Level.Year => "Annuale",
            _ => "Settimanale"
        };
        private void NotifyViewChanged() => ViewChanged?.Invoke(CurrentViewName);
        #endregion

        #region Form wrappers
        public void OnResize(Rectangle panelClientRect)
        {
            _panel = panelClientRect;
            _left = new Rectangle(_panel.Left, _panel.Top, LeftLabelWidth, _panel.Height);
            _header = new Rectangle(_left.Right, _panel.Top, Math.Max(0, _panel.Width - _left.Width), HeaderHeight + MinorHeaderHeight);
            _body = new Rectangle(_left.Right, _header.Bottom, _header.Width, Math.Max(0, _panel.Height - _header.Bottom));

            _backBuffer?.Dispose();
            if (_panel.Width > 0 && _panel.Height > 0)
                _backBuffer = new Bitmap(_panel.Width, _panel.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            RefreshLayout();
        }

        public void OnPaint(Graphics g)
        {
            if (_backBuffer == null)
            {
                g.Clear(Color.FromArgb(36, 36, 36));
                DrawLeft(g); DrawHeader(g); DrawBody(g);
                return;
            }
            using (var gg = Graphics.FromImage(_backBuffer))
            {
                _barRects.Clear();
                _rowsLeft.Clear();
                gg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                gg.Clear(Color.FromArgb(36, 36, 36));

                DrawLeft(gg);
                DrawHeader(gg);
                DrawBody(gg);

                // === BOX SELECT overlay
                if (_boxSelecting)
                {
                    var rect = GetBoxRect();
                    using var fill = new SolidBrush(Color.FromArgb(60, 140, 180, 255));
                    using var pen = new Pen(Color.FromArgb(180, 140, 180, 255), 1f);
                    gg.FillRectangle(fill, rect);
                    gg.DrawRectangle(pen, rect);
                }

                // === PREVIEW overlay (durata grigia, verde OK, rosso tratteggiato) ===
                if (_previewActive)
                {
                    // Durata prevista (grigio trasparente)
                    if (_previewDurRect.HasValue && !_previewDurRect.Value.IsEmpty)
                    {
                        using var brDur = new SolidBrush(Color.FromArgb(60, 180, 180, 180));
                        gg.FillRectangle(brDur, _previewDurRect.Value);
                    }

                    // Zona valida (verde trasparente)
                    if (_previewOkRect.HasValue && !_previewOkRect.Value.IsEmpty)
                    {
                        using var brOk = new SolidBrush(Color.FromArgb(70, 60, 200, 120));
                        gg.FillRectangle(brOk, _previewOkRect.Value);
                        using var penOk = new Pen(Color.FromArgb(120, 60, 200, 120), 1f);
                        gg.DrawRectangle(penOk, _previewOkRect.Value);
                    }

                    // Zona non valida (rosso tratteggiato / tratteggi diagonali)
                    if (_previewNoRect.HasValue && !_previewNoRect.Value.IsEmpty)
                    {
                        using var hatch = new System.Drawing.Drawing2D.HatchBrush(
                            System.Drawing.Drawing2D.HatchStyle.ForwardDiagonal,
                            Color.FromArgb(160, 220, 80, 80), Color.FromArgb(50, 0, 0, 0));
                        gg.FillRectangle(hatch, _previewNoRect.Value);
                        using var penNo = new Pen(Color.FromArgb(200, 200, 60, 60), 1f);
                        gg.DrawRectangle(penNo, _previewNoRect.Value);
                    }
                }
            }
            g.DrawImageUnscaled(_backBuffer, 0, 0);
        }


        public void OnMouseWheel(Point location, int delta, bool ctrl, bool shift)
        {
            if (ctrl)
            {
                var old = _zoomLevel;
                if (delta > 0) ZoomIn(location); else ZoomOut(location);
                if (old != _zoomLevel) NotifyViewChanged();
            }
            else
            {
                _vScroll = Math.Max(0, Math.Min(_vScroll - delta, Math.Max(0, _contentHeight - _body.Height)));
            }
        }

        public void OnMouseDown(Point location, System.Windows.Forms.MouseButtons button)
        {
            if (button != System.Windows.Forms.MouseButtons.Left) return;

            // === BOX SELECT: se SHIFT e click nel corpo, inizia rettangolo se non clicchi su una barra
            bool shiftDown = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

            if (_body.Contains(location))
            {
                var hit = _barRects.FirstOrDefault(kv => kv.Value.Contains(location));

                if (shiftDown && string.IsNullOrEmpty(hit.Key))
                {
                    // avvia box select
                    ClearMultiSelection();
                    SetSelection(null, null, null);
                    _boxSelecting = true;
                    _boxStart = _boxCurrent = location;
                    return;
                }

                if (!string.IsNullOrEmpty(hit.Key))
                {
                    bool ctrlDown = (Control.ModifierKeys & Keys.Control) == Keys.Control;

                    // ►► RESIZE: se colpisco una TASK e sono a bordo sinistro/destro → inizia resize
                    if (hit.Key.StartsWith("TASK|", StringComparison.Ordinal))
                    {
                        var rect = hit.Value;
                        bool nearLeft = Math.Abs(location.X - rect.Left) <= RESIZE_EDGE_PX;
                        bool nearRight = Math.Abs(location.X - rect.Right) <= RESIZE_EDGE_PX;

                        var (opId_t, proj_t, taskName_t) = ParseTaskKey(hit.Key);
                        var tSel = FindTask(opId_t, proj_t, taskName_t);

                        if (nearLeft || nearRight)
                        {
                            // priorità al resize rispetto al toggle multi con CTRL
                            ClearMultiSelection();
                            SetSelection(opId_t, proj_t, taskName_t);

                            _resizing = true;
                            _resizeLeft = nearLeft;
                            _dragKey = hit.Key;
                            _dragStartMouse = location;
                            _dragOrigStart = tSel.Start;
                            _dragOrigEnd = tSel.End;

                            // snapshot della coda dell'operatore (tutte le task che iniziano dopo l'end ORIGINALE)
                            _resizeOpTailSnapshot = _ops.Where(o => o.Id == opId_t)
                                .SelectMany(o => o.Projects)
                                .SelectMany(p => p.Tasks)
                                .Where(ti => !ReferenceEquals(ti, tSel) && ti.Start >= _dragOrigEnd)
                                .OrderBy(ti => ti.Start)
                                .Select(ti => (ti, ti.Start, ti.End))
                                .ToList();

                            _magnetFlash = null;
                            return;
                        }
                    }

                    // ►► Drag normale (task o summary)
                    if (hit.Key.StartsWith("TASK|", StringComparison.Ordinal))
                    {
                        var (opId_t, proj_t, taskName_t) = ParseTaskKey(hit.Key);

                        if (ctrlDown)
                        {
                            ToggleSelectionKey(hit.Key);
                            SetSelection(opId_t, proj_t, taskName_t);
                            return; // niente drag su toggle multi
                        }
                        else
                        {
                            ClearMultiSelection();
                            SetSelection(opId_t, proj_t, taskName_t);

                            _dragging = true;
                            _dragProject = false;
                            _dragKey = hit.Key;
                            _dragStartMouse = location;

                            var tSel = FindTask(opId_t, proj_t, taskName_t);
                            _dragOrigStart = tSel.Start;
                            _dragOrigEnd = tSel.End;

                            // snapshot del tail operatore: tutte le task che iniziano dopo questa
                            _dragOpTailSnapshot = _ops.Where(o => o.Id == opId_t)
                                .SelectMany(o => o.Projects)
                                .SelectMany(p => p.Tasks)
                                .Where(ti => !ReferenceEquals(ti, tSel) && ti.Start >= _dragOrigEnd)
                                .OrderBy(ti => ti.Start)
                                .Select(ti => (ti, ti.Start, ti.End))
                                .ToList();

                            // catena “agganciata” successiva se questa task è head
                            _dragLinkedTailSnapshot = null;
                            _dragDirSign = 0;
                            var chain = BuildForwardLinkedTasks(opId_t, tSel);
                            if (chain.Count > 1 && IsHeadTask(opId_t, tSel))
                            {
                                _dragLinkedTailSnapshot = chain.Skip(1)
                                    .Select(ti => (ti, ti.Start, ti.End))
                                    .ToList();
                            }
                            else
                            {
                                _dragLinkedTailSnapshot = new();
                            }

                            _magnetFlash = null;
                            _lastAvoidOverlapAt = DateTime.UtcNow;
                            return;
                        }
                    }
                    else if (hit.Key.StartsWith("SUM|", StringComparison.Ordinal))
                    {
                        var sp = hit.Key.Split('|'); // SUM|op|proj
                        var opId_s = sp[1]; var proj_s = sp[2];

                        if (ctrlDown)
                        {
                            ToggleSelectionKey(hit.Key);
                            SetSelection(opId_s, proj_s, null);
                            return; // niente drag su toggle multi
                        }
                        else
                        {
                            ClearMultiSelection();
                            SetSelection(opId_s, proj_s, null);

                            _dragging = true;
                            _dragProject = true;
                            _dragKey = hit.Key;
                            _dragStartMouse = location;

                            var pr = _ops.First(o => o.Id == opId_s).Projects.First(p => p.Code == proj_s);
                            _dragProjSnapshot = pr.Tasks
                                .Select(ti => (ti, ti.Start, ti.End))
                                .ToList();

                            // catena di progetti agganciati successivi se questo è head
                            _dragProjChainSnapshot = null;
                            _dragDirSign = 0;
                            var chainProj = BuildForwardLinkedProjects(opId_s, pr);
                            if (chainProj.Count > 1 && IsHeadProject(opId_s, pr))
                            {
                                _dragProjChainSnapshot = chainProj.Skip(1)
                                    .Select(pj => (pj, pj.Tasks.Select(ti => (ti, ti.Start, ti.End)).ToList()))
                                    .ToList();
                            }
                            else
                            {
                                _dragProjChainSnapshot = new();
                            }

                            _magnetFlash = null;
                            _lastAvoidOverlapAt = DateTime.UtcNow;
                            return;
                        }
                    }
                }
                else
                {
                    // click vuoto (senza Shift): pulisci selezioni
                    ClearMultiSelection();
                    SetSelection(null, null, null);
                    return;
                }
            }
        }

        public void OnMouseMove(Point location, System.Windows.Forms.MouseButtons button)
        {
            // ► BOX SELECT
            if (_boxSelecting)
            {
                _boxCurrent = location;
                return;
            }

            // ► PREVIEW INSERIMENTO (drag dal DGV): disegna sempre grigio + verde/rosso
            if (_inserting)
            {
                _previewActive = true;

                // rettangolo della riga operatore
                var rowRect = TryGetOperatorRowRect(_insertOpId);
                if (!rowRect.HasValue)
                {
                    _previewOkRect = _previewNoRect = _previewDurRect = null;
                    return;
                }

                // tempo proposto (snap)
                var startRaw = SnapStart(TimeAt(location.X));
                var endRaw = SnapEnd(startRaw + _insertDuration);

                // durata grigia prevista
                {
                    var (x, w) = MapToPixels(startRaw, endRaw);
                    _previewDurRect = new Rectangle(x, rowRect.Value.Top + 4, Math.Max(6, w), rowRect.Value.Height - 8);
                }

                // validità precedenze (verde/rosso)
                if (IsPlacementValidByPrecedence(_insertProj, _insertTaskName, startRaw, endRaw, out var minStart))
                {
                    _previewOkRect = _previewDurRect;
                    _previewNoRect = null;
                }
                else
                {
                    _previewOkRect = null;
                    _previewNoRect = _previewDurRect;
                }
                return;
            }

            // ► RESIZE (NO preview, come richiesto)
            if (_resizing)
            {
                var (opId_r, proj_r, taskName_r) = ParseTaskKey(_dragKey);
                var t = FindTask(opId_r, proj_r, taskName_r);
                if (t == null) return;

                var tAt = TimeAt(location.X);
                DateTime snapped = SnapToMinutes(tAt, RESIZE_STEP_MINUTES);

                DateTime anchorStart = _dragOrigStart;
                DateTime startR = anchorStart;
                DateTime endR;

                if (_resizeLeft)
                {
                    TimeSpan deltaLen = anchorStart - snapped;
                    endR = _dragOrigEnd + deltaLen;
                }
                else
                {
                    endR = snapped;
                }

                if (endR - startR < MIN_RESIZE_DURATION)
                    endR = startR + MIN_RESIZE_DURATION;

                t.Start = SnapStart(startR);
                t.End = SnapEnd(endR);

                // cascata in avanti delle task dell'operatore
                if (_resizeOpTailSnapshot != null && _resizeOpTailSnapshot.Count > 0)
                {
                    var deltaEnd = t.End - _dragOrigEnd;
                    if (deltaEnd.Ticks != 0)
                    {
                        foreach (var (tk, s0, e0) in _resizeOpTailSnapshot)
                        {
                            tk.Start = SnapStart(s0 + deltaEnd);
                            tk.End = SnapEnd(e0 + deltaEnd);
                        }
                    }
                }

                // puntello ordine intra-progetto
                var sTmp = t.Start; var eTmp = t.End;
                EnforceProjectOrder(opId_r, proj_r, taskName_r, ref sTmp, ref eTmp);
                t.Start = sTmp; t.End = eTmp;

                // niente preview in resize
                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return;
            }

            // ► DRAG (task o progetto) — voglio preview
            if (!_dragging) return;

            // --- DRAG INTERO PROGETTO ---
            if (_dragProject)
            {
                var sp = _dragKey.Split('|'); // SUM|op|proj
                var opId_p = sp[1]; var proj_p = sp[2];
                var pr = _ops.First(o => o.Id == opId_p).Projects.First(p => p.Code == proj_p);

                var ordered = pr.Tasks.OrderBy(tt => OrderIndex(tt.TaskName)).ToList();
                var durations = ordered
                    .Select(t => _dragProjSnapshot.First(snap => ReferenceEquals(snap.task, t)).e0
                               - _dragProjSnapshot.First(snap => ReferenceEquals(snap.task, t)).s0)
                    .ToList();

                double du = (location.X - _dragStartMouse.X) / _pxPerUnit;
                TimeSpan delta = _zoomLevel switch
                {
                    Level.Year => TimeSpan.FromDays(30 * Math.Round(du)),
                    Level.Month => TimeSpan.FromDays(du),
                    Level.Week => TimeSpan.FromDays(du),
                    Level.Day => TimeSpan.FromHours(du),
                    Level.Hour => TimeSpan.FromMinutes(du),
                    _ => TimeSpan.Zero
                };
                int dir = Math.Sign(delta.TotalSeconds);

                var firstSnap = _dragProjSnapshot.First(snap => ReferenceEquals(snap.task, ordered[0]));
                DateTime proposedStart = SnapStart(firstSnap.s0 + delta);

                DateTime s0 = PlaceBlockWithoutOverlapHard(opId_p, ordered, durations, proposedStart, dir);

                // applica catena
                DateTime cur = s0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var t = ordered[i];
                    var d = durations[i];
                    t.Start = SnapStart(cur);
                    t.End = SnapEnd(cur + d);
                    cur = t.End;
                }

                // magnet blocco
                DateTime blkStart = ordered.First().Start;
                DateTime blkEnd = ordered.Last().End;
                if (TryMagnetBlock(opId_p, blkStart, blkEnd, out var ns, out var ne, out var flash))
                {
                    _magnetFlash = (flash, DateTime.UtcNow);
                    DateTime cur2 = ns;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var t = ordered[i];
                        var d = durations[i];
                        t.Start = SnapStart(cur2);
                        t.End = SnapEnd(cur2 + d);
                        cur2 = t.End;
                    }
                }
                else
                {
                    _magnetFlash = null;
                }

                // preview sul progetto = uso la PRIMA task come rappresentante per precedenze
                _previewActive = true;
                var rowRect = TryGetOperatorRowRect(opId_p); // sul rigo operatore
                if (rowRect.HasValue)
                {
                    var (x, w) = MapToPixels(ordered.First().Start, ordered.Last().End);
                    _previewDurRect = new Rectangle(x, rowRect.Value.Top + 4, Math.Max(6, w), rowRect.Value.Height - 8);

                    if (IsPlacementValidByPrecedence(proj_p, ordered.First().TaskName, ordered.First().Start, ordered.First().End, out var _))
                    {
                        _previewOkRect = _previewDurRect; _previewNoRect = null;
                    }
                    else
                    {
                        _previewOkRect = null; _previewNoRect = _previewDurRect;
                    }
                }
                return;
            }

            // --- DRAG SINGOLA TASK ---
            var (opId_d, proj_d, taskName_d) = ParseTaskKey(_dragKey);
            var tDrag = FindTask(opId_d, proj_d, taskName_d);
            if (tDrag == null) return;

            double duTask = (location.X - _dragStartMouse.X) / _pxPerUnit;
            var startD = AddUnits(_dragOrigStart, duTask);
            var endD = AddUnits(_dragOrigEnd, duTask);

            startD = SnapStart(startD);
            endD = SnapEnd(endD);

            if (TryMagnet(opId_d, tDrag, ref startD, ref endD, out var flash2))
                _magnetFlash = (flash2, DateTime.UtcNow);
            else
                _magnetFlash = null;

            EnforceProjectOrder(opId_d, proj_d, taskName_d, ref startD, ref endD);

            int dirTask = Math.Sign((startD - tDrag.Start).TotalSeconds);
            PlaceWithoutOverlapHard(opId_d, tDrag, ref startD, ref endD, dirTask);

            var deltaForwardTask = startD - _dragOrigStart;
            if (deltaForwardTask.TotalSeconds > 0.5 && _dragOpTailSnapshot != null && _dragOpTailSnapshot.Count > 0)
            {
                foreach (var (tk, s0t, e0t) in _dragOpTailSnapshot)
                {
                    tk.Start = s0t + deltaForwardTask;
                    tk.End = e0t + deltaForwardTask;
                }
            }

            // applica al live object
            tDrag.Start = startD;
            tDrag.End = endD;

            // ► PREVIEW durante il drag task (verde/rosso sul rigo task)
            _previewActive = true;
            var rowTask = TryGetTaskRowRect(opId_d, proj_d, taskName_d);
            if (rowTask.HasValue)
            {
                var (x, w) = MapToPixels(tDrag.Start, tDrag.End);
                _previewDurRect = new Rectangle(x, rowTask.Value.Top + 4, Math.Max(6, w), rowTask.Value.Height - 8);

                if (IsPlacementValidByPrecedence(proj_d, taskName_d, tDrag.Start, tDrag.End, out var _))
                {
                    _previewOkRect = _previewDurRect; _previewNoRect = null;
                }
                else
                {
                    _previewOkRect = null; _previewNoRect = _previewDurRect;
                }
            }
        }



        // ►► Aggancio (magnet) per blocchi di progetto: allinea start del blocco alla end di una task peer,
        //     o end del blocco alla start di una task peer, se entro MAGNET_PX pixel.
        private bool TryMagnetBlock(string opId,
                                    DateTime blockStart,
                                    DateTime blockEnd,
                                    out DateTime snappedStart,
                                    out DateTime snappedEnd,
                                    out Rectangle flashRect)
        {
            flashRect = Rectangle.Empty;
            snappedStart = blockStart;
            snappedEnd = blockEnd;

            // Tutte le task dello stesso operatore (peer)
            var peers = _ops.Where(o => o.Id == opId)
                            .SelectMany(o => o.Projects)
                            .SelectMany(p => p.Tasks)
                            .OrderBy(t => t.Start)
                            .ToList();

            double bestDx = double.MaxValue;
            bool snapToStart = true; // true = aggancio start blocco a end peer; false = aggancio end blocco a start peer
            DateTime bestTo = default;

            foreach (var p in peers)
            {
                // Candidati: start blocco → end peer  |  end blocco → start peer
                var candidates = new (DateTime from, DateTime to, bool toStart)[]
                {
            (blockStart, p.End,   true),
            (blockEnd,   p.Start, false)
                };

                foreach (var c in candidates)
                {
                    double uFrom = UnitsFromStart(c.from);
                    double uTo = UnitsFromStart(c.to);
                    double dx = Math.Abs(uTo - uFrom) * _pxPerUnit;

                    if (dx < bestDx)
                    {
                        bestDx = dx;
                        bestTo = c.to;
                        snapToStart = c.toStart;
                    }
                }
            }

            // Se entro la soglia in pixel, applica lo snap
            if (bestDx <= MAGNET_PX)
            {
                var dur = blockEnd - blockStart;
                if (snapToStart)
                {
                    snappedStart = bestTo;
                    snappedEnd = bestTo + dur;
                }
                else
                {
                    snappedEnd = bestTo;
                    snappedStart = bestTo - dur;
                }

                var (xx, ww) = MapToPixels(snappedStart, snappedEnd);
                flashRect = new Rectangle(snapToStart ? xx : xx + ww - 2, _body.Top, 2, _body.Height);
                return true;
            }

            return false;
        }


        public PlanningChangedArgs OnMouseUp(Point location, System.Windows.Forms.MouseButtons button)
        {
            // BOX SELECT
            if (_boxSelecting)
            {
                _boxSelecting = false;
                var r = GetBoxRect();
                foreach (var kv in _barRects)
                    if (kv.Value.IntersectsWith(r))
                        _multiSel.Add(kv.Key);

                if (_multiSel.Count > 0)
                {
                    var first = _multiSel.First();
                    if (first.StartsWith("TASK|"))
                    {
                        var (op, pr, tk) = ParseTaskKey(first);
                        SetSelection(op, pr, tk);
                    }
                    else if (first.StartsWith("SUM|"))
                    {
                        var sp = first.Split('|');
                        SetSelection(sp[1], sp[2], null);
                    }
                }

                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return null;
            }

            // ► INSERIMENTO ESTERNO
            if (_inserting)
            {
                _previewActive = false;

                DateTime proposedStartRaw = SnapStart(TimeAt(location.X));
                DateTime proposedEndRaw = SnapEnd(proposedStartRaw + _insertDuration);

                bool valid = IsPlacementValidByPrecedence(_insertProj, _insertTaskName, proposedStartRaw, proposedEndRaw, out var _);
                if (!valid)
                {
                    _inserting = false;
                    _insertOpId = _insertProj = _insertTaskName = null;
                    _insertDuration = TimeSpan.Zero;
                    _previewOkRect = _previewNoRect = _previewDurRect = null;
                    return null;
                }

                AddOrUpdateTask(_insertOpId, _insertProj, _insertTaskName, proposedStartRaw, _insertDuration);
                var t = FindTask(_insertOpId, _insertProj, _insertTaskName);

                var args = new PlanningChangedArgs
                {
                    Id = t.Id,
                    ProjectId = t.ProjectId,
                    JobId = $"{_insertProj}-{SuffixFromTaskName(_insertTaskName)}",
                    ProjectCode = _insertProj,
                    Operatore = _insertOpId,
                    TaskName = _insertTaskName,
                    Start = t.Start,
                    End = t.End
                };
                PlanningChanged?.Invoke(args);

                _inserting = false;
                _insertOpId = _insertProj = _insertTaskName = null;
                _insertDuration = TimeSpan.Zero;

                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return args;
            }

            // ► chiusura resize
            if (_resizing)
            {
                _resizeOpTailSnapshot = null;
                _resizing = false;

                var (opId_s, proj_s, taskName_s) = ParseTaskKey(_dragKey);
                var tUp = FindTask(opId_s, proj_s, taskName_s);

                var argsRes = new PlanningChangedArgs
                {
                    Id = tUp.Id,
                    ProjectId = tUp.ProjectId,
                    JobId = $"{proj_s}-{SuffixFromTaskName(taskName_s)}",
                    ProjectCode = proj_s,
                    Operatore = opId_s,
                    TaskName = taskName_s,
                    Start = tUp.Start,
                    End = tUp.End
                };
                PlanningChanged?.Invoke(argsRes);

                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return argsRes;
            }

            if (!_dragging)
            {
                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return null;
            }

            _dragging = false;

            // === BLOCCO PROGETTO ===
            if (_dragProject)
            {
                _dragProject = false;

                var sp = _dragKey.Split('|'); // SUM|op|proj
                var opId_u = sp[1]; var proj_u = sp[2];
                var pr = _ops.First(o => o.Id == opId_u).Projects.First(p => p.Code == proj_u);

                var first = pr.Tasks.OrderBy(tt => OrderIndex(tt.TaskName)).FirstOrDefault();
                if (first != null)
                {
                    bool ok = IsPlacementValidByPrecedence(proj_u, first.TaskName, first.Start, first.End, out var _);
                    if (!ok)
                    {
                        foreach (var snap in _dragProjSnapshot)
                        {
                            snap.task.Start = snap.s0;
                            snap.task.End = snap.e0;
                        }

                        _dragProjSnapshot = null;
                        _dragOpTailSnapshot = null;
                        _dragLinkedTailSnapshot = null;
                        _dragProjChainSnapshot = null;
                        _dragDirSign = 0;

                        _previewActive = false;
                        _previewOkRect = _previewNoRect = _previewDurRect = null;
                        return null;
                    }
                }

                PlanningChangedArgs last = null;
                foreach (var t in pr.Tasks)
                {
                    var args = new PlanningChangedArgs
                    {
                        Id = t.Id,
                        ProjectId = t.ProjectId,
                        JobId = $"{proj_u}-{SuffixFromTaskName(t.TaskName)}",
                        ProjectCode = proj_u,
                        Operatore = opId_u,
                        TaskName = t.TaskName,
                        Start = t.Start,
                        End = t.End
                    };
                    PlanningChanged?.Invoke(args);
                    last = args;
                }
                _dragProjSnapshot = null;
                _dragOpTailSnapshot = null;
                _dragLinkedTailSnapshot = null;
                _dragProjChainSnapshot = null;
                _dragDirSign = 0;

                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return last;
            }

            // === TASK SINGOLA ===
            var (opId_s2, proj_s2, taskName_s2) = ParseTaskKey(_dragKey);
            var tUp2 = FindTask(opId_s2, proj_s2, taskName_s2);

            bool validSingle = IsPlacementValidByPrecedence(proj_s2, taskName_s2, tUp2.Start, tUp2.End, out var _);
            if (!validSingle)
            {
                tUp2.Start = _dragOrigStart;
                tUp2.End = _dragOrigEnd;

                if (_dragOpTailSnapshot != null)
                    foreach (var (tk, s0t, e0t) in _dragOpTailSnapshot) { tk.Start = s0t; tk.End = e0t; }

                _dragOpTailSnapshot = null;
                _dragLinkedTailSnapshot = null;
                _dragProjChainSnapshot = null;
                _dragDirSign = 0;

                _previewActive = false;
                _previewOkRect = _previewNoRect = _previewDurRect = null;
                return null;
            }

            var argsSingle = new PlanningChangedArgs
            {
                Id = tUp2.Id,
                ProjectId = tUp2.ProjectId,
                JobId = $"{proj_s2}-{SuffixFromTaskName(taskName_s2)}",
                ProjectCode = proj_s2,
                Operatore = opId_s2,
                TaskName = taskName_s2,
                Start = tUp2.Start,
                End = tUp2.End
            };
            PlanningChanged?.Invoke(argsSingle);

            _dragOpTailSnapshot = null;
            _dragLinkedTailSnapshot = null;
            _dragProjChainSnapshot = null;
            _dragDirSign = 0;

            _previewActive = false;
            _previewOkRect = _previewNoRect = _previewDurRect = null;
            return argsSingle;
        }


        public void OnClick(Point location)
        {
            if (!_left.Contains(location)) return;

            foreach (var kv in _rowsLeft)
            {
                var (kind, key, rect) = kv.Value;
                if (!rect.Contains(location)) continue;

                if (kind == "OP")
                {
                    var op = _ops.First(o => o.Id == key);
                    op.Expanded = !op.Expanded;
                    SetSelection(op.Id, null, null);
                    ComputeContentHeight();
                    return;
                }
                else if (kind == "PROJ")
                {
                    var (opId, proj) = ParseProjKey(key);
                    var pr = _ops.First(o => o.Id == opId).Projects.First(p => p.Code == proj);
                    pr.Expanded = !pr.Expanded;
                    SetSelection(opId, proj, null);
                    ComputeContentHeight();
                    return;
                }
            }
        }

        // Doppio click su una task → VISTA ORARIA
        public void OnDoubleClick(Point location)
        {
            if (!_body.Contains(location)) return;
            var hit = _barRects.FirstOrDefault(kv => kv.Value.Contains(location));
            if (string.IsNullOrEmpty(hit.Key)) return;

            // Doppio click su TASK -> vista oraria all'ora della task (come prima)
            if (hit.Key.StartsWith("TASK|", StringComparison.Ordinal))
            {
                var (opId, proj, taskName) = ParseTaskKey(hit.Key);
                var t = FindTask(opId, proj, taskName);
                SetSelection(opId, proj, taskName);

                _zoomLevel = Level.Hour;
                Reference = new DateTime(t.Start.Year, t.Start.Month, t.Start.Day, t.Start.Hour, 0, 0);
                RefreshLayout();
                NotifyViewChanged();
                return;
            }

            // Doppio click su SUM (progetto): mostra il giorno di pianificazione (primo start del progetto)
            if (hit.Key.StartsWith("SUM|", StringComparison.Ordinal))
            {
                var sp = hit.Key.Split('|'); // SUM|op|proj
                var opId = sp[1]; var proj = sp[2];

                var pr = _ops.First(o => o.Id == opId).Projects.First(p => p.Code == proj);
                if (pr.Tasks.Count == 0) return;

                var firstStart = pr.Tasks.Min(t => t.Start).Date;

                _zoomLevel = Level.Day;
                Reference = firstStart;
                RefreshLayout();
                NotifyViewChanged();
            }
        }

        #endregion

        #region Disegno
        private void DrawLeft(Graphics g)
        {
            using var bg = new SolidBrush(Color.FromArgb(50, 50, 50));
            using var txt = new SolidBrush(Color.Gainsboro);
            using var font = new Font(SystemFonts.DefaultFont, FontStyle.Regular);
            using var border = new Pen(Color.FromArgb(70, 70, 70));

            g.FillRectangle(bg, _left);
            g.DrawLine(border, _left.Right - 1, _panel.Top, _left.Right - 1, _panel.Bottom);

            int y = _header.Bottom - _vScroll;
            int rowIdx = 0;

            foreach (var op in _ops)
            {
                var r = new Rectangle(_left.Left, y, _left.Width, RowHeight);
                if (r.Bottom >= _header.Bottom && r.Top <= _panel.Bottom)
                {
                    _rowsLeft[rowIdx] = ("OP", op.Id, r);
                    using var zebra = new SolidBrush((rowIdx % 2 == 0) ? Color.FromArgb(58, 58, 58) : Color.FromArgb(52, 52, 52));
                    g.FillRectangle(zebra, r);
                    var caret = op.Expanded ? "▼" : "▶";
                    g.DrawString($"{caret}  {op.Label}", font, txt, new PointF(r.Left + 8, r.Top + 5));
                }
                y += RowHeight + RowGap; rowIdx++;

                if (op.Expanded)
                {
                    foreach (var pr in op.Projects)
                    {
                        var rp = new Rectangle(_left.Left, y, _left.Width, RowHeight);
                        if (rp.Bottom >= _header.Bottom && rp.Top <= _panel.Bottom)
                        {
                            _rowsLeft[rowIdx] = ("PROJ", ProjKey(op.Id, pr.Code), rp);
                            using var zebra2 = new SolidBrush((rowIdx % 2 == 0) ? Color.FromArgb(56, 56, 56) : Color.FromArgb(50, 50, 50));
                            g.FillRectangle(zebra2, rp);
                            var caret2 = pr.Expanded ? "▼" : "▶";

                            var total = pr.TotalDuration;
                            var totTxt = $"  [{(int)total.TotalHours:D2}:{total.Minutes:D2}]";

                            g.DrawString($"    {caret2}  {pr.Code}{totTxt}", font, txt, new PointF(rp.Left + 8, rp.Top + 5));
                        }
                        y += RowHeight + RowGap; rowIdx++;

                        if (pr.Expanded)
                        {
                            foreach (var t in GetDisplayTasks(pr)) // <== ORDINAMENTO REALE
                            {
                                var rt = new Rectangle(_left.Left, y, _left.Width, RowHeight);
                                if (rt.Bottom >= _header.Bottom && rt.Top <= _panel.Bottom)
                                {
                                    using var zebra3 = new SolidBrush((rowIdx % 2 == 0) ? Color.FromArgb(54, 54, 54) : Color.FromArgb(48, 48, 48));
                                    g.FillRectangle(zebra3, rt);

                                    var dur = (t.End > t.Start) ? (t.End - t.Start) : TimeSpan.Zero;
                                    var durTxt = $"  [{(int)dur.TotalHours:D2}:{dur.Minutes:D2}]";

                                    g.DrawString($"        • {t.TaskName}{durTxt}", font, txt, new PointF(rt.Left + 8, rt.Top + 5));
                                }
                                y += RowHeight + RowGap; rowIdx++;
                            }
                        }
                    }
                }
            }
        }


        private void DrawHeader(Graphics g)
        {
            using var bg = new SolidBrush(Color.FromArgb(45, 45, 45));
            using var pen = new Pen(Color.FromArgb(70, 70, 70));
            using var txt = new SolidBrush(Color.Gainsboro);
            using var small = new Font(SystemFonts.DefaultFont, FontStyle.Regular);
            using var bold = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

            g.FillRectangle(bg, _header);
            g.DrawRectangle(pen, _header);

            string title = _zoomLevel switch
            {
                Level.Hour => Reference.ToString("dddd d MMMM yyyy HH\\:mm"),
                Level.Day => Reference.ToString("dddd d MMMM yyyy"),
                Level.Week => $"Settimana {WeekNumberISO(Reference)} ({_winStart:dd MMM} – {_winEnd.AddDays(-1):dd MMM})",
                Level.Month => Reference.ToString("MMMM yyyy"),
                _ => Reference.ToString("yyyy")
            };
            g.DrawString(title, bold, txt, new PointF(_header.Left + 6, _header.Top + 4));

            var minor = new Rectangle(_header.Left, _header.Bottom - MinorHeaderHeight, _header.Width, MinorHeaderHeight);
            g.DrawRectangle(pen, minor);

            switch (_zoomLevel)
            {
                case Level.Hour:
                    for (int m = 0; m <= 60; m++)
                    {
                        int x = _body.Left + (int)(m * _pxPerUnit);
                        g.DrawLine(pen, x, minor.Top, x, minor.Bottom);
                        if (m < 60 && m % 5 == 0)
                            g.DrawString(_winStart.AddMinutes(m).ToString("HH:mm"), small, txt, new PointF(x + 2, minor.Top + 2));
                    }
                    break;

                case Level.Day:
                    for (int h = 0; h <= 16; h++)
                    {
                        int x = _body.Left + (int)(h * _pxPerUnit);
                        g.DrawLine(pen, x, minor.Top, x, minor.Bottom);
                        if (h < 16)
                            g.DrawString(_winStart.AddHours(h).ToString("HH"), small, txt, new PointF(x + 2, minor.Top + 2));
                    }
                    break;

                case Level.Week:
                    for (int d = 0; d <= 7; d++)
                    {
                        int x = _body.Left + (int)(d * _pxPerUnit);
                        g.DrawLine(pen, x, minor.Top, x, minor.Bottom);
                        if (d < 7) g.DrawString(_winStart.AddDays(d).ToString("ddd d", new CultureInfo("it-IT")), small, txt, new PointF(x + 2, minor.Top + 2));
                    }
                    break;

                case Level.Month:
                    int days = DateTime.DaysInMonth(_winStart.Year, _winStart.Month);
                    int step = Math.Max(1, days / 15);
                    for (int d = 0; d <= days; d++)
                    {
                        int x = _body.Left + (int)(d * _pxPerUnit);
                        g.DrawLine(pen, x, minor.Top, x, minor.Bottom);
                        if (d < days && (d % step == 0))
                            g.DrawString((d + 1).ToString(), small, txt, new PointF(x + 2, minor.Top + 2));
                    }
                    break;

                case Level.Year:
                    for (int m = 0; m <= 12; m++)
                    {
                        int x = _body.Left + (int)(m * _pxPerUnit);
                        g.DrawLine(pen, x, minor.Top, x, minor.Bottom);
                        if (m < 12) g.DrawString(new DateTime(2000, m + 1, 1).ToString("MMM", new CultureInfo("it-IT")), small, txt, new PointF(x + 2, minor.Top + 2));
                    }
                    break;
            }
        }

        private void DrawBody(Graphics g)
        {
            using var vline = new Pen(Color.FromArgb(55, 55, 55));
            switch (_zoomLevel)
            {
                case Level.Day:
                    for (int h = 0; h <= 16; h++)
                    {
                        int x = _body.Left + (int)(h * _pxPerUnit);
                        g.DrawLine(vline, x, _body.Top, x, _body.Bottom);
                    }
                    break;
                case Level.Week:
                    for (int d = 0; d <= 7; d++)
                        g.DrawLine(vline, _body.Left + (int)(d * _pxPerUnit), _body.Top, _body.Left + (int)(d * _pxPerUnit), _body.Bottom);
                    break;
                case Level.Month:
                    int days = DateTime.DaysInMonth(_winStart.Year, _winStart.Month);
                    for (int d = 0; d <= days; d++)
                        g.DrawLine(vline, _body.Left + (int)(d * _pxPerUnit), _body.Top, _body.Left + (int)(d * _pxPerUnit), _body.Bottom);
                    break;
                case Level.Year:
                    for (int m = 0; m <= 12; m++)
                        g.DrawLine(vline, _body.Left + (int)(m * _pxPerUnit), _body.Top, _body.Left + (int)(m * _pxPerUnit), _body.Bottom);
                    break;
                case Level.Hour:
                    for (int m = 0; m <= 60; m++)
                        g.DrawLine(vline, _body.Left + (int)(m * _pxPerUnit), _body.Top, _body.Left + (int)(m * _pxPerUnit), _body.Bottom);
                    break;
            }

            int y = _body.Top - _vScroll;
            int run = 0;
            using var sep = new Pen(Color.FromArgb(62, 62, 62));
            using var barPen = new Pen(Color.FromArgb(30, 30, 30));
            using var font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

            foreach (var op in _ops)
            {
                var rOp = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                FillZebra(g, rOp, run);
                g.DrawLine(sep, rOp.Left, rOp.Bottom, rOp.Right, rOp.Bottom);

                // Fondo occupazione operatore (merge intervalli)
                var opIntervals = _ops.Where(o => o.Id == op.Id)
                                      .SelectMany(o => o.Projects)
                                      .SelectMany(p => p.Tasks)
                                      .Select(t => new Interval
                                      {
                                          S = (t.Start < _winStart) ? _winStart : t.Start,
                                          E = (t.End > _winEnd) ? _winEnd : t.End
                                      })
                                      .Where(iv => iv.E > iv.S);
                var merged = MergeIntervals(opIntervals);

                using (var fill = new SolidBrush((_selOpId == op.Id && _selTask == null && _selProj == null)
                                    ? Color.FromArgb(110, 150, 240)
                                    : Color.FromArgb(120, 120, 120)))
                using (var penOp = new Pen(Color.FromArgb(30, 30, 30)))
                {
                    foreach (var iv in merged)
                    {
                        var (x, w) = MapToPixels(iv.S, iv.E);
                        var bar = new Rectangle(x, rOp.Top + 6, Math.Max(6, w), rOp.Height - 12);
                        var rf = new RectangleF(bar.Left + .5f, bar.Top + .5f, bar.Width - 1, bar.Height - 1);
                        using var path = new System.Drawing.Drawing2D.GraphicsPath();
                        float rad = 6f;
                        path.AddArc(rf.Left, rf.Top, rad, rad, 180, 90);
                        path.AddArc(rf.Right - rad, rf.Top, rad, rad, 270, 90);
                        path.AddArc(rf.Right - rad, rf.Bottom - rad, rad, rad, 0, 90);
                        path.AddArc(rf.Left, rf.Bottom - rad, rad, rad, 90, 90);
                        path.CloseFigure();
                        g.FillPath(fill, path);
                        g.DrawPath(penOp, path);
                    }
                }

                y += RowHeight + RowGap; run++;

                if (!op.Expanded) continue;

                foreach (var pr in op.Projects)
                {
                    var rProj = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                    FillZebra(g, rProj, run);
                    g.DrawLine(sep, rProj.Left, rProj.Bottom, rProj.Right, rProj.Bottom);

                    if (pr.SummaryStart.HasValue && pr.SummaryEnd.HasValue)
                        DrawSummaryBar(g, op.Id, pr, rProj, barPen, font);

                    y += RowHeight + RowGap; run++;

                    if (!pr.Expanded) continue;

                    // === RIGHE TASK: usa lo stesso ordinamento “reale” della colonna sinistra
                    foreach (var t in GetDisplayTasks(pr))
                    {
                        var rTask = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                        FillZebra(g, rTask, run);
                        g.DrawLine(sep, rTask.Left, rTask.Bottom, rTask.Right, rTask.Bottom);
                        DrawTaskBar(g, op.Id, pr.Code, t, rTask, barPen, font);
                        y += RowHeight + RowGap; run++;
                    }
                }
            }

            // Linea “now” verde
            var now = DateTime.Now;
            if (now >= _winStart && now < _winEnd)
            {
                var ct = SnapToMinutes(now, _dailySnapMinutes);
                double u = _zoomLevel switch
                {
                    Level.Hour => (ct - _winStart).TotalMinutes,
                    Level.Day => (ct - _winStart).TotalHours,
                    Level.Week => (ct - _winStart).TotalDays,
                    Level.Month => (ct - _winStart).TotalDays,
                    Level.Year => (ct.Month - 1) + (ct.Day - 1) / (double)DateTime.DaysInMonth(ct.Year, ct.Month),
                    _ => 0
                };
                int x = _body.Left + (int)Math.Round(u * _pxPerUnit);
                using var penNow = new Pen(Color.FromArgb(60, 220, 120), 2f) { DashPattern = new float[] { 4, 4 } };
                g.DrawLine(penNow, x, _body.Top, x, _body.Bottom);
            }

            // Flash magnet
            if (_magnetFlash.HasValue)
            {
                if ((DateTime.UtcNow - _magnetFlash.Value.t0).TotalMilliseconds <= 120)
                {
                    using var br = new SolidBrush(Color.FromArgb(220, 220, 120));
                    g.FillRectangle(br, _magnetFlash.Value.edgeRect);
                }
                else
                {
                    _magnetFlash = null;
                }
            }
        }


        #endregion

        #region Timeline / utils
        private void ComputeWindow()
        {
            switch (_zoomLevel)
            {
                case Level.Hour:
                    _winStart = new DateTime(Reference.Year, Reference.Month, Reference.Day, Reference.Hour, 0, 0);
                    _winEnd = _winStart.AddHours(1);
                    _pxPerUnit = _body.Width / 60.0; // unità = minuti
                    break;
                case Level.Day:
                    _winStart = Reference.Date.AddHours(5);
                    _winEnd = Reference.Date.AddHours(21);
                    _pxPerUnit = _body.Width / 16.0;
                    break;
                case Level.Week:
                    int delta = ((int)Reference.DayOfWeek + 6) % 7; // lun inizio
                    _winStart = Reference.Date.AddDays(-delta);
                    _winEnd = _winStart.AddDays(7);
                    _pxPerUnit = _body.Width / 7.0;
                    break;
                case Level.Month:
                    _winStart = new DateTime(Reference.Year, Reference.Month, 1);
                    _winEnd = _winStart.AddMonths(1);
                    _pxPerUnit = _body.Width / (double)DateTime.DaysInMonth(_winStart.Year, _winStart.Month);
                    break;
                default:
                    _winStart = new DateTime(Reference.Year, 1, 1);
                    _winEnd = _winStart.AddYears(1);
                    _pxPerUnit = _body.Width / 12.0;
                    break;
            }
        }

        private void ComputeContentHeight()
        {
            int rows = 0;
            foreach (var op in _ops)
            {
                rows++;
                if (op.Expanded)
                {
                    rows += op.Projects.Count;
                    foreach (var pr in op.Projects)
                        if (pr.Expanded) rows += pr.Tasks.Count;
                }
            }
            _contentHeight = rows * (RowHeight + RowGap);
            _vScroll = Math.Min(_vScroll, Math.Max(0, _contentHeight - _body.Height));
        }

        private (int x, int w) MapToPixels(DateTime start, DateTime end)
        {
            double u1 = UnitsFromStart(start);
            double u2 = UnitsFromStart(end);
            int x = _body.Left + (int)Math.Round(u1 * _pxPerUnit);
            int w = Math.Max(6, (int)Math.Round((u2 - u1) * _pxPerUnit));
            return (x, w);
        }

        private double UnitsFromStart(DateTime t)
        {
            if (t < _winStart) t = _winStart;
            if (t > _winEnd) t = _winEnd;
            return _zoomLevel switch
            {
                Level.Hour => (t - _winStart).TotalMinutes,
                Level.Day => (t - _winStart).TotalHours,
                Level.Week => (t - _winStart).TotalDays,
                Level.Month => (t - _winStart).TotalDays,
                Level.Year => (t.Month - 1),
                _ => 0
            };
        }

        private DateTime AddUnits(DateTime t, double du) =>
            _zoomLevel switch
            {
                Level.Hour => t.AddMinutes(du),
                Level.Day => t.AddHours(du),
                Level.Week => t.AddDays(du),
                Level.Month => t.AddDays(du),
                Level.Year => t.AddMonths((int)Math.Round(du)),
                _ => t
            };

        private DateTime SnapStart(DateTime t) =>
            (_zoomLevel == Level.Day || _zoomLevel == Level.Hour) ? SnapToMinutes(t, _dailySnapMinutes) : t;
        private DateTime SnapEnd(DateTime t) =>
            (_zoomLevel == Level.Day || _zoomLevel == Level.Hour) ? SnapToMinutes(t, _dailySnapMinutes) : t;

        private static DateTime SnapToMinutes(DateTime t, int step)
        {
            int m = (t.Minute / step) * step;
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, m, 0);
        }

        private void ZoomIn(Point center)
        {
            if (_zoomLevel == Level.Hour) return;

            // tempo sotto al puntatore nella scala corrente
            var tCenter = TimeAt(center.X);

            var old = _zoomLevel;
            _zoomLevel++;

            // ancora la Reference al tempo centrale per la nuova scala
            Reference = _zoomLevel switch
            {
                Level.Hour => new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0),
                Level.Day => tCenter.Date,
                Level.Week => tCenter, // ComputeWindow allinea al lunedì
                Level.Month => new DateTime(tCenter.Year, tCenter.Month, 1),
                Level.Year => new DateTime(tCenter.Year, 1, 1),
                _ => Reference
            };

            RefreshLayout();
        }

        private void ZoomOut(Point center)
        {
            if (_zoomLevel == Level.Year) return;

            // tempo sotto al puntatore nella scala corrente
            var tCenter = TimeAt(center.X);

            var old = _zoomLevel;
            _zoomLevel--;

            // ancora la Reference al tempo centrale per la nuova scala
            Reference = _zoomLevel switch
            {
                Level.Hour => new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0),
                Level.Day => tCenter.Date,
                Level.Week => tCenter,
                Level.Month => new DateTime(tCenter.Year, tCenter.Month, 1),
                Level.Year => new DateTime(tCenter.Year, 1, 1),
                _ => Reference
            };

            RefreshLayout();
        }


        private static int WeekNumberISO(DateTime date)
        {
            var ci = CultureInfo.GetCultureInfo("it-IT");
            return ci.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }
        #endregion

        #region Logica vincoli
        private (string opId, string proj, string task) ParseTaskKey(string key)
        {
            var sp = key.Split('|');
            return (sp[1], sp[2], sp[3]);
        }
        private (string opId, string proj) ParseProjKey(string key)
        {
            var sp = key.Split('|');
            return (sp[1], sp[2]);
        }
        private static string ProjKey(string opId, string proj) => $"PROJ|{opId}|{proj}";
        private static string SuffixFromTaskName(string taskName) =>
            taskName switch { "Disegno 3D" => "3D", "Distinta" => "DST", "Disegno 2D" => "2D", "Stampare" => "PRN", _ => "UNK" };

        private TaskItem FindTask(string opId, string proj, string taskName)
            => _ops.First(o => o.Id == opId)
                   .Projects.First(p => p.Code == proj)
                   .Tasks.First(t => t.TaskName == taskName);

        private TaskItem FindTaskOrNull(string opId, string proj, string taskName)
        {
            var op = _ops.FirstOrDefault(o => o.Id == opId);
            var pr = op?.Projects.FirstOrDefault(p => p.Code == proj);
            return pr?.Tasks.FirstOrDefault(t => t.TaskName == taskName);
        }

        private void EnforceProjectOrder(string opId, string proj, string taskName, ref DateTime s, ref DateTime e)
        {
            var op = _ops.First(o => o.Id == opId);
            var pr = op.Projects.First(p => p.Code == proj);

            // Trova le fasi del progetto (se presenti)
            var t3D = pr.Tasks.FirstOrDefault(t => t.TaskName == "Disegno 3D");
            var t2D = pr.Tasks.FirstOrDefault(t => t.TaskName == "Disegno 2D");
            var tDST = pr.Tasks.FirstOrDefault(t => t.TaskName == "Distinta");
            var tPRN = pr.Tasks.FirstOrDefault(t => t.TaskName == "Stampare");

            // 1) vincolo MIN di partenza per la task corrente
            DateTime? minStart = null;
            if (string.Equals(taskName, "Disegno 2D", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(taskName, "Distinta", StringComparison.OrdinalIgnoreCase))
            {
                if (t3D != null) minStart = t3D.End;
            }
            else if (string.Equals(taskName, "Stampare", StringComparison.OrdinalIgnoreCase))
            {
                var maxDocs = new[] { t2D?.End, tDST?.End }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(DateTime.MinValue).Max();
                if (maxDocs != DateTime.MinValue) minStart = maxDocs;
                else if (t3D != null) minStart = t3D.End;
            }

            if (minStart.HasValue && s < minStart.Value)
            {
                var dur = e - s;
                s = minStart.Value;
                e = s + dur;
            }

            // 2) aggiusta le task DIPENDENTI dopo lo spostamento della corrente
            if (string.Equals(taskName, "Disegno 3D", StringComparison.OrdinalIgnoreCase))
            {
                // 2D e Distinta devono partire >= fine 3D
                if (t2D != null && t2D.Start < e)
                {
                    var delta = e - t2D.Start;
                    t2D.Start += delta; t2D.End += delta;
                }
                if (tDST != null && tDST.Start < e)
                {
                    var delta = e - tDST.Start;
                    tDST.Start += delta; tDST.End += delta;
                }

                // Stampare dopo max(2D, Distinta)
                if (tPRN != null)
                {
                    var req = new[] { t2D?.End, tDST?.End }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(e).Max();
                    if (tPRN.Start < req)
                    {
                        var delta = req - tPRN.Start;
                        tPRN.Start += delta; tPRN.End += delta;
                    }
                }
            }
            else if (string.Equals(taskName, "Disegno 2D", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(taskName, "Distinta", StringComparison.OrdinalIgnoreCase))
            {
                // Stampare dopo max(2D, Distinta)
                if (tPRN != null)
                {
                    var req = new[] { t2D?.End, tDST?.End }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(e).Max();
                    if (tPRN.Start < req)
                    {
                        var delta = req - tPRN.Start;
                        tPRN.Start += delta; tPRN.End += delta;
                    }
                }
            }
            // Se sposto/stiro Stampare, nessun figlio da aggiornare.
        }

        private void PlaceWithoutOverlapHard(string opId, TaskItem moving, ref DateTime s, ref DateTime e, int dir)
        {
            var peers = _ops.Where(o => o.Id == opId)
                            .SelectMany(o => o.Projects)
                            .SelectMany(p => p.Tasks)
                            .Where(t => !ReferenceEquals(t, moving))
                            .OrderBy(t => t.Start)
                            .ToList();

            var dur = e - s;

            // ---- PRIMA PASSATA: cerca conflitti ----
            List<TaskItem> conflicts = new List<TaskItem>();
            foreach (var j in peers)
            {
                if (s < j.End && j.Start < e) conflicts.Add(j);
            }

            if (conflicts.Count == 0)
            {
                // Snap duro ai vicini se entri nel margine
                TaskItem prev = null;
                foreach (var t in peers)
                    if (t.End <= s) prev = t; else break;

                TaskItem next = null;
                foreach (var t in peers)
                {
                    if (t.Start >= e) { next = t; break; }
                }

                if (prev != null && s < prev.End.AddMinutes(HARD_SNAP_MARGIN_MINUTES))
                {
                    s = prev.End;
                    e = s + dur;
                }
                else if (next != null && e > next.Start.AddMinutes(-HARD_SNAP_MARGIN_MINUTES))
                {
                    e = next.Start;
                    s = e - dur;
                }
                return;
            }

            // ---- SE C'È CONFLITTO: aggancia a DX (dir >= 0) o SX (dir < 0) ----
            if (dir >= 0)
            {
                DateTime lastEnd = conflicts[0].End;
                foreach (var j in conflicts) if (j.End > lastEnd) lastEnd = j.End;
                s = lastEnd;
                e = s + dur;
            }
            else
            {
                DateTime firstStart = conflicts[0].Start;
                foreach (var j in conflicts) if (j.Start < firstStart) firstStart = j.Start;
                e = firstStart;
                s = e - dur;
            }

            // ---- SECONDA PASSATA DI SICUREZZA ----
            conflicts.Clear();
            foreach (var j in peers)
            {
                if (s < j.End && j.Start < e) conflicts.Add(j);
            }
            if (conflicts.Count > 0)
            {
                if (dir >= 0)
                {
                    DateTime lastEnd = conflicts[0].End;
                    foreach (var j in conflicts) if (j.End > lastEnd) lastEnd = j.End;
                    s = lastEnd;
                    e = s + dur;
                }
                else
                {
                    DateTime firstStart = conflicts[0].Start;
                    foreach (var j in conflicts) if (j.Start < firstStart) firstStart = j.Start;
                    e = firstStart;
                    s = e - dur;
                }
            }
        }
        #endregion

        #region Helpers varie / Intervalli / Magnet / Catene / Box
        private DateTime TimeAt(int x)
        {
            double u = (x - _body.Left) / _pxPerUnit;
            return _zoomLevel switch
            {
                Level.Hour => _winStart.AddMinutes(u),
                Level.Day => _winStart.AddHours(u),
                Level.Week => _winStart.AddDays(u),
                Level.Month => _winStart.AddDays(u),
                Level.Year => _winStart.AddMonths((int)Math.Round(u)),
                _ => _winStart
            };
        }

        private struct Interval { public DateTime S; public DateTime E; }
        private static List<Interval> MergeIntervals(IEnumerable<Interval> src)
        {
            var list = src.Where(iv => iv.E > iv.S)
                          .OrderBy(iv => iv.S)
                          .ToList();
            var outL = new List<Interval>();
            foreach (var iv in list)
            {
                if (outL.Count == 0) { outL.Add(iv); continue; }
                var last = outL[outL.Count - 1];
                if (iv.S <= last.E)
                {
                    last.E = (iv.E > last.E) ? iv.E : last.E;
                    outL[outL.Count - 1] = last;
                }
                else outL.Add(iv);
            }
            return outL;
        }

        private static void FillZebra(Graphics g, Rectangle row, int run)
        {
            if (row.Bottom < 0 || row.Top > g.VisibleClipBounds.Bottom) return;
            using var zebra = new SolidBrush((run % 2 == 0) ? Color.FromArgb(48, 48, 48) : Color.FromArgb(44, 44, 44));
            g.FillRectangle(zebra, row);
        }

        private void DrawSummaryBar(Graphics g, string opId, ProjectRow pr, Rectangle rowRect, Pen barPen, Font font)
        {
            var s = pr.SummaryStart.Value;
            var e = pr.SummaryEnd.Value;
            if (e <= _winStart || s >= _winEnd) return;

            var sCl = s < _winStart ? _winStart : s;
            var eCl = e > _winEnd ? _winEnd : e;

            var (x, w) = MapToPixels(sCl, eCl);
            var bar = new Rectangle(x, rowRect.Top + 6, Math.Max(8, w), rowRect.Height - 12);

            bool isPrimary = (_selProj == pr.Code && _selOpId == opId && _selTask == null);
            bool isMulti = _multiSel.Contains($"SUM|{opId}|{pr.Code}");
            Color sumColor = (isPrimary || isMulti) ? Color.FromArgb(110, 150, 240) : Color.FromArgb(160, 160, 160);

            using var brush = new SolidBrush(sumColor);
            g.FillRectangle(brush, bar);
            g.DrawRectangle(barPen, bar);

            string key = $"SUM|{opId}|{pr.Code}";
            _barRects[key] = bar;
        }

        private void DrawTaskBar(Graphics g, string opId, string projCode, TaskItem task, Rectangle rowRect, Pen barPen, Font font)
        {
            if (task.End <= _winStart || task.Start >= _winEnd) return;

            var s = task.Start < _winStart ? _winStart : task.Start;
            var e = task.End > _winEnd ? _winEnd : task.End;

            var (x, w) = MapToPixels(s, e);
            var bar = new Rectangle(x, rowRect.Top + 4, Math.Max(6, w), rowRect.Height - 8);
            if (bar.Right < _body.Left || bar.Left > _body.Right) return;

            bool isPrimary = (_selOpId == opId && _selProj == projCode && _selTask == task.TaskName);
            bool isMulti = _multiSel.Contains($"TASK|{opId}|{projCode}|{task.TaskName}");

            Color yellowNormal = Color.FromArgb(255, 222, 89);
            Color yellowCompleted = Color.FromArgb(255, 204, 77);
            Color blueSelected = Color.FromArgb(110, 150, 240);

            Color taskColor = (isPrimary || isMulti) ? blueSelected
                                                     : (task.Completed ? yellowCompleted : yellowNormal);

            using var brush = new SolidBrush(taskColor);

            var r = new RectangleF(bar.Left + .5f, bar.Top + .5f, bar.Width - 1, bar.Height - 1);
            using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                float rad = 6f;
                gp.AddArc(r.Left, r.Top, rad, rad, 180, 90);
                gp.AddArc(r.Right - rad, r.Top, rad, rad, 270, 90);
                gp.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
                gp.AddArc(r.Left, r.Bottom - rad, rad, rad, 90, 90);
                gp.CloseFigure();
                g.FillPath(brush, gp);
                g.DrawPath(barPen, gp);
            }

            string key = $"TASK|{opId}|{projCode}|{task.TaskName}";
            _barRects[key] = bar;
        }

        private bool TryMagnet(string opId, TaskItem moving, ref DateTime s, ref DateTime e, out Rectangle flashRect)
        {
            flashRect = Rectangle.Empty;
            var peers = _ops.Where(o => o.Id == opId).SelectMany(o => o.Projects).SelectMany(p => p.Tasks)
                            .Where(t => !ReferenceEquals(t, moving)).ToList();

            DateTime bestTo = default; double bestDx = double.MaxValue; bool snapToEndStart = false;
            foreach (var p in peers)
            {
                var cand = new (DateTime from, DateTime to, bool endStart)[]
                {
                    (s, p.End, true),
                    (e, p.Start, false)
                };
                foreach (var c in cand)
                {
                    double uFrom = UnitsFromStart(c.from);
                    double uTo = UnitsFromStart(c.to);
                    double dx = Math.Abs(uTo - uFrom) * _pxPerUnit;
                    if (dx < bestDx)
                    {
                        bestDx = dx; bestTo = c.to; snapToEndStart = c.endStart;
                    }
                }
            }

            if (bestDx <= MAGNET_PX)
            {
                var dur = e - s;
                if (snapToEndStart) { s = bestTo; e = s + dur; }
                else { e = bestTo; s = e - dur; }

                var (xx, ww) = MapToPixels(s, e);
                flashRect = new Rectangle(snapToEndStart ? xx : xx + ww - 2, _body.Top, 2, _body.Height);
                return true;
            }
            return false;
        }

        // ==== Catene agganciate ====
        private bool IsHeadTask(string opId, TaskItem t)
        {
            var peers = _ops.Where(o => o.Id == opId).SelectMany(o => o.Projects).SelectMany(p => p.Tasks)
                .Where(x => !ReferenceEquals(x, t)).OrderBy(x => x.End).ToList();
            var prev = peers.LastOrDefault(x => x.End <= t.Start.AddMinutes(LINK_TOL_MINUTES));
            return prev == null || Math.Abs((t.Start - prev.End).TotalMinutes) > LINK_TOL_MINUTES;
        }

        private List<TaskItem> BuildForwardLinkedTasks(string opId, TaskItem start)
        {
            var list = _ops.Where(o => o.Id == opId).SelectMany(o => o.Projects).SelectMany(p => p.Tasks)
                .OrderBy(t => t.Start).ThenBy(t => t.End).ToList();

            var chain = new List<TaskItem> { start };
            int idx = list.IndexOf(start);
            for (int i = idx; i >= 0 && i < list.Count - 1; i++)
            {
                var a = list[i];
                var b = list[i + 1];
                if (Math.Abs((b.Start - a.End).TotalMinutes) <= LINK_TOL_MINUTES) chain.Add(b);
                else break;
            }
            return chain;
        }

        private bool IsHeadProject(string opId, ProjectRow pr)
        {
            var projects = _ops.First(o => o.Id == opId).Projects
                .OrderBy(p => p.SummaryStart ?? DateTime.MaxValue).ToList();
            var idx = projects.IndexOf(pr);
            if (idx <= 0) return true;
            var prev = projects[idx - 1];
            if (!prev.SummaryEnd.HasValue || !pr.SummaryStart.HasValue) return true;
            return Math.Abs((pr.SummaryStart.Value - prev.SummaryEnd.Value).TotalMinutes) > LINK_TOL_MINUTES;
        }

        private List<ProjectRow> BuildForwardLinkedProjects(string opId, ProjectRow start)
        {
            var projects = _ops.First(o => o.Id == opId).Projects
                .OrderBy(p => p.SummaryStart ?? DateTime.MaxValue).ToList();
            var chain = new List<ProjectRow> { start };
            int idx = projects.IndexOf(start);
            for (int i = idx; i >= 0 && i < projects.Count - 1; i++)
            {
                var a = projects[i];
                var b = projects[i + 1];
                if (a.SummaryEnd.HasValue && b.SummaryStart.HasValue &&
                    Math.Abs((b.SummaryStart.Value - a.SummaryEnd.Value).TotalMinutes) <= LINK_TOL_MINUTES)
                    chain.Add(b);
                else break;
            }
            return chain;
        }

        // === BOX SELECT helpers
        private Rectangle GetBoxRect()
        {
            int x1 = Math.Min(_boxStart.X, _boxCurrent.X);
            int y1 = Math.Min(_boxStart.Y, _boxCurrent.Y);
            int x2 = Math.Max(_boxStart.X, _boxCurrent.X);
            int y2 = Math.Max(_boxStart.Y, _boxCurrent.Y);
            var rect = Rectangle.FromLTRB(x1, y1, x2, y2);

            // limita alla zona body
            rect.Intersect(_body);
            return rect;
        }
        #endregion

        #region Tastiera: Delete + Ctrl+A
        public void OnKeyDown(Keys key)
        {
            // CTRL+A invariato...

            if (key != Keys.Delete) return;

            var toDelete = new List<(string Id, string ProjectId, string JobId, string ProjectCode, string Operatore, string TaskName)>();

            void RemoveTaskSafe(TaskItem t)
            {
                var op = _ops.FirstOrDefault(o => string.Equals(o.Id, t.OpId, StringComparison.OrdinalIgnoreCase));
                if (op == null) return;
                var pr = op.Projects.FirstOrDefault(p => string.Equals(p.Code, t.ProjectCode, StringComparison.OrdinalIgnoreCase));
                if (pr == null) return;

                pr.Tasks.Remove(t);
                _allTasks.Remove(t);

                toDelete.Add((
                    t.Id,                                           // NEW: ID reale attività
                    t.ProjectId,                                    // NEW: ID progetto (se c'è)
                    $"{t.ProjectCode}-{SuffixFromTaskName(t.TaskName)}", // legacy
                    t.ProjectCode,
                    t.OpId,
                    t.TaskName
                ));
            }

            // 1) Multi-selezione
            foreach (var keySel in _multiSel.ToList())
            {
                if (keySel.StartsWith("TASK|", StringComparison.Ordinal))
                {
                    var (opId, proj, taskName) = ParseTaskKey(keySel);
                    var t = FindTaskOrNull(opId, proj, taskName);
                    if (t != null) RemoveTaskSafe(t);
                }
                else if (keySel.StartsWith("SUM|", StringComparison.Ordinal))
                {
                    var sp = keySel.Split('|'); // SUM|op|proj
                    var opId = sp[1]; var proj = sp[2];
                    var op = _ops.FirstOrDefault(o => o.Id == opId);
                    var pr = op?.Projects.FirstOrDefault(p => p.Code == proj);
                    if (pr != null)
                    {
                        foreach (var t in pr.Tasks.ToList()) RemoveTaskSafe(t);
                    }
                }
            }

            // 2) Se non c’era multi, usa selezione primaria
            if (toDelete.Count == 0 && _selOpId != null)
            {
                if (_selTask != null) // task singola
                {
                    var t = FindTaskOrNull(_selOpId, _selProj, _selTask);
                    if (t != null) RemoveTaskSafe(t);
                }
                else if (_selProj != null) // SUM progetto
                {
                    var op = _ops.FirstOrDefault(o => o.Id == _selOpId);
                    var pr = op?.Projects.FirstOrDefault(p => p.Code == _selProj);
                    if (pr != null)
                    {
                        foreach (var t in pr.Tasks.ToList()) RemoveTaskSafe(t);
                    }
                }
            }

            if (toDelete.Count > 0)
            {
                // pulizia selezioni
                ClearMultiSelection();
                SetSelection(null, null, null);

                // elimina progetti vuoti
                foreach (var op in _ops.ToList())
                {
                    foreach (var pr in op.Projects.ToList())
                    {
                        if (pr.Tasks.Count == 0)
                            op.Projects.Remove(pr);
                    }
                }

                ComputeContentHeight();
                RefreshLayout();

                // Evento con ID reali
                ItemsDeleted?.Invoke(new ItemsDeletedArgs { Items = toDelete });
            }
        }


        #endregion

        // === Auto-centering sui dati quando la finestra è vuota ===
        public bool EnsureDataVisible()
        {
            ComputeWindow();
            if (WindowHasAnyData()) return false;

            var range = DataRange();
            if (range == null) return false;

            var (min, _) = range.Value;

            switch (_zoomLevel)
            {
                case Level.Hour:
                    Reference = new DateTime(min.Year, min.Month, min.Day, min.Hour, 0, 0);
                    break;
                case Level.Day:
                    Reference = min.Date;
                    break;
                case Level.Week:
                    Reference = min;
                    break;
                case Level.Month:
                    Reference = new DateTime(min.Year, min.Month, 1);
                    break;
                case Level.Year:
                    Reference = new DateTime(min.Year, 1, 1);
                    break;
            }

            ComputeWindow();
            return true;
        }

        private bool WindowHasAnyData()
        {
            foreach (var o in _ops)
                foreach (var p in o.Projects)
                    foreach (var t in p.Tasks)
                        if (t.End > _winStart && t.Start < _winEnd)
                            return true;
            return false;
        }

        private (DateTime min, DateTime max)? DataRange()
        {
            if (_allTasks == null || _allTasks.Count == 0) return null;
            var min = _allTasks.Min(t => t.Start);
            var max = _allTasks.Max(t => t.End);
            return (min, max);
        }

        // === API pubbliche per drop dall'esterno =====================

        public bool TryGetDropTarget(Point clientPoint, out string opId, out DateTime start)
        {
            opId = null; start = default;
            if (!_body.Contains(clientPoint)) return false;

            // 1) calcolo operatore dalla Y (stessa scansione di DrawBody)
            int y = _body.Top - _vScroll;
            foreach (var op in _ops)
            {
                var rOp = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                if (rOp.Contains(new Point((_body.Left + _body.Right) / 2, clientPoint.Y)))
                {
                    opId = op.Id;
                    break;
                }
                y += RowHeight + RowGap;

                if (!op.Expanded) continue;

                foreach (var pr in op.Projects)
                {
                    // riga progetto
                    var rProj = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                    y += RowHeight + RowGap;

                    if (!pr.Expanded) continue;

                    // righe task
                    foreach (var _ in pr.Tasks)
                    {
                        var rTask = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                        y += RowHeight + RowGap;
                    }
                }
            }
            if (opId == null) return false;

            // 2) ricava tempo dalla X
            start = TimeAt(clientPoint.X);
            start = SnapStart(start);
            return true;
        }

        /// <summary>
        /// Crea o aggiorna una task nel gantt (e struttura dati interna).
        /// </summary>
        public void AddOrUpdateTask(string opId, string projectCode, string taskName, DateTime start, TimeSpan duration)
        {
            var end = SnapEnd(start + duration);

            // estrae ProjectId (se la chiave è del tipo CODICE__ATT:123)
            string projectId = ExtractProjectId(projectCode);

            var op = _ops.FirstOrDefault(o => string.Equals(o.Id, opId, StringComparison.OrdinalIgnoreCase));
            if (op == null)
            {
                op = new OperatorRow { Id = opId, Label = opId };
                _ops.Add(op);
            }

            var pr = op.Projects.FirstOrDefault(p => string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase));
            if (pr == null)
            {
                pr = new ProjectRow { Code = projectCode, Expanded = true };
                op.Projects.Add(pr);
            }

            var t = pr.Tasks.FirstOrDefault(x => string.Equals(x.TaskName, taskName, StringComparison.OrdinalIgnoreCase));
            if (t == null)
            {
                t = new TaskItem
                {
                    Id = $"{projectCode}-{SuffixFromTaskName(taskName)}",
                    ProjectId = projectId,
                    ProjectCode = projectCode,
                    TaskName = taskName,
                    OpId = opId,
                    Start = start,
                    End = end,
                    Completed = false
                };
                pr.Tasks.Add(t);
                _allTasks.Add(t);
            }
            else
            {
                t.ProjectId = projectId;    // aggiorna in caso sia null
                t.OpId = opId;
                t.Start = start;
                t.End = end;
                t.Completed = false;
            }

            pr.Tasks.Sort((a, b) =>
            {
                int oa = OrderIndex(a.TaskName), ob = OrderIndex(b.TaskName);
                if (oa != ob) return oa.CompareTo(ob);
                return a.Start.CompareTo(b.Start);
            });

            var s = t.Start; var e = t.End;
            EnforceProjectOrder(opId, projectCode, t.TaskName, ref s, ref e);
            t.Start = s; t.End = e;
        }



        /// <summary>
        /// Ricalcola finestra e contenuto per ridisegnare (da chiamare dopo modifiche esterne).
        /// </summary>
        public void RefreshAfterExternalChange()
        {
            ComputeWindow();
            ComputeContentHeight();
        }

        /// <summary>
        /// Posiziona un intero progetto (lista task ordinate) come BLOCCO compatto end->start
        /// evitando sovrapposizioni con le altre task dell'operatore. Restituisce lo start della prima.
        /// </summary>
        private DateTime PlaceBlockWithoutOverlapHard(string opId,
                                                      List<TaskItem> ordered,
                                                      List<TimeSpan> durations,
                                                      DateTime proposedStart,
                                                      int dir)
        {
            if (ordered == null || ordered.Count == 0) return proposedStart;

            // peers = tutte le altre task dell'operatore, ESCLUSE quelle del blocco
            var blockSet = new HashSet<TaskItem>(ordered);
            var peers = _ops.Where(o => o.Id == opId)
                            .SelectMany(o => o.Projects)
                            .SelectMany(p => p.Tasks)
                            .Where(t => !blockSet.Contains(t))
                            .OrderBy(t => t.Start)
                            .ToList();

            TimeSpan totalDur = TimeSpan.Zero;
            foreach (var d in durations) totalDur += d;

            DateTime s = proposedStart;
            DateTime e = s + totalDur;

            // trova il "prev" (il peer che finisce prima di s) e il "next" (il primo che inizia dopo s)
            TaskItem prev = null;
            foreach (var t in peers)
            {
                if (t.End <= s) prev = t; else break;
            }
            TaskItem next = null;
            foreach (var t in peers)
            {
                if (t.Start >= s) { next = t; break; }
            }

            // Se collide con prev o next, aggiusta:
            if (prev != null && s < prev.End) { s = prev.End; e = s + totalDur; }
            if (next != null && e > next.Start) // non ci stai tra prev e next
            {
                // se ti muovi a dx, aggancia prima di next; se a sx, aggancia dopo prev
                if (dir >= 0 && next != null) { e = next.Start; s = e - totalDur; }
                else if (dir < 0 && prev != null) { s = prev.End; e = s + totalDur; }
            }

            // ulteriore clamp: se ancora collidi, spingi lato dir
            // (gestione conflitti multipli consecutivi)
            bool changed = true; int guard = 0;
            while (changed && guard++ < 10)
            {
                changed = false;
                foreach (var j in peers)
                {
                    if (s < j.End && j.Start < e)
                    {
                        if (dir >= 0) { s = j.End; e = s + totalDur; changed = true; }
                        else { e = j.Start; s = e - totalDur; changed = true; }
                    }
                }
            }

            return s;
        }

        /// <summary>
        /// Inserisce/aggiorna un progetto composto dalle fasi indicate, come BLOCCO compatto end->start,
        /// evitando sovrapposizioni con le altre task dello stesso operatore.
        /// Ogni fase ha la stessa durata (durPerPhase).
        /// </summary>
        public void AddOrUpdateProjectBlock(string opId, string projectCode, IList<string> orderedPhases, DateTime proposedStart, TimeSpan durPerPhase)
        {
            if (orderedPhases == null || orderedPhases.Count == 0) return;

            string projectId = ExtractProjectId(projectCode);

            var op = _ops.FirstOrDefault(o => string.Equals(o.Id, opId, StringComparison.OrdinalIgnoreCase));
            if (op == null)
            {
                op = new OperatorRow { Id = opId, Label = opId };
                _ops.Add(op);
            }
            var pr = op.Projects.FirstOrDefault(p => string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase));
            if (pr == null)
            {
                pr = new ProjectRow { Code = projectCode, Expanded = true };
                op.Projects.Add(pr);
            }

            var orderedTasks = new List<TaskItem>();
            foreach (var phase in orderedPhases)
            {
                var t = pr.Tasks.FirstOrDefault(x => string.Equals(x.TaskName, phase, StringComparison.OrdinalIgnoreCase));
                if (t == null)
                {
                    t = new TaskItem
                    {
                        Id = $"{projectCode}-{SuffixFromTaskName(phase)}",
                        ProjectId = projectId,
                        ProjectCode = projectCode,
                        TaskName = phase,
                        OpId = opId,
                        Start = proposedStart,
                        End = proposedStart + durPerPhase,
                        Completed = false
                    };
                    pr.Tasks.Add(t);
                    _allTasks.Add(t);
                }
                else
                {
                    t.ProjectId = projectId; // assicurati di averla
                }
                orderedTasks.Add(t);
            }

            pr.Tasks.Sort((a, b) =>
            {
                int ra = PhaseRank(a.TaskName), rb = PhaseRank(b.TaskName);
                if (ra != rb) return ra.CompareTo(rb);
                return a.Start.CompareTo(b.Start);
            });
            orderedTasks = orderedTasks.OrderBy(t => PhaseRank(t.TaskName)).ToList();

            int dir = +1;
            DateTime blockStart = PlaceBlockWithoutOverlapHard(
                opId,
                orderedTasks,
                Enumerable.Repeat(durPerPhase, orderedTasks.Count).ToList(),
                SnapStart(proposedStart),
                dir);

            DateTime cur = blockStart;
            foreach (var t in orderedTasks)
            {
                t.Start = SnapStart(cur);
                t.End = SnapEnd(cur + durPerPhase);
                cur = t.End;
            }

            pr.Tasks.Sort((a, b) =>
            {
                int ra = PhaseRank(a.TaskName), rb = PhaseRank(b.TaskName);
                if (ra != rb) return ra.CompareTo(rb);
                return a.Start.CompareTo(b.Start);
            });
        }



        public Cursor GetHoverCursor(Point location)
        {
            if (!_body.Contains(location)) return Cursors.Default;

            foreach (var kv in _barRects)
            {
                if (!kv.Key.StartsWith("TASK|", StringComparison.Ordinal)) continue;
                var r = kv.Value;
                if (!r.Contains(location)) continue;

                if (Math.Abs(location.X - r.Left) <= RESIZE_EDGE_PX ||
                    Math.Abs(location.X - r.Right) <= RESIZE_EDGE_PX)
                    return Cursors.SizeWE;

                return Cursors.Default;
            }
            return Cursors.Default;
        }

        private void ValidateResizeWithoutOverlap(string opId, TaskItem moving, ref DateTime s, ref DateTime e, bool leftEdge)
        {
            var peers = _ops.Where(o => o.Id == opId)
                            .SelectMany(o => o.Projects)
                            .SelectMany(p => p.Tasks)
                            .Where(t => !ReferenceEquals(t, moving))
                            .OrderBy(t => t.Start)
                            .ToList();

            // assicura durata minima
            if (e - s < MIN_RESIZE_DURATION)
            {
                if (leftEdge) s = e - MIN_RESIZE_DURATION; else e = s + MIN_RESIZE_DURATION;
            }

            bool changed = true; int guard = 0;
            while (changed && guard++ < 10)
            {
                changed = false;
                foreach (var j in peers)
                {
                    if (s < j.End && j.Start < e)
                    {
                        if (leftEdge)
                        {
                            s = j.End;
                            if (e - s < MIN_RESIZE_DURATION) s = e - MIN_RESIZE_DURATION;
                            changed = true;
                        }
                        else
                        {
                            e = j.Start;
                            if (e - s < MIN_RESIZE_DURATION) e = s + MIN_RESIZE_DURATION;
                            changed = true;
                        }
                    }
                }
            }
        }

        private void EnforceProjectOrderBoundsOnResize(string opId, string proj, TaskItem moving, bool leftEdge, ref DateTime s, ref DateTime e)
        {
            var pr = _ops.First(o => o.Id == opId).Projects.First(p => p.Code == proj);
            var t3D = pr.Tasks.FirstOrDefault(t => t.TaskName == "Disegno 3D");
            var t2D = pr.Tasks.FirstOrDefault(t => t.TaskName == "Disegno 2D");
            var tDST = pr.Tasks.FirstOrDefault(t => t.TaskName == "Distinta");
            var tPRN = pr.Tasks.FirstOrDefault(t => t.TaskName == "Stampare");

            // durata minima
            if (e - s < MIN_RESIZE_DURATION)
            {
                if (leftEdge) s = e - MIN_RESIZE_DURATION; else e = s + MIN_RESIZE_DURATION;
            }

            if (string.Equals(moving.TaskName, "Disegno 2D", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(moving.TaskName, "Distinta", StringComparison.OrdinalIgnoreCase))
            {
                // bordo sinistro non prima della fine del 3D
                if (leftEdge && t3D != null && s < t3D.End) s = t3D.End;

                // bordo destro non oltre l'inizio di Stampare (se presente)
                if (!leftEdge && tPRN != null && e > tPRN.Start) e = tPRN.Start;
            }
            else if (string.Equals(moving.TaskName, "Stampare", StringComparison.OrdinalIgnoreCase))
            {
                // bordo sinistro non prima di max(fine 2D, fine Distinta) o fine 3D
                var req = new[] { t2D?.End, tDST?.End }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(t3D?.End ?? DateTime.MinValue).Max();
                if (leftEdge && req != DateTime.MinValue && s < req) s = req;
                // bordo destro: nessun vincolo specifico intra-progetto
            }
            else if (string.Equals(moving.TaskName, "Disegno 3D", StringComparison.OrdinalIgnoreCase))
            {
                // allungamento a destra non deve superare l'inizio della prima fase successiva (se NON vuoi spingere in resize)
                var nextStarts = new List<DateTime>();
                if (t2D != null) nextStarts.Add(t2D.Start);
                if (tDST != null) nextStarts.Add(tDST.Start);
                if (tPRN != null) nextStarts.Add(tPRN.Start);
                if (!leftEdge && nextStarts.Count > 0)
                {
                    var minNext = nextStarts.Min();
                    if (e > minNext) e = minNext;
                }
                // bordo sinistro: nessun vincolo specifico
            }

            // durata minima di sicurezza
            if (e - s < MIN_RESIZE_DURATION)
            {
                if (leftEdge) s = e - MIN_RESIZE_DURATION; else e = s + MIN_RESIZE_DURATION;
            }
        }


        // Ritorna 0=3D, 1=Distinta o 2D, 2=Stampare; sconosciuto = int.MaxValue
        private static int PhaseOrder(string taskName)
        {
            switch (taskName?.Trim())
            {
                case "Disegno 3D": return 0;
                case "Distinta": return 1;
                case "Disegno 2D": return 1; // stesso livello della Distinta
                case "Stampare": return 2;
                default: return int.MaxValue;
            }
        }

        // Predecessori "logici" della fase target secondo la regola richiesta
        private static IReadOnlyList<string> PredecessorPhases(string taskName)
        {
            switch (taskName?.Trim())
            {
                case "Disegno 3D":
                    return Array.Empty<string>();
                case "Distinta":
                case "Disegno 2D":
                    return new[] { "Disegno 3D" };
                case "Stampare":
                    // Il cliente ha scritto "poi distinta O 2D".
                    // Politica: per Stampare serve almeno UNO tra Distinta e Disegno 2D.
                    // Se entrambi esistono, si usa la END "più tardi" dei due.
                    return new[] { "Distinta", "Disegno 2D" };
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Trova, per un dato progetto, il vincolo di inizio più restrittivo della fase richiesta:
        /// - per Distinta o 2D: end del 3D (se esiste; se non esiste → invalid perché "3D non pianificato").
        /// - per Stampare: se esiste almeno uno tra Distinta/2D, usa la max End tra quelli presenti;
        ///                 se non ne esiste nessuno → invalid ("predecessore mancante").
        /// - per 3D: nessun vincolo.
        /// </summary>
        /// <param name="projectCode">Codice progetto</param>
        /// <param name="taskName">Nome fase target</param>
        /// <param name="earliestAllowed">Se true, valore minimo di Start consentito</param>
        /// <param name="hasPredecessor">True se il vincolo dipende da un predecessore presente</param>
        /// <returns>true se la fase può essere valutata (cioè le regole sono determinabili); false se manca un predecessore obbligatorio</returns>
        private bool ComputeEarliestAllowedStart(string projectCode, string taskName, out DateTime earliestAllowed, out bool hasPredecessor)
        {
            earliestAllowed = DateTime.MinValue;
            hasPredecessor = false;

            var preds = PredecessorPhases(taskName);
            if (preds.Count == 0)
            {
                // 3D non ha predecessori → nessun vincolo
                earliestAllowed = DateTime.MinValue;
                return true;
            }

            // Recupera TUTTE le task del PROGETTO (qualsiasi operatore)
            var allProjTasks = _ops.SelectMany(o => o.Projects)
                                   .Where(p => string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase))
                                   .SelectMany(p => p.Tasks)
                                   .ToList();

            if (taskName == "Distinta" || taskName == "Disegno 2D")
            {
                // Serve il 3D
                var t3d = allProjTasks.Where(t => t.TaskName == "Disegno 3D").OrderByDescending(t => t.End).FirstOrDefault();
                if (t3d == null)
                {
                    // 3D non pianificato → impossibile valutare Start consentito
                    hasPredecessor = false;
                    return false;
                }
                earliestAllowed = t3d.End;
                hasPredecessor = true;
                return true;
            }

            if (taskName == "Stampare")
            {
                // Serve almeno uno tra Distinta e 2D; se entrambi esistono prendi la END più "tarda"
                var tDst = allProjTasks.Where(t => t.TaskName == "Distinta").OrderByDescending(t => t.End).FirstOrDefault();
                var t2d = allProjTasks.Where(t => t.TaskName == "Disegno 2D").OrderByDescending(t => t.End).FirstOrDefault();

                if (tDst == null && t2d == null)
                {
                    hasPredecessor = false; // nessun predecessore presente
                    return false;
                }

                if (tDst != null && t2d != null)
                    earliestAllowed = (tDst.End >= t2d.End) ? tDst.End : t2d.End;
                else
                    earliestAllowed = (tDst != null) ? tDst.End : t2d.End;

                hasPredecessor = true;
                return true;
            }

            // Default sicuro
            earliestAllowed = DateTime.MinValue;
            hasPredecessor = false;
            return true;
        }

        /// <summary>
        /// Verifica se (Start,End) proposti rispettano il vincolo di precedenza globale
        /// per il progetto/fase indicati. Ritorna anche l’eventuale Start minimo consentito.
        /// </summary>
        private bool IsPlacementValidByPrecedence(string projectCode, string taskName, DateTime start, DateTime end, out DateTime minStartAllowed)
        {
            if (!ComputeEarliestAllowedStart(projectCode, taskName, out minStartAllowed, out var hasPred))
            {
                // Predecessore obbligatorio mancante → sempre non valido
                return false;
            }

            // Se non ci sono predecessori (3D) o non c'è vincolo concreto, è valido
            if (!hasPred) return true;

            // Valido solo se lo Start proposto è >= vincolo
            return start >= minStartAllowed;
        }

        /// <summary>
        /// Avvia l’anteprima di inserimento esterno (drag da DGV al Gantt).
        /// Durante il move del mouse sul Gantt verranno mostrati grigio/verde/rosso.
        /// </summary>
        public void BeginInsertPreview(string opId, string projectCode, string taskName, TimeSpan expectedDuration)
        {
            _inserting = true;
            _insertOpId = opId;
            _insertProj = projectCode;
            _insertTaskName = taskName;
            _insertDuration = expectedDuration;

            _previewActive = true;
            _previewOkRect = _previewNoRect = _previewDurRect = null;
        }

        /// <summary>
        /// Annulla l’anteprima di inserimento (ad esempio se l’utente esce dall’area Gantt o cancella l’operazione).
        /// </summary>
        public void CancelInsertPreview()
        {
            _inserting = false;
            _insertOpId = _insertProj = _insertTaskName = null;
            _insertDuration = TimeSpan.Zero;

            _previewActive = false;
            _previewOkRect = _previewNoRect = _previewDurRect = null;
        }
        private bool TryGetOperatorRowRect(string opId, out Rectangle rowRect)
        {
            int y = _body.Top - _vScroll;

            foreach (var op in _ops)
            {
                var rOp = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                if (string.Equals(op.Id, opId, StringComparison.OrdinalIgnoreCase))
                {
                    rowRect = rOp;
                    return true;
                }

                y += RowHeight + RowGap;

                if (!op.Expanded) continue;

                foreach (var pr in op.Projects)
                {
                    // riga progetto
                    y += RowHeight + RowGap;

                    if (!pr.Expanded) continue;

                    // righe task
                    y += pr.Tasks.Count * (RowHeight + RowGap);
                }
            }

            rowRect = Rectangle.Empty;
            return false;
        }

        /// <summary>
        /// Estrae l'ID attività dalla chiave composita del progetto,
        /// es. "ABC123__ATT:456"  →  "456".
        /// Restituisce null se il suffisso non è presente o non è numerico.
        /// </summary>
        private static string ExtractProjectId(string projectCode)
        {
            if (string.IsNullOrWhiteSpace(projectCode)) return null;

            const string TAG = "__ATT:";
            int idx = projectCode.LastIndexOf(TAG, StringComparison.Ordinal);
            if (idx < 0) return null;

            string tail = projectCode.Substring(idx + TAG.Length).Trim();
            if (string.IsNullOrEmpty(tail)) return null;

            // prendi solo la parte numerica iniziale (tollerante ad eventuali caratteri dopo)
            int i = 0;
            while (i < tail.Length && char.IsDigit(tail[i])) i++;

            string num = tail.Substring(0, i);
            return string.IsNullOrWhiteSpace(num) ? null : num;
        }

        // Ritorna il rettangolo della riga operatore (colonna destra/body). Null se non visibile.
        private Rectangle? TryGetOperatorRowRect(string opId)
        {
            int y = _body.Top - _vScroll;
            foreach (var op in _ops)
            {
                var rOp = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                if (string.Equals(op.Id, opId, StringComparison.OrdinalIgnoreCase))
                    return rOp;

                y += RowHeight + RowGap;

                if (!op.Expanded) continue;

                foreach (var pr in op.Projects)
                {
                    // riga progetto
                    y += RowHeight + RowGap;

                    if (!pr.Expanded) continue;

                    // righe task (in ordine di display)
                    foreach (var _ in GetDisplayTasks(pr))
                        y += RowHeight + RowGap;
                }
            }
            return null;
        }

        // Ritorna il rettangolo della riga task (colonna destra/body). Null se non visibile.
        private Rectangle? TryGetTaskRowRect(string opId, string proj, string taskName)
        {
            int y = _body.Top - _vScroll;
            foreach (var op in _ops)
            {
                var rOp = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                y += RowHeight + RowGap;

                if (!op.Expanded) { continue; }

                foreach (var pr in op.Projects)
                {
                    var rProj = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                    y += RowHeight + RowGap;

                    if (!pr.Expanded) continue;

                    foreach (var t in GetDisplayTasks(pr))
                    {
                        var rTask = new Rectangle(_body.Left, y, _body.Width, RowHeight);
                        if (string.Equals(op.Id, opId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(pr.Code, proj, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(t.TaskName, taskName, StringComparison.OrdinalIgnoreCase))
                            return rTask;

                        y += RowHeight + RowGap;
                    }
                }
            }
            return null;
        }
    }
}