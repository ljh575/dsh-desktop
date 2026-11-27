using System;
using System.Drawing;
using System.Windows.Forms;

namespace DshDesktop
{
    // Shown when the window is closed while closeaction=ask. Instead of deciding for the user it
    // asks which behaviour this close should use: park the window in the notification area (the DSH
    // session survives) or quit. Yes = tray, No = quit, Cancel/Esc/X = leave the window open.
    // The checkbox writes the answer back as closeaction= so the question is asked only once.
    internal sealed class CloseDialog : DsDialog
    {
        private readonly CheckBox _remember;

        public CloseDialog(Theme theme) : base(theme, Strings.T("close.title"), 460, 330)
        {
            AddText(Strings.T("close.head"), 20, 70, 420, 26,
                UiFont(9f, FontStyle.Bold), Palette.Text);
            AddText(Strings.T("close.trayhint"), 20, 104, 420, 40,
                UiFont(9f), Palette.TextMuted);
            AddText(Strings.T("close.quithint"), 20, 150, 420, 22,
                UiFont(9f), Palette.TextMuted);

            _remember = new CheckBox();
            _remember.Text = Strings.T("close.remember");
            _remember.AutoSize = false;
            _remember.SetBounds(S(20), S(184), S(420), S(26));
            _remember.ForeColor = Palette.Text;
            _remember.BackColor = Palette.Surface;
            _remember.UseVisualStyleBackColor = false;
            Controls.Add(_remember);
            _remember.BringToFront();

            PillButton tray = AddButton(Strings.T("close.tray"), 20, 238, 150, true);
            tray.Click += delegate { DialogResult = DialogResult.Yes; Close(); };
            PillButton quit = AddButton(Strings.T("close.quit"), 180, 238, 120, false);
            quit.Click += delegate { DialogResult = DialogResult.No; Close(); };
            PillButton cancel = AddButton(Strings.T("close.cancel"), 310, 238, 120, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = tray;
            CancelButton = cancel;
        }

        /// <summary>True when the user asked to stop being prompted.</summary>
        public bool RememberChoice
        {
            get { return _remember.Checked; }
        }
    }
}
