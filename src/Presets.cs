// Cyfan - conversion presets (DVD, Blu-ray, AVCHD, MP4, editing, audio, remux).
using System;
using System.Collections.Generic;
using System.IO;

namespace Cyfan
{
    class Pass
    {
        public List<string> Args;
        public string Output;
        public string Label;
        public Pass(string label, List<string> args, string output) { Label = label; Args = args; Output = output; }
    }

    class Plan
    {
        public List<Pass> Passes = new List<Pass>();
        public string Warning;
    }

    class ConvertOptions
    {
        public bool Hw;
        public int ScaleW;          // 0 = keep the source resolution
        public int ScaleH;
        public double TargetMB;     // only for the "fit to size" preset
        public bool KeepAudioCopy;  // pass the audio through when possible
    }

    class Preset
    {
        public string Id;
        public string Group;
        public string Name;
        public string Ext;
        public string Notes;
        public bool AudioOnly;
        public bool NeedsTargetSize;
        public bool FixedResolution;   // resolution choice does not apply (disc presets)

        public string Display { get { return "[" + Group + "]  " + Name; } }

        public override string ToString() { return Display; }

        // ---------------------------------------------------------------- helpers

        static void AddRange(List<string> a, params string[] items) { a.AddRange(items); }

        // Optional scaling filter (keeps the aspect ratio, pads with black if asked)
        static string ScaleFilter(int w, int h, bool pad)
        {
            if (w <= 0 || h <= 0) return null;
            // force_divisible_by=2 keeps the aspect ratio from producing an odd
            // width or height (x264 rejects odd dimensions).
            string s = "scale=" + w + ":" + h + ":force_original_aspect_ratio=decrease:force_divisible_by=2";
            if (pad) s += ",pad=" + w + ":" + h + ":(ow-iw)/2:(oh-ih)/2";
            return s + ",setsar=1";
        }

        // Picks the H.264/H.265 encoder; uses hardware acceleration when asked and available.
        static List<string> VideoEncoder(bool hevc, int crf, bool hw, out string used)
        {
            List<string> a = new List<string>();
            string hwEnc = hw ? FF.HwEncoder(hevc) : null;
            if (hwEnc != null)
            {
                used = hwEnc;
                if (hwEnc.EndsWith("_nvenc"))
                    AddRange(a, "-c:v", hwEnc, "-preset", "p5", "-rc", "vbr", "-cq", crf.ToString(), "-b:v", "0");
                else if (hwEnc.EndsWith("_qsv"))
                    AddRange(a, "-c:v", hwEnc, "-global_quality", crf.ToString());
                else
                    AddRange(a, "-c:v", hwEnc, "-rc", "cqp", "-qp_i", crf.ToString(), "-qp_p", crf.ToString());
                return a;
            }
            used = hevc ? "libx265" : "libx264";
            AddRange(a, "-c:v", used, "-preset", "medium", "-crf", crf.ToString());
            if (hevc) AddRange(a, "-tag:v", "hvc1");
            return a;
        }

        static void AddAudio(List<string> a, MediaInfo src, ConvertOptions o, string codec, string bitrate)
        {
            if (src != null && !src.HasAudio) { a.Add("-an"); return; }
            if (o != null && o.KeepAudioCopy && codec == "aac" && src != null && src.ACodec == "aac")
            {
                AddRange(a, "-c:a", "copy");
                return;
            }
            AddRange(a, "-c:a", codec, "-b:a", bitrate);
        }

        static void AddScale(List<string> a, ConvertOptions o)
        {
            string vf = ScaleFilter(o.ScaleW, o.ScaleH, false);
            if (vf != null) AddRange(a, "-vf", vf);
        }

        // -------------------------------------------------------------------- plan

        public Plan Build(MediaInfo src, ConvertOptions o, string outPath)
        {
            Plan plan = new Plan();
            List<string> a = new List<string>();
            string enc;

            switch (Id)
            {
                // ---------------- disc presets ----------------
                case "dvd-pal":
                case "dvd-ntsc":
                    {
                        AddRange(a, "-target", Id == "dvd-pal" ? "pal-dvd" : "ntsc-dvd");
                        if (src != null && src.Width > 0 && (double)src.Width / Math.Max(1, src.Height) > 1.5)
                            AddRange(a, "-aspect", "16:9");
                        else
                            AddRange(a, "-aspect", "4:3");
                        plan.Warning = L.T("Produces a DVD-compliant MPEG-2 stream. Burning a playable disc needs a separate authoring program (DVDStyler, ImgBurn and the like).");
                        break;
                    }
                case "svcd-pal":
                    {
                        AddRange(a, "-target", "pal-svcd");
                        plan.Warning = L.T("Produces an SVCD-compliant stream; burning a disc needs a separate program.");
                        break;
                    }
                case "bluray-1080":
                case "bluray-720":
                    {
                        bool is1080 = Id == "bluray-1080";
                        int w = is1080 ? 1920 : 1280;
                        int h = is1080 ? 1080 : 720;
                        AddRange(a, "-vf", ScaleFilter(w, h, true));
                        if (o.Hw && FF.HwEncoder(false) != null)
                        {
                            List<string> ve = VideoEncoder(false, 20, true, out enc);
                            a.AddRange(ve);
                            AddRange(a, "-maxrate", is1080 ? "24M" : "15M", "-bufsize", "30M");
                        }
                        else
                        {
                            AddRange(a, "-c:v", "libx264", "-preset", "medium",
                                        "-profile:v", "high", "-level", is1080 ? "4.1" : "4.0",
                                        "-b:v", is1080 ? "20M" : "12M",
                                        "-maxrate", is1080 ? "24M" : "15M", "-bufsize", "30M",
                                        "-bf", "2", "-g", "24");
                        }
                        AddRange(a, "-pix_fmt", "yuv420p");
                        if (src != null && src.HasAudio) AddRange(a, "-c:a", "ac3", "-b:a", "640k", "-ar", "48000");
                        else a.Add("-an");
                        AddRange(a, "-f", "mpegts");
                        plan.Warning = L.T("Produces a Blu-ray compliant H.264 + AC-3 stream (.m2ts). Feed it to an authoring tool such as tsMuxeR to build the disc structure.");
                        break;
                    }
                case "avchd":
                    {
                        AddRange(a, "-vf", ScaleFilter(1920, 1080, true));
                        AddRange(a, "-c:v", "libx264", "-preset", "medium",
                                    "-profile:v", "high", "-level", "4.0",
                                    "-b:v", "16M", "-maxrate", "18M", "-bufsize", "24M",
                                    "-bf", "2", "-g", "24", "-pix_fmt", "yuv420p");
                        if (src != null && src.HasAudio) AddRange(a, "-c:a", "ac3", "-b:a", "448k", "-ar", "48000");
                        else a.Add("-an");
                        AddRange(a, "-f", "mpegts");
                        plan.Warning = L.T("Produces an AVCHD compliant stream (without the camera folder structure).");
                        break;
                    }

                // ---------------- modern MP4 ----------------
                case "mp4-archive":
                    AddScale(a, o);
                    a.AddRange(VideoEncoder(false, 16, o.Hw, out enc));
                    AddRange(a, "-pix_fmt", "yuv420p");
                    AddAudio(a, src, o, "aac", "320k");
                    AddRange(a, "-movflags", "+faststart");
                    break;

                case "mp4-high":
                    AddScale(a, o);
                    a.AddRange(VideoEncoder(false, 18, o.Hw, out enc));
                    AddRange(a, "-pix_fmt", "yuv420p");
                    AddAudio(a, src, o, "aac", "256k");
                    AddRange(a, "-movflags", "+faststart");
                    break;

                case "mp4-hevc":
                    AddScale(a, o);
                    a.AddRange(VideoEncoder(true, 24, o.Hw, out enc));
                    AddRange(a, "-pix_fmt", "yuv420p");
                    AddAudio(a, src, o, "aac", "192k");
                    AddRange(a, "-movflags", "+faststart");
                    plan.Warning = L.T("H.265 files may not open on older players and some TVs.");
                    break;

                case "mp4-web":
                    {
                        string vf = ScaleFilter(o.ScaleW > 0 ? o.ScaleW : 1920, o.ScaleH > 0 ? o.ScaleH : 1080, false);
                        AddRange(a, "-vf", vf);
                        a.AddRange(VideoEncoder(false, 21, o.Hw, out enc));
                        AddRange(a, "-pix_fmt", "yuv420p", "-g", "60");
                        AddAudio(a, src, o, "aac", "192k");
                        AddRange(a, "-ar", "48000", "-movflags", "+faststart");
                        break;
                    }

                case "mp4-phone":
                    AddRange(a, "-vf", ScaleFilter(1280, 720, false));
                    a.AddRange(VideoEncoder(false, 23, o.Hw, out enc));
                    AddRange(a, "-pix_fmt", "yuv420p", "-profile:v", "main", "-level", "3.1");
                    AddAudio(a, src, o, "aac", "128k");
                    AddRange(a, "-movflags", "+faststart");
                    break;

                case "mp4-small":
                    AddRange(a, "-vf", ScaleFilter(854, 480, false));
                    a.AddRange(VideoEncoder(false, 28, o.Hw, out enc));
                    AddRange(a, "-pix_fmt", "yuv420p");
                    AddAudio(a, src, o, "aac", "96k");
                    AddRange(a, "-movflags", "+faststart");
                    break;

                // ---------------- fit to a target size (two passes) ----------------
                case "fit-size":
                    {
                        if (src == null || src.Duration <= 0)
                            throw new Exception(L.T("The source length could not be read, so a target size cannot be computed."));
                        if (o.TargetMB <= 0)
                            throw new Exception(L.T("Enter the target size in MB."));

                        int audioKbps = src.HasAudio ? 128 : 0;
                        double totalKbps = o.TargetMB * 8192.0 / src.Duration;   // MB -> kbit/s
                        double videoKbps = totalKbps - audioKbps - 32;           // container overhead
                        if (videoKbps < 100)
                            throw new Exception(L.T("The target size is too small for this length. Try at least {0} MB.",
                                Math.Ceiling((150 + audioKbps) * src.Duration / 8192.0)));

                        List<string> common = new List<string>();
                        string vf = ScaleFilter(o.ScaleW, o.ScaleH, false);
                        if (vf != null) AddRange(common, "-vf", vf);
                        AddRange(common, "-c:v", "libx264", "-preset", "medium",
                                 "-b:v", ((int)videoKbps) + "k", "-pix_fmt", "yuv420p");

                        string logBase = Path.Combine(Path.GetTempPath(), "cyfan_2pass_" + Guid.NewGuid().ToString("N"));

                        List<string> p1 = new List<string>(common);
                        AddRange(p1, "-pass", "1", "-passlogfile", logBase, "-an", "-f", "mp4");
                        plan.Passes.Add(new Pass(L.T("Pass 1 (analysis)"), p1, "NUL"));

                        List<string> p2 = new List<string>(common);
                        AddRange(p2, "-pass", "2", "-passlogfile", logBase);
                        if (src.HasAudio) AddRange(p2, "-c:a", "aac", "-b:a", audioKbps + "k");
                        else p2.Add("-an");
                        AddRange(p2, "-movflags", "+faststart");
                        plan.Passes.Add(new Pass(L.T("Pass 2 (encoding)"), p2, outPath));

                        plan.Warning = L.T("Video bit rate computed as {0} kbps. The result lands close to the target, not exactly on it.",
                            (int)videoKbps);
                        return plan;
                    }

                // ---------------- editing ----------------
                case "prores":
                    AddScale(a, o);
                    AddRange(a, "-c:v", "prores_ks", "-profile:v", "3", "-pix_fmt", "yuv422p10le",
                                "-vendor", "apl0");
                    if (src != null && src.HasAudio) AddRange(a, "-c:a", "pcm_s16le");
                    else a.Add("-an");
                    plan.Warning = L.T("ProRes files are very large (several GB per minute). They are meant for editing.");
                    break;

                case "dnxhr":
                    AddRange(a, "-vf", (ScaleFilter(o.ScaleW > 0 ? o.ScaleW : 1920, o.ScaleH > 0 ? o.ScaleH : 1080, true)));
                    AddRange(a, "-c:v", "dnxhd", "-profile:v", "dnxhr_hq", "-pix_fmt", "yuv422p");
                    if (src != null && src.HasAudio) AddRange(a, "-c:a", "pcm_s16le");
                    else a.Add("-an");
                    plan.Warning = L.T("DNxHR files are very large. They are meant for Avid/Resolve editing.");
                    break;

                // ---------------- audio ----------------
                case "mp3":
                    AddRange(a, "-vn", "-c:a", "libmp3lame", "-b:a", "320k");
                    break;
                case "aac-audio":
                    AddRange(a, "-vn", "-c:a", "aac", "-b:a", "256k");
                    break;
                case "wav":
                    AddRange(a, "-vn", "-c:a", "pcm_s16le", "-ar", "48000");
                    break;
                case "audio-copy":
                    AddRange(a, "-vn", "-c:a", "copy");
                    plan.Warning = L.T("The audio stream is put into an .mka container untouched (truly lossless).");
                    break;

                // ---------------- container change (lossless) ----------------
                // Streams are selected by type so that multiple audio/subtitle tracks
                // survive and data streams (Sony timecode and the like) cannot break
                // the whole operation.
                case "tv-audio":
                    AddRange(a, "-c:v", "copy", "-c:s", "copy",
                                "-c:a", "aac", "-b:a", "320k", "-ar", "48000", "-ac", "2",
                                "-map", "0:v?", "-map", "0:a?", "-map", "0:s?",
                                "-movflags", "+faststart");
                    plan.Warning = L.T("The picture is copied bit for bit (no quality loss); only the audio is converted to AAC.");
                    break;

                case "remux-mp4":
                    AddRange(a, "-c", "copy", "-map", "0:v?", "-map", "0:a?", "-map", "0:s?",
                                "-movflags", "+faststart");
                    plan.Warning = L.T("Lossless container change. Some audio codecs such as DTS do not fit into MP4; choose MKV then.");
                    break;
                case "remux-mkv":
                    AddRange(a, "-c", "copy", "-map", "0:v?", "-map", "0:a?", "-map", "0:s?");
                    break;
                case "remux-mov":
                    AddRange(a, "-c", "copy", "-map", "0:v?", "-map", "0:a?", "-map", "0:s?");
                    break;
                case "remux-ts":
                    AddRange(a, "-c", "copy", "-map", "0:v?", "-map", "0:a?", "-f", "mpegts");
                    break;

                // ---------------- other ----------------
                case "gif":
                    {
                        int w = o.ScaleW > 0 ? o.ScaleW : 480;
                        AddRange(a, "-vf",
                            "fps=12,scale=" + w + ":-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse",
                            "-loop", "0", "-an");
                        plan.Warning = L.T("GIF files are huge; trim the video on the Tools tab first for long clips.");
                        break;
                    }

                default:
                    throw new Exception("Unknown preset: " + Id);
            }

            plan.Passes.Add(new Pass(L.T("Converting"), a, outPath));
            return plan;
        }

        // ------------------------------------------------------------ preset list

        public static List<Preset> All()
        {
            List<Preset> l = new List<Preset>();
            string disc = L.T("Disc");
            string mp4 = "MP4";
            string edit = L.T("Editing");
            string audio = L.T("Audio");
            string remux = L.T("Container");
            string other = L.T("Other");

            l.Add(New("dvd-pal", disc, L.T("DVD-Video  PAL  (720x576, 25p)"), ".mpg", true,
                L.T("MPEG-2 + AC-3. The DVD standard in Europe.")));
            l.Add(New("dvd-ntsc", disc, L.T("DVD-Video  NTSC  (720x480, 29.97p)"), ".mpg", true,
                L.T("MPEG-2 + AC-3. The DVD standard in America and Japan.")));
            l.Add(New("svcd-pal", disc, L.T("SVCD  PAL  (480x576)"), ".mpg", true,
                L.T("Older CD-based video format.")));
            l.Add(New("bluray-1080", disc, L.T("Blu-ray compliant 1080p  (H.264 + AC-3)"), ".m2ts", true,
                L.T("20 Mbps, High profile 4.1. Ready for tsMuxeR.")));
            l.Add(New("bluray-720", disc, L.T("Blu-ray compliant 720p  (H.264 + AC-3)"), ".m2ts", true,
                L.T("12 Mbps. Smaller file, still a compliant stream.")));
            l.Add(New("avchd", disc, L.T("AVCHD compliant 1080  (H.264 + AC-3)"), ".mts", true,
                L.T("16 Mbps. The original format of camera files.")));

            l.Add(New("mp4-archive", mp4, L.T("4K / source archive  (H.264, CRF 16)"), ".mp4", false,
                L.T("Visually almost lossless. Large file.")));
            l.Add(New("mp4-high", mp4, L.T("High quality  (H.264, CRF 18)"), ".mp4", false,
                L.T("The best balance for everyday use. Recommended.")));
            l.Add(New("mp4-hevc", mp4, L.T("H.265 / HEVC  - half the size  (CRF 24)"), ".mp4", false,
                L.T("Same quality in about half the space. Slower to encode.")));
            l.Add(New("mp4-web", mp4, L.T("Web / YouTube 1080p"), ".mp4", false,
                L.T("Fast-starting stream, 48 kHz audio.")));
            l.Add(New("mp4-phone", mp4, L.T("Phone / tablet 720p"), ".mp4", false,
                L.T("Main profile, opens on older devices too.")));
            l.Add(New("mp4-small", mp4, L.T("Shrink for e-mail / messaging  (480p)"), ".mp4", false,
                L.T("Low quality, very small file.")));

            Preset fit = New("fit-size", mp4, L.T("Fit to a target size  (two passes)"), ".mp4", false,
                L.T("Computes the bit rate from the MB value you enter. For fitting a disc or an attachment."));
            fit.NeedsTargetSize = true;
            l.Add(fit);

            l.Add(New("prores", edit, L.T("Apple ProRes 422"), ".mov", false,
                L.T("For Premiere/Resolve/Final Cut. Very large file.")));
            l.Add(New("dnxhr", edit, L.T("Avid DNxHR HQ"), ".mxf", false,
                L.T("For Avid/Resolve. Very large file.")));

            l.Add(NewAudio("mp3", audio, L.T("MP3  320 kbps"), ".mp3", L.T("Plays everywhere.")));
            l.Add(NewAudio("aac-audio", audio, L.T("AAC  256 kbps"), ".m4a", L.T("More efficient than MP3.")));
            l.Add(NewAudio("wav", audio, L.T("WAV  (uncompressed)"), ".wav", L.T("Lossless, large file.")));
            l.Add(NewAudio("audio-copy", audio, L.T("Extract audio losslessly"), ".mka", L.T("No re-encoding at all.")));

            l.Add(New("tv-audio", remux, L.T("Make audio TV-friendly  (video untouched)"), ".mp4", true,
                L.T("Most TVs cannot play the PCM audio of XAVC S. The picture is copied bit for bit, the audio becomes AAC 320k.")));
            l.Add(New("remux-mp4", remux, L.T("Put into MP4  (lossless)"), ".mp4", true,
                L.T("Takes seconds, no quality loss.")));
            l.Add(New("remux-mkv", remux, L.T("Put into MKV  (lossless)"), ".mkv", true,
                L.T("Accepts every codec. The safest choice.")));
            l.Add(New("remux-mov", remux, L.T("Put into MOV  (lossless)"), ".mov", true,
                L.T("For Apple applications.")));
            l.Add(New("remux-ts", remux, L.T("Put into MPEG-TS  (lossless)"), ".ts", true,
                L.T("For broadcast and disc workflows.")));

            l.Add(New("gif", other, L.T("Animated GIF"), ".gif", false,
                L.T("12 fps, 480 pixels wide by default.")));

            return l;
        }

        static Preset New(string id, string group, string name, string ext, bool fixedRes, string notes)
        {
            Preset p = new Preset();
            p.Id = id; p.Group = group; p.Name = name; p.Ext = ext; p.Notes = notes;
            p.FixedResolution = fixedRes;
            return p;
        }

        static Preset NewAudio(string id, string group, string name, string ext, string notes)
        {
            Preset p = New(id, group, name, ext, true, notes);
            p.AudioOnly = true;
            return p;
        }
    }
}
