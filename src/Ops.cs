// Cyfan - operations: merging, splitting, converting, trimming and so on.
// Independent of the user interface; progress is reported through IJobSink so
// everything here can also run headless (--selftest, --merge).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cyfan
{
    interface IJobSink
    {
        void Log(string text);
        void Progress(double frac, string status);
        bool Cancelled { get; }
    }

    static class Ops
    {
        // --------------------------------------------------------------- helpers

        static List<string> Base()
        {
            List<string> a = new List<string>();
            a.Add("-hide_banner");
            a.Add("-loglevel"); a.Add("error");
            a.Add("-progress"); a.Add("pipe:1");
            a.Add("-nostats");
            a.Add("-y");
            return a;
        }

        // Stream selection for lossless copying.
        //
        // "-map 0" cannot be used: Sony XAVC S files (AX100 and friends) carry a
        // third data stream for timecode / metadata, and that stream cannot be
        // copied into MP4 or MKV - ffmpeg says "Cannot map stream - unsupported
        // type" or "Could not find tag for codec none" and writes nothing at all.
        // -ignore_unknown only silences one of those two errors. So streams are
        // selected explicitly by type; the trailing "?" keeps it from failing when
        // a file has no stream of that type. Multiple audio and subtitle tracks are
        // kept, data streams are dropped.
        static void AddMapAV(List<string> a, bool withAudio)
        {
            a.Add("-map"); a.Add("0:v?");
            if (withAudio) { a.Add("-map"); a.Add("0:a?"); }
            a.Add("-map"); a.Add("0:s?");
        }

        static void Step(string label, List<string> args, double totalSec, IJobSink sink, double baseFrac, double spanFrac)
        {
            if (sink.Cancelled) throw new OperationCanceledException();
            sink.Log(label);
            RunResult r = Runner.Run(FF.FFmpeg, args, totalSec,
                delegate(double frac, string status)
                {
                    sink.Progress(baseFrac + frac * spanFrac, status);
                },
                delegate { return sink.Cancelled; });

            if (r.Cancelled) throw new OperationCanceledException();
            if (!r.Ok)
            {
                string msg = L.T("ffmpeg failed (code {0}).", r.ExitCode);
                if (!string.IsNullOrEmpty(r.StdErr))
                {
                    sink.Log(L.T("ffmpeg output:"));
                    foreach (string line in r.StdErr.Split('\n'))
                        if (line.Trim().Length > 0) sink.Log("   " + line.Trim());
                    msg += "\r\n\r\n" + FirstLines(r.StdErr, 3) + Hint(r.StdErr);
                }
                throw new Exception(msg);
            }
        }

        static string FirstLines(string s, int n)
        {
            string[] lines = s.Split('\n');
            StringBuilder sb = new StringBuilder();
            int c = 0;
            foreach (string l in lines)
            {
                if (l.Trim().Length == 0) continue;
                sb.AppendLine(l.Trim());
                if (++c >= n) break;
            }
            return sb.ToString();
        }

        static string Hint(string err)
        {
            if (err == null) return "";
            if (err.IndexOf("Invalid data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("could not find codec", StringComparison.OrdinalIgnoreCase) >= 0)
                return L.T("\r\nHint: the files may differ from each other. Try re-encoding.");
            if (err.IndexOf("Could not write header", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("Invalid audio stream", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("muxer does not support", StringComparison.OrdinalIgnoreCase) >= 0)
                return L.T("\r\nHint: this codec does not fit the selected container. Try .mkv as the output.");
            if (err.IndexOf("No space left", StringComparison.OrdinalIgnoreCase) >= 0)
                return L.T("\r\nHint: no space left on the disk.");
            if (err.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0)
                return L.T("\r\nHint: no write permission in the output folder, or the file is open in another program.");
            return "";
        }

        public static string WriteConcatList(IList<MediaInfo> items)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "cyfan_concat_" + Guid.NewGuid().ToString("N") + ".txt");
            StringBuilder sb = new StringBuilder();
            foreach (MediaInfo m in items)
            {
                string p = m.Path.Replace('\\', '/').Replace("'", "'\\''");
                sb.Append("file '").Append(p).Append("'\n");
            }
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            return tmp;
        }

        static double TotalDuration(IList<MediaInfo> items)
        {
            double t = 0;
            foreach (MediaInfo m in items) t += m.Duration;
            return t;
        }

        public static void EnsureDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        static bool IsMp4Like(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".mov" || ext == ".m4v";
        }

        // "merged.mp4" -> "merged_%03d.mp4"
        public static string PatternFor(string outPath)
        {
            string dir = Path.GetDirectoryName(outPath);
            string name = Path.GetFileNameWithoutExtension(outPath);
            string ext = Path.GetExtension(outPath);
            if (string.IsNullOrEmpty(ext)) ext = ".mp4";
            return Path.Combine(dir, name + "_%03d" + ext);
        }

        static List<string> PartsOf(string outPath)
        {
            return FindParts(Path.GetDirectoryName(outPath),
                             Path.GetFileNameWithoutExtension(outPath),
                             Path.GetExtension(outPath));
        }

        public static List<string> FindParts(string outDir, string baseName, string ext)
        {
            List<string> l = new List<string>();
            try
            {
                string[] files = Directory.GetFiles(outDir, baseName + "_*" + ext);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                l.AddRange(files);
            }
            catch { }
            return l;
        }

        // Segment muxer options: merging and splitting fit into a single pass, so a
        // huge intermediate file never has to be written.
        //
        // "movflags=+faststart" matters for MP4 parts: without it the index (moov)
        // stays at the end of the file and many players cannot tell the total
        // length, so the time display keeps changing during playback. Single-file
        // output already got +faststart; parts must behave the same. For the
        // segment muxer the option is passed to the inner muxer.
        static void AddSegment(List<string> a, double segSeconds, string outPath)
        {
            a.Add("-f"); a.Add("segment");
            a.Add("-segment_time"); a.Add(Util.Num(segSeconds));
            a.Add("-reset_timestamps"); a.Add("1");
            a.Add("-segment_start_number"); a.Add("1");
            if (IsMp4Like(outPath))
            { a.Add("-segment_format_options"); a.Add("movflags=+faststart"); }
        }

        // ------------------------------------------------------- input validation

        // Parses the stream without decoding it: a damaged H.264 stream or a
        // half-finished copy shows up in a fraction of a second.
        // Returns null when healthy, otherwise the first error line.
        public static string CheckFile(MediaInfo m)
        {
            List<string> a = new List<string>();
            a.Add("-hide_banner");
            a.Add("-v"); a.Add("error");
            a.Add("-i"); a.Add(m.Path);
            a.Add("-c"); a.Add("copy");
            a.Add("-f"); a.Add("null");
            a.Add("-");
            RunResult r;
            try { r = Runner.Run(FF.FFmpeg, a, 0, null, null); }
            catch (Exception ex) { return ex.Message; }
            if (!string.IsNullOrEmpty(r.StdErr)) return FirstLines(r.StdErr, 1).Trim();
            if (r.ExitCode != 0) return "ffmpeg exit code " + r.ExitCode;
            return null;
        }

        // Scans every input before merging. A broken file makes concat stop halfway
        // and ffmpeg STILL EXITS WITH CODE 0, so the loss would go unnoticed.
        static void CheckInputs(IList<MediaInfo> items, IJobSink sink)
        {
            List<string> bad = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (sink.Cancelled) throw new OperationCanceledException();
                sink.Progress(0, L.T("Checking files: {0} / {1}", i + 1, items.Count));
                string err = CheckFile(items[i]);
                if (err != null)
                {
                    bad.Add(items[i].Name + "  ->  " + err);
                    sink.Log(L.T("BROKEN: ") + items[i].Name + "  (" + err + ")");
                }
            }
            if (bad.Count == 0) { sink.Log(L.T("All files are healthy.")); return; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("{0} files look broken; the merge would have stopped halfway:", bad.Count));
            sb.AppendLine();
            foreach (string b in bad) sb.AppendLine("   " + b);
            sb.AppendLine();
            sb.Append(L.T("Copy these files again from the source, or remove them from the list. Files left half-copied cause this error."));
            throw new Exception(sb.ToString());
        }

        // The concat demuxer wants every input to have the SAME stream layout. Some
        // Sony files carry a timecode (data) stream and some do not; with a mixed
        // list ffmpeg reports a demuxing error but RETURNS EXIT CODE 0 - it silently
        // truncates. So files with a different layout are reduced to v/a/s first
        // (lossless copy).
        static List<MediaInfo> NormalizeForConcat(IList<MediaInfo> items, List<string> temps, IJobSink sink)
        {
            bool same = true;
            for (int i = 1; i < items.Count; i++)
                if (items[i].Layout != items[0].Layout) { same = false; break; }

            List<MediaInfo> result = new List<MediaInfo>(items);
            if (same) return result;

            sink.Log(L.T("The files do not have the same stream layout; extra streams are being removed:"));
            foreach (MediaInfo m in items) sink.Log("   " + m.Name + "  ->  " + m.Layout);

            string tmpDir = Path.Combine(Path.GetTempPath(), "cyfan_norm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            temps.Add(tmpDir);

            for (int i = 0; i < result.Count; i++)
            {
                MediaInfo m = result[i];
                if (m.Layout != null && m.Layout.IndexOf("data", StringComparison.Ordinal) < 0 &&
                    m.Layout.IndexOf("attachment", StringComparison.Ordinal) < 0)
                    continue;   // already v/a/s only

                string tmp = Path.Combine(tmpDir, i.ToString("000", CultureInfo.InvariantCulture) +
                                                  Path.GetExtension(m.Path));
                List<string> a = Base();
                a.Add("-i"); a.Add(m.Path);
                a.Add("-c"); a.Add("copy");
                AddMapAV(a, true);
                if (IsMp4Like(tmp)) { a.Add("-movflags"); a.Add("+faststart"); }
                a.Add(tmp);
                Step(L.T("   preparing: ") + m.Name, a, m.Duration, sink, 0, 0);

                MediaInfo nm = MediaInfo.Probe(tmp);
                if (!nm.Ok) throw new Exception(L.T("Could not prepare the file: ") + m.Name);
                result[i] = nm;
            }

            for (int i = 1; i < result.Count; i++)
                if (result[i].Layout != result[0].Layout)
                    throw new Exception(
                        "The files have different stream layouts (" + result[0].Layout + " / " +
                        result[i].Layout + "): " + result[i].Name + ".\r\n\r\n" +
                        "A lossless merge could lose content here. Use \"Re-encode\".");
            return result;
        }

        // Safety net after merging: ffmpeg can return 0 even after a demuxing error,
        // so the output length is compared with what was expected.
        static void VerifyDuration(IList<string> made, double expected, IJobSink sink)
        {
            double got = 0;
            foreach (string f in made)
            {
                MediaInfo m = MediaInfo.Probe(f);
                if (m.Ok) got += m.Duration;
            }
            if (expected <= 0) return;
            if (got < expected - Math.Max(0.5, expected * 0.01))
                throw new Exception(L.T("Output is short: expected {0}, got {1}.\r\n\r\nOne of the files may be broken or incompatible. Try \"Re-encode\".",
                    Util.Dur(expected), Util.Dur(got)));
            sink.Log(L.T("Length verified: {0} (expected {1})", Util.Dur(got), Util.Dur(expected)));
        }

        static void CleanTemps(List<string> temps)
        {
            foreach (string t in temps)
            {
                try
                {
                    if (Directory.Exists(t)) Directory.Delete(t, true);
                    else if (File.Exists(t)) File.Delete(t);
                }
                catch { }
            }
        }

        // ----------------------------------------------------------------- merging

        public static void MergeLossless(IList<MediaInfo> items, string outPath, IJobSink sink)
        {
            MergeLossless(items, outPath, 0, false, sink);
        }

        public static List<string> MergeLossless(IList<MediaInfo> items, string outPath,
                                                 double segSeconds, IJobSink sink)
        {
            return MergeLossless(items, outPath, segSeconds, false, sink);
        }

        // TV audio: most televisions cannot play PCM (as used by XAVC S) inside MP4.
        // The video stream is copied untouched and only the audio is re-encoded, so
        // picture quality is bit-for-bit identical.
        static void AddTvAudio(List<string> a)
        {
            a.Add("-c:v"); a.Add("copy");
            a.Add("-c:s"); a.Add("copy");
            a.Add("-c:a"); a.Add("aac");
            a.Add("-b:a"); a.Add("320k");
            a.Add("-ar"); a.Add("48000");
            a.Add("-ac"); a.Add("2");
        }

        // Does this audio codec usually fail on televisions?
        public static bool AudioNeedsTvFix(MediaInfo m)
        {
            if (m == null || !m.HasAudio || m.ACodec == null) return false;
            string c = m.ACodec.ToLowerInvariant();
            return c.StartsWith("pcm") || c == "dts" || c == "truehd" || c == "flac" || c == "alac";
        }

        // segSeconds > 0 writes the result as parts of that length instead of one file.
        // tvAudio copies the video and converts the audio to AAC.
        public static List<string> MergeLossless(IList<MediaInfo> items, string outPath,
                                                 double segSeconds, bool tvAudio, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            double expected = TotalDuration(items);
            List<string> temps = new List<string>();
            string list = null;
            try
            {
                CheckInputs(items, sink);
                IList<MediaInfo> use = NormalizeForConcat(items, temps, sink);
                list = WriteConcatList(use);

                List<string> a = Base();
                a.Add("-f"); a.Add("concat");
                a.Add("-safe"); a.Add("0");
                a.Add("-i"); a.Add(list);
                if (tvAudio) AddTvAudio(a);
                else { a.Add("-c"); a.Add("copy"); }
                AddMapAV(a, true);

                string label = tvAudio
                    ? L.T("Merge ({0} files) - video lossless, audio converted to AAC", items.Count)
                    : L.T("Lossless merge ({0} files)", items.Count);

                if (segSeconds > 0)
                {
                    AddSegment(a, segSeconds, outPath);
                    a.Add(PatternFor(outPath));
                    label += L.T(" + writing {0} parts", Util.Dur(segSeconds));
                }
                else
                {
                    if (IsMp4Like(outPath)) { a.Add("-movflags"); a.Add("+faststart"); }
                    a.Add(outPath);
                }

                Step(label, a, expected, sink, 0, 1);

                List<string> made;
                if (segSeconds > 0) made = PartsOf(outPath);
                else { made = new List<string>(); made.Add(outPath); }
                VerifyDuration(made, expected, sink);
                return made;
            }
            finally
            {
                if (list != null) { try { File.Delete(list); } catch { } }
                CleanTemps(temps);
            }
        }

        public static void MergeReencode(IList<MediaInfo> items, string outPath, int crf, bool hw, IJobSink sink)
        {
            MergeReencode(items, outPath, crf, hw, 0, sink);
        }

        public static List<string> MergeReencode(IList<MediaInfo> items, string outPath, int crf, bool hw,
                                                 double segSeconds, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            MediaInfo first = items[0];
            int w = first.Width > 0 ? first.Width : 1920;
            int h = first.Height > 0 ? first.Height : 1080;
            double fps = first.Fps > 0 ? first.Fps : 25;

            List<string> a = Base();
            foreach (MediaInfo m in items) { a.Add("-i"); a.Add(m.Path); }

            // Silent source for files that have no audio
            List<string> audioRef = new List<string>();
            int extra = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].HasAudio) audioRef.Add(i.ToString(CultureInfo.InvariantCulture));
                else
                {
                    double dur = Math.Max(items[i].Duration, 0.1);
                    a.Add("-f"); a.Add("lavfi");
                    a.Add("-t"); a.Add(Util.Num(dur));
                    a.Add("-i"); a.Add("anullsrc=channel_layout=stereo:sample_rate=48000");
                    audioRef.Add(extra.ToString(CultureInfo.InvariantCulture));
                    extra++;
                }
            }

            StringBuilder fc = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                fc.Append("[").Append(i).Append(":v]scale=").Append(w).Append(":").Append(h)
                  .Append(":force_original_aspect_ratio=decrease,pad=").Append(w).Append(":").Append(h)
                  .Append(":(ow-iw)/2:(oh-ih)/2,setsar=1,fps=").Append(Util.Num(fps))
                  .Append("[v").Append(i).Append("];");
                fc.Append("[").Append(audioRef[i]).Append(":a]aresample=48000,")
                  .Append("aformat=sample_fmts=fltp:channel_layouts=stereo[a").Append(i).Append("];");
            }
            for (int i = 0; i < items.Count; i++) fc.Append("[v").Append(i).Append("][a").Append(i).Append("]");
            fc.Append("concat=n=").Append(items.Count).Append(":v=1:a=1[outv][outa]");

            a.Add("-filter_complex"); a.Add(fc.ToString());
            a.Add("-map"); a.Add("[outv]");
            a.Add("-map"); a.Add("[outa]");

            string enc = hw ? FF.HwEncoder(false) : null;
            if (enc != null)
            {
                a.Add("-c:v"); a.Add(enc);
                if (enc.EndsWith("_nvenc")) { a.Add("-preset"); a.Add("p5"); a.Add("-rc"); a.Add("vbr"); a.Add("-cq"); a.Add(crf.ToString()); a.Add("-b:v"); a.Add("0"); }
                else if (enc.EndsWith("_qsv")) { a.Add("-global_quality"); a.Add(crf.ToString()); }
                else { a.Add("-rc"); a.Add("cqp"); a.Add("-qp_i"); a.Add(crf.ToString()); a.Add("-qp_p"); a.Add(crf.ToString()); }
            }
            else
            {
                a.Add("-c:v"); a.Add("libx264");
                a.Add("-preset"); a.Add("medium");
                a.Add("-crf"); a.Add(crf.ToString());
            }
            a.Add("-pix_fmt"); a.Add("yuv420p");
            a.Add("-c:a"); a.Add("aac");
            a.Add("-b:a"); a.Add("256k");

            string label = L.T("Re-encoding merge ({0} files, {1})", items.Count, enc == null ? "libx264" : enc);
            if (segSeconds > 0)
            {
                // Force a keyframe at each boundary so the cuts land exactly
                a.Add("-force_key_frames"); a.Add("expr:gte(t,n_forced*" + Util.Num(segSeconds) + ")");
                AddSegment(a, segSeconds, outPath);
                a.Add(PatternFor(outPath));
                label += L.T(" + writing {0} parts", Util.Dur(segSeconds));
            }
            else
            {
                if (IsMp4Like(outPath)) { a.Add("-movflags"); a.Add("+faststart"); }
                a.Add(outPath);
            }

            double expected = TotalDuration(items);
            Step(label, a, expected, sink, 0, 1);

            List<string> made;
            if (segSeconds > 0) made = PartsOf(outPath);
            else { made = new List<string>(); made.Add(outPath); }
            VerifyDuration(made, expected, sink);
            return made;
        }

        // --------------------------------------------------------------- splitting

        // Fixed-length parts. exact=false keeps it lossless (cuts snap to keyframes).
        public static List<string> SplitByDuration(MediaInfo src, double segSeconds, string outDir,
                                                   string baseName, string ext, bool exact, int crf, IJobSink sink)
        {
            EnsureDir(outDir);
            string pattern = Path.Combine(outDir, baseName + "_%03d" + ext);

            List<string> a = Base();
            a.Add("-i"); a.Add(src.Path);
            if (exact)
            {
                a.Add("-c:v"); a.Add("libx264");
                a.Add("-preset"); a.Add("medium");
                a.Add("-crf"); a.Add(crf.ToString());
                a.Add("-pix_fmt"); a.Add("yuv420p");
                a.Add("-force_key_frames"); a.Add("expr:gte(t,n_forced*" + Util.Num(segSeconds) + ")");
                if (src.HasAudio) { a.Add("-c:a"); a.Add("aac"); a.Add("-b:a"); a.Add("192k"); }
                else a.Add("-an");
            }
            else
            {
                a.Add("-c"); a.Add("copy");
                AddMapAV(a, true);
            }
            AddSegment(a, segSeconds, pattern);
            a.Add(pattern);

            Step(exact ? L.T("Exact-second split (each part {0})", Util.Dur(segSeconds))
                       : L.T("Lossless split (each part {0})", Util.Dur(segSeconds)),
                 a, src.Duration, sink, 0, 1);

            return FindParts(outDir, baseName, ext);
        }

        // Size-limited parts.
        //
        // Note: ffmpeg's -fs option is not enough on its own - because of the write
        // buffer it can overshoot the limit by up to half a megabyte. So each part's
        // length is computed from the measured bit rate, cut with -t, and then the
        // file is actually measured; if the limit was exceeded the length is reduced
        // and the part written again.
        public static List<string> SplitBySize(MediaInfo src, long maxBytes, string outDir,
                                               string baseName, string ext, IJobSink sink)
        {
            EnsureDir(outDir);
            if (maxBytes < 131072) throw new Exception("The part size must be at least 128 KB.");

            List<string> made = new List<string>();
            double pos = 0;
            int n = 1;
            double bitrate = src.EffectiveBitRate();
            if (bitrate <= 0) throw new Exception("The bit rate of the file could not be read; try splitting by length.");

            while (pos < src.Duration - 0.2)
            {
                if (sink.Cancelled) throw new OperationCanceledException();
                string outPath = Path.Combine(outDir,
                    baseName + "_" + n.ToString("000", CultureInfo.InvariantCulture) + ext);

                double guess = maxBytes * 8.0 / bitrate * 0.97;
                if (guess < 0.3) throw new Exception("The part size is too small for this file's bit rate (" +
                    Util.Rate(bitrate) + "). Choose a larger size.");

                MediaInfo part = null;
                double accepted = 0;
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    double remaining = src.Duration - pos;
                    double take = Math.Min(guess, remaining);

                    List<string> a = Base();
                    a.Add("-ss"); a.Add(Util.Num(pos));
                    a.Add("-i"); a.Add(src.Path);
                    a.Add("-t"); a.Add(Util.Num(take));
                    a.Add("-c"); a.Add("copy");
                    AddMapAV(a, true);
                    a.Add("-avoid_negative_ts"); a.Add("make_zero");
                    a.Add("-fs"); a.Add(maxBytes.ToString(CultureInfo.InvariantCulture));
                    if (IsMp4Like(outPath)) { a.Add("-movflags"); a.Add("+faststart"); }
                    a.Add(outPath);

                    double frac = src.Duration > 0 ? pos / src.Duration : 0;
                    sink.Log("part " + n + (attempt > 1 ? " (attempt " + attempt + ")" : "") +
                             ": from " + Util.Num(pos) + " s, asking for " + Util.Num(take) + " s");
                    RunResult r = Runner.Run(FF.FFmpeg, a, Math.Max(0.1, take),
                        delegate(double f, string st)
                        {
                            sink.Progress(Math.Min(1, frac + f * take / Math.Max(0.1, src.Duration)),
                                          "part " + n + "   " + st);
                        },
                        delegate { return sink.Cancelled; });
                    if (r.Cancelled) throw new OperationCanceledException();
                    if (!r.Ok && !File.Exists(outPath))
                        throw new Exception("The part could not be written (code " + r.ExitCode + ").\r\n" +
                                            FirstLines(r.StdErr, 3));

                    part = MediaInfo.Probe(outPath);
                    if (!part.Ok || part.Duration < 0.1)
                        throw new Exception("Part " + n + " is unreadable or empty. This file cannot be split by " +
                                            "size; try splitting by length.");

                    if (part.Size <= maxBytes) { accepted = take; break; }

                    // Over the limit. The shrink factor is applied to the REQUESTED
                    // length: the keyframe overshoot at the start of a part is a
                    // constant, so shrinking the measured length converges to a fixed
                    // point that is still too large.
                    double factor = maxBytes / (double)part.Size * 0.93;
                    if (factor > 0.93) factor = 0.93;
                    guess = Math.Max(0.2, take * factor);
                    sink.Log("   over the limit: " + part.Size + " bytes / limit " + maxBytes +
                             " -> retrying with " + Util.Num(guess) + " s");
                    if (attempt == 5)
                        throw new Exception("Part " + n + " could not be brought under the requested size. " +
                            "This video's keyframe interval may be too large for that size; try a larger part " +
                            "size or split by length.");
                }

                made.Add(outPath);
                sink.Log("   " + Path.GetFileName(outPath) + "  -  " + Util.Size(part.Size) +
                         ", " + Util.Dur(part.Duration));

                // Update the bit rate from the real measurement. Dividing by the
                // requested length overstates it slightly, which keeps the following
                // parts on the safe side.
                if (accepted > 0.5) bitrate = part.Size * 8.0 / accepted;

                // IMPORTANT: advance by the REQUESTED length, not the measured one.
                // ffmpeg ends the cut at pos+take but starts the part at the previous
                // keyframe; advancing by the measured length would skip the difference.
                pos += accepted;
                n++;
                sink.Progress(Math.Min(1, pos / Math.Max(0.1, src.Duration)),
                              (n - 1) + " parts done, " + Util.Dur(pos) + " / " + Util.Dur(src.Duration));

                if (n > 999) throw new Exception("More than 999 parts would be created; choose a larger part size.");
            }
            return made;
        }

        // -------------------------------------------------------------- converting

        public static void Convert(MediaInfo src, Preset preset, ConvertOptions o, string outPath, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            Plan plan = preset.Build(src, o, outPath);
            if (plan.Warning != null) sink.Log(L.T("Note: ") + plan.Warning);

            double span = 1.0 / Math.Max(1, plan.Passes.Count);
            for (int i = 0; i < plan.Passes.Count; i++)
            {
                Pass pass = plan.Passes[i];
                List<string> a = Base();
                a.Add("-i"); a.Add(src.Path);
                a.AddRange(pass.Args);
                a.Add(pass.Output);
                Step(pass.Label + "  ->  " + Path.GetFileName(pass.Output == "NUL" ? "(analysis)" : pass.Output),
                     a, src.Duration, sink, i * span, span);
            }
            CleanupPassLogs();
        }

        static void CleanupPassLogs()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), "cyfan_2pass_*"))
                { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        // ------------------------------------------------------------------- tools

        public static void Trim(MediaInfo src, double start, double end, bool lossless, int crf,
                                string outPath, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            if (end <= 0 || end > src.Duration) end = src.Duration;
            if (start < 0) start = 0;
            double dur = end - start;
            if (dur <= 0.05) throw new Exception("The end must be after the start.");

            List<string> a = Base();
            a.Add("-ss"); a.Add(Util.Num(start));
            a.Add("-i"); a.Add(src.Path);
            a.Add("-t"); a.Add(Util.Num(dur));
            if (lossless)
            {
                a.Add("-c"); a.Add("copy");
                AddMapAV(a, true);
                a.Add("-avoid_negative_ts"); a.Add("make_zero");
            }
            else
            {
                a.Add("-c:v"); a.Add("libx264");
                a.Add("-preset"); a.Add("medium");
                a.Add("-crf"); a.Add(crf.ToString());
                a.Add("-pix_fmt"); a.Add("yuv420p");
                if (src.HasAudio) { a.Add("-c:a"); a.Add("aac"); a.Add("-b:a"); a.Add("192k"); }
                else a.Add("-an");
            }
            if (IsMp4Like(outPath)) { a.Add("-movflags"); a.Add("+faststart"); }
            a.Add(outPath);

            Step("Trim " + Util.Dur(start) + " - " + Util.Dur(end) +
                 (lossless ? " (lossless)" : " (re-encoding)"), a, dur, sink, 0, 1);
        }

        public static void RemoveAudio(MediaInfo src, string outPath, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            List<string> a = Base();
            a.Add("-i"); a.Add(src.Path);
            a.Add("-c"); a.Add("copy");
            AddMapAV(a, false);
            a.Add("-an");
            a.Add(outPath);
            Step("Removing audio", a, src.Duration, sink, 0, 1);
        }

        public static void Rotate(MediaInfo src, int degrees, int crf, string outPath, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            string vf;
            if (degrees == 90) vf = "transpose=1";
            else if (degrees == 270) vf = "transpose=2";
            else if (degrees == 180) vf = "transpose=1,transpose=1";
            else throw new Exception("The angle must be 90, 180 or 270.");

            List<string> a = Base();
            a.Add("-i"); a.Add(src.Path);
            a.Add("-vf"); a.Add(vf);
            a.Add("-c:v"); a.Add("libx264");
            a.Add("-preset"); a.Add("medium");
            a.Add("-crf"); a.Add(crf.ToString());
            a.Add("-pix_fmt"); a.Add("yuv420p");
            if (src.HasAudio) { a.Add("-c:a"); a.Add("copy"); } else a.Add("-an");
            a.Add(outPath);
            Step("Rotating by " + degrees + " degrees (re-encodes)", a, src.Duration, sink, 0, 1);
        }

        public static void GrabFrame(MediaInfo src, double atSecond, string outPath, IJobSink sink)
        {
            EnsureDir(Path.GetDirectoryName(outPath));
            List<string> a = Base();
            a.Add("-ss"); a.Add(Util.Num(atSecond));
            a.Add("-i"); a.Add(src.Path);
            a.Add("-frames:v"); a.Add("1");
            a.Add(outPath);
            Step("Grabbing frame at " + Util.DurPrecise(atSecond), a, 1, sink, 0, 1);
        }

        public static string InfoDump(MediaInfo src)
        {
            List<string> a = new List<string>();
            a.Add("-hide_banner");
            a.Add("-show_format");
            a.Add("-show_streams");
            a.Add("-of"); a.Add("default");
            a.Add("--"); a.Add(src.Path);
            try { return Runner.Capture(FF.FFprobe, a); }
            catch (Exception ex) { return "Could not read: " + ex.Message; }
        }
    }
}
