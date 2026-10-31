// Cyfan - main window, shared job plumbing (progress/log/cancel) and ffmpeg setup.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Cyfan
{
    // Context handed to a job running on the background thread
    class JobContext : IJobSink
    {
        readonly MainForm _form;
        volatile bool _cancel;
        public JobContext(MainForm form) { _form = form; }

        public bool Cancelled { get { return _cancel; } }
        internal void RequestCancel() { _cancel = true; }

        public void Log(string text) { _form.LogFromJob(text); }
        public void Status(string text) { _form.ProgressFromJob(-1, text); }
        public void Progress(double frac, string status) { _form.ProgressFromJob(frac, status); }

        // Shown to the user when the job finishes
        public string ResultPath;
        public string ResultMessage;
    }

    interface IJobHost
    {
        void StartJob(string title, Action<JobContext> work);
        bool EnsureFFmpeg();
        void Log(string text);
        bool Busy { get; }
    }

    class MainForm : Form, IJobHost
    {
        TabControl _tabs;
        ProgressBar _prog;
        Label _status, _lblFF;
        Button _btnCancel, _btnFF;
        TextBox _log;
        JobContext _job;
        Thread _thread;

        MergePage _merge;
        SplitPage _split;
        ConvertPage _convert;
        ToolsPage _tools;

        public MainForm()
        {
            Text = App.Name + " - " + L.T("Merge, split and convert video");
            // Pages are laid out for 980 px. Narrower than that and controls inside
            // group boxes get clipped (a group box cannot scroll), so the minimum
            // width guarantees the design width. Vertical shortfall is handled by
            // the tab page's own scrolling.
            Size = new Size(1040, 850);
            MinimumSize = new Size(1040, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); }
            catch { }

            // NOTE - the order matters: WinForms lays docked controls out from the
            // end of the collection towards the start, so the control added LAST is
            // placed FIRST and takes all remaining space. The Dock=Fill tab control
            // must get the leftover area, so it is added FIRST; the menu and bottom
            // panel claim their edges afterwards. In the reverse order the tabs
            // cover the whole window and the bottom panel hides the action buttons.
            BuildTabs();
            BuildBottom();
            BuildMenu();

            FF.Locate();
            RefreshFFStatus();
            Log(App.Name + " " + App.Version);
            if (FF.Ready) Log("ffmpeg: " + FF.FFmpeg);

            Shown += delegate
            {
                if (!FF.Ready)
                {
                    if (MessageBox.Show(
                        L.T("ffmpeg is not installed on this computer. It does all the video work.\r\n\r\nInstall it now?"),
                        L.T("ffmpeg is required"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        InstallFFmpeg();
                }
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (Busy)
                {
                    if (MessageBox.Show(L.T("A task is still running. Quit anyway?"), L.T("Confirm"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    { e.Cancel = true; return; }
                    if (_job != null) _job.RequestCancel();
                }
                Settings.Save();
            };
        }

        // ------------------------------------------------------------------ layout

        void BuildMenu()
        {
            MenuStrip ms = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem(L.T("&File"));
            file.DropDownItems.Add(L.T("Exit"), null, delegate { Close(); });

            ToolStripMenuItem tools = new ToolStripMenuItem(L.T("&Tools"));
            tools.DropDownItems.Add(L.T("Install or update ffmpeg..."), null, delegate { InstallFFmpeg(); });
            tools.DropDownItems.Add(L.T("Choose ffmpeg folder..."), null, delegate { PickFFmpegDir(); });
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(L.T("Open settings folder"), null, delegate { Util.RevealInExplorer(App.DataDir); });
            tools.DropDownItems.Add(L.T("Clear log"), null, delegate { _log.Clear(); });

            ToolStripMenuItem lang = new ToolStripMenuItem(L.T("&Language"));
            ToolStripMenuItem en = new ToolStripMenuItem("English", null, delegate { SetLanguage(false); });
            ToolStripMenuItem tr = new ToolStripMenuItem("Turkce", null, delegate { SetLanguage(true); });
            en.Checked = !L.Tr;
            tr.Checked = L.Tr;
            lang.DropDownItems.Add(en);
            lang.DropDownItems.Add(tr);

            ToolStripMenuItem help = new ToolStripMenuItem(L.T("&Help"));
            help.DropDownItems.Add(L.T("About"), null, delegate { ShowAbout(); });

            ms.Items.Add(file);
            ms.Items.Add(tools);
            ms.Items.Add(lang);
            ms.Items.Add(help);
            MainMenuStrip = ms;
            Controls.Add(ms);
        }

        void SetLanguage(bool turkish)
        {
            if (turkish == L.Tr) return;
            L.Set(turkish);
            if (MessageBox.Show(L.T("Language changed. Restart the application now?"), App.Name,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Application.Restart();
        }

        void BuildBottom()
        {
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 220;
            bottom.Padding = new Padding(10, 6, 10, 8);

            // ffmpeg status row (very bottom)
            Panel ffRow = new Panel();
            ffRow.Dock = DockStyle.Bottom;
            ffRow.Height = 32;
            _btnFF = new Button();
            _btnFF.Dock = DockStyle.Right;
            _btnFF.Width = 250;
            _btnFF.Click += delegate { InstallFFmpeg(); };
            _lblFF = new Label();
            _lblFF.Dock = DockStyle.Fill;
            _lblFF.TextAlign = ContentAlignment.MiddleLeft;
            _lblFF.AutoEllipsis = true;
            ffRow.Controls.Add(_lblFF);
            ffRow.Controls.Add(_btnFF);

            // progress row (top)
            Panel progRow = new Panel();
            progRow.Dock = DockStyle.Top;
            progRow.Height = 56;
            _btnCancel = new Button();
            _btnCancel.Dock = DockStyle.Right;
            _btnCancel.Width = 120;
            _btnCancel.Text = L.T("CANCEL");
            _btnCancel.Enabled = false;
            _btnCancel.Click += delegate
            {
                if (_job != null) { _job.RequestCancel(); _btnCancel.Enabled = false; Log(L.T("Cancelling...")); }
            };
            Panel progInner = new Panel();
            progInner.Dock = DockStyle.Fill;
            progInner.Padding = new Padding(0, 0, 10, 0);
            _prog = new ProgressBar();
            _prog.Dock = DockStyle.Top;
            _prog.Height = 24;
            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoEllipsis = true;
            progInner.Controls.Add(_status);
            progInner.Controls.Add(_prog);
            progRow.Controls.Add(progInner);
            progRow.Controls.Add(_btnCancel);

            _log = new TextBox();
            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.FromArgb(250, 250, 250);
            _log.Font = new Font("Consolas", 8.5f);

            bottom.Controls.Add(_log);
            bottom.Controls.Add(progRow);
            bottom.Controls.Add(ffRow);
            Controls.Add(bottom);
        }

        void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(14, 6);

            _merge = new MergePage(this);
            _split = new SplitPage(this);
            _convert = new ConvertPage(this);
            _tools = new ToolsPage(this);

            _tabs.TabPages.Add(Wrap(L.T("Merge"), _merge));
            _tabs.TabPages.Add(Wrap(L.T("Split"), _split));
            _tabs.TabPages.Add(Wrap(L.T("Convert"), _convert));
            _tabs.TabPages.Add(Wrap(L.T("Tools"), _tools));
            Controls.Add(_tabs);
        }

        static TabPage Wrap(string title, Control page)
        {
            TabPage t = new TabPage(title);
            t.Padding = new Padding(10);
            t.BackColor = SystemColors.Control;
            // If the window is smaller than the page design size, scrollbars appear here
            t.AutoScroll = true;
            page.Dock = DockStyle.Fill;
            t.Controls.Add(page);
            return t;
        }

        void ShowAbout()
        {
            string ver = FF.VersionString();
            MessageBox.Show(
                App.Name + " " + App.Version + "\r\n" + L.T("Merge, split and convert video") + "\r\n\r\n" +
                "\"Cyfan\" is Welsh for \"whole, complete\": it makes parts whole again, " +
                "and splits the whole into the parts you want.\r\n\r\n" +
                "All video work is done by ffmpeg.\r\n" +
                "ffmpeg: " + (ver == null ? L.T("not installed") : ver) + "\r\n" +
                (FF.FFmpeg == null ? "" : FF.FFmpeg + "\r\n") +
                "\r\nffmpeg is separate free software (https://ffmpeg.org). It is not " +
                "bundled with this program; it is downloaded when you ask for it.\r\n\r\n" +
                "https://github.com/" + App.Repo,
                L.T("About"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------- job plumbing

        public bool Busy { get { return _thread != null && _thread.IsAlive; } }

        // So the layout test can walk through the tabs
        internal TabControl Tabs { get { return _tabs; } }

        public void StartJob(string title, Action<JobContext> work)
        {
            if (Busy) { MessageBox.Show(L.T("A task is already running."), App.Name); return; }
            _job = new JobContext(this);
            _prog.Value = 0;
            _status.Text = title + " " + L.T("Starting...");
            Log("=== " + title + " ===");
            SetBusyUi(true);

            JobContext ctx = _job;
            _thread = new Thread((ThreadStart)delegate
            {
                string error = null;
                DateTime t0 = DateTime.Now;
                try { work(ctx); }
                catch (OperationCanceledException) { ctx.RequestCancel(); }
                catch (Exception ex) { error = ex.Message; }
                TimeSpan elapsed = DateTime.Now - t0;
                string err = error;
                BeginInvoke((MethodInvoker)delegate { JobFinished(title, ctx, err, elapsed); });
            });
            _thread.IsBackground = true;
            _thread.Start();
        }

        void JobFinished(string title, JobContext ctx, string error, TimeSpan elapsed)
        {
            SetBusyUi(false);
            if (ctx.Cancelled)
            {
                _status.Text = L.T("Cancelled");
                Log(L.T("Cancelled"));
            }
            else if (error != null)
            {
                _prog.Value = 0;
                _status.Text = L.T("Error");
                Log("ERROR: " + error);
                MessageBox.Show(error, L.T("{0} failed", title), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                _prog.Value = 100;
                _status.Text = L.T("Done") + "  (" + Util.Dur(elapsed.TotalSeconds) + ")";
                string msg = ctx.ResultMessage == null ? L.T("{0} finished", title) : ctx.ResultMessage;
                Log(msg);
                if (ctx.ResultPath != null)
                {
                    if (MessageBox.Show(msg + "\r\n\r\n" + L.T("Open the folder?"), L.T("Done"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Util.RevealInExplorer(ctx.ResultPath);
                }
                else MessageBox.Show(msg, L.T("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            _thread = null;
            _job = null;
        }

        void SetBusyUi(bool busy)
        {
            _tabs.Enabled = !busy;
            _btnFF.Enabled = !busy;
            _btnCancel.Enabled = busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        internal void LogFromJob(string text)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Log(text); }); return; }
            Log(text);
        }

        internal void ProgressFromJob(double frac, string status)
        {
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { ApplyProgress(frac, status); }); return; }
            ApplyProgress(frac, status);
        }

        void ApplyProgress(double frac, string status)
        {
            if (frac >= 0)
            {
                int v = (int)Math.Round(frac * 100);
                if (v < 0) v = 0; if (v > 100) v = 100;
                _prog.Value = v;
                _status.Text = v + "%   " + status;
            }
            else _status.Text = status;
        }

        public void Log(string text)
        {
            if (text == null) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { Log(text); }); return; }
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\r\n");
        }

        // ------------------------------------------------------------------- ffmpeg

        public bool EnsureFFmpeg()
        {
            if (FF.Ready) return true;
            if (MessageBox.Show(L.T("ffmpeg is not installed. It is required for this.\r\n\r\nInstall it now?"),
                L.T("ffmpeg is required"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                InstallFFmpeg();
            return FF.Ready;
        }

        void RefreshFFStatus()
        {
            if (FF.Ready)
            {
                string ver = FF.VersionString();
                _lblFF.ForeColor = Color.FromArgb(0, 110, 40);
                _lblFF.Text = "ffmpeg " + (ver == null ? "" : ver) + " " + L.T("is ready") + "  -  " + FF.FFmpeg;
                _btnFF.Text = L.T("Reinstall / update ffmpeg...");
            }
            else
            {
                _lblFF.ForeColor = Color.FromArgb(190, 0, 0);
                _lblFF.Text = L.T("ffmpeg is not installed - nothing can run.");
                _btnFF.Text = L.T("INSTALL ffmpeg  (one click)");
            }
            if (_merge != null) _merge.OnFFmpegChanged();
            if (_split != null) _split.OnFFmpegChanged();
            if (_convert != null) _convert.OnFFmpegChanged();
            if (_tools != null) _tools.OnFFmpegChanged();
        }

        void PickFFmpegDir()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = L.T("Select the folder that contains ffmpeg.exe and ffprobe.exe");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (!File.Exists(Path.Combine(d.SelectedPath, "ffmpeg.exe")))
            {
                MessageBox.Show(L.T("There is no ffmpeg.exe in that folder."), App.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Settings.Set("FFmpegFolder", d.SelectedPath);
            Settings.Save();
            FF.Locate();
            RefreshFFStatus();
            Log(L.T("ffmpeg folder set manually: ") + d.SelectedPath);
        }

        void InstallFFmpeg()
        {
            if (Busy) { MessageBox.Show(L.T("Finish the running task first."), App.Name); return; }
            string choice = FFmpegInstallDialog.Ask(this);
            if (choice == null) return;

            string zipFromUser = null;
            if (choice == "zip")
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Title = L.T("Select the ffmpeg zip file");
                d.Filter = L.T("Zip archive") + "|*.zip|" + L.T("All files") + "|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                zipFromUser = d.FileName;
            }

            string userZip = zipFromUser;
            StartJob(L.T("ffmpeg installation"), delegate(JobContext ctx)
            {
                string zip = null;
                bool temp = false;
                try
                {
                    if (choice == "auto")
                    {
                        zip = FF.DownloadZip(
                            delegate(string m) { ctx.Log(m); },
                            delegate(long done, long total)
                            {
                                if (total > 0) ctx.Progress((double)done / total,
                                    L.T("Downloading: {0} / {1}", Util.Size(done), Util.Size(total)));
                            },
                            delegate { return ctx.Cancelled; });
                        temp = true;
                    }
                    else if (choice == "zip") zip = userZip;

                    if (zip != null)
                    {
                        ctx.Status(L.T("Installing..."));
                        string dest = FF.InstallFromZip(zip, delegate(string m) { ctx.Log(m); });
                        ctx.ResultMessage = L.T("ffmpeg installed: ") + dest;
                    }
                    else
                    {
                        FF.InstallWinget(delegate(string m) { ctx.Log(m); });
                        ctx.ResultMessage = L.T("ffmpeg installed with winget.");
                    }
                }
                finally
                {
                    if (temp && zip != null && File.Exists(zip))
                    { try { File.Delete(zip); } catch { } }
                }

                FF.Locate();
                if (!FF.Ready)
                    throw new Exception(L.T("Installation finished but ffmpeg still cannot be found. See the log lines."));
                ctx.Log(L.T("ffmpeg is ready: ") + FF.FFmpeg);
                BeginInvoke((MethodInvoker)delegate { RefreshFFStatus(); });
            });
        }
    }

    // ------------------------------------------------------- install method dialog

    static class FFmpegInstallDialog
    {
        public static string Ask(IWin32Window owner)
        {
            Form d = new Form();
            d.Text = L.T("ffmpeg installation");
            d.ClientSize = new Size(560, 290);
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false;
            d.StartPosition = FormStartPosition.CenterParent;
            d.Font = new Font("Segoe UI", 9f);

            Label info = new Label();
            info.SetBounds(16, 12, 525, 46);
            info.Text = L.T("ffmpeg does all the video work. Pick a method; after installing, the program looks for ffmpeg next to itself.");
            d.Controls.Add(info);

            string result = null;

            Button b1 = Btn(d, L.T("Download and install automatically   (recommended)"),
                L.T("Downloads about 110 MB from gyan.dev. No administrator rights needed."), 64);
            Button b2 = Btn(d, L.T("Install with winget"),
                L.T("Windows package manager (Gyan.FFmpeg). Installs system-wide."), 118);
            Button b3 = Btn(d, L.T("Install from a zip file I already have..."),
                L.T("For a computer without internet. Bring the zip on a USB stick."), 172);

            if (!FF.WingetAvailable())
            {
                b2.Enabled = false;
                b2.Text = "  " + L.T("Install with winget") + "\r\n  (" + L.T("winget is not available on this computer") + ")";
            }

            b1.Click += delegate { result = "auto"; d.DialogResult = DialogResult.OK; };
            b2.Click += delegate { result = "winget"; d.DialogResult = DialogResult.OK; };
            b3.Click += delegate { result = "zip"; d.DialogResult = DialogResult.OK; };

            Button cancel = new Button();
            cancel.SetBounds(441, 236, 100, 30);
            cancel.Text = L.T("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            d.Controls.Add(cancel);
            d.CancelButton = cancel;

            if (d.ShowDialog(owner) != DialogResult.OK) return null;
            return result;
        }

        static Button Btn(Form d, string title, string desc, int y)
        {
            Button b = new Button();
            b.SetBounds(16, y, 525, 48);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Text = "  " + title + "\r\n  " + desc;
            d.Controls.Add(b);
            return b;
        }
    }
}
