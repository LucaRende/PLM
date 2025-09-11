using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PLM
{
    public partial class Form_Crm : Form
    {
        public Form_Crm()
        {
            InitializeComponent();
        }

        private void Form_Crm_Load(object sender, EventArgs e)
        {
            MetodiUniversali.CaricaCrmNelPanel(pn_Contenitore);
        }
    }
}
