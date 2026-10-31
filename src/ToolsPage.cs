// Cyfan - Tools tab: trim, rotate, remove audio, grab a frame, show file info.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Cyfan
{
    class ToolsPage : PageBase
    {
        MediaInfo _src;
        TextBox _srcBox, _trimStart, _trimEnd, _frameAt;
        Label _info;
        CheckBox _trimLossless;
        ComboBox _rotate;

        public ToolsPage(IJobHost host) : base(host)
        {
            Ui.Lbl(this, L.T("File:"), 0, 4, 60);
            _srcBox = Ui.Txt(this, "", 62, 1, 728);
            _srcBox.ReadOnly = true;
            _srcBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button bSrc = Ui.Btn(this, L.T("Choose File..."), 800, 0, 160, 26, delegate
            {
                string[] f = Ui.OpenFiles(FindForm(), L.T("Select a file"), false);
                if (f != null && f.Length > 0) SetSource(f[0]);
            });
            Ui.AnchorRightTop(bSrc);
            Ui.EnableDrop(this, delegate(string[] files) { SetSource(files[0]); });
            Ui.EnableDrop(_srcBox, delegate(string[] files) { SetSource(files[0]); });

            _info = Ui.Lbl(this, L.T("Pick the file to work on, or drag it here."), 0, 32, 960, 34);
            _info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ---- trim
            GroupBox gt = Ui.Grp(this, L.T("Trim - take a section of the video"), 0, 70, 960, 90);
            gt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(gt, L.T("Start:"), 14, 27, 60);
            _trimStart = Ui.Txt(gt, "00:00:00", 80, 24, 90);
            Ui.Lbl(gt, L.T("End:"), 188, 27, 40);
            _trimEnd = Ui.Txt(gt, "", 230, 24, 90);
            Ui.Lbl(gt, L.T("(empty = end of file)"), 328, 27, 130);
            _trimLossless = Ui.Chk(gt, L.T("Lossless (cuts at the nearest keyframe, very fast)"), 14, 54, 400, true);
            Button bTrim = Ui.Btn(gt, L.T("Trim and Save..."), 786, 23, 160, 28, delegate { DoTrim(); });
            Ui.AnchorRightTop(bTrim);

            // ---- rotate
            GroupBox gr = Ui.Grp(this, L.T("Rotate"), 0, 168, 960, 64);
            gr.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(gr, L.T("Angle:"), 14, 27, 44);
            _rotate = Ui.Cmb(gr, 62, 24, 220, new object[] {
                L.T("90 degrees clockwise"), L.T("180 degrees"), L.T("90 degrees counter-clockwise") }, 0);
            Ui.Lbl(gr, L.T("Rotating re-encodes the video (it cannot be lossless)."), 296, 27, 400);
            Button bRot = Ui.Btn(gr, L.T("Rotate and Save..."), 786, 23, 160, 28, delegate { DoRotate(); });
            Ui.AnchorRightTop(bRot);

            // ---- audio
            GroupBox ga = Ui.Grp(this, L.T("Audio"), 0, 240, 960, 64);
            ga.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(ga, L.T("Removes the audio completely and leaves the picture untouched (lossless). To save the audio as a separate file, use the \"Audio\" presets on the Convert tab."),
                   14, 26, 760, 30);
            Button bMute = Ui.Btn(ga, L.T("Remove Audio..."), 786, 23, 160, 28, delegate { DoRemoveAudio(); });
            Ui.AnchorRightTop(bMute);

            // ---- frame
            GroupBox gf = Ui.Grp(this, L.T("Grab frame (PNG)"), 0, 312, 960, 64);
            gf.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(gf, L.T("Time:"), 14, 27, 50);
            _frameAt = Ui.Txt(gf, "00:00:01", 66, 24, 90);
            Ui.Lbl(gf, L.T("Saves the frame at that moment as an image."), 170, 27, 400);
            Button bFrame = Ui.Btn(gf, L.T("Save Frame..."), 786, 23, 160, 28, delegate { DoFrame(); });
            Ui.AnchorRightTop(bFrame);

            // ---- info
            GroupBox gi = Ui.Grp(this, L.T("File info"), 0, 384, 960, 64);
            gi.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Ui.Lbl(gi, L.T("Codecs, bit rate, streams - everything ffprobe reports."), 14, 27, 500);
            Button bInfo = Ui.Btn(gi, L.T("Show Info"), 786, 23, 160, 28, delegate
            {
                if (_src == null) { Need(); return; }
                Cursor = Cursors.WaitCursor;
                string text;
                try { text = Ops.InfoDump(_src); }
                finally { Cursor = Cursors.Default; }
                Ui.ShowText(FindForm(), L.T("{0} - technical details", _src.Name), text);
            });
            Ui.AnchorRightTop(bInfo);
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
            _info.Text = L.T("Length: {0}    Size: {1}    Bit rate: {2}    {3}  {4}    Audio: {5}",
                Util.Dur(m.Duration), Util.Size(m.Size), Util.Rate(m.EffectiveBitRate()),
                m.Resolution, (m.VCodec == null ? "" : m.VCodec) + (m.Fps > 0 ? " " + Util.Num(m.Fps) + "fps" : ""),
                m.AudioText);
            if (_trimEnd.Text.Length == 0) _trimEnd.Text = Util.Dur(m.Duration);
        }

        public override void OnFFmpegChanged()
        {
            if (FF.Ready && _src != null) SetSource(_src.Path);
        }

        void Need()
        {
            MessageBox.Show(L.T("Select a file first."), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        string AskOut(string suggestSuffix, string ext, string filter)
        {
            string dir = Path.GetDirectoryName(_src.Path);
            string name = Path.GetFileNameWithoutExtension(_src.Path) + suggestSuffix + ext;
            return Ui.SaveFile(FindForm(), L.T("Save"), filter, Path.Combine(dir, name));
        }

        void DoTrim()
        {
            if (_src == null) { Need(); return; }
            if (!Ready()) return;
            double start = Util.ParseTime(_trimStart.Text);
            if (start < 0) { MessageBox.Show(L.T("Start time is not valid."), App.Name); return; }
            double end = _trimEnd.Text.Trim().Length == 0 ? _src.Duration : Util.ParseTime(_trimEnd.Text);
            if (end < 0) { MessageBox.Show(L.T("End time is not valid."), App.Name); return; }
            if (end > _src.Duration) end = _src.Duration;
            if (end - start <= 0.05) { MessageBox.Show(L.T("End must be after start."), App.Name); return; }

            bool lossless = _trimLossless.Checked;
            string ext = lossless ? Path.GetExtension(_src.Path) : ".mp4";
            string outPath = AskOut(L.T("_trimmed"), ext, L.T("Video files") + "|*" + ext + "|" + L.T("All files") + "|*.*");
            if (outPath == null) return;

            MediaInfo src = _src;
            Host.StartJob(L.T("Trimming"), delegate(JobContext ctx)
            {
                Ops.Trim(src, start, end, lossless, 18, outPath, ctx);
                ctx.ResultPath = outPath;
                ctx.ResultMessage = L.T("Trimmed: {0}  ({1})", Path.GetFileName(outPath), Util.Dur(end - start));
            });
        }

        void DoRotate()
        {
            if (_src == null) { Need(); return; }
            if (!Ready()) return;
            int deg = _rotate.SelectedIndex == 0 ? 90 : (_rotate.SelectedIndex == 1 ? 180 : 270);
            string outPath = AskOut(L.T("_rotated"), ".mp4", "MP4|*.mp4|Matroska|*.mkv");
            if (outPath == null) return;
            MediaInfo src = _src;
            Host.StartJob(L.T("Rotating"), delegate(JobContext ctx)
            {
                Ops.Rotate(src, deg, 18, outPath, ctx);
                ctx.ResultPath = outPath;
                ctx.ResultMessage = L.T("Rotated by {0} degrees: {1}", deg, Path.GetFileName(outPath));
            });
        }

        void DoRemoveAudio()
        {
            if (_src == null) { Need(); return; }
            if (!Ready()) return;
            if (!_src.HasAudio)
            {
                MessageBox.Show(L.T("This file has no audio."), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string ext = Path.GetExtension(_src.Path);
            string outPath = AskOut(L.T("_muted"), ext, L.T("Video files") + "|*" + ext + "|" + L.T("All files") + "|*.*");
            if (outPath == null) return;
            MediaInfo src = _src;
            Host.StartJob(L.T("Removing audio"), delegate(JobContext ctx)
            {
                Ops.RemoveAudio(src, outPath, ctx);
                ctx.ResultPath = outPath;
                ctx.ResultMessage = L.T("Audio removed: ") + Path.GetFileName(outPath);
            });
        }

        void DoFrame()
        {
            if (_src == null) { Need(); return; }
            if (!Ready()) return;
            double at = Util.ParseTime(_frameAt.Text);
            if (at < 0 || at > _src.Duration) { MessageBox.Show(L.T("The time is not valid."), App.Name); return; }
            string outPath = AskOut(L.T("_frame"), ".png", L.T("PNG image") + "|*.png|" + L.T("JPEG image") + "|*.jpg");
            if (outPath == null) return;
            MediaInfo src = _src;
            Host.StartJob(L.T("Grabbing frame"), delegate(JobContext ctx)
            {
                Ops.GrabFrame(src, at, outPath, ctx);
                ctx.ResultPath = outPath;
                ctx.ResultMessage = L.T("Frame saved: ") + Path.GetFileName(outPath);
            });
        }
    }
}
