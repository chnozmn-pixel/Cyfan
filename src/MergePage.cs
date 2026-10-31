// Cyfan - Merge tab.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Cyfan
{
    class MergePage : PageBase
    {
        readonly List<MediaInfo> _items = new List<MediaInfo>();
        ListView _lv;
        Label _warn;
        TextBox _out;
        RadioButton _rbCopy, _rbEnc;
        ComboBox _crf;
        CheckBox _hw, _chkSplit, _tvAudio;
        TextBox _splitDur;
        Label _splitInfo;
        bool _tvAudioUserSet;
        Button _run, _bOut, _bAdd, _bDel, _bUp, _bDown, _bSort, _bClear;

        public MergePage(IJobHost host) : base(host)
        {
            Ui.Lbl(this, L.T("Files to merge - order is top to bottom. You can drag files here."), 0, 0, 700);

            _lv = new ListView();
            _lv.SetBounds(0, 20, 790, 200);
            _lv.View = View.Details;
            _lv.FullRowSelect = true;
            _lv.GridLines = true;
            _lv.HideSelection = false;
            _lv.ShowItemToolTips = true;
            _lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lv.Columns.Add(L.T("File"), 290);
            _lv.Columns.Add(L.T("Length"), 70);
            _lv.Columns.Add(L.T("Resolution"), 90);
            _lv.Columns.Add(L.T("FPS"), 50);
            _lv.Columns.Add(L.T("Video"), 70);
            _lv.Columns.Add(L.T("Audio"), 130);
            _lv.Columns.Add(L.T("Size"), 80);
            Controls.Add(_lv);
            Ui.EnableDrop(_lv, AddFiles);
            Ui.EnableDrop(this, AddFiles);

            int bx = 800, by = 20;
            _bAdd = Ui.Btn(this, L.T("Add Files..."), bx, by, 160, 30, delegate
            {
                string[] f = Ui.OpenFiles(FindForm(), L.T("Files to merge - order is top to bottom. You can drag files here."), true);
                if (f != null) AddFiles(f);
            });
            _bDel = Ui.Btn(this, L.T("Remove"), bx, by + 36, 160, 26, delegate
            {
                List<int> idx = new List<int>();
                foreach (int i in _lv.SelectedIndices) idx.Add(i);
                idx.Sort(); idx.Reverse();
                foreach (int i in idx) _items.RemoveAt(i);
                Refresh2();
            });
            _bUp = Ui.Btn(this, L.T("Up"), bx, by + 66, 160, 26, delegate { MoveItem(-1); });
            _bDown = Ui.Btn(this, L.T("Down"), bx, by + 94, 160, 26, delegate { MoveItem(1); });
            _bSort = Ui.Btn(this, L.T("Sort by Name"), bx, by + 124, 160, 26, delegate
            {
                _items.Sort(delegate(MediaInfo a, MediaInfo b)
                { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
                Refresh2();
            });
            _bClear = Ui.Btn(this, L.T("Clear List"), bx, by + 152, 160, 26, delegate
            {
                _items.Clear(); _out.Text = ""; Refresh2();
            });
            foreach (Button b in new Button[] { _bAdd, _bDel, _bUp, _bDown, _bSort, _bClear }) Ui.AnchorRightTop(b);

            _warn = Ui.Lbl(this, "", 0, 228, 960, 34);
            _warn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Label lo = Ui.Lbl(this, L.T("Output file:"), 0, 270, 90);
            lo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _out = Ui.Txt(this, "", 92, 267, 698);
            _out.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _out.TextChanged += delegate { if (_splitInfo != null) UpdateSplitInfo(); };
            _bOut = Ui.Btn(this, L.T("Save As..."), 800, 266, 160, 26, delegate
            {
                string init = _out.Text;
                if (string.IsNullOrEmpty(init) && _items.Count > 0) init = DefaultOutput();
                string p = Ui.SaveFile(FindForm(), L.T("Save the merged file"),
                    "MP4|*.mp4|" + L.T("Matroska - safe for every codec") + "|*.mkv|MPEG-TS|*.ts|QuickTime|*.mov", init);
                if (p != null) _out.Text = p;
            });
            _bOut.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            GroupBox g = Ui.Grp(this, L.T("Merge method"), 0, 300, 960, 112);
            g.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _rbCopy = Ui.Rad(g, L.T("Lossless (stream copy) - takes seconds, no quality loss. For clips recorded with the same settings."), 14, 22, 640, true);

            // XAVC S carries PCM audio, which most TVs cannot play. This converts
            // only the audio and leaves the picture untouched.
            _tvAudio = Ui.Chk(g, L.T("Make audio TV-friendly (AAC) - video untouched"), 660, 22, 290, false);
            _tvAudio.Click += delegate { _tvAudioUserSet = true; };

            _rbEnc = Ui.Rad(g, L.T("Re-encode - evens out different resolution / fps / audio settings. Slow."), 14, 48, 500, false);
            Ui.Lbl(g, L.T("Quality:"), 540, 51, 46);
            _crf = Ui.Cmb(g, 588, 47, 180, new object[] {
                L.T("16 - near lossless"), L.T("18 - high (recommended)"), L.T("20 - good"),
                L.T("23 - normal"), L.T("26 - small file")
            }, 1);
            _crf.Enabled = false;
            _hw = Ui.Chk(g, L.T("Hardware acceleration"), 782, 47, 170, false);
            _hw.Enabled = false;
            _rbEnc.CheckedChanged += delegate
            {
                _crf.Enabled = _rbEnc.Checked;
                _hw.Enabled = _rbEnc.Checked && FF.HwEncoder(false) != null;
                if (!_hw.Enabled) _hw.Checked = false;
                // Re-encoding already produces AAC audio
                _tvAudio.Enabled = !_rbEnc.Checked;
            };

            // Splitting in the same pass: for long recordings this avoids writing a
            // huge intermediate file first.
            _chkSplit = Ui.Chk(g, L.T("Split while merging - each part:"), 14, 76, 260, false);
            _splitDur = Ui.Txt(g, "00:30:00", 282, 76, 90);
            _splitDur.Enabled = false;
            _splitInfo = Ui.Lbl(g, "", 382, 79, 560);
            _splitInfo.ForeColor = Color.FromArgb(80, 80, 80);
            _chkSplit.CheckedChanged += delegate
            {
                _splitDur.Enabled = _chkSplit.Checked;
                UpdateSplitInfo();
            };
            _splitDur.TextChanged += delegate { UpdateSplitInfo(); };
            UpdateSplitInfo();

            _run = Ui.Btn(this, L.T("MERGE"), 0, 424, 200, 40, delegate { Run(); });
            _run.Font = new Font(Font, FontStyle.Bold);
            _run.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            Refresh2();
        }

        public override void OnFFmpegChanged()
        {
            // If ffmpeg was installed later, re-read the files that are already listed
            if (!FF.Ready || _items.Count == 0) return;
            List<string> paths = new List<string>();
            foreach (MediaInfo m in _items) paths.Add(m.Path);
            _items.Clear();
            AddFiles(paths.ToArray());
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
                    if (!info.Ok)
                    {
                        Host.Log(L.T("Could not read, skipped: ") + info.Name +
                                 (info.Error == null ? "" : " (" + info.Error + ")"));
                        continue;
                    }
                    _items.Add(info);
                }
            }
            finally { Cursor = Cursors.Default; }
            Refresh2();
        }

        void MoveItem(int delta)
        {
            if (_lv.SelectedIndices.Count != 1) return;
            int i = _lv.SelectedIndices[0];
            int j = i + delta;
            if (j < 0 || j >= _items.Count) return;
            MediaInfo t = _items[i]; _items[i] = _items[j]; _items[j] = t;
            Refresh2();
            _lv.Items[j].Selected = true;
            _lv.Items[j].Focused = true;
            _lv.Focus();
        }

        void Refresh2()
        {
            _lv.BeginUpdate();
            _lv.Items.Clear();
            foreach (MediaInfo m in _items)
            {
                ListViewItem it = new ListViewItem(m.Name);
                it.SubItems.Add(Util.Dur(m.Duration));
                it.SubItems.Add(m.Resolution);
                it.SubItems.Add(m.Fps > 0 ? Util.Num(m.Fps) : "-");
                it.SubItems.Add(m.VCodec == null ? "-" : m.VCodec);
                it.SubItems.Add(m.AudioText);
                it.SubItems.Add(Util.Size(m.Size));
                it.ToolTipText = m.Path;
                _lv.Items.Add(it);
            }
            _lv.EndUpdate();
            UpdateWarning();
            if (_out.Text.Length == 0 && _items.Count > 0) _out.Text = DefaultOutput();
            if (_splitInfo != null) UpdateSplitInfo();
            _run.Enabled = _items.Count >= 2;
        }

        string DefaultOutput()
        {
            MediaInfo f = _items[0];
            string dir = Path.GetDirectoryName(f.Path);
            string ext = ".mkv";
            bool allMp4 = true;
            foreach (MediaInfo m in _items)
                if (Path.GetExtension(m.Path).ToLowerInvariant() != ".mp4") { allMp4 = false; break; }
            if (allMp4) ext = ".mp4";
            return Path.Combine(dir, L.T("merged") + ext);
        }

        void UpdateWarning()
        {
            if (_items.Count < 2)
            {
                _warn.ForeColor = SystemColors.ControlText;
                _warn.Text = _items.Count == 0 ? "" : L.T("Add at least two files to merge.");
                return;
            }
            List<string> problems = new List<string>();
            string key = _items[0].CompatKey();
            bool videoDiff = false, audioDiff = false, noAudio = false, someAudio = false;
            foreach (MediaInfo m in _items)
            {
                if (m.CompatKey() != key)
                {
                    if (m.VCodec != _items[0].VCodec || m.Width != _items[0].Width ||
                        m.Height != _items[0].Height || m.Fps != _items[0].Fps || m.PixFmt != _items[0].PixFmt)
                        videoDiff = true;
                    if (m.ACodec != _items[0].ACodec || m.SampleRate != _items[0].SampleRate ||
                        m.Channels != _items[0].Channels)
                        audioDiff = true;
                }
                if (m.HasAudio) someAudio = true; else noAudio = true;
            }
            if (videoDiff) problems.Add(L.T("video settings differ"));
            if (audioDiff) problems.Add(L.T("audio settings differ"));
            if (noAudio && someAudio) problems.Add(L.T("some files have no audio"));

            double total = 0;
            long bytes = 0;
            bool layoutDiff = false;
            foreach (MediaInfo m in _items)
            {
                total += m.Duration;
                bytes += m.Size;
                if (m.Layout != _items[0].Layout) layoutDiff = true;
            }
            // An extra stream (Sony timecode and the like) is dropped automatically
            string note = layoutDiff
                ? L.T("   Note: some files carry an extra stream; it will be dropped automatically while merging.")
                : "";

            // TV audio problem: PCM/DTS and friends do not play on most televisions
            bool tvFix = false;
            foreach (MediaInfo m in _items) if (Ops.AudioNeedsTvFix(m)) tvFix = true;
            if (_tvAudio != null)
            {
                if (tvFix && !_tvAudioUserSet) _tvAudio.Checked = true;
                if (tvFix) note += L.T("   Audio is {0} - most TVs cannot play it, so \"Make audio TV-friendly\" was ticked.",
                                       _items[0].ACodec);
            }

            if (problems.Count > 0)
            {
                _warn.ForeColor = Ui.Warn;
                _warn.Text = L.T("Careful: ") + string.Join(", ", problems.ToArray()) +
                    L.T(".  A lossless merge may produce a broken result - use \"Re-encode\".") + note;
            }
            else
            {
                _warn.ForeColor = Ui.Good;
                _warn.Text = L.T("All files have the same format - lossless merge is safe.   Total: ") +
                    Util.Dur(total) + ",  " + Util.Size(bytes) + note;
            }
        }

        // "30" -> 30 minutes, "00:30:00" -> 30 minutes. -1 when invalid.
        double SegSeconds()
        {
            string s = _splitDur.Text.Trim();
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

        void UpdateSplitInfo()
        {
            if (!_chkSplit.Checked)
            {
                _splitInfo.ForeColor = Color.FromArgb(80, 80, 80);
                _splitInfo.Text = L.T("(off - saved as a single file)");
                return;
            }
            double seg = SegSeconds();
            if (seg <= 0)
            {
                _splitInfo.ForeColor = Ui.Bad;
                _splitInfo.Text = L.T("Enter the length as 00:30:00 or 30 (minutes).");
                return;
            }
            double total = 0;
            foreach (MediaInfo m in _items) total += m.Duration;
            _splitInfo.ForeColor = Color.FromArgb(80, 80, 80);
            string name = L.T("merged");
            try
            {
                if (_out.Text.Length > 0) name = Path.GetFileNameWithoutExtension(_out.Text);
            }
            catch { }
            string prefix = "";
            if (total > 0)
                prefix = L.T("Total {0} -> about {1} parts.  ", Util.Dur(total), (int)Math.Ceiling(total / seg));
            _splitInfo.Text = prefix + L.T("Files: ") + name + "_001, " + name + "_002 ...";
        }

        void Run()
        {
            if (!Ready()) return;
            if (_items.Count < 2) { MessageBox.Show(L.T("Select at least two files."), App.Name); return; }
            string outPath = _out.Text.Trim();
            if (outPath.Length == 0) { MessageBox.Show(L.T("Specify the output file."), App.Name); return; }
            foreach (MediaInfo m in _items)
                if (string.Equals(m.Path, outPath, StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show(L.T("The output file cannot be one of the inputs."), App.Name); return; }

            double seg = 0;
            if (_chkSplit.Checked)
            {
                seg = SegSeconds();
                if (seg <= 0) { MessageBox.Show(L.T("Part length is not valid."), App.Name); return; }
                double total = 0;
                foreach (MediaInfo m in _items) total += m.Duration;
                if (seg >= total)
                {
                    MessageBox.Show(L.T("Part length must be shorter than the total length ({0}).", Util.Dur(total)),
                        App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                List<string> old = Ops.FindParts(Path.GetDirectoryName(outPath),
                    Path.GetFileNameWithoutExtension(outPath), Path.GetExtension(outPath));
                if (old.Count > 0 &&
                    MessageBox.Show(L.T("{0} parts with the same name already exist. Overwrite?", old.Count),
                        L.T("Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            else if (File.Exists(outPath) &&
                MessageBox.Show(L.T("{0} already exists. Overwrite?", Path.GetFileName(outPath)), L.T("Confirm"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            List<MediaInfo> items = new List<MediaInfo>(_items);
            bool copy = _rbCopy.Checked;
            int crf = int.Parse(_crf.Text.Substring(0, 2));
            bool hw = _hw.Checked;
            double segSeconds = seg;
            bool tvAudio = _tvAudio.Checked && _tvAudio.Enabled;

            Host.StartJob(L.T("Merging"), delegate(JobContext ctx)
            {
                List<string> made = copy
                    ? Ops.MergeLossless(items, outPath, segSeconds, tvAudio, ctx)
                    : Ops.MergeReencode(items, outPath, crf, hw, segSeconds, ctx);

                long size = 0;
                foreach (string f in made)
                {
                    long s2 = 0;
                    try { s2 = new FileInfo(f).Length; }
                    catch { }
                    size += s2;
                    if (made.Count > 1) ctx.Log("   " + Path.GetFileName(f) + "   " + Util.Size(s2));
                }
                ctx.ResultPath = made.Count > 0 ? made[0] : outPath;
                ctx.ResultMessage = made.Count > 1
                    ? L.T("Merged and written as {0} parts (total {1}).", made.Count, Util.Size(size))
                    : L.T("Merged: ") + Path.GetFileName(outPath) + "  (" + Util.Size(size) + ")";
            });
        }
    }
}
