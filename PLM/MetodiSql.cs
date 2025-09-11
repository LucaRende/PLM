using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLM
{
    public static class MetodiSql
    {
        // Percorso DataBase CSV
        public static string db_LavorazioniUfficioTecnico = @"H:\LUCA R - NON CANCELLARE!\db_PLM\db_LavorazioniUffcioTecnico.csv";
        public static string db_Attivita_Crm = @"H:\LUCA R - NON CANCELLARE!\db_PLM\db_Attivita_Crm.csv";
        public static string db_Storie_Crm = @"H:\LUCA R - NON CANCELLARE!\db_PLM\db_Storie_Crm.csv";
        public static string db_ProgettiInAttivita_CRM_PlmNuovo = @"H:\LUCA R - NON CANCELLARE!\db_PLM\db_ProgettiInAttivita_CRM_PlmNuovo.csv";

        // Dati di conessione PLM
        public static string ipServer_PLM = @"192.168.1.248\SQLEXPRESS";
        public static string portaServer_PLM = "49232";
        public static string nomeDB_PLM = "psdbProgetti";
        public static string utenteSQL_PLM = "plmsoftware";
        public static string passwordSQL_PLM = "P0p0lin0";

        // Dati di conessione CRM
        public static string ipServer_CRM = @"192.168.1.248\SQLEXPRESS";
        public static string portaServer_CRM = "49232";
        public static string nomeDB_CRM = "psdbProgetti";
        public static string utenteSQL_CRM = "plmsoftware";
        public static string passwordSQL_CRM = "P0p0lin0";

        // Tabelle PLM
        public static string tabella_TempiticheProgetti_PLM = "TempisticheProgetti";
        public static string tabella_ProgettiInOrdine_PLM = "ProgettiInOrdine";
        public static string tabella_Ordini_PLM = "Ordini";

        // Tabelle CRM
        public static string tabella_Attivita_CRM = "Attivita";

        //Liste
        //PLM
        public static List<Classe.Ordine_PLM> listaOrdini_PLM = new List<Classe.Ordine_PLM>();
        public static List<Classe.Progetti> listaProgettiInOrdine_PLM = new List<Classe.Progetti>();
        // CRM
        public static List<Classe.Attivita_CRM> listaAttivita_CRM = new List<Classe.Attivita_CRM>();
        public static List<Classe.ProgettoInAttivitaCRM> listaProgettiInAttivita_CRM = new List<Classe.ProgettoInAttivitaCRM>();
        public static List<Classe.Storia> listaStorie = new List<Classe.Storia>();
        //PLM NUOVO
        public static List<Classe.Lavorazioni> listalavorazioniInProgetto = new List<Classe.Lavorazioni>();
        public static List<Classe.Lavorazioni_UfficioTecnico> listalavorazioniInProgetto_UfficioTecnico = new List<Classe.Lavorazioni_UfficioTecnico>();
        public static List<Classe.Operatore> listaOperatori = new List<Classe.Operatore>();

        public static List<T> LeggiDatiDaSQL<T>
        ( 
            string ipServer,
            string portaServer, 
            string nomeDB,
            string utenteSQL, 
            string passwordSQL, 
            string tabella,
            string? selectSql = null
        ) 
            where T : new()
        {
            string connString =
                $"Data Source={ipServer},{portaServer};Initial Catalog={nomeDB};User ID={utenteSQL};Password={passwordSQL};TrustServerCertificate=True;";

            var risultati = new List<T>();
            using var conn = new SqlConnection(connString);
            conn.Open();

            // se non passo una SELECT, costruisco un SELECT * (meglio passare selectSql)
            string sql = selectSql ?? $"SELECT * FROM {tabella}";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
            using var reader = cmd.ExecuteReader();

            var props = typeof(T).GetProperties();

            while (reader.Read())
            {
                var istanza = new T();

                foreach (var prop in props)
                {
                    try
                    {
                        int ord = GetOrdinalSafe(reader, prop.Name);
                        if (ord < 0 || reader.IsDBNull(ord)) continue;

                        object valore = reader.GetValue(ord);
                        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                        // Se il tipo coincide già, assegna diretto
                        if (targetType.IsInstanceOfType(valore))
                        {
                            prop.SetValue(istanza, valore);
                            continue;
                        }

                        // Conversione tipizzata
                        if (TryChangeType(valore, targetType, out var converted))
                        {
                            prop.SetValue(istanza, converted);
                        }
                        else
                        {
                            // Fallback prudente: prova Convert.ChangeType con cultura corrente
                            var obj = Convert.ChangeType(valore, targetType, CultureInfo.CurrentCulture);
                            prop.SetValue(istanza, obj);
                        }
                    }
                    catch
                    {
                        // ignora la singola proprietà problematica
                        continue;
                    }
                }

                risultati.Add(istanza);
            }

            return risultati;

            static int GetOrdinalSafe(SqlDataReader r, string name)
            {
                for (int i = 0; i < r.FieldCount; i++)
                    if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                        return i;
                return -1;
            }
        }

        /// <summary>
        /// Conversione robusta che rispetta la cultura corrente (es. it-IT con virgola).
        /// Gestisce decimal/double/float anche quando arrivano come stringa, decimal, double, ecc.
        /// </summary>
        private static bool TryChangeType(object value, Type targetType, out object? result)
        {
            result = null;

            // Nullable gestito a monte; se stringa vuota per numerici -> fallisce
            if (value is null) return false;

            // Enums
            if (targetType.IsEnum)
            {
                try
                {
                    if (value is string sEnum)
                    {
                        result = Enum.Parse(targetType, sEnum, ignoreCase: true);
                        return true;
                    }
                    result = Enum.ToObject(targetType, value);
                    return true;
                }
                catch { return false; }
            }

            // Guid
            if (targetType == typeof(Guid))
            {
                if (value is Guid g) { result = g; return true; }
                if (Guid.TryParse(value.ToString(), out var g2)) { result = g2; return true; }
                return false;
            }

            // Boolean
            if (targetType == typeof(bool))
            {
                if (value is bool b) { result = b; return true; }
                var s = value.ToString()?.Trim();
                if (bool.TryParse(s, out var b2)) { result = b2; return true; }
                // true/false come 0/1
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i01))
                {
                    result = i01 != 0;
                    return true;
                }
                return false;
            }

            // Numerici interi
            if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(short) || targetType == typeof(byte))
            {
                try
                {
                    // Prova conversione diretta con Convert (accetta molti tipi SQL)
                    result = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    // Come stringa con cultura corrente
                    var s = value.ToString();
                    if (targetType == typeof(int) && int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) { result = i; return true; }
                    if (targetType == typeof(long) && long.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var l)) { result = l; return true; }
                    if (targetType == typeof(short) && short.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var sh)) { result = sh; return true; }
                    if (targetType == typeof(byte) && byte.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var by)) { result = by; return true; }
                    return false;
                }
            }

            // Numerici decimali (qui è dove puoi "perdere la virgola" se usi la cultura sbagliata)
            if (targetType == typeof(decimal))
            {
                if (value is decimal dec) { result = dec; return true; }
                if (value is double dble) { result = (decimal)dble; return true; }
                var s = value.ToString();

                // 1) Prova con cultura corrente (virgola in it-IT)
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var dec1)) { result = dec1; return true; }
                // 2) Fallback con Invariant (punto)
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec2)) { result = dec2; return true; }
                // 3) Fallback normalizzando separatori
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var norm = NormalizeDecimalString(s);
                    if (decimal.TryParse(norm, NumberStyles.Any, CultureInfo.CurrentCulture, out var dec3)) { result = dec3; return true; }
                }
                return false;
            }

            if (targetType == typeof(double))
            {
                if (value is double dd) { result = dd; return true; }
                if (value is float ff) { result = (double)ff; return true; }
                if (value is decimal dc) { result = (double)dc; return true; }
                var s = value.ToString();

                if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var d1)) { result = d1; return true; }
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) { result = d2; return true; }
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var norm = NormalizeDecimalString(s);
                    if (double.TryParse(norm, NumberStyles.Any, CultureInfo.CurrentCulture, out var d3)) { result = d3; return true; }
                }
                return false;
            }

            if (targetType == typeof(float))
            {
                if (value is float f) { result = f; return true; }
                if (value is double dd2) { result = (float)dd2; return true; }
                if (value is decimal dc2) { result = (float)dc2; return true; }
                var s = value.ToString();

                if (float.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var f1)) { result = f1; return true; }
                if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f2)) { result = f2; return true; }
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var norm = NormalizeDecimalString(s);
                    if (float.TryParse(norm, NumberStyles.Any, CultureInfo.CurrentCulture, out var f3)) { result = f3; return true; }
                }
                return false;
            }

            // DateTime
            if (targetType == typeof(DateTime))
            {
                if (value is DateTime dt) { result = dt; return true; }
                var s = value.ToString();
                if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt1)) { result = dt1; return true; }
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2)) { result = dt2; return true; }
                return false;
            }

            // TimeSpan
            if (targetType == typeof(TimeSpan))
            {
                if (value is TimeSpan ts) { result = ts; return true; }
                if (value is DateTime dtt) { result = dtt.TimeOfDay; return true; }
                var s = value.ToString();
                if (TimeSpan.TryParse(s, CultureInfo.CurrentCulture, out var ts1)) { result = ts1; return true; }
                if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts2)) { result = ts2; return true; }
                return false;
            }

            // String
            if (targetType == typeof(string))
            {
                result = value.ToString();
                return true;
            }

            // Fallback generico
            try
            {
                result = Convert.ChangeType(value, targetType, CultureInfo.CurrentCulture);
                return true;
            }
            catch
            {
                return false;
            }

            // Local helper: normalizza separatori decimali in modo amichevole alla cultura corrente
            static string NormalizeDecimalString(string s)
            {
                s = s.Trim();

                // Se la cultura corrente usa la virgola, sostituisci il punto con la virgola
                var decSep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                var otherSep = decSep == "," ? "." : ",";
                // Attenzione a migliaia: elimina separatore migliaia comune per evitare ambiguità
                var groupSep = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;

                // Rimuovi separatore delle migliaia più comune (sia corrente che alternativo)
                if (!string.IsNullOrEmpty(groupSep) && groupSep != "\u00A0")
                    s = s.Replace(groupSep, "");
                s = s.Replace("\u00A0", ""); // NBSP usato in alcune esportazioni

                // Se contiene entrambi, prova a tenere l'ultimo come decimale
                if (s.Contains(",") && s.Contains("."))
                {
                    // Esempi:
                    // 1.234,56 (it) -> 1234,56
                    // 1,234.56 (en) -> 1234.56 -> poi convertito
                    int lastComma = s.LastIndexOf(',');
                    int lastDot = s.LastIndexOf('.');

                    if (lastDot > lastComma)
                    {
                        // ultimo è punto -> consideralo decimale, rimuovi le virgole (migliaia)
                        s = s.Replace(",", "");
                    }
                    else
                    {
                        // ultimo è virgola -> considerala decimale, rimuovi i punti (migliaia)
                        s = s.Replace(".", "");
                    }
                }

                // Allinea il separatore decimale alla cultura corrente
                s = s.Replace(otherSep, decSep);

                return s;
            }
        }

        public static void CaricaDatiPLM()
        {
            // Leggo la lista di tutti gli ordini nel PLM
            listaOrdini_PLM = LeggiDatiDaSQL<Classe.Ordine_PLM>
            (
            ipServer_PLM,
            portaServer_PLM,
            nomeDB_PLM,
            utenteSQL_PLM,
            passwordSQL_PLM,
            tabella_Ordini_PLM
            )
            .OrderByDescending(x => x.ID_Ordine)
            .ToList();

            // Leggo la lista di tutti i progetti in ordine nel PLM
            listaProgettiInOrdine_PLM = LeggiDatiDaSQL<Classe.Progetti>
            (
            ipServer_PLM,
            portaServer_PLM,
            nomeDB_PLM,
            utenteSQL_PLM,
            passwordSQL_PLM,
            tabella_ProgettiInOrdine_PLM
            )
            .ToList();
        }

        public static void CaricaDatiCRM()
        {
            // -------------------------------------------------------- da attivare appena ricevero i dati per collegarmi alle tabelle del CRM --------------
            //// Leggo la lista delle attività da CRM
            //listaProgettiInOrdine_PLM = LeggiDatiDaSQL<Classe.Progetti>
            //(
            //ipServer_CRM,
            //portaServer_CRM,
            //nomeDB_CRM,
            //utenteSQL_CRM,
            //passwordSQL_CRM,
            //tabella_Attivita_CRM
            //)
            //.ToList();

            // -------------------------------------------------------- Questi dati li creerò bene in un secondo momento --------------------------------------------------------
            Classe.Operatore operatore1 = new Classe.Operatore { nomeCognome = "Mirco Bonetti" };
            Classe.Operatore operatore2 = new Classe.Operatore { nomeCognome = "Federico Cavazza" };
            Classe.Operatore operatore3 = new Classe.Operatore { nomeCognome = "Caterina Dello Stritto" };
            Classe.Operatore operatore4 = new Classe.Operatore { nomeCognome = "Kejvin Myrtaj" };
            Classe.Operatore operatore5 = new Classe.Operatore { nomeCognome = "Christian D'angelo" };
            Classe.Operatore operatore6 = new Classe.Operatore { nomeCognome = "Marco Garzesi" };

            listaOperatori.Add(operatore1);
            listaOperatori.Add(operatore2);
            listaOperatori.Add(operatore3);
            listaOperatori.Add(operatore4);
            listaOperatori.Add(operatore5);
            listaOperatori.Add(operatore6);

            // -------------------------------------------------------- Questi dati me li darà il CRM --------------------------------------------------------

            // Creo ----------- attivita ----------- di prova
            Classe.Attivita_CRM attivita_1 = new Classe.Attivita_CRM
            {
                ID_Attivita = 1,
                dataCreazione = DateTime.Now,
                dataConsegna = DateTime.Now,
                clienteApplicato = "Atlanta",
                oggetto = "Richiesta offerta",
                tipologia = "Preventivo",
                esito = true,
                trasferimento = "mirco.bonetti",
                aperta = true
            };
            listaAttivita_CRM.Add(attivita_1);

            Classe.Attivita_CRM attivita_2 = new Classe.Attivita_CRM
            {
                ID_Attivita = 2,
                dataCreazione = DateTime.Now,
                dataConsegna = DateTime.Now,
                clienteApplicato = "Baumer",
                oggetto = "Richiesta offerta",
                tipologia = "Preventivo",
                esito = true,
                trasferimento = "mirco.bonetti",
                aperta = true
            };
            listaAttivita_CRM.Add(attivita_2);

            // ----------- Storia ----------- di prova per estraplare i codici PS o CAR
            Classe.Storia storia_1 = new Classe.Storia();
            storia_1.ID_Attivita = 1;
            storia_1.data = DateTime.Now;
            storia_1.contenuto = "Codici da preventivare: PS178444/2 PS178002/1";
            listaStorie.Add(storia_1);

            Classe.Storia storia_3 = new Classe.Storia();
            storia_3.ID_Attivita = 2;
            storia_3.data = DateTime.Now;
            storia_3.contenuto = "Codici da produrre: PS178444/2 PS178002/1";
            listaStorie.Add(storia_3);

            Classe.Storia storia_2 = new Classe.Storia();
            storia_2.ID_Attivita = 2;
            storia_2.data = DateTime.Now;
            storia_2.contenuto = "Codici da preventivare: PS178009/2 PS178012/1";
            listaStorie.Add(storia_2);

            //--------------------------------------------------------------------------------------------------------------------------------------------------

            foreach (var attivita in listaAttivita_CRM)
            {
                // Inserisco la storia di prova nell'attività corretta
                attivita.storia = listaStorie.Where(x => x.ID_Attivita == attivita.ID_Attivita).ToList();
            }

            // -------------------------------------------------------------------------------------------------------------------------------------------------
            // Creo lista ----------- progettiInAttivitaCRM ----------- Estrapolo i dati del crm, vado nelle storie di tutti i progetti e
            // estrapolo i codici progetto

            listaProgettiInAttivita_CRM = MetodiCsv.letturaCsv<Classe.ProgettoInAttivitaCRM>(db_ProgettiInAttivita_CRM_PlmNuovo);
            listalavorazioniInProgetto_UfficioTecnico = MetodiCsv.letturaCsv<Classe.Lavorazioni_UfficioTecnico>(db_LavorazioniUfficioTecnico);

            // Entro in tutte le attivita del CRM una alla volta
            foreach (var attivita in listaAttivita_CRM)
            {
                // Se l'attività è nulla saltala
                if (attivita == null || attivita.storia == null)
                    continue;

                // Estraggo dalla listaStorie inerente a quell'attività i codici
                foreach (var storia in attivita.storia)
                {
                    // Pulisco la lista progetti in attivita CRM
                    //listaProgettiInAttivita_CRM.Clear();

                    if (storia.contenuto.Contains("Codici da produrre:"))
                    {
                        string[] paroleContenuteInStoria = storia.contenuto.Split(' ');

                        foreach (var parola in paroleContenuteInStoria)
                        {
                            if (parola.Contains("PS") || parola.Contains("CAR"))
                            {
                                if (parola.Contains("/"))
                                {
                                    var parts = parola.Split('/');
                                    string codice = parts[0];
                                    int qta = Convert.ToInt16(parts[1]);

                                    // ⛔ Se esiste già in DB o già raccolto, salta
                                    bool esisteGia = listaProgettiInAttivita_CRM.Any(x => x.ID_Attivita == attivita.ID_Attivita && x.codiceProgetto == codice);

                                    if (esisteGia)
                                        continue;

                                    // ✅ Crea solo se NON esiste
                                    var nuovoProgettoInAttivitaCRM = new Classe.ProgettoInAttivitaCRM
                                    {
                                        ID_Attivita = attivita.ID_Attivita,
                                        codiceProgetto = codice,
                                        quantita = qta,
                                        tipologiaAttivita = "Ordine",
                                        listaLavorazioni = new List<Classe.Lavorazioni>
                                        {
                                            AssegnaLavorazioni("Ordine", attivita.ID_Attivita, codice)
                                        },
                                        lavorazioniCompletate = false
                                    };

                                    listaProgettiInAttivita_CRM.Add(nuovoProgettoInAttivitaCRM);
                                    storia.progettiInAttivitaCRM = new List<Classe.ProgettoInAttivitaCRM>(listaProgettiInAttivita_CRM);
                                }

                            }
                        }
                    }
                    else if (storia.contenuto.Contains("Codici da preventivare:"))
                    {
                        string[] paroleContenuteInStoria = storia.contenuto.Split(' ');
                        foreach (var parola in paroleContenuteInStoria)
                        {
                            if (parola.Contains("PS") || parola.Contains("CAR"))
                            {
                                if (parola.Contains("/"))
                                {
                                    string[] codiceCommessaInAttivitaCRMConQta = parola.Split('/');
                                    string codice = codiceCommessaInAttivitaCRMConQta[0];
                                    int qta = Convert.ToInt16(codiceCommessaInAttivitaCRMConQta[1]);

                                    // ⛔ Se esiste già (nel DB o già raccolto in listaProgettiInAttivita_CRM), salta
                                    bool esisteGia = listaProgettiInAttivita_CRM.Any(x => x.ID_Attivita == attivita.ID_Attivita && x.codiceProgetto == codice);

                                    if (esisteGia)
                                        continue;

                                    // ✅ Crea solo se NON esiste
                                    Classe.ProgettoInAttivitaCRM nuovoProgettoInAttivitaCRM = new Classe.ProgettoInAttivitaCRM();
                                    nuovoProgettoInAttivitaCRM.ID_Attivita = attivita.ID_Attivita;
                                    nuovoProgettoInAttivitaCRM.codiceProgetto = codice;
                                    nuovoProgettoInAttivitaCRM.quantita = qta;
                                    nuovoProgettoInAttivitaCRM.tipologiaAttivita = "Preventivo";
                                    nuovoProgettoInAttivitaCRM.listaLavorazioni = new List<Classe.Lavorazioni>
                                    {
                                        AssegnaLavorazioni("Preventivo", attivita.ID_Attivita, codice)
                                    };
                                    nuovoProgettoInAttivitaCRM.lavorazioniCompletate = false;

                                    listaProgettiInAttivita_CRM.Add(nuovoProgettoInAttivitaCRM);
                                    storia.progettiInAttivitaCRM = new List<Classe.ProgettoInAttivitaCRM>(listaProgettiInAttivita_CRM);
                                }

                            }
                        }
                    }
                }
            }

            MetodiCsv.scritturaCsv<Classe.Lavorazioni_UfficioTecnico>(db_LavorazioniUfficioTecnico, listalavorazioniInProgetto_UfficioTecnico);
            MetodiCsv.scritturaCsv<Classe.Attivita_CRM>(db_Attivita_Crm, listaAttivita_CRM);
            MetodiCsv.scritturaCsv<Classe.Storia>(db_Storie_Crm, listaStorie);
            MetodiCsv.scritturaCsv<Classe.ProgettoInAttivitaCRM>(db_ProgettiInAttivita_CRM_PlmNuovo, listaProgettiInAttivita_CRM);

            // Rimuovo quei codici che sono già stati completati, quidni leggo la lista e progettiInAttvita CRM dal database in csv, se esiste già un progetto con quel codice con quel ID_Attivita lo aggiorno nella lista mettendo completato true

        }
        
        private static Classe.Lavorazioni AssegnaLavorazioni(string tipologiaLavoro, int ID_Attivita, string codiceProgetto)
        {
            var lavori = new Classe.Lavorazioni();

            //-------------------------------------------------------- Lavorazioni Ufficio Tecnico ------------------------------------------------------
            
            if (lavori.lavorazioni_UfficioTecnico == null)
                lavori.lavorazioni_UfficioTecnico = new List<Classe.Lavorazioni_UfficioTecnico>();

            var nuovoLavoroUfficioTecnico = new Classe.Lavorazioni_UfficioTecnico
            {
                ID_Attivita = ID_Attivita,                
                codiceProgetto = codiceProgetto
            };

            switch (tipologiaLavoro)
            {
                case "Preventivo":
                    nuovoLavoroUfficioTecnico.Disegno_2D_DaFare = false;
                    nuovoLavoroUfficioTecnico.Disegno_3D_DaFare = true;
                    nuovoLavoroUfficioTecnico.Distinta_DaFare = true;
                    nuovoLavoroUfficioTecnico.Stampare_DaFare = false;
                    break;
                case "Ordine":
                    nuovoLavoroUfficioTecnico.Disegno_2D_DaFare = true;
                    nuovoLavoroUfficioTecnico.Disegno_3D_DaFare = true;
                    nuovoLavoroUfficioTecnico.Distinta_DaFare = true;
                    nuovoLavoroUfficioTecnico.Stampare_DaFare = true;
                    break;
            }

            lavori.ID_Attivita = ID_Attivita;
            lavori.codiceProgetto = codiceProgetto;
            lavori.lavorazioni_UfficioTecnico.Add(nuovoLavoroUfficioTecnico);

            listalavorazioniInProgetto_UfficioTecnico.Add(nuovoLavoroUfficioTecnico);
            listalavorazioniInProgetto.Add(lavori);

            //--------------------------------------------------------------------------------------------------------------------------------------------

            return lavori;
        }
    }
}