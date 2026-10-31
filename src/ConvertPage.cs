// Cyfan - Convert tab: DVD / Blu-ray / AVCHD / MP4 / editing / audio presets, batch capable.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Cyfan
{
    // Scales one file's progress into the overall progress of a batch.
    class SubSink : IJobSink
    {
        readonly IJobSink _inner;
        readonly double _base, _span;
        readonly string _prefix;
        public SubSink(IJobSink inner, double baseFrac, double span, string prefix)
        { _inner = inner; _base = baseFrac; _span = span; _prefix = prefix; }

        public void Log(string text) { _inner.Log(text); }
        public void Progress(double frac, string status)
        { _inner.Progress(_base + frac * _span, _prefix + status); }
        public bool Cancelled { get { return _inner.Cancelled; } }
    }

    class ConvertPage : PageBase
    {
        readonly List<MediaInfo> _items = new List<MediaInfo>();
        readonly List<Preset> _presets = Preset.All();

        ListView _lv;
        ComboBox _preset, _res;
        Label _notes, _outInfo;
        CheckBox _hw, _sameDir, _keepAudio;
        TextBox _outDir, _suffix, _targetMB;
        Label _lblTarget;
        Button _run;

        static object[] ResNames()
        {
            return new object[] {
                L.T("Same as source"), "3840x2160 (4K)", "2560x1440", "1920x1080", "1280x720", "854x480"
            };
        }
        static readonly int[,] ResVals = new int[,] { { 0, 0 }, { 3840, 2160 }, { 2560, 1440 }, { 1920, 1080 }, { 1280, 720 }, { 854, 480 } };

        public ConvertPage(IJobHost host) : base(host)
        {
            Ui.Lbl(this, L.T("Files to convert (you can pick several - all are processed with the same preset):"), 0, 0, 700);

            _lv = new ListView();
            _lv.SetBounds(0, 20, 790, 130);
            _lv.View = View.Details;
            _lv.FullRowSelect = true;
            _lv.GridLines = true;
            _lv.ShowItemToolTips = true;
            _lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lv.Columns.Add(L.T("File"), 300);
            _lv.Columns.Add(L.T("Length"), 70);
            _lv.Columns.Add(L.T("Resolution"), 90);
            _lv.Columns.Add(L.T("Video"), 70);
            _lv.Columns.Add(L.T("Audio"), 130);
            _lv.Columns.Add(L.T("Size"), 80);
            Controls.Add(_lv);
            Ui.EnableDrop(_lv, AddFiles);
            Ui.EnableDrop(this, AddFiles);

            Button bAdd = Ui.Btn(this, L.T("Add Files..."), 800, 20, 160, 30, delegate
            {
                string[] f = Ui.OpenFiles(FindForm(), L.T("Select the files to convert"), true);
                if (f != null) AddFiles(f);
            });
            Button bDel = Ui.Btn(this, L.T("Remove"), 800, 56, 160, 26, delegate
            {
                List<int> idx = new List<int>();
                foreach (int i in _lv.SelectedIndices) idx.Add(i);
                idx.Sort(); idx.Reverse();
                foreach (int i in idx) _items.RemoveAt(i);
                RefreshList();
            });
            Button bClear = Ui.Btn(this, L.T("Clear List"), 800, 86, 160, 26, delegate
            {
                _items.Clear(); RefreshList();
            });
            foreach (Button b in new Button[] { bAdd, bDel, bClear }) Ui.AnchorRightTop(b);

            // ---- preset
            GroupBox g = Ui.Grp(this, L.T("Target format"), 0, 158, 960, 150);
            g.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Ui.Lbl(g, L.T("Preset:"), 14, 27, 60);
            _preset = Ui.Cmb(g, 78, 24, 520, null, 0);
            foreach (Preset p in _presets) _preset.Items.Add(p);
            _preset.SelectedIndex = IndexOfId(Settings.Get("Preset", "mp4-high"));
            _preset.SelectedIndexChanged += delegate { SyncPreset(); };

            _hw = Ui.Chk(g, L.T("Hardware acceleration"), 614, 24, 170, false);
            _keepAudio = Ui.Chk(g, L.T("Copy audio when possible"), 790, 24, 160, true);

            _notes = Ui.Lbl(g, "", 78, 52, 870, 32);
            _notes.ForeColor = Color.FromArgb(80, 80, 80);

            Ui.Lbl(g, L.T("Resolution:"), 14, 94, 75);
            _res = Ui.Cmb(g, 92, 91, 180, ResNames(), 0);

            _lblTarget = Ui.Lbl(g, L.T("Target size (MB):"), 290, 94, 110);
            _targetMB = Ui.Txt(g, "700", 404, 91, 80);
            _lblTarget.Visible = false;
            _targetMB.Visible = false;

            _outInfo = Ui.Lbl(g, "", 500, 94, 450, 20);

            // ---- output
            GroupBox go = Ui.Grp(this, L.T("Output"), 0, 314, 960, 92);
            go.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _sameDir = Ui.Chk(go, L.T("Save next to the source file"), 14, 22, 230, true);
            Ui.Lbl(go, L.T("Folder:"), 250, 25, 50);
            _outDir = Ui.Txt(go, Settings.Get("OutputFolder", ""), 302, 22, 476);
            _outDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _outDir.Enabled = false;
            Button bDir = Ui.Btn(go, L.T("Change..."), 786, 21, 160, 26, delegate
            {
                string d = Ui.PickFolder(FindForm(), L.T("Output folder"), _outDir.Text);
                if (d != null) { _outDir.Text = d; Settings.Set("OutputFolder", d); }
            });
            Ui.AnchorRightTop(bDir);
            bDir.Enabled = false;
            _sameDir.CheckedChanged += delegate
            {
                _outDir.Enabled = !_sameDir.Checked;
                bDir.Enabled = !_sameDir.Checked;
            };
            Ui.Lbl(go, L.T("Add to file name:"), 14, 60, 120);
            _suffix = Ui.Txt(go, L.T("_converted"), 136, 57, 200);
            Ui.Lbl(go, L.T("example:  holiday_converted.mp4"), 348, 60, 400);

            _run = Ui.Btn(this, L.T("CONVERT"), 0, 414, 200, 40, delegate { Run(); });
            _run.Font = new Font(Font, FontStyle.Bold);
            _run.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _run.Enabled = false;

            SyncPreset();
        }

        int IndexOfId(string id)
        {
            for (int i = 0; i < _presets.Count; i++) if (_presets[i].Id == id) return i;
            return 0;
        }

        Preset Current { get { return _preset.SelectedItem as Preset; } }

        void SyncPreset()
        {
            Preset p = Current;
            if (p == null) return;
            Settings.Set("Preset", p.Id);
            _notes.Text = p.Notes;
            _res.Enabled = !p.FixedResolution && !p.AudioOnly;
            _hw.Enabled = !p.AudioOnly && FF.HwEncoder(false) != null && p.Id != "audio-copy" && p.Id != "tv-audio";
            if (!_hw.Enabled) _hw.Checked = false;
            _keepAudio.Enabled = !p.AudioOnly && p.Id.StartsWith("mp4");
            _lblTarget.Visible = p.NeedsTargetSize;
            _targetMB.Visible = p.NeedsTargetSize;
            _outInfo.Text = L.T("Output extension: ") + p.Ext;
            if (p.FixedResolution && !p.AudioOnly) _outInfo.Text += L.T("   (resolution is fixed by the preset)");
        }

        void AddFiles(string[] paths)
        {
            if (!FF.Ready && !Host.EnsureFFmpeg()) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (string p in paths)
                {
                    if (!File.Exists(p)) continue;
                    string full = Path.GetFullPath(p);
                    bool dup = false;
                    foreach (MediaInfo m in _items)
                        if (string.Equals(m.Path, full, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    if (dup) continue;
                    MediaInfo info = MediaInfo.Probe(full);
                    if (!info.Ok) { Host.Log(L.T("Could not read, skipped: ") + info.Name); continue; }
                    _items.Add(info);
                }
            }
            finally { Cursor = Cursors.Default; }
            RefreshList();
        }

        void RefreshList()
        {
            _lv.BeginUpdate();
            _lv.Items.Clear();
            foreach (MediaInfo m in _items)
            {
                ListViewItem it = new ListViewItem(m.Name);
                it.SubItems.Add(Util.Dur(m.Duration));
                it.SubItems.Add(m.Resolution);
                it.SubItems.Add(m.VCodec == null ? "-" : m.VCodec);
                it.SubItems.Add(m.AudioText);
                it.SubItems.Add(Util.Size(m.Size));
                it.ToolTipText = m.Path;
                _lv.Items.Add(it);
            }
            _lv.EndUpdate();
            _run.Enabled = _items.Count > 0;
            if (_items.Count > 0 && _outDir.Text.Length == 0)
                _outDir.Text = Path.GetDirectoryName(_items[0].Path);
        }

        public override void OnFFmpegChanged()
        {
            SyncPreset();
            if (!FF.Ready || _items.Count == 0) return;
            List<string> paths = new List<string>();
            foreach (MediaInfo m in _items) paths.Add(m.Path);
            _items.Clear();
            AddFiles(paths.ToArray());
        }

        void Run()
        {
            if (!Ready()) return;
            Preset p = Current;
            if (p == null || _items.Count == 0) { MessageBox.Show(L.T("Select files and a preset."), App.Name); return; }

            ConvertOptions o = new ConvertOptions();
            o.Hw = _hw.Checked && _hw.Enabled;
            o.KeepAudioCopy = _keepAudio.Checked && _keepAudio.Enabled;
            int ri = _res.SelectedIndex;
            if (_res.Enabled && ri > 0) { o.ScaleW = ResVals[ri, 0]; o.ScaleH = ResVals[ri, 1]; }
            if (p.NeedsTargetSize)
            {
                double mb;
                if (!double.TryParse(_targetMB.Text.Trim().Replace(',', '.'), NumberStyles.Any, Util.Inv, out mb) || mb <= 0)
                { MessageBox.Show(L.T("Enter the target size in MB."), App.Name); return; }
                o.TargetMB = mb;
            }

            string suffix = _suffix.Text;
            bool sameDir = _sameDir.Checked;
            string outDir = _outDir.Text.Trim();
            if (!sameDir && outDir.Length == 0) { MessageBox.Show(L.T("Choose an output folder."), App.Name); return; }

            List<MediaInfo> items = new List<MediaInfo>(_items);
            List<string> outs = new List<string>();
            foreach (MediaInfo m in items)
            {
                string dir = sameDir ? Path.GetDirectoryName(m.Path) : outDir;
                string name = Path.GetFileNameWithoutExtension(m.Path) + suffix + p.Ext;
                string full = Path.Combine(dir, Util.SafeName(name));
                if (string.Equals(full, m.Path, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(L.T("The output would overwrite the source: {0}\r\nAdd a suffix to the file name.", m.Name),
                        App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                outs.Add(full);
            }
            int exists = 0;
            foreach (string f in outs) if (File.Exists(f)) exists++;
            if (exists > 0 && MessageBox.Show(L.T("{0} output files already exist. Overwrite?", exists),
                L.T("Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            Settings.Save();

            Host.StartJob(L.T("Converting"), delegate(JobContext ctx)
            {
                double span = 1.0 / items.Count;
                long totalOut = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (ctx.Cancelled) throw new OperationCanceledException();
                    string prefix = items.Count > 1 ? "(" + (i + 1) + "/" + items.Count + ") " : "";
                    ctx.Log(prefix + items[i].Name + "  ->  " + Path.GetFileName(outs[i]));
                    SubSink sub = new SubSink(ctx, i * span, span, prefix);
                    Ops.Convert(items[i], p, o, outs[i], sub);
                    try { totalOut += new FileInfo(outs[i]).Length; }
                    catch { }
                }
                ctx.ResultPath = outs[0];
                ctx.ResultMessage = L.T("{0} files converted ({1}), total {2}.", items.Count, p.Name, Util.Size(totalOut));
            });
        }
    }
}
