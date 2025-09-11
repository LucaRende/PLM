using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PLM.Classe;

namespace PLM
{
    public partial class Form_UfficioTecnico_Pianificazione : Form
    {
        int ID_AttivitaCRM_Selezionata = 0;

        List<Classe.Attivita_CRM> listaAttivitaCrmFiltrata = new List<Classe.Attivita_CRM>();
        List<Classe.ProgettoInAttivitaCRM> listaProgettiInAttivitaCrmFiltrata = new List<Classe.ProgettoInAttivitaCRM>();

        // ------------------------------------------------------------------- GANTT -----------------------------------------------------
        private readonly Gantt gantt = new();   // <-- CAMPO di classe
        private PLM.Gantt.PlanningChangedArgs _ultimoCambioGantt;

        // Durata fissa (in ore) per ogni task creata via drag&drop
        public int GrandezzaFissaTask = 2;

        // Flag per evitare di collegare gli handler più volte
        private bool _dragWiredProjects = false;
        private bool _dragWiredTasks = false;

        private static bool HasProp(object obj, string name)
            => obj.GetType().GetProperty(name) != null;

        private static void SetStringProp(object obj, string name, string value)
        {
            var p = obj.GetType().GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }
        private static void SetDateProp(object obj, string name, DateTime? value)
        {
            var p = obj.GetType().GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }
        private static void SetDoubleProp(object obj, string name, double? value)
        {
            var p = obj.GetType().GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }
        private static void SetBoolProp(object obj, string name, bool value)
        {
            var p = obj.GetType().GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }

        // Payload per il drag dal DataGridView
        private class DragLavorazioniPayload
        {
            public List<Classe.Lavorazioni_UfficioTecnico> Items { get; set; } = new();
            public int DurataOre { get; set; } = 8; // default
            public List<string> SelectedPhases { get; set; } = new(); // fasi da piazzare (ordinate)
        }

        //--------------------------------------------------------------------------------------------------------------------------------------

        public Form_UfficioTecnico_Pianificazione()
        {
            InitializeComponent();

            // Sostituzione pannello con NoFlickerPanel
            var old = pn_Contenitore_Gantt;
            var pan = new NoFlickerPanel()
            {
                Name = old.Name,
                Dock = old.Dock,
                Location = old.Location,
                Size = old.Size,
                BackColor = old.BackColor
            };
            var parent = old.Parent;
            int idx = parent.Controls.GetChildIndex(old);
            parent.Controls.Remove(old);
            parent.Controls.Add(pan);
            parent.Controls.SetChildIndex(pan, idx);
            pn_Contenitore_Gantt = pan; // riassegna il campo della form

            // Tasti inoltrati al Gantt
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                // Inoltra Canc e Ctrl+A al Gantt
                if (e.KeyCode == Keys.Delete || (e.KeyCode == Keys.A && (ModifierKeys & Keys.Control) == Keys.Control))
                {
                    gantt.OnKeyDown(e.KeyCode);
                    pn_Contenitore_Gantt.Invalidate();
                    e.Handled = true;
                }
            };
        }

        private void Form_UfficioTecnico_Pianificazione_Load(object sender, EventArgs e)
        {
            MetodiSql.CaricaDatiCRM();
            listaAttivitaCrmFiltrata = MetodiSql.listaAttivita_CRM.Where(x => x.aperta == true).ToList();

            // Grafica ------------ dgw_AttivitaCRM ----------------------
            dgw_AttivitaCRM.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgw_AttivitaCRM.ColumnHeadersHeight = 40;
            dgw_AttivitaCRM.EnableHeadersVisualStyles = false;
            dgw_AttivitaCRM.ColumnHeadersDefaultCellStyle.Padding = new Padding(15, 8, 0, 8);
            dgw_AttivitaCRM.ForeColor = Color.Black;
            dgw_AttivitaCRM.AutoGenerateColumns = false;
            dgw_AttivitaCRM.DataSource = null;
            dgw_AttivitaCRM.DataSource = listaAttivitaCrmFiltrata;

            // Grafica ------------ dgw_LavorazioniInAttivita ----------------------
            dgw_LavorazioniInAttivita.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgw_LavorazioniInAttivita.ColumnHeadersHeight = 40;
            dgw_LavorazioniInAttivita.EnableHeadersVisualStyles = false;
            dgw_LavorazioniInAttivita.ColumnHeadersDefaultCellStyle.Padding = new Padding(15, 8, 0, 8);
            dgw_LavorazioniInAttivita.ForeColor = Color.Black;
            dgw_LavorazioniInAttivita.AutoGenerateColumns = false;
            dgw_LavorazioniInAttivita.MultiSelect = true;

            // Grafica ------------ dgw_ProgettiInAttivita ----------------------
            dgw_ProgettiInAttivita.MultiSelect = true;

            //-------------------------------------------- Gantt -------------------------------------------------------------------
            InizializzaGantt();

            gantt.ItemsDeleted += HandleGanttItemsDeleted;
            dgw_ProgettiInAttivita.CellDoubleClick += dgw_ProgettiInAttivita_CellDoubleClick;


            gantt.PlanningChanged += args =>
            {
                _ultimoCambioGantt = args;           // memorizza l'ultima modifica
                AggiornaDatiDopoModificaManualeGantt();
            };

            // Wiring drag (una volta sola)
            WireDragFromProjectsGrid();   // drag da dgw_ProgettiInAttivita
            WireDragFromTasksGrid();      // drag da dgw_LavorazioniInAttivita
        }

        private static List<LavorazioneStato> EstraiStatoLavorazioni(Classe.Lavorazioni_UfficioTecnico x)
        {
            var outList = new List<LavorazioneStato>();
            if (x == null) return outList;

            var groups = new Dictionary<string, (bool daFare, bool assegnato, bool fatto)>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in x.GetType().GetProperties().Where(pi => pi.PropertyType == typeof(bool)))
            {
                bool val = (bool)(p.GetValue(x) ?? false);
                var t = p.Name.Split('_');                 // es: ["Disegno","2D","DaFare"] | ["Distinta","Fatto"] | ["Stampare","DaFare"]

                if (t.Length < 2) continue;

                string baseName = t[0];
                string phase = null;
                string status;

                if (t.Length == 3) { phase = t[1]; status = t[2]; }   // es: Disegno_2D_DaFare
                else
                {
                    status = t[1];                   // es: Distinta_Fatto o Stampare_DaFare
                    if (baseName.Equals("Stampare", StringComparison.OrdinalIgnoreCase))
                        phase = "2D";               // normalizza Stampare_DaFare -> Stampare_2D_DaFare
                }

                string key = phase != null ? $"{baseName}_{phase}" : baseName; // "Disegno_2D", "Distinta", "Stampare_2D"

                var s = groups.TryGetValue(key, out var tmp) ? tmp : default;
                if (status.Equals("DaFare", StringComparison.OrdinalIgnoreCase)) s.daFare |= val;
                else if (status.Equals("Assegnato", StringComparison.OrdinalIgnoreCase)) s.assegnato |= val;
                else if (status.Equals("Fatto", StringComparison.OrdinalIgnoreCase)) s.fatto |= val;
                groups[key] = s;
            }

            foreach (var kv in groups)
            {
                var (daFare, assegnato, fatto) = kv.Value;
                if (!(daFare || assegnato || fatto)) continue; // mostra solo fasi “attive”; rimuovi se vuoi tutte

                outList.Add(new LavorazioneStato
                {
                    lavorazione = kv.Key.Replace("_", " "),   // "Disegno 2D", "Distinta", "Stampare 2D"
                    statoCompletato = fatto,
                    statoAssegnato = assegnato
                });
            }

            // ---- ORDINAMENTO di visualizzazione (come vanno fatte)
            int Rank(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return int.MaxValue;
                var n = name.Trim();
                if (n.StartsWith("Stampare", StringComparison.OrdinalIgnoreCase)) n = "Stampare";
                // usa lo stesso ordine logico che utilizzi per i piazzamenti
                var idx = Array.IndexOf(OrdineLogicoFasi, n);
                return idx < 0 ? int.MaxValue : idx;
            }

            return outList.OrderBy(l => Rank(l.lavorazione)).ToList();
        }


        private void AggiornaDatiDopoModificaManualeGantt()
        {
            if (_ultimoCambioGantt == null) return;
            var args = _ultimoCambioGantt;

            // Determina una "effective key":
            // 1) preferisci ProjectKey (codice__ATT:id) se il Gantt la fornisce
            // 2) altrimenti, se ProjectCode contiene già "__ATT:", usalo
            // 3) fallback legacy: ProjectCode semplice (non ideale, ma compat)
            string effectiveKey = null;
            var projectKeyProp = args.GetType().GetProperty("ProjectKey"); // nel caso tu abbia già aggiunto ProjectKey in PlanningChangedArgs
            if (projectKeyProp != null)
            {
                var pk = projectKeyProp.GetValue(args) as string;
                if (!string.IsNullOrWhiteSpace(pk)) effectiveKey = pk;
            }
            if (string.IsNullOrWhiteSpace(effectiveKey))
            {
                if (!string.IsNullOrWhiteSpace(args.ProjectCode) && args.ProjectCode.Contains("__ATT:", StringComparison.Ordinal))
                    effectiveKey = args.ProjectCode;
            }
            if (string.IsNullOrWhiteSpace(effectiveKey))
                effectiveKey = args.ProjectCode ?? "N/D";

            // Risolvi il record UT tramite chiave composta
            Classe.Lavorazioni_UfficioTecnico ut = FindUT(effectiveKey);

            // Se non trovato, crealo usando lo split della chiave composta
            if (ut == null)
            {
                SplitProjectKey(effectiveKey, out var codeSplit, out var idSplit);
                ut = new Classe.Lavorazioni_UfficioTecnico
                {
                    ID_Attivita = idSplit ?? 0,
                    codiceProgetto = codeSplit
                };
                MetodiSql.listalavorazioniInProgetto_UfficioTecnico.Add(ut);
            }

            double durataOre = Math.Max(0.01, (args.End - args.Start).TotalHours);

            switch (args.TaskName)
            {
                case "Disegno 3D":
                    SetBoolProp(ut, "Disegno_3D_Assegnato", true);
                    SetStringProp(ut, "Operatore_Disegno_3D_Assegnato", args.Operatore);
                    SetDateProp(ut, "Data_Disegno_3D_Assegnato_InizioPrevista", args.Start);
                    SetDateProp(ut, "Data_Disegno_3D_Assegnato_FinePrevista", args.End);
                    SetDoubleProp(ut, "Tempo_Disegno_3D_Prevista", durataOre);
                    break;

                case "Distinta":
                    SetBoolProp(ut, "Distinta_Assegnato", true);
                    SetStringProp(ut, "Operatore_Distinta_Assegnato", args.Operatore);
                    SetDateProp(ut, "Data_Distinta_Assegnato_InizioPrevista", args.Start);
                    SetDateProp(ut, "Data_Distinta_Assegnato_FinePrevista", args.End);
                    SetDoubleProp(ut, "Tempo_Distinta_Prevista", durataOre);
                    break;

                case "Disegno 2D":
                    ut.Disegno_2D_Assegnato = true;
                    ut.Operatore_Disegno_2D_Assegnato = args.Operatore;
                    ut.Data_Disegno_2D_Assegnato_InizioPrevista = args.Start;
                    ut.Data_Disegno_2D_Assegnato_FinePrevista = args.End;
                    ut.Tempo_Disegno_2D_Prevista = durataOre;
                    break;

                case "Stampare":
                    if (HasProp(ut, "Operatore_Stampare_Assegnato"))
                    {
                        SetBoolProp(ut, "Stampare_Assegnato", true);
                        SetStringProp(ut, "Operatore_Stampare_Assegnato", args.Operatore);
                        SetDateProp(ut, "Data_Stampare_Assegnato_InizioPrevista", args.Start);
                        SetDateProp(ut, "Data_Stampare_Assegnato_FinePrevista", args.End);
                        SetDoubleProp(ut, "Tempo_Stampare_Prevista", durataOre);
                    }
                    else
                    {
                        ut.Stampare_2D_Assegnato = true;
                        SetStringProp(ut, "Operatore_Stampare_2D_Assegnato", args.Operatore);
                        SetDateProp(ut, "Data_Stampare_2D_Assegnato_InizioPrevista", args.Start);
                        SetDateProp(ut, "Data_Stampare_2D_Assegnato_FinePrevista", args.End);
                        SetDoubleProp(ut, "Tempo_Stampare_2D_Prevista", durataOre);
                    }
                    break;
            }

            // Scrivi CSV
            MetodiCsv.scritturaCsv(MetodiSql.db_LavorazioniUfficioTecnico,
                                   MetodiSql.listalavorazioniInProgetto_UfficioTecnico);

            // Rebind griglie
            RebindGridsAfterUTChange();
        }

        public void InizializzaGantt()
        {
            // setup iniziale
            gantt.SetViewFromCombo("giornaliera");

            gantt.LoadFromUfficioTecnico(MetodiSql.listalavorazioniInProgetto_UfficioTecnico,
                                         MetodiSql.listaOperatori);

            gantt.OnResize(pn_Contenitore_Gantt.ClientRectangle);

            pn_Contenitore_Gantt.Resize += (_, __) =>
            {
                gantt.OnResize(pn_Contenitore_Gantt.ClientRectangle);
                pn_Contenitore_Gantt.Invalidate();
            };

            pn_Contenitore_Gantt.Paint += (_, e) => { gantt.OnPaint(e.Graphics); };

            pn_Contenitore_Gantt.MouseWheel += (_, e) =>
            {
                bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
                gantt.OnMouseWheel(e.Location, e.Delta, ctrl, shift);
                pn_Contenitore_Gantt.Invalidate();
            };

            pn_Contenitore_Gantt.MouseDown += (_, e) =>
            {
                pn_Contenitore_Gantt.Focus();
                gantt.OnMouseDown(e.Location, e.Button);
                pn_Contenitore_Gantt.Invalidate();
            };

            pn_Contenitore_Gantt.MouseMove += (_, e) =>
            {
                // mostra il cursore di resize quando sono sui bordi di una barra
                pn_Contenitore_Gantt.Cursor = gantt.GetHoverCursor(e.Location);

                gantt.OnMouseMove(e.Location, e.Button);

                // invalida sempre quando si trascina (drag task/progetto, resize o box)
                if (e.Button == MouseButtons.Left) pn_Contenitore_Gantt.Invalidate();
            };

            pn_Contenitore_Gantt.MouseUp += (_, e) =>
            {
                var change = gantt.OnMouseUp(e.Location, e.Button);
                if (change != null)
                {
                    _ultimoCambioGantt = change;
                    AggiornaDatiDopoModificaManualeGantt();
                }
                pn_Contenitore_Gantt.Invalidate();
            };

            pn_Contenitore_Gantt.MouseClick += (_, e) =>
            {
                gantt.OnClick(e.Location);
                pn_Contenitore_Gantt.Invalidate();
            };

            // ====================== Drag & Drop dal DGV con PREVIEW ======================
            pn_Contenitore_Gantt.AllowDrop = true;

            // 1) DragEnter: controlla il payload
            pn_Contenitore_Gantt.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(DragLavorazioniPayload)))
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            };

            // 2) DragOver: attiva/aggiorna PREVIEW (bloccando se già pianificato)
            pn_Contenitore_Gantt.DragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent(typeof(DragLavorazioniPayload)))
                {
                    e.Effect = DragDropEffects.None;
                    gantt.CancelInsertPreview();
                    pn_Contenitore_Gantt.Invalidate();
                    return;
                }

                var payload = (DragLavorazioniPayload)e.Data.GetData(typeof(DragLavorazioniPayload));
                var clientPt = pn_Contenitore_Gantt.PointToClient(new Point(e.X, e.Y));

                if (!gantt.TryGetDropTarget(clientPt, out var opId, out var startPreview))
                {
                    e.Effect = DragDropEffects.None;
                    gantt.CancelInsertPreview();
                    pn_Contenitore_Gantt.Invalidate();
                    return;
                }

                // Prendi un UT di contesto
                var ut = payload.Items.FirstOrDefault();
                if (ut == null)
                {
                    e.Effect = DragDropEffects.None;
                    gantt.CancelInsertPreview();
                    pn_Contenitore_Gantt.Invalidate();
                    return;
                }

                // Fasi selezionate dal payload (filtrate da “già pianificato”)
                var fasiRaw = (payload.SelectedPhases != null && payload.SelectedPhases.Count > 0)
                                ? payload.SelectedPhases
                                : FasiDaPiazzareInOrdine(ut);

                var fasi = fasiRaw.Where(f => !FaseGiaAssegnata(ut, f)).ToList();

                // Se non c'è nulla di piazzabile → nessuna preview (non consentire drop)
                if (fasi.Count == 0)
                {
                    e.Effect = DragDropEffects.None;
                    gantt.CancelInsertPreview();
                    pn_Contenitore_Gantt.Invalidate();
                    return;
                }

                // Durata totale per preview grigia
                TimeSpan durPerPhase = TimeSpan.FromHours(Math.Max(1, payload.DurataOre));
                TimeSpan totalDur = TimeSpan.FromHours(Math.Max(1, payload.DurataOre) * fasi.Count);

                string projectKey = BuildProjectKey(ut);

                // Accendi preview con la PRIMA fase (per precedenze)
                gantt.BeginInsertPreview(opId, projectKey, fasi[0], totalDur);

                // Simula mouse move per disegnare overlay
                gantt.OnMouseMove(clientPt, MouseButtons.Left);
                pn_Contenitore_Gantt.Invalidate();

                e.Effect = DragDropEffects.Copy;
            };

            // 3) DragLeave: spegni preview
            pn_Contenitore_Gantt.DragLeave += (s, e) =>
            {
                gantt.CancelInsertPreview();
                pn_Contenitore_Gantt.Invalidate();
            };

            // 4) DragDrop: COMMIT con chiavi composite e blocco duplicati
            pn_Contenitore_Gantt.DragDrop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(typeof(DragLavorazioniPayload))) return;

                var payload = (DragLavorazioniPayload)e.Data.GetData(typeof(DragLavorazioniPayload));
                var clientPoint = pn_Contenitore_Gantt.PointToClient(new Point(e.X, e.Y));

                gantt.CancelInsertPreview(); // rimuovi overlay prima del commit

                if (!gantt.TryGetDropTarget(clientPoint, out var opId, out var start))
                    return;

                bool changedAnything = false;

                foreach (var ut in payload.Items.DistinctBy(u => (u.ID_Attivita, u.codiceProgetto)))
                {
                    string projectKey = BuildProjectKey(ut);

                    var raw = (payload.SelectedPhases != null && payload.SelectedPhases.Count > 0)
                              ? payload.SelectedPhases
                              : FasiDaPiazzareInOrdine(ut);

                    // Filtra doppioni già pianificati
                    var fasi = raw.Where(f => !FaseGiaAssegnata(ut, f)).ToList();
                    if (fasi.Count == 0) continue; // niente da fare per questo progetto

                    TimeSpan dur = TimeSpan.FromHours(Math.Max(1, payload.DurataOre));

                    // Inserisci blocco compatto nel Gantt
                    gantt.AddOrUpdateProjectBlock(opId, projectKey, fasi, start, dur);

                    // Aggiorna modello UT per ogni fase piazzata
                    DateTime cur = start;
                    foreach (var fase in fasi)
                    {
                        ScriviAssegnazioneNelModello(ut, fase, opId, cur, cur + dur, payload.DurataOre);
                        cur = cur + dur;
                    }

                    changedAnything = true;
                }

                if (!changedAnything)
                {
                    // Tutto già pianificato → nessuna azione
                    pn_Contenitore_Gantt.Invalidate();
                    return;
                }

                // salva e refresh
                MetodiCsv.scritturaCsv(MetodiSql.db_LavorazioniUfficioTecnico,
                                       MetodiSql.listalavorazioniInProgetto_UfficioTecnico);

                gantt.RefreshAfterExternalChange();
                pn_Contenitore_Gantt.Invalidate();

                // Rebind griglie
                RebindGridsAfterUTChange();
            };

        }


        private void RebindGridsAfterUTChange()
        {
            // 1) Ricarica dal CSV la lista UT globale (fonte della verità)
            MetodiSql.listalavorazioniInProgetto_UfficioTecnico =
                MetodiCsv.letturaCsv<Classe.Lavorazioni_UfficioTecnico>(MetodiSql.db_LavorazioniUfficioTecnico) ?? new List<Classe.Lavorazioni_UfficioTecnico>();

            // 2) Selezione corrente della griglia Progetti
            Classe.ProgettoInAttivitaCRM selProj = null;
            if (dgw_ProgettiInAttivita.CurrentRow?.DataBoundItem is Classe.ProgettoInAttivitaCRM cur)
                selProj = cur;
            else if (dgw_ProgettiInAttivita.SelectedRows.Count > 0 &&
                     dgw_ProgettiInAttivita.SelectedRows[0].DataBoundItem is Classe.ProgettoInAttivitaCRM cur2)
                selProj = cur2;

            // 3) Selezione corrente dell'attività CRM (per rigenerare l'elenco progetti filtrato)
            Classe.Attivita_CRM selAtt = null;
            if (dgw_AttivitaCRM.CurrentRow?.DataBoundItem is Classe.Attivita_CRM a) selAtt = a;

            // 4) Rilegge i progetti in attività dal CSV (come fai in SelectionChanged)
            MetodiSql.listaProgettiInAttivita_CRM = MetodiCsv.letturaCsv<Classe.ProgettoInAttivitaCRM>(MetodiSql.db_ProgettiInAttivita_CRM_PlmNuovo)
                                                   ?? new List<Classe.ProgettoInAttivitaCRM>();

            listaProgettiInAttivitaCrmFiltrata = (MetodiSql.listaProgettiInAttivita_CRM ?? Enumerable.Empty<Classe.ProgettoInAttivitaCRM>())
                .Where(x => x.lavorazioniCompletate == false)
                .Where(p => selAtt == null || p.ID_Attivita == selAtt.ID_Attivita)
                .ToList();

            // 5) Rebind griglia Progetti conservando, se possibile, la riga selezionata
            dgw_ProgettiInAttivita.DataSource = null;
            dgw_ProgettiInAttivita.DataSource = listaProgettiInAttivitaCrmFiltrata;

            if (selProj != null)
            {
                var idx = listaProgettiInAttivitaCrmFiltrata.FindIndex(p =>
                    p.ID_Attivita == selProj.ID_Attivita &&
                    string.Equals(p.codiceProgetto, selProj.codiceProgetto, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) dgw_ProgettiInAttivita.Rows[idx].Selected = true;
            }

            // 6) Rebind griglia Lavorazioni per il progetto correntemente selezionato
            BindLavorazioniGridFor(selProj);

            // 7) Refresh UI
            dgw_ProgettiInAttivita.Refresh();
            dgw_LavorazioniInAttivita.Refresh();
        }


        private void BindLavorazioniGridFor(Classe.ProgettoInAttivitaCRM selProj)
        {
            List<LavorazioneStato> stato = new();

            if (selProj != null)
            {
                // Cerca la riga UT del progetto selezionato nella lista UT appena ricaricata
                var ut = MetodiSql.listalavorazioniInProgetto_UfficioTecnico
                         .FirstOrDefault(x => x.ID_Attivita == selProj.ID_Attivita &&
                                              string.Equals(x.codiceProgetto, selProj.codiceProgetto, StringComparison.OrdinalIgnoreCase));

                stato = EstraiStatoLavorazioni(ut);
            }

            dgw_LavorazioniInAttivita.DataSource = null;
            dgw_LavorazioniInAttivita.DataSource = stato;
        }


        private static readonly string[] OrdineLogicoFasi = { "Disegno 3D", "Disegno 2D", "Distinta", "Stampare" };

        private static bool FaseDaFare(Classe.Lavorazioni_UfficioTecnico ut, string fase)
        {
            // Legge i bool *DaFare* dal modello UT (gestisce sia “Stampare” che “Stampare_2D”)
            try
            {
                switch (fase)
                {
                    case "Disegno 3D":
                        return (bool?)ut?.GetType().GetProperty("Disegno_3D_DaFare")?.GetValue(ut) == true;

                    case "Disegno 2D":
                        return (bool?)ut?.GetType().GetProperty("Disegno_2D_DaFare")?.GetValue(ut) == true;

                    case "Distinta":
                        return (bool?)ut?.GetType().GetProperty("Distinta_DaFare")?.GetValue(ut) == true;

                    case "Stampare":
                        // compatibilità: alcuni DB usano Stampare_2D_DaFare
                        var p1 = (bool?)ut?.GetType().GetProperty("Stampare_DaFare")?.GetValue(ut) == true;
                        var p2 = (bool?)ut?.GetType().GetProperty("Stampare_2D_DaFare")?.GetValue(ut) == true;
                        return p1 || p2;
                }
            }
            catch { }
            return false;
        }

        private static int PhaseRankForm(string fase)
        {
            if (string.Equals(fase, "Disegno 3D", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(fase, "Disegno 2D", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(fase, "Distinta", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(fase, "Stampare", StringComparison.OrdinalIgnoreCase)) return 2;
            return 99;
        }

        // ---------------------- Wiring Drag dai DataGridView ----------------------

        private void WireDragFromProjectsGrid()
        {
            if (_dragWiredProjects) return;
            _dragWiredProjects = true;

            dgw_ProgettiInAttivita.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (dgw_ProgettiInAttivita.SelectedRows.Count == 0) return;

                var sel = new List<Classe.Lavorazioni_UfficioTecnico>();

                foreach (DataGridViewRow r in dgw_ProgettiInAttivita.SelectedRows)
                {
                    if (r?.DataBoundItem is Classe.ProgettoInAttivitaCRM pj)
                    {
                        var uts = MetodiCsv.letturaCsv<Classe.Lavorazioni_UfficioTecnico>(MetodiSql.db_LavorazioniUfficioTecnico)
                            .Where(x => x.ID_Attivita == pj.ID_Attivita && x.codiceProgetto == pj.codiceProgetto)
                            .ToList();

                        if (uts.Count > 0) sel.AddRange(uts);
                    }
                }
                if (sel.Count == 0) return;

                // Costruisci elenco fasi piazzabili (DaFare & non assegnate) conservando ordine logico
                var fasiPiazzabili = new List<string>();
                foreach (var ut in sel)
                {
                    foreach (var f in FasiDaPiazzareInOrdine(ut))
                        if (!fasiPiazzabili.Contains(f, StringComparer.OrdinalIgnoreCase))
                            fasiPiazzabili.Add(f);
                }

                // Se non c'è nulla di piazzabile, NON parte il drag (≡ attività già pianificate)
                if (fasiPiazzabili.Count == 0) return;

                var payload = new DragLavorazioniPayload
                {
                    Items = sel,
                    DurataOre = Math.Max(1, GrandezzaFissaTask),
                    SelectedPhases = fasiPiazzabili
                };

                dgw_ProgettiInAttivita.DoDragDrop(payload, DragDropEffects.Copy);
            };
        }


        private void WireDragFromTasksGrid()
        {
            if (_dragWiredTasks) return;
            _dragWiredTasks = true;

            dgw_LavorazioniInAttivita.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (dgw_LavorazioniInAttivita.SelectedRows.Count == 0) return;

                // progetto selezionato in dgw_ProgettiInAttivita
                var rowsProj = dgw_ProgettiInAttivita.SelectedRows.Cast<DataGridViewRow>().ToList();
                if (rowsProj.Count == 0 && dgw_ProgettiInAttivita.CurrentRow != null)
                    rowsProj = new List<DataGridViewRow> { dgw_ProgettiInAttivita.CurrentRow };
                if (rowsProj.Count == 0) return;

                var sel = new List<Classe.Lavorazioni_UfficioTecnico>();
                foreach (var r in rowsProj)
                {
                    if (r?.DataBoundItem is Classe.ProgettoInAttivitaCRM pj)
                    {
                        var uts = MetodiCsv.letturaCsv<Classe.Lavorazioni_UfficioTecnico>(MetodiSql.db_LavorazioniUfficioTecnico)
                            .Where(x => x.ID_Attivita == pj.ID_Attivita && x.codiceProgetto == pj.codiceProgetto)
                            .ToList();

                        if (uts.Count > 0) sel.AddRange(uts);
                    }
                }
                if (sel.Count == 0) return;

                // fasi selezionate nella griglia lavorazioni (testo colonna)
                var selectedPhases = new List<string>();
                foreach (DataGridViewRow r in dgw_LavorazioniInAttivita.SelectedRows)
                {
                    if (r?.DataBoundItem is LavorazioneStato ls && !string.IsNullOrWhiteSpace(ls.lavorazione))
                    {
                        var f = ls.lavorazione.Trim();
                        if (f.StartsWith("Stampare", StringComparison.OrdinalIgnoreCase)) f = "Stampare";
                        selectedPhases.Add(f);
                    }
                }
                if (selectedPhases.Count == 0) return;

                // Filtra: non trascinare fasi già pianificate
                var selectable = new List<string>();
                foreach (var ut in sel)
                {
                    foreach (var f in selectedPhases.Distinct(StringComparer.OrdinalIgnoreCase))
                        if (!FaseGiaAssegnata(ut, f) && FaseDaFare(ut, f))
                            if (!selectable.Contains(f, StringComparer.OrdinalIgnoreCase))
                                selectable.Add(f);
                }

                if (selectable.Count == 0) return; // tutte già piazzate → NON parte il drag

                var payload = new DragLavorazioniPayload
                {
                    Items = sel,
                    DurataOre = Math.Max(1, GrandezzaFissaTask),
                    SelectedPhases = selectable
                };

                dgw_LavorazioniInAttivita.DoDragDrop(payload, DragDropEffects.Copy);
            };
        }


        // ---------------------- Helpers di mapping ----------------------

        private void ScriviAssegnazioneNelModello(
      Classe.Lavorazioni_UfficioTecnico ut, string fase,
      string operatore, DateTime start, DateTime end, int durataOre)
        {
            double d = Math.Max(0.01, durataOre);

            switch (fase)
            {
                case "Disegno 3D":
                    SetBoolProp(ut, "Disegno_3D_Assegnato", true);
                    SetStringProp(ut, "Operatore_Disegno_3D_Assegnato", operatore);
                    SetDateProp(ut, "Data_Disegno_3D_Assegnato_InizioPrevista", start);
                    SetDateProp(ut, "Data_Disegno_3D_Assegnato_FinePrevista", end);
                    SetDoubleProp(ut, "Tempo_Disegno_3D_Prevista", d);
                    if (HasProp(ut, "Disegno_3D_Fatto")) ut.Disegno_3D_Fatto = false;
                    break;

                case "Disegno 2D":
                    ut.Disegno_2D_Assegnato = true;
                    ut.Operatore_Disegno_2D_Assegnato = operatore;
                    ut.Data_Disegno_2D_Assegnato_InizioPrevista = start;
                    ut.Data_Disegno_2D_Assegnato_FinePrevista = end;
                    ut.Tempo_Disegno_2D_Prevista = d;
                    ut.Disegno_2D_Fatto = false;
                    break;

                case "Distinta":
                    SetBoolProp(ut, "Distinta_Assegnato", true);
                    SetStringProp(ut, "Operatore_Distinta_Assegnato", operatore);
                    SetDateProp(ut, "Data_Distinta_Assegnato_InizioPrevista", start);
                    SetDateProp(ut, "Data_Distinta_Assegnato_FinePrevista", end);
                    SetDoubleProp(ut, "Tempo_Distinta_Prevista", d);
                    if (HasProp(ut, "Distinta_Fatto")) ut.Distinta_Fatto = false;
                    break;

                case "Stampare":
                    if (HasProp(ut, "Operatore_Stampare_Assegnato"))  // versione nuova
                    {
                        SetBoolProp(ut, "Stampare_Assegnato", true);
                        SetStringProp(ut, "Operatore_Stampare_Assegnato", operatore);
                        SetDateProp(ut, "Data_Stampare_Assegnato_InizioPrevista", start);
                        SetDateProp(ut, "Data_Stampare_Assegnato_FinePrevista", end);
                        SetDoubleProp(ut, "Tempo_Stampare_Prevista", d);
                        if (HasProp(ut, "Stampare_Fatto")) ut.Stampare_2D_Fatto = false;
                    }
                    else                                            // compatibilità 2D
                    {
                        ut.Stampare_2D_Assegnato = true;
                        SetStringProp(ut, "Operatore_Stampare_2D_Assegnato", operatore);
                        SetDateProp(ut, "Data_Stampare_2D_Assegnato_InizioPrevista", start);
                        SetDateProp(ut, "Data_Stampare_2D_Assegnato_FinePrevista", end);
                        SetDoubleProp(ut, "Tempo_Stampare_2D_Prevista", d);
                        if (HasProp(ut, "Stampare_2D_Fatto")) ut.Stampare_2D_Fatto = false;
                    }
                    break;
            }

            // *** FIX: reinserisci/aggiorna in lista con chiave COMPOSTA ***
            var idx = MetodiSql.listalavorazioniInProgetto_UfficioTecnico
                .FindIndex(z => z.ID_Attivita == ut.ID_Attivita &&
                                string.Equals(z.codiceProgetto, ut.codiceProgetto, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
                MetodiSql.listalavorazioniInProgetto_UfficioTecnico[idx] = ut;
            else
                MetodiSql.listalavorazioniInProgetto_UfficioTecnico.Add(ut);
        }

        private void btn_associaLavoro_Click(object sender, EventArgs e)
        {
            // Facoltativo: potresti leggere da UI il valore di GrandezzaFissaTask (NumericUpDown etc.)
            // GrandezzaFissaTask = (int)numericDurata.Value;
        }

        private void btn_RimuoviLavoro_Click(object sender, EventArgs e)
        {
            // Eventuale logica custom di rimozione manuale
        }

        private void dtp_DataInizioVisualizzazionePianificazioneGantt_ValueChanged(object sender, EventArgs e)
        {
            AggiornaGantt();
        }

        private void cb_TipoDiVisualizzazione_SelectedIndexChanged(object sender, EventArgs e)
        {
            AggiornaGantt();
        }

        private void AggiornaGantt()
        {
            gantt.SetViewFromCombo("giornaliera");

            if (gantt.EnsureDataVisible())
                gantt.RefreshLayout();

            pn_Contenitore_Gantt.Invalidate();
        }

        private void btn_FrecciaIndietro_Click(object sender, EventArgs e)
        {
            gantt.GoPrev();         // ⬅️ usa il livello corrente (Oraria = -1 ora, Giornaliera = -1 giorno, ecc.)
            pn_Contenitore_Gantt.Invalidate();     // ridisegna
        }

        private void btn_FrecciaAvanti_Click(object sender, EventArgs e)
        {
            gantt.GoNext();         // ⬅️ idem ma avanti
            pn_Contenitore_Gantt.Invalidate();
        }

        private void HandleGanttItemsDeleted(PLM.Gantt.ItemsDeletedArgs args)
        {
            if (args == null || args.Items == null || args.Items.Count == 0) return;

            // Helper locali per pulire una singola fase
            void Clear3D(Classe.Lavorazioni_UfficioTecnico ut)
            {
                ut.Disegno_3D_Assegnato = false;
                ut.Operatore_Disegno_3D_Assegnato = null;
                ut.Data_Disegno_3D_Assegnato_InizioPrevista = null;
                ut.Data_Disegno_3D_Assegnato_FinePrevista = null;
                ut.Tempo_Disegno_3D_Prevista = null;
                ut.Disegno_3D_Fatto = false;
            }
            void Clear2D(Classe.Lavorazioni_UfficioTecnico ut)
            {
                ut.Disegno_2D_Assegnato = false;
                ut.Operatore_Disegno_2D_Assegnato = null;
                ut.Data_Disegno_2D_Assegnato_InizioPrevista = null;
                ut.Data_Disegno_2D_Assegnato_FinePrevista = null;
                ut.Tempo_Disegno_2D_Prevista = null;
                ut.Disegno_2D_Fatto = false;
            }
            void ClearDistinta(Classe.Lavorazioni_UfficioTecnico ut)
            {
                var p = ut.GetType();
                p.GetProperty("Distinta_Assegnato")?.SetValue(ut, false);
                p.GetProperty("Operatore_Distinta_Assegnato")?.SetValue(ut, null);
                p.GetProperty("Data_Distinta_Assegnato_InizioPrevista")?.SetValue(ut, null);
                p.GetProperty("Data_Distinta_Assegnato_FinePrevista")?.SetValue(ut, null);
                p.GetProperty("Tempo_Distinta_Prevista")?.SetValue(ut, null);
                p.GetProperty("Distinta_Fatto")?.SetValue(ut, false);
            }
            void ClearStampare(Classe.Lavorazioni_UfficioTecnico ut)
            {
                var p = ut.GetType();
                if (p.GetProperty("Stampare_Assegnato") != null)
                {
                    p.GetProperty("Stampare_Assegnato")?.SetValue(ut, false);
                    p.GetProperty("Operatore_Stampare_Assegnato")?.SetValue(ut, null);
                    p.GetProperty("Data_Stampare_Assegnato_InizioPrevista")?.SetValue(ut, null);
                    p.GetProperty("Data_Stampare_Assegnato_FinePrevista")?.SetValue(ut, null);
                    p.GetProperty("Tempo_Stampare_Prevista")?.SetValue(ut, null);
                    p.GetProperty("Stampare_Fatto")?.SetValue(ut, false);
                }
                else
                {
                    ut.Stampare_2D_Assegnato = false;
                    SetStringProp(ut, "Operatore_Stampare_2D_Assegnato", null);
                    SetDateProp(ut, "Data_Stampare_2D_Assegnato_InizioPrevista", null);
                    SetDateProp(ut, "Data_Stampare_2D_Assegnato_FinePrevista", null);
                    SetDoubleProp(ut, "Tempo_Stampare_2D_Prevista", null);
                    if (HasProp(ut, "Stampare_2D_Fatto")) ut.Stampare_2D_Fatto = false;
                }
            }

            // Per ogni elemento cancellato dal Gantt, pulisci anche le dipendenze
            foreach (var it in args.Items)
            {
                var ut = FindUT(it.ProjectCode);
                if (ut == null) continue;

                switch (it.TaskName)
                {
                    case "Disegno 3D":
                        // Cancello 3D → cascata su 2D, Distinta, Stampare
                        Clear3D(ut);
                        Clear2D(ut);
                        ClearDistinta(ut);
                        ClearStampare(ut);
                        break;

                    case "Disegno 2D":
                        // Cancello 2D → cancella Stampare (dipende da max(2D,Distinta); qui imponiamo cascata)
                        Clear2D(ut);
                        ClearStampare(ut);
                        break;

                    case "Distinta":
                        // Cancello Distinta → cancella Stampare
                        ClearDistinta(ut);
                        ClearStampare(ut);
                        break;

                    case "Stampare":
                        ClearStampare(ut);
                        break;
                }
            }

            // Salva CSV
            MetodiCsv.scritturaCsv(MetodiSql.db_LavorazioniUfficioTecnico,
                                   MetodiSql.listalavorazioniInProgetto_UfficioTecnico);

            // ► Rebind griglie
            RebindGridsAfterUTChange();

            // ► Ricarica il Gantt dai dati UT (così spariscono anche le dipendenze cancellate)
            gantt.LoadFromUfficioTecnico(MetodiSql.listalavorazioniInProgetto_UfficioTecnico,
                                         MetodiSql.listaOperatori);
            gantt.RefreshAfterExternalChange();

            // Ridisegna il Gantt
            pn_Contenitore_Gantt.Invalidate();
        }


        // ===== Chiave composita (codiceProgetto + ID_Attivita) per non collassare i progetti omonimi =====
        private static string BuildProjectKey(Classe.Lavorazioni_UfficioTecnico ut)
            => $"{(string.IsNullOrWhiteSpace(ut.codiceProgetto) ? "N/D" : ut.codiceProgetto)}__ATT:{ut.ID_Attivita}";

        private static void SplitProjectKey(string key, out string codiceProgetto, out int? idAttivita)
        {
            codiceProgetto = key;
            idAttivita = null;
            if (string.IsNullOrWhiteSpace(key)) return;
            var idx = key.LastIndexOf("__ATT:", StringComparison.Ordinal);
            if (idx <= 0) return;
            codiceProgetto = key.Substring(0, idx);
            if (int.TryParse(key.Substring(idx + "__ATT:".Length), out var id)) idAttivita = id;
        }

        // Trova il record UT usando la chiave composita; se id è nullo, ripiega su solo codiceProgetto (compat).
        private static Classe.Lavorazioni_UfficioTecnico FindUT(string codiceProgettoOrComposite)
        {
            SplitProjectKey(codiceProgettoOrComposite, out var code, out var id);
            if (id.HasValue)
                return MetodiSql.listalavorazioniInProgetto_UfficioTecnico
                    .FirstOrDefault(x => x.ID_Attivita == id.Value &&
                                         string.Equals(x.codiceProgetto, code, StringComparison.OrdinalIgnoreCase));
            // fallback legacy
            return MetodiSql.listalavorazioniInProgetto_UfficioTecnico
                .FirstOrDefault(x => string.Equals(x.codiceProgetto, code, StringComparison.OrdinalIgnoreCase));
        }

        private void dgw_ProgettiInAttivita_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var sel = dgw_ProgettiInAttivita.Rows[e.RowIndex].DataBoundItem as Classe.ProgettoInAttivitaCRM;
            if (sel == null) return;

            // Tutte le righe UT del progetto selezionato
            var uts = MetodiSql.listalavorazioniInProgetto_UfficioTecnico
                .Where(u => u.ID_Attivita == sel.ID_Attivita &&
                            string.Equals(u.codiceProgetto, sel.codiceProgetto, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (uts.Count == 0) return;

            // Trova il primo giorno pianificato (priorità agli INIZI, fallback alle FINE)
            DateTime? target = uts
                .SelectMany(u => new DateTime?[]
                {
            u.Data_Disegno_3D_Assegnato_InizioPrevista,
            u.Data_Disegno_2D_Assegnato_InizioPrevista,
            u.Data_Distinta_Assegnato_InizioPrevista,
            (DateTime?)u.GetType().GetProperty("Data_Stampare_Assegnato_InizioPrevista")?.GetValue(u),
            (DateTime?)u.GetType().GetProperty("Data_Stampare_2D_Assegnato_InizioPrevista")?.GetValue(u),

            // fallback se non ho start
            u.Data_Disegno_3D_Assegnato_FinePrevista,
            u.Data_Disegno_2D_Assegnato_FinePrevista,
            u.Data_Distinta_Assegnato_FinePrevista,
            (DateTime?)u.GetType().GetProperty("Data_Stampare_Assegnato_FinePrevista")?.GetValue(u),
            (DateTime?)u.GetType().GetProperty("Data_Stampare_2D_Assegnato_FinePrevista")?.GetValue(u),
                })
                .Where(d => d.HasValue)
                .OrderBy(d => d.Value)
                .FirstOrDefault();

            if (!target.HasValue) return;

            // Vai alla vista Giornaliera sul giorno della pratica
            gantt.SetViewFromCombo("giornaliera");
            gantt.SetReference(target.Value.Date);
            pn_Contenitore_Gantt.Invalidate();
        }

        private static bool FaseGiaAssegnata(Classe.Lavorazioni_UfficioTecnico ut, string fase)
        {
            if (ut == null) return false;

            switch (fase)
            {
                case "Disegno 3D":
                    return (bool)(ut.GetType().GetProperty("Disegno_3D_Assegnato")?.GetValue(ut) ?? false);

                case "Disegno 2D":
                    return ut.Disegno_2D_Assegnato;

                case "Distinta":
                    return (bool)(ut.GetType().GetProperty("Distinta_Assegnato")?.GetValue(ut) ?? false);

                case "Stampare":
                    // compatibilità: alcuni DB usano *_2D_*
                    bool v1 = (bool)(ut.GetType().GetProperty("Stampare_Assegnato")?.GetValue(ut) ?? false);
                    bool v2 = ut.Stampare_2D_Assegnato;
                    return v1 || v2;

                default:
                    return false;
            }
        }

        private static List<string> FasiDaPiazzareInOrdine(Classe.Lavorazioni_UfficioTecnico ut)
        {
            // Usa il tuo OrdineLogicoFasi: "Disegno 3D", "Disegno 2D", "Distinta", "Stampare"
            return OrdineLogicoFasi
                .Where(f => FaseDaFare(ut, f))        // deve essere "DaFare"
                .Where(f => !FaseGiaAssegnata(ut, f)) // e non già pianificata
                .ToList();
        }

        private void dgw_AttivitaCRM_SelectionChanged(object sender, EventArgs e)
        {
            if (dgw_AttivitaCRM.CurrentRow != null)
            {
                dgw_ProgettiInAttivita.ClearSelection();

                var attivitaSelezionata = dgw_AttivitaCRM.CurrentRow.DataBoundItem as Classe.Attivita_CRM;
                if (attivitaSelezionata != null)
                {
                    listaProgettiInAttivitaCrmFiltrata.Clear();

                    MetodiSql.listaProgettiInAttivita_CRM = MetodiCsv.letturaCsv<Classe.ProgettoInAttivitaCRM>(MetodiSql.db_ProgettiInAttivita_CRM_PlmNuovo);

                    listaProgettiInAttivitaCrmFiltrata = MetodiSql.listaProgettiInAttivita_CRM
                            .Where(x => x.lavorazioniCompletate == false)
                            .Where(p => p.ID_Attivita == attivitaSelezionata.ID_Attivita)
                            .ToList();

                    dgw_ProgettiInAttivita.ForeColor = Color.Black;
                    dgw_ProgettiInAttivita.AutoGenerateColumns = false;
                    dgw_ProgettiInAttivita.DataSource = null;
                    dgw_ProgettiInAttivita.DataSource = listaProgettiInAttivitaCrmFiltrata;
                }
            }
        }

        private void dgw_ProgettiInAttivita_SelectionChanged(object sender, EventArgs e)
        {
            if (dgw_ProgettiInAttivita.CurrentRow == null) return;

            var sel = dgw_ProgettiInAttivita.CurrentRow.DataBoundItem as Classe.ProgettoInAttivitaCRM;
            if (sel == null) return;

            var lavorazioniUt = MetodiCsv.letturaCsv<Classe.Lavorazioni_UfficioTecnico>(MetodiSql.db_LavorazioniUfficioTecnico)
                .Where(x => x.ID_Attivita == sel.ID_Attivita && x.codiceProgetto == sel.codiceProgetto)
                .ToList();

            // prendi la prima (se hai una sola riga per progetto) oppure cicla se ne hai più
            var statoLavorazioni = EstraiStatoLavorazioni(lavorazioniUt.FirstOrDefault());

            // Bind griglia stato
            dgw_LavorazioniInAttivita.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgw_LavorazioniInAttivita.ColumnHeadersHeight = 40;
            dgw_LavorazioniInAttivita.EnableHeadersVisualStyles = false;
            dgw_LavorazioniInAttivita.ColumnHeadersDefaultCellStyle.Padding = new Padding(15, 8, 0, 8);
            dgw_LavorazioniInAttivita.ForeColor = Color.Black;
            dgw_LavorazioniInAttivita.AutoGenerateColumns = false;
            dgw_LavorazioniInAttivita.DataSource = null;
            dgw_LavorazioniInAttivita.DataSource = statoLavorazioni;

            // Se vuoi: wiring del drag dei task è già fatto una sola volta
        }
    }
}
