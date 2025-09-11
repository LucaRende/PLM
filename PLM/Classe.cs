using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace PLM
{
    public class Classe
    {

        // ---------------------------------------------------------- Classi del CRM ----------------------------------------------------------
        public class Attivita_CRM
        {
            public int ID_Attivita { get; set; }
            public DateTime dataCreazione { get; set; }
            public string dataCreazioneFormattata
            {
                get { return dataCreazione.ToString("dd/MM/yyyy"); }
            }
            public DateTime dataConsegna { get; set; }
            public string dataConsegnaFormattata
            {
                get { return dataConsegna.ToString("dd/MM/yyyy"); }
            }
            public string clienteApplicato { get; set; }
            public string oggetto { get; set; }
            public string tipologia { get; set; }
            public string codiceCommessa { get; set; }
            public bool esito { get; set; } // Esito di preventivo, serve davvero?
            public List<Storia> storia { get; set; }
            public string trasferimento { get; set; }
            public List<Gruppo> gruppo { get; set; }
            public bool aperta { get; set; }
        }

        public class Gruppo()
        {
            public string operatore { get; set; }
            public string livelloPermesso { get; set; }
            public bool assegnata { get; set; }
        }

        public class Storia
        {
            public int ID_Attivita { get; set; }
            public DateTime data { get; set; }
            public string dataFormattata
            {
                get { return data.ToString("dd/MM/yyyy"); }
            }
            public string contenuto { get; set; }
            public List<ProgettoInAttivitaCRM> progettiInAttivitaCRM { get; set; }
        }


        // ---------------------------------------------------------- Classi del PLM NUOVO ----------------------------------------------------------
        public class ProgettoInAttivitaCRM
        {
            public int ID_Attivita { get; set; }
            public string codiceProgetto { get; set; }
            public int quantita { get; set; }
            public string tipologiaAttivita { get; set; } // Se preventivo o ordine per gestire il WorkFlow
            public List<Lavorazioni> listaLavorazioni { get; set; }
            public bool lavorazioniCompletate { get; set; }
        }

        public class Lavorazioni()
        {
            public int ID_Attivita { get; set; }
            public string codiceProgetto { get; set; }
            public List<Lavorazioni_UfficioTecnico> lavorazioni_UfficioTecnico {  get; set; }
            public bool lavorazioni_UfficioTecnico_Completate { get; set; }
        }

        public class Lavorazioni_UfficioTecnico()
        {
            // Identificatori
            public int ID_Attivita { get; set; }
            public string codiceProgetto { get; set; }
            //2D
            public bool Disegno_2D_DaFare { get; set; } // Lavorazione da fare ?
            public bool Disegno_2D_Assegnato { get; set; }  // lavorazione associata ?
            public string? Operatore_Disegno_2D_Assegnato { get; set; } // nome dell' Operatore associato
            public DateTime? Data_Disegno_2D_Assegnato_InizioPrevista { get; set; } // inizio lavorazione prevista
            public double? Tempo_Disegno_2D_Prevista { get; set; } // tempo previsto per lavorazione
            public DateTime? Data_Disegno_2D_Assegnato_FinePrevista { get; set; } // Fine prevista della lavorazione
            public bool Disegno_2D_Fatto { get; set; } // lavorazione completata ?
            // 3D
            public bool Disegno_3D_DaFare { get; set; }
            public bool Disegno_3D_Assegnato { get; set; }
            public string? Operatore_Disegno_3D_Assegnato { get; set; } // nome dell' Operatore associato
            public DateTime? Data_Disegno_3D_Assegnato_InizioPrevista { get; set; } // inizio lavorazione prevista
            public double? Tempo_Disegno_3D_Prevista { get; set; } // tempo previsto per lavorazione
            public DateTime? Data_Disegno_3D_Assegnato_FinePrevista { get; set; } // Fine prevista della lavorazione
            public bool Disegno_3D_Fatto { get; set; }
            // Distinta
            public bool Distinta_DaFare { get; set; }
            public bool Distinta_Assegnato { get; set; }
            public string? Operatore_Distinta_Assegnato { get; set; } // nome dell' Operatore associato
            public DateTime? Data_Distinta_Assegnato_InizioPrevista { get; set; } // inizio lavorazione prevista
            public double? Tempo_Distinta_Prevista { get; set; } // tempo previsto per lavorazione
            public DateTime? Data_Distinta_Assegnato_FinePrevista { get; set; } // Fine prevista della lavorazione
            public bool Distinta_Fatto { get; set; }
            //Stampa Disegno cartaceo
            public bool Stampare_DaFare { get; set; }
            public bool Stampare_2D_Assegnato { get; set; }
            public string? Operatore_Stampare_2D_Assegnato { get; set; } // nome dell' Operatore associato
            public DateTime? Data_Stampare_2D_Assegnato_InizioPrevista { get; set; } // inizio lavorazione prevista
            public double? Tempo_Stampare_2Da_Prevista { get; set; } // tempo previsto per lavorazione
            public DateTime? Data_Stampare_2D_Assegnato_FinePrevista { get; set; } // Fine prevista della lavorazione
            public bool Stampare_2D_Fatto { get; set; }

        }

        public class LavorazioneStato
        {
            public string lavorazione { get; set; }
            public bool statoCompletato { get; set; }
            public bool statoAssegnato { get; set; }
        }

        public class Operatore()
        {
            public string nomeCognome { get; set; }
        }

        // ---------------------------------------------------------- Classi del PLM ----------------------------------------------------------
        public class Ordine_PLM
        {
            public int ID_Ordine { get; set; }
            public string nOrdine { get; set; }
            public string nConfermaOrdine { get; set; }
            public bool Pubblicato { get; set; }
            public bool Terminato { get; set; }
            public bool Cancellato { get; set; }
        }

        public class Progetti
        {
            public int ID_Cartella { get; set; }
            public int ID_Ordine { get; set; }
            public string codiceProgetto { get; set; }
            public bool utcheck {  get; set; } // ufficio tecnico
            public bool mgzcheck { get; set; } // magazzino
            public bool amncheck { get; set; } // Assegnata
            public bool tgacheck { get; set; } // Alluminio
            public bool tgpcheck { get; set; } // Plastica
            public bool mntcheck { get; set; } //serve per sapere se completata
        }
    }
}
