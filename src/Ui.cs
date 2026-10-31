// Cyfan - kucuk arayuz yardimcilari (kod tekrarini azaltmak icin).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Cyfan
{
    static class Ui
    {
        public static string VideoFilter
        {
            get
            {
                return L.T("Video files") +
                    "|*.mp4;*.mts;*.m2ts;*.mov;*.mkv;*.avi;*.mxf;*.ts;*.m4v;*.mpg;*.mpeg;*.wmv;*.flv;*.webm;*.3gp" +
                    "|" + L.T("All files") + "|*.*";
            }
        }

        public static Label Lbl(Control parent, string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            parent.Controls.Add(l);
            return l;
        }

        public static Label Lbl(Control parent, string text, int x, int y, int w)
        {
            return Lbl(parent, text, x, y, w, 18);
        }

        public static Button Btn(Control parent, string text, int x, int y, int w, int h, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            if (click != null) b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        public static TextBox Txt(Control parent, string text, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Text = text == null ? "" : text;
            t.SetBounds(x, y, w, 23);
            parent.Controls.Add(t);
            return t;
        }

        public static ComboBox Cmb(Control parent, int x, int y, int w, object[] items, int selected)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 23);
            if (items != null) c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = Math.Min(Math.Max(0, selected), c.Items.Count - 1);
            parent.Controls.Add(c);
            return c;
        }

        public static CheckBox Chk(Control parent, string text, int x, int y, int w, bool checked_)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 22);
            c.Checked = checked_;
            parent.Controls.Add(c);
            return c;
        }

        public static RadioButton Rad(Control parent, string text, int x, int y, int w, bool checked_)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.SetBounds(x, y, w, 22);
            r.Checked = checked_;
            parent.Controls.Add(r);
            return r;
        }

        public static GroupBox Grp(Control parent, string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.SetBounds(x, y, w, h);
            parent.Controls.Add(g);
            return g;
        }

        public static void Anchor(Control c, bool right, bool bottom)
        {
            AnchorStyles a = AnchorStyles.Top | AnchorStyles.Left;
            if (right) a |= AnchorStyles.Right;
            if (bottom) a |= AnchorStyles.Bottom;
            c.Anchor = a;
        }

        public static void AnchorRightTop(Control c)
        {
            c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        }

        // Dosya surukleyip birakmayi etkinlestirir.
        public static void EnableDrop(Control c, Action<string[]> onFiles)
        {
            c.AllowDrop = true;
            c.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            c.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) onFiles(files);
            };
        }

        public static string[] OpenFiles(IWin32Window owner, string title, bool multi)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Title = title;
            d.Multiselect = multi;
            d.Filter = VideoFilter;
            if (d.ShowDialog(owner) != DialogResult.OK) return null;
            return d.FileNames;
        }

        public static string SaveFile(IWin32Window owner, string title, string filter, string initial)
        {
            SaveFileDialog d = new SaveFileDialog();
            d.Title = title;
            d.Filter = filter;
            d.OverwritePrompt = true;
            if (!string.IsNullOrEmpty(initial))
            {
                try
                {
                    d.InitialDirectory = Path.GetDirectoryName(initial);
                    d.FileName = Path.GetFileName(initial);
                }
                catch { }
            }
            if (d.ShowDialog(owner) != DialogResult.OK) return null;
            return d.FileName;
        }

        public static string PickFolder(IWin32Window owner, string desc, string initial)
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = desc;
            if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) d.SelectedPath = initial;
            if (d.ShowDialog(owner) != DialogResult.OK) return null;
            return d.SelectedPath;
        }

        public static readonly Color Good = Color.FromArgb(0, 110, 40);
        public static readonly Color Warn = Color.FromArgb(165, 70, 0);
        public static readonly Color Bad = Color.FromArgb(190, 0, 0);

        // Uzun metni cok satirli salt okunur kutuda gosterir.
        public static void ShowText(IWin32Window owner, string title, string text)
        {
            Form f = new Form();
            f.Text = title;
            f.ClientSize = new Size(760, 600);
            f.StartPosition = FormStartPosition.CenterParent;
            f.Font = new Font("Segoe UI", 9f);
            TextBox t = new TextBox();
            t.Dock = DockStyle.Fill;
            t.Multiline = true;
            t.ReadOnly = true;
            t.ScrollBars = ScrollBars.Both;
            t.WordWrap = false;
            t.Font = new Font("Consolas", 9f);
            t.Text = text == null ? "" : text.Replace("\n", "\r\n").Replace("\r\r", "\r");
            f.Controls.Add(t);
            f.ShowDialog(owner);
        }
    }

    // Tum sayfalarin ortak temeli
    abstract class PageBase : UserControl
    {
        protected readonly IJobHost Host;
        // Sayfalarin tasarim boyutu. Denetimler mutlak koordinatlarla yerlestirilip
        // Anchor ile buyuyor; Anchor mesafeleri bu boyuta gore hesaplanir, bu yuzden
        // denetimler eklenmeden once ayarlanmasi gerekir.
        public const int DesignW = 980;
        public const int DesignH = 500;

        protected PageBase(IJobHost host)
        {
            Host = host;
            BackColor = SystemColors.Control;
            Size = new Size(DesignW, DesignH);

            // MinimumSize sart: sayfa TabPage'e Dock=Fill ile konulurken TabPage
            // henuz 200x100 varsayilan boyutundadir ve sayfa bir an icin 180x80'e
            // sikisir. Saga yaslanmis denetimler o anda sifir genislige kirpilir,
            // pencere buyudugunde de eski mesafelerine donemez; sonuc ust uste
            // binen denetimlerdir. MinimumSize bu sikismayi engeller, pencere
            // tasarimdan kucukse TabPage kayma cubugu gosterir.
            MinimumSize = new Size(DesignW, DesignH);
        }
        public virtual void OnFFmpegChanged() { }

        protected bool Ready()
        {
            if (!Host.EnsureFFmpeg()) return false;
            if (Host.Busy) { MessageBox.Show(L.T("A task is already running."), App.Name); return false; }
            return true;
        }

        protected MediaInfo ProbeOrWarn(string path)
        {
            MediaInfo m = MediaInfo.Probe(path);
            if (!m.Ok)
            {
                MessageBox.Show(L.T("Could not read the file: ") + Path.GetFileName(path) +
                    (m.Error == null ? "" : "\r\n\r\n" + m.Error), App.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return m;
        }
    }
}
