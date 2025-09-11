using Guna.UI2.WinForms;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLM
{
    public static class MetodiUniversali
    {
        // Creo gli oggetti di tipo FORM
        public static Form_Home formHome = new Form_Home();
        public static Form_Crm formCrm = new Form_Crm();
        public static Form_UfficioTecnico_Pianificazione formUfficioTecnicoPianificazione = new Form_UfficioTecnico_Pianificazione();

        // Directory utili
        public static string percorsoCrm = @"http://192.168.1.249/intrasofter/system/isFra018.asp"; // Indirizzo CRM

        public static async void CaricaCrmNelPanel(Panel pn)
        {
            var webView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            pn.Controls.Clear();
            pn.Controls.Add(webView);

            await webView.EnsureCoreWebView2Async(null);
            webView.Source = new Uri(percorsoCrm);
        }

        public static void ApriFormInPanel(Panel pn, Form fm)
        {
            // pulisco il contenitore prima
            pn.Controls.Clear();

            // preparo il form da caricare
            Form frm = fm;
            frm.TopLevel = false;          // non è più una finestra indipendente
            frm.FormBorderStyle = FormBorderStyle.None; // niente bordi
            frm.Dock = DockStyle.Fill;     // si adatta al panel

            // lo aggiungo al contenitore
            pn.Controls.Add(frm);
            frm.Show();
        }
    }
}