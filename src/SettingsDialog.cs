// The settings menu: dsh-desktop.ini as a form. Values are written back to the same
// file through an editor that preserves its comments, so hand edits and menu
// edits never diverge. Laid out like DSH's own settings page: sections, a title
// and a muted hint per row, pill toggles, and a themed scroll bar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DshDesktop
{
    internal sealed class SettingsDialog : DsDialog, IMessageFilter
    {
        private readonly Settings _s;
        private readonly Settings _file;   // what dsh-desktop.ini says right now
        private readonly IniFile _ini;
        private readonly bool _dark;
        public bool RestartRequested;

        private readonly Panel _body = new Panel();
        private readonly Panel _content = new Panel();
        private readonly ScrollBarEx _scroll;
        private int _y;

        private ToggleSwitch _server, _toolbar, _tray, _telemetry;
        private TextBox _url, _host, _workspace, _data, _profile, _runtimeDir, _appDir, _args;
        private SelectCell _icon;
        private TextBox _port, _zoom, _startup;
        private readonly List<Control> _serverOnly = new List<Control>();
        private readonly List<Control> _clientOnly = new List<Control>();

        private const int ContentWidth = 600;
        private const int RowControlWidth = 250;

        // The dialog is built before it has an owner, so the cap has to happen once
        // the owner is known: a dialog taller than the window it opens over pushes
        // its own header off the top and stops reading as an overlay.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int maxHeight = Screen.PrimaryScreen.WorkingArea.Height - S(80);
            int maxWidth = Screen.PrimaryScreen.WorkingArea.Width - S(80);
            if (Owner != null && Owner.ClientSize.Height > 0)
                maxHeight = Math.Min(maxHeight, Owner.ClientSize.Height - S(72));
            if (Owner != null && Owner.ClientSize.Width > 0)
                maxWidth = Math.Min(maxWidth, Owner.ClientSize.Width - S(64));

            int height = Math.Max(S(380), Math.Min(ClientSize.Height, maxHeight));
            int width = Math.Max(S(420), Math.Min(ClientSize.Width, maxWidth));
            if (height == ClientSize.Height && width == ClientSize.Width) return;

            ClientSize = new Size(width, height);
            _body.SetBounds(S(0), S(53), width, height - S(53) - S(64));
            LayoutScroll();

            // CenterParent ran against the pre-resize size, so re-center now;
            // otherwise the shrunken dialog keeps the old top and its header ends
            // up above the window.
            if (Owner != null)
            {
                Rectangle owner = Owner.Bounds;
                Location = new Point(
                    owner.Left + (owner.Width - Width) / 2,
                    owner.Top + (owner.Height - Height) / 2);
            }
        }

        public SettingsDialog(Settings settings, Theme theme) : base(theme, Strings.T("settings.title"), ContentWidth + 40, 620, 16)
        {
            _s = settings;
            try { _file = Settings.Parse(new string[0]); } catch { _file = settings; }
            _dark = theme.Dark;
            try { _ini = IniFile.Load(App.IniPath); }
            catch { _ini = IniFile.Empty(); }

            _body.SetBounds(S(0), S(53), ClientSize.Width, ClientSize.Height - S(53) - S(64));
            _body.BackColor = Palette.Surface;
            _body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_body);
            _body.BringToFront();

            _content.SetBounds(0, 0, _body.Width - S(14), 100);
            _content.BackColor = Palette.Surface;
            _body.Controls.Add(_content);

            _scroll = new ScrollBarEx(theme, Scale);
            _scroll.SetBounds(_body.Width - S(12), S(4), S(10), _body.Height - S(8));
            _scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _scroll.ValueChanged += delegate { _content.Top = -_scroll.Value; };
            _body.Controls.Add(_scroll);
            _scroll.BringToFront();

            Build();
            BuildFooter();
            _body.Resize += delegate { LayoutScroll(); };
            LayoutScroll();

            Application.AddMessageFilter(this);
            FormClosed += delegate { Application.RemoveMessageFilter(this); };
        }

        private void LayoutScroll()
        {
            _content.SetBounds(0, -_scroll.Value, _body.Width - S(14), _content.Height);
            _scroll.SetBounds(_body.Width - S(12), S(4), S(10), _body.Height - S(8));
            _scroll.Configure(_content.Height, _body.Height);
        }

        // Wheel events go to the focused control, which is usually a text field, so
        // the scrolling is routed by cursor position instead.
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL) SelectCell.CloseActiveMenu();
            if (m.Msg != WM_MOUSEWHEEL || _scroll == null) return false;
            if (_scroll.MaxValue <= 0) return false;
            if (!_body.RectangleToScreen(_body.ClientRectangle).Contains(Cursor.Position)) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            _scroll.Value -= Math.Sign(delta) * S(56);
            return true;
        }

        private void BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = S(64);
            footer.BackColor = Palette.Footer;
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Palette.Border))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            Controls.Add(footer);
            footer.BringToFront();

            PillButton openIni = FlatButton(Strings.T("settings.openIni"), false);
            openIni.SetBounds(S(20), S(15), S(122), S(34));
            openIni.Click += delegate
            {
                try { Process.Start(App.IniPath); }
                catch
                {
                    try { Process.Start("notepad.exe", "\"" + App.IniPath + "\""); } catch { }
                }
            };
            footer.Controls.Add(openIni);

            int right = ClientSize.Width - S(20);
            PillButton restart = FlatButton(Strings.T("settings.saveRestart"), true);
            restart.SetBounds(right - S(122), S(15), S(122), S(34));
            restart.Click += delegate { Finish(true); };
            footer.Controls.Add(restart);

            PillButton save = FlatButton(Strings.T("settings.save"), false);
            save.SetBounds(right - S(122) - S(88), S(15), S(82), S(34));
            save.Click += delegate { Finish(false); };
            footer.Controls.Add(save);

            PillButton cancel = FlatButton(Strings.T("settings.cancel"), false);
            cancel.SetBounds(right - S(122) - S(88) - S(88), S(15), S(82), S(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(cancel);
        }

        private PillButton FlatButton(string text, bool primary)
        {
            // Same rounded button the dialogs use; a plain WinForms button here
            // was the last square thing left in the form.
            return new PillButton(text,
                primary ? Palette.Accent : Palette.Elevated,
                primary ? Palette.AccentText : Palette.Text,
                primary ? Color.Empty : Palette.Border, Scale);
        }

        private void Finish(bool restart)
        {
            if (!Save()) return;

            // A plain Save used to look like it did nothing: the file changed but the
            // running app kept the old values. Ask whether to apply them now.
            if (!restart)
            {
                using (ConfirmDialog ask = new ConfirmDialog(
                    Palette, Strings.T("settings.title"), Strings.T("settings.applyAsk"),
                    Strings.T("settings.applyNow"), Strings.T("settings.applyLater")))
                {
                    restart = ask.ShowDialog(this) == DialogResult.OK;
                }
            }

            RestartRequested = restart;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Section(string title, bool first)
        {
            if (!first) _y += S(14);
            Label label = new Label();
            label.Text = title;
            label.AutoSize = false;
            label.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            label.ForeColor = Palette.Text;
            label.BackColor = Palette.Surface;
            label.TextAlign = ContentAlignment.BottomLeft;
            label.SetBounds(S(24), _y, S(ContentWidth), S(30));
            _content.Controls.Add(label);

            if (!first)
            {
                Panel line = new Panel();
                line.BackColor = Palette.Border;
                line.SetBounds(S(24), _y - S(10), S(ContentWidth), 1);
                _content.Controls.Add(line);
            }
            _y += S(36);
        }

        private void Row(string title, string hint, Control control, bool serverOnly, bool clientOnly)
        {
            Row(title, hint, control, serverOnly, clientOnly, RowControlWidth);
        }

        private void Row(string title, string hint, Control control, bool serverOnly, bool clientOnly, int fieldWidth)
        {
            int height = string.IsNullOrEmpty(hint) ? S(44) : S(56);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = false;
            titleLabel.Font = new Font("Microsoft YaHei UI", 9f);
            titleLabel.ForeColor = Palette.Text;
            titleLabel.BackColor = Palette.Surface;
            titleLabel.TextAlign = ContentAlignment.TopLeft;
            titleLabel.SetBounds(S(24), _y + S(6), S(ContentWidth - RowControlWidth - 24), S(20));
            _content.Controls.Add(titleLabel);

            if (!string.IsNullOrEmpty(hint))
            {
                Label hintLabel = new Label();
                hintLabel.Text = hint;
                hintLabel.AutoSize = false;
                hintLabel.Font = new Font("Microsoft YaHei UI", 8.25f);
                hintLabel.ForeColor = Palette.TextMuted;
                hintLabel.BackColor = Palette.Surface;
                hintLabel.TextAlign = ContentAlignment.TopLeft;
                hintLabel.SetBounds(S(24), _y + S(25), S(ContentWidth - RowControlWidth - 24), S(24));
                _content.Controls.Add(hintLabel);
            }

            int left = S(24 + ContentWidth - RowControlWidth);
            if (control is ToggleSwitch)
            {
                control.SetBounds(left + S(RowControlWidth - 44), _y + S(10), S(44), S(24));
                _content.Controls.Add(control);
                if (serverOnly) _serverOnly.Add(control);
                if (clientOnly) _clientOnly.Add(control);
                _y += height;
                return;
            }

            // Every field sits in the same rounded cell: DSH draws its inputs with
            // --dsw-radius-md and a hairline border, and mismatched corners between
            // the text fields and the selects is what made this look rough.
            if (control is SelectCell)
            {
                // The select paints its own rounded cell, so it needs no frame.
                control.SetBounds(left, _y + S(8), S(fieldWidth), S(32));
                _content.Controls.Add(control);
                if (serverOnly) _serverOnly.Add(control);
                if (clientOnly) _clientOnly.Add(control);
                _y += height;
                return;
            }

            // A read-only value is a value, not an input: show it as plain text on the
            // panel, the way DSH renders settings it will not let you edit.
            if (control is TextBox && ((TextBox)control).ReadOnly)
            {
                Label value = new Label();
                value.Text = control.Text;
                value.AutoSize = false;
                value.Font = new Font("Microsoft YaHei UI", 9f);
                value.ForeColor = Palette.TextMuted;
                value.BackColor = FieldBackMuted();
                value.TextAlign = ContentAlignment.MiddleLeft;
                Panel muted = FieldCell(value, fieldWidth, true);
                muted.SetBounds(left, _y + S(8), S(fieldWidth), S(32));
                _content.Controls.Add(muted);
                if (serverOnly) _serverOnly.Add(muted);
                if (clientOnly) _clientOnly.Add(muted);
                _y += height;
                return;
            }

            Panel cell = FieldCell(control, fieldWidth);
            cell.SetBounds(left, _y + S(8), S(fieldWidth), S(32));
            _content.Controls.Add(cell);
            if (serverOnly) _serverOnly.Add(cell);
            if (clientOnly) _clientOnly.Add(cell);
            _y += height;
        }

        private Color FieldBackMuted()
        {
            // Same step the address field uses for a value it will not accept edits
            // into, so a read-only row reads as a value rather than an input.
            return _dark ? App.Shift(Palette.Surface, -16) : Color.FromArgb(238, 241, 245);
        }

        private Color FieldBack()
        {
            return _dark ? App.Shift(Palette.Surface, -10) : Color.White;
        }

        // Rounded input frame: background, 1px border at --dsw-radius-md, and the
        // real control inset inside it, vertically centred.
        private Panel FieldCell(Control inner, int width)
        {
            return FieldCell(inner, width, inner is TextBox && ((TextBox)inner).ReadOnly);
        }

        private Panel FieldCell(Control inner, int width, bool readOnly)
        {
            Color fill = readOnly ? FieldBackMuted() : FieldBack();

            Panel cell = new Panel();
            cell.BackColor = Palette.Surface;
            cell.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // The cell must paint only inside the rounded shape: a square fill
                // left its corners showing around the border.
                using (SolidBrush back = new SolidBrush(Palette.Surface))
                    g.FillRectangle(back, cell.ClientRectangle);
                Rectangle r = new Rectangle(0, 0, cell.ClientSize.Width - 1, cell.ClientSize.Height - 1);
                using (GraphicsPath path = Theme.Rounded(r, S(8)))
                {
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    using (Pen pen = new Pen(Palette.Border)) g.DrawPath(pen, path);
                }
            };

            if (inner is TextBox)
            {
                TextBox box = (TextBox)inner;
                box.BorderStyle = BorderStyle.None;
            }
            inner.BackColor = fill;
            inner.ForeColor = readOnly ? Palette.TextMuted : Palette.Text;

            int innerHeight;
            if (inner is TextBox) innerHeight = ((TextBox)inner).PreferredHeight;
            else innerHeight = S(24);
            if (inner is NumericUpDown) ((NumericUpDown)inner).BorderStyle = BorderStyle.None;
            if (inner is NumericUpDown) innerHeight = S(24);

            int height = S(32);
            inner.SetBounds(S(11), (height - innerHeight) / 2, S(width) - S(22), innerHeight);
            cell.Controls.Add(inner);
            return cell;
        }

        private void RowWithBrowse(string title, string hint, TextBox box)
        {
            int height = string.IsNullOrEmpty(hint) ? S(44) : S(56);
            Row(title, hint, box, false, false, RowControlWidth - 76);
            _y -= height;

            PillButton browse = FlatButton(Strings.T("settings.browse"), false);
            browse.SetBounds(S(24 + ContentWidth - 70), _y + S(9), S(70), S(30));
            browse.Click += delegate { Browse(box); };
            _content.Controls.Add(browse);
            _y += height;
        }

        private TextBox ReadOnlyInput(string value)
        {
            TextBox box = Input(value);
            box.ReadOnly = true;
            box.Cursor = Cursors.Default;
            box.TabStop = false;
            box.ForeColor = Palette.TextMuted;
            box.BackColor = FieldBackMuted();
            return box;
        }

        private TextBox Input(string value)
        {
            TextBox box = new TextBox();
            box.Text = value ?? "";
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Microsoft YaHei UI", 9f);
            box.BackColor = FieldBack();
            box.ForeColor = Palette.Text;
            return box;
        }

        // A flat combo box loses its border in a light theme, which makes it read
        // as a label; the shell draws a hairline frame around the same background
        // so the field is visible in both themes without a grey band.
        private SelectCell Choice(string[] items, int index)
        {
            SelectCell select = new SelectCell(Palette, Scale);
            select.Items = items;
            select.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            select.BackColor = Palette.Surface;
            return select;
        }

        private static SelectCell ComboOf(Control field)
        {
            if (field is SelectCell) return (SelectCell)field;
            Panel cell = field as Panel;
            if (cell == null || cell.Controls.Count == 0) return null;
            return cell.Controls[0] as SelectCell;
        }

        // NumericUpDown always paints its own spin-button strip, which showed up as
        // a grey block inside the rounded cell. These are plain fields like the
        // address ones; the value is validated when saving.
        private TextBox Number(decimal value, decimal min, decimal max, decimal step, int decimals)
        {
            TextBox box = Input(decimals > 0
                ? value.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture)
                : ((int)value).ToString(CultureInfo.InvariantCulture));
            box.Tag = new decimal[] { min, max, step };
            return box;
        }

        private static int IntValue(TextBox box, int min, int max, int fallback)
        {
            int parsed;
            string text = (box.Text ?? "").Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;
            return Math.Max(min, Math.Min(max, parsed));
        }

        private static double DoubleValue(TextBox box, double min, double max, double fallback)
        {
            double parsed;
            string text = (box.Text ?? "").Trim();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return fallback;
            return Math.Max(min, Math.Min(max, parsed));
        }

        private void Browse(TextBox target)
        {
            if (target == null) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                string current = (target.Text ?? "").Trim();
                if (current.Length > 0)
                {
                    try
                    {
                        string resolved = App.ResolveDir(current, current);
                        if (Directory.Exists(resolved)) dialog.SelectedPath = resolved;
                    }
                    catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
            }
        }

        private void Build()
        {
            _y = S(8);

            Section(Strings.T("settings.section.start"), true);

            _server = new ToggleSwitch(Palette, _file.Server, Scale);
            _server.CheckedChanged += delegate { SyncEnabled(); };
            Row(Strings.T("settings.server"), Strings.T("settings.server.hint"), _server, false, false);

            _url = Input(_file.Url);
            Row(Strings.T("settings.url"), Strings.T("settings.url.hint"), _url, false, true);


            _host = Input(_file.Host);
            Row(Strings.T("settings.host"), Strings.T("settings.host.hint"), _host, true, false);
            _serverOnly.Add(_host);

            _port = Number(_file.Port, 0, 65535, 1, 0);
            Row(Strings.T("settings.port"), Strings.T("settings.port.hint"), _port, true, false);
            _serverOnly.Add(_port);

            _startup = Number(_file.StartupSeconds, 10, 900, 10, 0);
            Row(Strings.T("settings.timeout"), Strings.T("settings.timeout.hint"), _startup, true, false);
            _serverOnly.Add(_startup);

            Section(Strings.T("settings.section.ui"), false);

            _icon = Choice(new string[]
            {
                "default", "auto",
                Strings.T("settings.icon.light"), Strings.T("settings.icon.dark")
            }, IconIndex(_file.Icon));
            Row(Strings.T("settings.icon"), Strings.T("settings.icon.hint"), _icon, false, false);

            _zoom = Number((decimal)_file.Zoom, 0.5M, 2.0M, 0.1M, 1);
            Row(Strings.T("settings.zoom"), Strings.T("settings.zoom.hint"), _zoom, false, false);

            _toolbar = new ToggleSwitch(Palette, _file.Toolbar, Scale);
            Row(Strings.T("settings.toolbar"), Strings.T("settings.toolbar.hint"), _toolbar, false, false);

            _tray = new ToggleSwitch(Palette, _file.CloseToTray, Scale);
            Row(Strings.T("settings.tray"), Strings.T("settings.tray.hint"), _tray, false, false);

            Section(Strings.T("settings.section.paths"), false);

            _workspace = Input(_file.Workspace);
            RowWithBrowse(Strings.T("settings.workspace"), Strings.T("settings.workspace.hint"), _workspace);

            _data = Input(_file.Data);
            RowWithBrowse(Strings.T("settings.data"), Strings.T("settings.data.hint"), _data);

            _profile = Input(_file.Profile);
            RowWithBrowse(Strings.T("settings.profile"), Strings.T("settings.profile.hint"), _profile);

            // The runtime and app folders are fixed by the package layout: editing
            // them only ever breaks the launcher, so they are shown read-only.
            _runtimeDir = ReadOnlyInput(_file.RuntimeDir);
            Row(Strings.T("settings.runtime"), Strings.T("settings.runtime.hint"), _runtimeDir, true, false);
            _serverOnly.Add(_runtimeDir);

            _appDir = ReadOnlyInput(_file.AppDir);
            Row(Strings.T("settings.app"), Strings.T("settings.app.hint"), _appDir, true, false);
            _serverOnly.Add(_appDir);

            Section(Strings.T("settings.section.advanced"), false);

            _args = Input(_file.ExtraArgs);
            Row(Strings.T("settings.args"), Strings.T("settings.args.hint"), _args, false, false);

            _telemetry = new ToggleSwitch(Palette, _file.Telemetry, Scale);
            Row(Strings.T("settings.telemetry"), Strings.T("settings.telemetry.hint"), _telemetry, false, false);

            _y += S(10);
            _content.Height = _y;
            SyncEnabled();
        }

        private static int IconIndex(string icon)
        {
            string value = (icon ?? "default").Trim().ToLowerInvariant();
            if (value == "auto") return 1;
            if (value == "light") return 2;
            if (value == "dark") return 3;
            return 0;
        }

        private static string[] IconValues = new string[] { "default", "auto", "light", "dark" };

        private void SyncEnabled()
        {
            bool server = _server.Checked;
            foreach (Control control in _serverOnly) SetEnabled(control, server);
            foreach (Control control in _clientOnly) SetEnabled(control, !server);
        }

        private void SetEnabled(Control control, bool enabled)
        {
            control.Enabled = enabled;
            if (control is ToggleSwitch) return;
            Color back = enabled
                ? (_dark ? App.Shift(Palette.Surface, -10) : Color.White)
                : (_dark ? App.Shift(Palette.Surface, -4) : Color.FromArgb(242, 244, 246));
            control.BackColor = back;
            control.ForeColor = enabled ? Palette.Text : Palette.TextMuted;
        }

        private bool Save()
        {
            string url = (_url.Text ?? "").Trim();
            if (!_server.Checked)
            {
                if (url.Length == 0)
                {
                    MessageBox.Show(this, Strings.T("settings.needUrl"), Strings.T("settings.title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _url.Focus();
                    return false;
                }
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    MessageBox.Show(this, Strings.T("settings.badUrl"), Strings.T("settings.title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _url.Focus();
                    return false;
                }
            }

            try
            {
                _ini.Set("server", _server.Checked ? "1" : "0");
                _ini.Set("url", url);
                _ini.Set("host", (_host.Text ?? "").Trim());
                _ini.Set("port", IntValue(_port, 0, 65535, 0).ToString(CultureInfo.InvariantCulture));
                _ini.Set("workspace", (_workspace.Text ?? "").Trim());
                _ini.Set("data", (_data.Text ?? "").Trim());
                _ini.Set("profile", (_profile.Text ?? "").Trim());
                _ini.Set("runtime", (_runtimeDir.Text ?? "").Trim());
                _ini.Set("app", (_appDir.Text ?? "").Trim());
                _ini.Set("args", (_args.Text ?? "").Trim());
                _ini.Set("toolbar", _toolbar.Checked ? "1" : "0");
                _ini.Set("zoom", DoubleValue(_zoom, 0.5, 2.0, 1.0).ToString("0.0", CultureInfo.InvariantCulture));
                _ini.Set("icon", IconValues[Math.Max(0, ComboOf(_icon).SelectedIndex)]);
                _ini.Set("closetotray", _tray.Checked ? "1" : "0");
                _ini.Set("telemetry", _telemetry.Checked ? "1" : "0");
                _ini.Set("startupseconds", IntValue(_startup, 10, 900, 120).ToString(CultureInfo.InvariantCulture));
                _ini.Save(App.IniPath);
                App.Log("settings saved to " + App.IniPath);
                // Keep the running process consistent with the file, so anything
                // that reads App.S later sees the new values too.
                try { App.S = Settings.Parse(new string[0]); } catch (Exception ex) { App.Log("re-read failed: " + ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                App.Log("settings save failed: " + ex.Message);
                MessageBox.Show(this, Strings.T("settings.saveFailed") + ": " + ex.Message
                    + Environment.NewLine + Environment.NewLine + App.IniPath,
                    Strings.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
