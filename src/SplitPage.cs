// Cyfan - Split tab: by length, by size, or into an equal number of parts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Cyfan
{
    class SplitPage : PageBase
    {
        MediaInfo _src;
        TextBox _srcBox, _dur, _size, _parts, _outDir, _baseName;
        Label _info, _estimate;
        RadioButton _rbDur, _rbSize, _rbParts;
        ComboBox _unit, _disc;
        CheckBox _exact;
        Button _run;

        static readonly long[] DiscBytes = new long[] {
            0, 25L*1024*1024, 64L*1024*1024, 700L*1024*1024,
            4000L*1024*1024, 4700000000L, 8500000000L,
            25000000000L, 50000000000L
        };

        static object[] DiscNames()
        {
            return new object[] {
                L.T("(choose)"), L.T("E-mail attachment  25 MB"), L.T("Message  64 MB"), L.T("CD  700 MB"),
                L.T("FAT32 / SD limit  4 GB"), L.T("DVD-5  4.7 GB"), L.T("DVD-9  8.5 GB"),
                L.T("Blu-ray  25 GB"), L.T("Blu-ray  50 GB")
            };
        }

        public SplitPage(IJobHost host) : base(host)
        {
            Ui.Lbl(this, L.T("File to split:"), 0, 4, 120);
            _srcBox = Ui.Txt(this, "", 122, 1, 668);
            _srcBox.ReadOnly = true;
            _srcBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button bSrc = Ui.Btn(this, L.T("Choose File..."), 800, 0, 160, 26, delegate
            {
                string[] f = Ui.OpenFiles(FindForm(), L.T("Select the file to split"), false);
                if (f != null && f.Length > 0) SetSource(f[0]);
            });
            Ui.AnchorRightTop(bSrc);
            Ui.EnableDrop(this, delegate(string[] files) { SetSource(files[0]); });
            Ui.EnableDrop(_srcBox, delegate(string[] files) { SetSource(files[0]); });

            _info = Ui.Lbl(this, L.T("You can drag a file here."), 0, 32, 960, 34);
            _info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ---- how to split
            GroupBox g = Ui.Grp(this, L.T("How should it be split?"), 0, 70, 960, 164);
            g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _rbDur = Ui.Rad(g, L.T("By length - each part:"), 14, 24, 180, true);
            _dur = Ui.Txt(g, "00:10:00", 200, 24, 100);
            Ui.Lbl(g, L.T("(hh:mm:ss or minutes as a number)"), 308, 27, 260);

            _rbSize = Ui.Rad(g, L.T("By size - each part at most:"), 14, 58, 180, false);
            _size = Ui.Txt(g, "4", 200, 58, 60);
            _unit = Ui.Cmb(g, 266, 58, 70, new object[] { "GB", "MB" }, 0);
            Ui.Lbl(g, L.T("preset limits:"), 348, 61, 80);
            _disc = Ui.Cmb(g, 430, 58, 220, DiscNames(), 0);
            Ui.Lbl(g, L.T("1 GB = 1024 MB"), 660, 61, 120);

            _rbParts = Ui.Rad(g, L.T("Equal number of parts:"), 14, 92, 180, false);
            _parts = Ui.Txt(g, "2", 200, 92, 60);
            Ui.Lbl(g, L.T("parts"), 266, 95, 60);

            _exact = Ui.Chk(g, L.T("Cut exactly at the given second (re-encodes, slow - default: cuts at the nearest keyframe)"),
                            14, 126, 700, false);

            _rbDur.CheckedChanged += delegate { SyncMode(); };
            _rbSize.CheckedChanged += delegate { SyncMode(); };
            _rbParts.CheckedChanged += delegate { SyncMode(); };
            _dur.TextChanged += delegate { UpdateEstimate(); };
            _size.TextChanged += delegate { UpdateEstimate(); };
            _parts.TextChanged += delegate { UpdateEstimate(); };
            _unit.SelectedIndexChanged += delegate { UpdateEstimate(); };
            _disc.SelectedIndexChanged += delegate
            {
                int i = _disc.SelectedIndex;
                if (i <= 0) return;
                _rbSize.Checked = true;
                long b = DiscBytes[i];
                if (b >= 1073741824L) { _unit.SelectedIndex = 0; _size.Text = Util.Num(Math.Round(b / 1073741824.0, 2)); }
                else { _unit.SelectedIndex = 1; _size.Text = Util.Num(Math.Round(b / 1048576.0, 0)); }
            };

            // ---- output
            GroupBox go = Ui.Grp(this, L.T("Output"), 0, 242, 960, 96);
            go.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(go, L.T("Folder:"), 14, 27, 60);
            _outDir = Ui.Txt(go, "", 78, 24, 700);
            _outDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button bDir = Ui.Btn(go, L.T("Change..."), 786, 23, 160, 26, delegate
            {
                string d = Ui.PickFolder(FindForm(), L.T("Folder for the parts"), _outDir.Text);
                if (d != null) _outDir.Text = d;
            });
            Ui.AnchorRightTop(bDir);
            Ui.Lbl(go, L.T("File name:"), 14, 60, 60);
            _baseName = Ui.Txt(go, "", 78, 57, 240);
            Ui.Lbl(go, L.T("parts are named <name>_001, <name>_002 ..."), 326, 60, 420);

            _estimate = Ui.Lbl(this, "", 0, 346, 960, 20);
            _estimate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _run = Ui.Btn(this, L.T("SPLIT"), 0, 372, 200, 40, delegate { Run(); });
            _run.Font = new Font(Font, FontStyle.Bold);
            _run.Enabled = false;

            SyncMode();
        }

        void SyncMode()
        {
            _dur.Enabled = _rbDur.Checked;
            _size.Enabled = _rbSize.Checked;
            _unit.Enabled = _rbSize.Checked;
            _parts.Enabled = _rbParts.Checked;
            // Splitting by size always copies losslessly
            _exact.Enabled = !_rbSize.Checked;
            if (_rbSize.Checked) _exact.Checked = false;
            UpdateEstimate();
        }

        void SetSource(string path)
        {
            if (!File.Exists(path)) return;
            if (!FF.Ready && !Host.EnsureFFmpeg()) return;
            Cursor = Cursors.WaitCursor;
            MediaInfo m;
            try { m = ProbeOrWarn(Path.GetFullPath(path)); }
            finally { Cursor = Cursors.Default; }
            if (m == null) return;

            _src = m;
            _srcBox.Text = m.Path;
            _info.ForeColor = SystemColors.ControlText;
            _info.Text = L.T("Length: {0}    Size: {1}    Bit rate: {2}    {3}  {4}    Audio: {5}",
                Util.Dur(m.Duration), Util.Size(m.Size), Util.Rate(m.EffectiveBitRate()),
                m.Resolution, m.VCodec == null ? "" : m.VCodec, m.AudioText);
            _outDir.Text = Path.GetDirectoryName(m.Path);
            _baseName.Text = Util.SafeName(Path.GetFileNameWithoutExtension(m.Path)) + L.T("_part");
            _run.Enabled = true;
            UpdateEstimate();
        }

        long SizeBytes()
        {
            double v;
            if (!double.TryParse(_size.Text.Trim().Replace(',', '.'), NumberStyles.Any, Util.Inv, out v) || v <= 0)
                return -1;
            return (long)(v * (_unit.SelectedIndex == 0 ? 1073741824.0 : 1048576.0));
        }

        double SegSeconds()
        {
            string s = _dur.Text.Trim();
            if (s.Length == 0) return -1;
            if (s.IndexOf(':') < 0)
            {
                double mins;
                if (double.TryParse(s.Replace(',', '.'), NumberStyles.Any, Util.Inv, out mins) && mins > 0)
                    return mins * 60.0;
                return -1;
            }
            double t = Util.ParseTime(s);
            return t > 0 ? t : -1;
        }

        int PartCount()
        {
            int n;
            if (int.TryParse(_parts.Text.Trim(), out n) && n >= 2) return n;
            return -1;
        }

        void UpdateEstimate()
        {
            if (_src == null) { _estimate.Text = ""; return; }
            _estimate.ForeColor = SystemColors.ControlText;
            try
            {
                if (_rbDur.Checked)
                {
                    double seg = SegSeconds();
                    if (seg <= 0) { Bad(L.T("Enter the length as 00:10:00 or 10 (minutes).")); return; }
                    if (seg >= _src.Duration) { Bad(L.T("Part length must be shorter than the file.")); return; }
                    int n = (int)Math.Ceiling(_src.Duration / seg);
                    _estimate.Text = L.T("Result: about {0} parts, each {1} and roughly {2}.",
                        n, Util.Dur(seg), Util.Size((long)(_src.Size / (double)n)));
                }
                else if (_rbSize.Checked)
                {
                    long max = SizeBytes();
                    if (max <= 0) { Bad(L.T("Enter the size as a number.")); return; }
                    if (max >= _src.Size) { Bad(L.T("Part size must be smaller than the file ({0}).", Util.Size(_src.Size))); return; }
                    int n = (int)Math.Ceiling(_src.Size / (double)max);
                    double segSec = _src.EffectiveBitRate() > 0 ? max * 8.0 / _src.EffectiveBitRate() : 0;
                    _estimate.Text = L.T("Result: about {0} parts, each at most {1}{2}.  No part will exceed the limit; parts may overlap by up to a few seconds at the start (keyframe alignment).",
                        n, Util.Size(max), segSec > 0 ? " (~" + Util.Dur(segSec) + ")" : "");
                }
                else
                {
                    int n = PartCount();
                    if (n < 2) { Bad(L.T("The number of parts must be at least 2.")); return; }
                    double seg = _src.Duration / n;
                    _estimate.Text = L.T("Result: {0} parts, each about {1} and {2}.",
                        n, Util.Dur(seg), Util.Size((long)(_src.Size / (double)n)));
                }
            }
            catch { _estimate.Text = ""; }
        }

        void Bad(string msg)
        {
            _estimate.ForeColor = Ui.Bad;
            _estimate.Text = msg;
        }

        public override void OnFFmpegChanged()
        {
            if (FF.Ready && _src != null) SetSource(_src.Path);
        }

        void Run()
        {
            if (!Ready()) return;
            if (_src == null) { MessageBox.Show(L.T("Select a file first."), App.Name); return; }

            string outDir = _outDir.Text.Trim();
            string baseName = Util.SafeName(_baseName.Text.Trim());
            if (outDir.Length == 0) { MessageBox.Show(L.T("Choose an output folder."), App.Name); return; }
            if (baseName.Length == 0) { MessageBox.Show(L.T("Enter the part file name."), App.Name); return; }

            bool exact = _exact.Checked && _exact.Enabled;
            string ext = exact ? ".mp4" : Path.GetExtension(_src.Path);
            if (string.IsNullOrEmpty(ext)) ext = ".mkv";

            List<string> existing = Ops.FindParts(outDir, baseName, ext);
            if (existing.Count > 0 &&
                MessageBox.Show(L.T("{0} part files with the same name already exist. Overwrite?", existing.Count),
                    L.T("Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            MediaInfo src = _src;
            int mode = _rbDur.Checked ? 0 : (_rbSize.Checked ? 1 : 2);
            double seg = 0;
            long maxBytes = 0;
            int parts = 0;

            if (mode == 0)
            {
                seg = SegSeconds();
                if (seg <= 0 || seg >= src.Duration) { MessageBox.Show(L.T("Part length is invalid."), App.Name); return; }
            }
            else if (mode == 1)
            {
                maxBytes = SizeBytes();
                if (maxBytes <= 0 || maxBytes >= src.Size) { MessageBox.Show(L.T("Part size is invalid."), App.Name); return; }
            }
            else
            {
                parts = PartCount();
                if (parts < 2) { MessageBox.Show(L.T("The number of parts must be at least 2."), App.Name); return; }
                seg = src.Duration / parts;
            }

            Host.StartJob(L.T("Splitting"), delegate(JobContext ctx)
            {
                List<string> made;
                if (mode == 1) made = Ops.SplitBySize(src, maxBytes, outDir, baseName, ext, ctx);
                else made = Ops.SplitByDuration(src, seg, outDir, baseName, ext, exact, 18, ctx);

                long total = 0;
                foreach (string f in made)
                {
                    long sz = 0;
                    try { sz = new FileInfo(f).Length; }
                    catch { }
                    total += sz;
                    ctx.Log("   " + Path.GetFileName(f) + "   " + Util.Size(sz));
                }
                ctx.ResultPath = made.Count > 0 ? made[0] : outDir;
                ctx.ResultMessage = L.T("{0} parts created (total {1}).", made.Count, Util.Size(total));
            });
        }
    }
}
