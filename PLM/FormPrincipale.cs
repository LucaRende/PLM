using Guna.UI2.WinForms;
using System.Collections.Immutable;

namespace PLM
{
    public partial class FormPrincipale : Form
    {
        List<FlowLayoutPanel> bottoniPrimari = new List<FlowLayoutPanel>();

        // bool bottoni menu
        bool menuCommercialeEspanso = false;
        bool menuUfficioTecnicoEspanso = false;
        bool menuProduzioneEspanso = false;
        bool menuItEspanso = false;
        bool menuAmministrazioneEspanso = false;

        // Quantitativo bottoni secondari
        int qtaBottoniCommerciale = 2;
        int qtaBottoniUfficioTecnico = 4;
        int qtaBottoniProduzione = 3;
        int qtaBottoniIt = 1;
        int qtaBottoniAmministrazione = 4;

        // Altezza bottoni secondari
        int altezzaBottoniSecondari = 25;

        public FormPrincipale()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            CaricaBottoniPrimari();

            btn_MassimizzaForm_Click(sender, e);

            // Centra nomeProgramma
            lbl_nomeProgramma.Location = new Point((this.Size.Width - 41) / 2, 5);
        }

        private void CaricaBottoniPrimari()
        {
            bottoniPrimari.AddRange(flp_btnCommerciale, flp_btnUfficioTecnico, flp_btnProduzione, flp_btnIT, flp_btnAmministrazione);
        }

        private void btn_Home_Click(object sender, EventArgs e)
        {

        }

        private void btn_Commerciale_Click(object sender, EventArgs e)
        {
            menuCommercialeEspanso = EspandiMenuSecondario(menuCommercialeEspanso, btn_Commerciale, flp_btnCommerciale, qtaBottoniCommerciale);
        }

        private void btn_UfficioTecnico_Click(object sender, EventArgs e)
        {
            menuUfficioTecnicoEspanso = EspandiMenuSecondario(menuUfficioTecnicoEspanso, btn_UfficioTecnico, flp_btnUfficioTecnico, qtaBottoniUfficioTecnico);
        }

        private void btn_Produzione_Click(object sender, EventArgs e)
        {
            menuProduzioneEspanso = EspandiMenuSecondario(menuProduzioneEspanso, btn_Produzione, flp_btnProduzione, qtaBottoniProduzione);
        }

        private void btn_IT_Click(object sender, EventArgs e)
        {
            menuItEspanso = EspandiMenuSecondario(menuItEspanso, btn_IT, flp_btnIT, qtaBottoniIt);
        }

        private void btn_Amministrazione_Click(object sender, EventArgs e)
        {
            menuAmministrazioneEspanso = EspandiMenuSecondario(menuAmministrazioneEspanso, btn_Amministrazione, flp_btnAmministrazione, qtaBottoniAmministrazione);
        }

        private bool EspandiMenuSecondario(bool menuEspanso, Guna2Button bottone, FlowLayoutPanel flp, int qtaBottoni)
        {
            if (menuEspanso) //Se espanso collassa
            {
                bottone.Image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                flp.Size = new System.Drawing.Size(170, 35);
                menuEspanso = false;
            }
            else // Se non espanso espandi
            {
                bottone.Image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                flp.Size = new System.Drawing.Size(170, 35 + altezzaBottoniSecondari * qtaBottoni);
                menuEspanso = true;

                // e richiudi tutti gli altri
            }

            return menuEspanso;
        }

        private void btn_CrmCommerciale_Click(object sender, EventArgs e)
        {
            MetodiUniversali.ApriFormInPanel(pn_Contenitore, MetodiUniversali.formCrm);
        }

        private void btn_CrmAmministrazione_Click(object sender, EventArgs e)
        {
            MetodiUniversali.ApriFormInPanel(pn_Contenitore, MetodiUniversali.formCrm);
        }

        private void btn_CrmUfficioTecnico_Click(object sender, EventArgs e)
        {
            MetodiUniversali.ApriFormInPanel(pn_Contenitore, MetodiUniversali.formCrm);
        }

        private void btn_PianificazioneUfficioTecnico_Click(object sender, EventArgs e)
        {
            MetodiUniversali.ApriFormInPanel(pn_Contenitore, MetodiUniversali.formUfficioTecnicoPianificazione);
        }

        private void btn_MassimizzaForm_Click(object sender, EventArgs e)
        {
            //Ridimensiona panel
            pn_Inferiore.Size = new System.Drawing.Size(1600, this.Size.Height - 30);

            // Centra nomeProgramma
            lbl_nomeProgramma.Location = new Point((this.Size.Width - 41) / 2, 5);
        }
    }
}