// Cyfan - cekirdek: ayarlar, yardimcilar, ffmpeg bulma/kurma/calistirma, medya bilgisi.
// C# 5 ile derlenir (Windows ile gelen csc.exe), bu yuzden string interpolation,
// ?. ve benzeri yeni soz dizimi kullanilmaz.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Cyfan
{
    static class App
    {
        public const string Name = "Cyfan";
        public const string Version = "1.0.0";
        public const string Repo = "chnozmn-pixel/Cyfan";

        public static readonly string ExeDir;
        public static readonly string DataDir;

        static App()
        {
            ExeDir = Path.GetDirectoryName(Application_ExecutablePath());
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
            try { Directory.CreateDirectory(DataDir); }
            catch { }
        }

        // System.Windows.Forms'a bagimli olmamak icin
        static string Application_ExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }

    static class Util
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Dur(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "-";
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        public static string DurPrecise(double seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);
        }

        public static string Size(long bytes)
        {
            if (bytes <= 0) return "-";
            double b = bytes;
            if (b >= 1073741824.0) return (b / 1073741824.0).ToString("0.00", Inv) + " GB";
            if (b >= 1048576.0) return (b / 1048576.0).ToString("0.0", Inv) + " MB";
            if (b >= 1024.0) return (b / 1024.0).ToString("0") + " KB";
            return bytes.ToString(Inv) + " B";
        }

        public static string Rate(double bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "-";
            if (bitsPerSecond >= 1000000.0) return (bitsPerSecond / 1000000.0).ToString("0.0", Inv) + " Mbps";
            return (bitsPerSecond / 1000.0).ToString("0") + " kbps";
        }

        // "90", "1:30", "01:02:03", "1:02:03.25" kabul eder. Hatali ise -1.
        public static double ParseTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.Trim().Replace(',', '.');
            string[] parts = s.Split(':');
            double total = 0;
            try
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    double v = double.Parse(parts[i], Inv);
                    if (v < 0) return -1;
                    total = total * 60.0 + v;
                }
            }
            catch { return -1; }
            return total;
        }

        public static string SafeName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // Windows komut satiri icin tek argumani tirnaklar.
        public static string Quote(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length > 0 && arg.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return arg;
            StringBuilder sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string JoinArgs(IList<string> args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Quote(args[i]));
            }
            return sb.ToString();
        }

        public static string Num(double d)
        {
            return d.ToString("0.###", Inv);
        }

        public static void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select," + Quote(path));
                else if (Directory.Exists(path)) Process.Start("explorer.exe", Quote(path));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ ayarlar

    static class Settings
    {
        static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string FilePath { get { return Path.Combine(App.DataDir, "ayarlar.txt"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    _map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in _map) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            string v;
            if (_map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return def;
        }

        public static bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static void Set(string key, string value) { _map[key] = value == null ? "" : value; }
        public static void Set(string key, bool value) { _map[key] = value ? "1" : "0"; }
    }

    // ------------------------------------------------------- ffmpeg bul / kur

    static class FF
    {
        public const string ZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        public const string DownloadPage = "https://www.gyan.dev/ffmpeg/builds/";

        public static string FFmpeg;
        public static string FFprobe;

        static List<string> _encoders;

        public static bool Ready { get { return FFmpeg != null && FFprobe != null; } }

        public static string PortableBin { get { return Path.Combine(App.DataDir, "ffmpeg"); } }

        public static void Locate()
        {
            FFmpeg = Find("ffmpeg");
            FFprobe = Find("ffprobe");
            _encoders = null;
        }

        static string Find(string name)
        {
            string exe = name + ".exe";
            // 1) Kullanicinin elle verdigi yol
            string over = Settings.Get("FFmpegFolder", null);
            if (over != null)
            {
                string p = Path.Combine(over, exe);
                if (File.Exists(p)) return p;
            }
            // 2) Program klasoru (tasinabilir kullanim) ve yedek veri klasoru
            string[] local = new string[] {
                Path.Combine(App.ExeDir, exe),
                Path.Combine(Path.Combine(App.ExeDir, "ffmpeg"), exe),
                Path.Combine(Path.Combine(App.ExeDir, "ffmpeg\\bin"), exe),
                Path.Combine(Path.Combine(App.ExeDir, "bin"), exe),
                Path.Combine(PortableBin, exe)
            };
            foreach (string p in local) if (File.Exists(p)) return p;

            // 3) PATH
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
            {
                foreach (string dir in path.Split(';'))
                {
                    if (dir.Length == 0) continue;
                    try { string p = Path.Combine(dir.Trim(), exe); if (File.Exists(p)) return p; }
                    catch { }
                }
            }
            // 4) Bilinen kurulum konumlari
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] known = new string[] {
                Path.Combine(lad, "Microsoft\\WinGet\\Links\\" + exe),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg\\bin\\" + exe),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey\\bin\\" + exe)
            };
            foreach (string p in known) if (File.Exists(p)) return p;

            // 5) winget paket klasorunde ara
            try
            {
                string pkgs = Path.Combine(lad, "Microsoft\\WinGet\\Packages");
                if (Directory.Exists(pkgs))
                {
                    string[] hits = Directory.GetFiles(pkgs, exe, SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
            }
            catch { }
            return null;
        }

        public static bool DirWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".yazma_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string InstallDir()
        {
            if (DirWritable(App.ExeDir)) return App.ExeDir;
            Directory.CreateDirectory(PortableBin);
            return PortableBin;
        }

        // Zip icinden ffmpeg.exe + ffprobe.exe cikarir; klasor yapisindan bagimsiz.
        public static string InstallFromZip(string zipPath, Action<string> log)
        {
            string dest = InstallDir();
            string tmp = Path.Combine(Path.GetTempPath(), "cyfan_ff_" + Guid.NewGuid().ToString("N"));
            try
            {
                log("Arsiv aciliyor...");
                Directory.CreateDirectory(tmp);
                ZipFile.ExtractToDirectory(zipPath, tmp);
                string[] want = new string[] { "ffmpeg.exe", "ffprobe.exe" };
                foreach (string name in want)
                {
                    string[] hits = Directory.GetFiles(tmp, name, SearchOption.AllDirectories);
                    if (hits.Length == 0)
                        throw new Exception("Arsivde " + name + " bulunamadi. Secilen dosya beklenen ffmpeg arsivi degil.");
                    File.Copy(hits[0], Path.Combine(dest, name), true);
                    log("Kuruldu: " + name);
                }
                return dest;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
                catch { }
            }
        }

        public static string DownloadZip(Action<string> log, Action<long, long> progress, Func<bool> cancelled)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; }
            catch { }
            string zip = Path.Combine(Path.GetTempPath(), "ffmpeg_" + Guid.NewGuid().ToString("N") + ".zip");
            log("Indiriliyor: " + ZipUrl);
            bool ok = false;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ZipUrl);
            req.UserAgent = App.Name + "/" + App.Version;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            using (WebResponse resp = req.GetResponse())
            using (Stream input = resp.GetResponseStream())
            using (FileStream output = File.Create(zip))
            {
                long total = resp.ContentLength;
                byte[] buf = new byte[262144];
                long done = 0;
                int read;
                try
                {
                    while ((read = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, read);
                        done += read;
                        if (progress != null) progress(done, total);
                        if (cancelled != null && cancelled()) throw new OperationCanceledException("Indirme iptal edildi.");
                    }
                    if (total > 0 && done < total)
                        throw new Exception("Indirme yarida kesildi (" + Util.Size(done) + " / " + Util.Size(total) + ").");
                    ok = true;
                }
                finally
                {
                    if (!ok) { try { output.Close(); File.Delete(zip); } catch { } }
                }
            }
            log("Indirildi: " + Util.Size(new FileInfo(zip).Length));
            return zip;
        }

        public static void InstallWinget(Action<string> log)
        {
            string winget = null;
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
                foreach (string dir in path.Split(';'))
                {
                    try { string p = Path.Combine(dir.Trim(), "winget.exe"); if (File.Exists(p)) { winget = p; break; } }
                    catch { }
                }
            if (winget == null) throw new Exception("Bu bilgisayarda winget yok. Otomatik indirmeyi kullanin.");

            log("winget calistiriliyor...");
            ProcessStartInfo psi = new ProcessStartInfo(winget,
                "install --id Gyan.FFmpeg -e --source winget --accept-package-agreements --accept-source-agreements");
            psi.UseShellExecute = true;
            Process p2 = Process.Start(psi);
            p2.WaitForExit();
            log("winget cikis kodu: " + p2.ExitCode);
            // -1978335189 = zaten kurulu
            if (p2.ExitCode != 0 && p2.ExitCode != -1978335189)
                throw new Exception("winget kurulumu basarisiz oldu (kod " + p2.ExitCode + ").");
        }

        public static bool WingetAvailable()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path == null) return false;
            foreach (string dir in path.Split(';'))
            {
                try { if (File.Exists(Path.Combine(dir.Trim(), "winget.exe"))) return true; }
                catch { }
            }
            return false;
        }

        public static string VersionString()
        {
            if (FFmpeg == null) return null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(FFmpeg, "-hide_banner -version");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string first = p.StandardOutput.ReadLine();
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (first == null) return null;
                    Match m = Regex.Match(first, @"ffmpeg version (\S+)");
                    return m.Success ? m.Groups[1].Value : first;
                }
            }
            catch { return null; }
        }

        public static List<string> Encoders()
        {
            if (_encoders != null) return _encoders;
            _encoders = new List<string>();
            if (FFmpeg == null) return _encoders;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(FFmpeg, "-hide_banner -encoders");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string all = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    foreach (Match m in Regex.Matches(all, @"^\s*[VAS][\w\.]{5}\s+(\S+)", RegexOptions.Multiline))
                        _encoders.Add(m.Groups[1].Value);
                }
            }
            catch { }
            return _encoders;
        }

        public static bool HasEncoder(string name)
        {
            return Encoders().Contains(name);
        }

        // Donanim hizlandirmali H.264/H.265 kodlayicisi varsa adini dondurur.
        public static string HwEncoder(bool hevc)
        {
            string[] order = hevc
                ? new string[] { "hevc_nvenc", "hevc_qsv", "hevc_amf" }
                : new string[] { "h264_nvenc", "h264_qsv", "h264_amf" };
            foreach (string e in order) if (HasEncoder(e)) return e;
            return null;
        }
    }

    // --------------------------------------------------------- ffmpeg calistir

    class RunResult
    {
        public int ExitCode;
        public string StdErr;
        public bool Cancelled;
        public bool Ok { get { return ExitCode == 0 && !Cancelled; } }
    }

    static class Runner
    {
        // ffmpeg'i calistirir; ilerlemeyi (0..1) ve durum metnini bildirir.
        public static RunResult Run(string exe, IList<string> args, double totalSeconds,
                                    Action<double, string> progress, Func<bool> cancelled)
        {
            RunResult r = new RunResult();
            ProcessStartInfo psi = new ProcessStartInfo(exe, Util.JoinArgs(args));
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            StringBuilder err = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (err) { if (err.Length < 20000) err.AppendLine(e.Data); } }
                };
                p.Start();
                p.BeginErrorReadLine();

                string speed = "";
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.StartsWith("out_time=", StringComparison.Ordinal))
                    {
                        double done = Util.ParseTime(line.Substring(9));
                        if (done >= 0 && progress != null)
                        {
                            double frac = totalSeconds > 0 ? done / totalSeconds : 0;
                            if (frac > 1) frac = 1;
                            string st = Util.Dur(done);
                            if (totalSeconds > 0) st += " / " + Util.Dur(totalSeconds);
                            if (speed.Length > 0) st += "   hiz: " + speed;
                            progress(frac, st);
                        }
                    }
                    else if (line.StartsWith("speed=", StringComparison.Ordinal))
                    {
                        speed = line.Substring(6).Trim();
                    }
                    if (cancelled != null && cancelled())
                    {
                        try { if (!p.HasExited) p.Kill(); }
                        catch { }
                        r.Cancelled = true;
                        break;
                    }
                }
                p.WaitForExit();
                r.ExitCode = p.ExitCode;
            }
            r.StdErr = err.ToString().Trim();
            return r;
        }

        // Kisa komutlar icin (ffprobe): ciktiyi dondurur.
        public static string Capture(string exe, IList<string> args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, Util.JoinArgs(args));
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            using (Process p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return outp;
            }
        }
    }

    // ------------------------------------------------------------ medya bilgisi

    class MediaInfo
    {
        public string Path;
        public string Name;
        public bool Ok;
        public string Error;

        public double Duration;
        public long Size;
        public double BitRate;       // bit/s (kap geneli)
        public string FormatName;

        public bool HasVideo, HasAudio;
        public string VCodec, PixFmt, AspectRatio;
        public int Width, Height;
        public double Fps;
        public double VBitRate;

        public string ACodec, ChannelLayout;
        public int SampleRate, Channels;
        public double ABitRate;

        public int StreamCount;
        // Akislarin sirali turu, ornegin "video,audio" ya da "video,audio,data".
        // concat demuxer butun dosyalarin ayni duzende olmasini ister.
        public string Layout;

        public static MediaInfo Probe(string path)
        {
            MediaInfo m = new MediaInfo();
            m.Path = path;
            m.Name = System.IO.Path.GetFileName(path);
            try { m.Size = new FileInfo(path).Length; }
            catch { }

            if (!FF.Ready) { m.Error = "ffprobe is not available"; return m; }

            List<string> a = new List<string>();
            a.Add("-v"); a.Add("error");
            a.Add("-show_entries");
            a.Add("format=duration,size,bit_rate,format_name:stream=index,codec_type,codec_name,width,height,r_frame_rate,pix_fmt,display_aspect_ratio,sample_rate,channels,channel_layout,bit_rate");
            a.Add("-of"); a.Add("flat");
            a.Add("--"); a.Add(path);

            string outp;
            try { outp = Runner.Capture(FF.FFprobe, a); }
            catch (Exception ex) { m.Error = ex.Message; return m; }

            if (string.IsNullOrEmpty(outp)) { m.Error = "ffprobe could not read the file"; return m; }

            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in outp.Split('\n'))
            {
                string line = raw.Trim();
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string key = line.Substring(0, i);
                string val = line.Substring(i + 1).Trim();
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"') val = val.Substring(1, val.Length - 2);
                kv[key] = val;
            }

            m.Duration = D(kv, "format.duration");
            m.BitRate = D(kv, "format.bit_rate");
            m.FormatName = S(kv, "format.format_name");

            // Akislari tara: ilk video ve ilk ses
            StringBuilder layout = new StringBuilder();
            for (int idx = 0; idx < 16; idx++)
            {
                string pre = "streams.stream." + idx + ".";
                string type = S(kv, pre + "codec_type");
                if (type == null) continue;
                m.StreamCount++;
                if (layout.Length > 0) layout.Append(',');
                layout.Append(type);
                if (type == "video" && !m.HasVideo)
                {
                    m.HasVideo = true;
                    m.VCodec = S(kv, pre + "codec_name");
                    m.Width = (int)D(kv, pre + "width");
                    m.Height = (int)D(kv, pre + "height");
                    m.PixFmt = S(kv, pre + "pix_fmt");
                    m.AspectRatio = S(kv, pre + "display_aspect_ratio");
                    m.VBitRate = D(kv, pre + "bit_rate");
                    string fr = S(kv, pre + "r_frame_rate");
                    if (fr != null)
                    {
                        string[] pq = fr.Split('/');
                        double num, den;
                        if (pq.Length == 2 && double.TryParse(pq[0], NumberStyles.Any, Util.Inv, out num)
                                           && double.TryParse(pq[1], NumberStyles.Any, Util.Inv, out den) && den != 0)
                            m.Fps = Math.Round(num / den, 3);
                    }
                }
                else if (type == "audio" && !m.HasAudio)
                {
                    m.HasAudio = true;
                    m.ACodec = S(kv, pre + "codec_name");
                    m.SampleRate = (int)D(kv, pre + "sample_rate");
                    m.Channels = (int)D(kv, pre + "channels");
                    m.ChannelLayout = S(kv, pre + "channel_layout");
                    m.ABitRate = D(kv, pre + "bit_rate");
                }
            }

            m.Layout = layout.ToString();
            if (!m.HasVideo && !m.HasAudio) { m.Error = "Dosyada video veya ses akisi bulunamadi"; return m; }
            m.Ok = true;
            return m;
        }

        static string S(Dictionary<string, string> kv, string key)
        {
            string v;
            if (kv.TryGetValue(key, out v) && v.Length > 0 && v != "unknown" && v != "N/A") return v;
            return null;
        }

        static double D(Dictionary<string, string> kv, string key)
        {
            string v = S(kv, key);
            double d;
            if (v != null && double.TryParse(v, NumberStyles.Any, Util.Inv, out d)) return d;
            return 0;
        }

        public string Resolution { get { return Width > 0 ? Width + "x" + Height : "-"; } }

        public string AudioText
        {
            get
            {
                if (!HasAudio) return L.T("(no audio)");
                string s = ACodec;
                if (SampleRate > 0) s += " " + SampleRate + "Hz";
                if (Channels > 0) s += " " + Channels + "ch";
                return s;
            }
        }

        // Kayipsiz birlestirme icin uyumluluk imzasi
        public string CompatKey()
        {
            return string.Join("|", new string[] {
                VCodec, Width.ToString(), Height.ToString(), Util.Num(Fps), PixFmt,
                ACodec, SampleRate.ToString(), Channels.ToString()
            });
        }

        // Ortalama bit hizi (bit/s) - boyuta gore parcalama icin
        public double EffectiveBitRate()
        {
            if (BitRate > 0) return BitRate;
            if (Duration > 0 && Size > 0) return Size * 8.0 / Duration;
            return 0;
        }
    }
}
