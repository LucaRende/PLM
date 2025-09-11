using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLM
{
    public class NoFlickerPanel : Panel
    {
        public NoFlickerPanel()
        {
            // Abilita il double buffer e evita repaint inutili
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;

            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }

        // Evita cancellazione dello sfondo che causa flash grigio
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // non chiamare base → niente clear del background
            // opzionale: puoi riempire tu se serve
            // e.Graphics.Clear(Color.FromArgb(36,36,36));
        }

        // Ignora WM_ERASEBKGND
        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (m.Msg == WM_ERASEBKGND) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
    }
}
