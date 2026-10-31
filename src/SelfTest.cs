// Cyfan - arayuz olmadan calisan dogrulama testi:  Cyfan.exe --selftest <klasor>
// Test kliplerini kendisi uretir, sonra birlestirme / parcalama / donusturme /
// arac islemlerini gercekten calistirip sonuclari olcer.
using System;
using System.Collections.Generic;
using System.IO;

namespace Cyfan
{
    class ConsoleSink : IJobSink
    {
        readonly bool _verbose;
        public ConsoleSink(bool verbose) { _verbose = verbose; }
        public void Log(string text) { if (_verbose) Console.WriteLine("      " + text); }
        public void Progress(double frac, string status) { }
        public bool Cancelled { get { return false; } }
    }

    static class SelfTest
    {
        static int _pass, _fail;
        static IJobSink _sink = new ConsoleSink(false);
        static IJobSink _loud = new ConsoleSink(true);

        static void Check(string what, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  [OK]    " + what + (detail == null ? "" : "  -  " + detail)); }
            else { _fail++; Console.WriteLine("  [HATA]  " + what + (detail == null ? "" : "  -  " + detail)); }
        }

        static void Near(string what, double actual, double expected, double tolerance)
        {
            bool ok = Math.Abs(actual - expected) <= tolerance;
            Check(what, ok, "beklenen " + Util.Num(expected) + ", olcum " + Util.Num(actual));
        }

        public static int Run(string dir)
        {
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetTempPath(), "cyfan_selftest");
            Console.WriteLine();
            Console.WriteLine("=== " + App.Name + " " + App.Version + " kendi kendini test ===");
            Console.WriteLine("Klasor: " + dir);

            FF.Locate();
            if (!FF.Ready)
            {
                Console.WriteLine("ffmpeg bulunamadi - test yapilamaz.");
                return 2;
            }
            Console.WriteLine("ffmpeg: " + FF.FFmpeg);
            Console.WriteLine("Donanim kodlayici: " + (FF.HwEncoder(false) == null ? "yok" : FF.HwEncoder(false)));
            Console.WriteLine();

            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Klasor hazirlanamadi: " + ex.Message);
                return 2;
            }

            try
            {
                List<MediaInfo> clips = MakeClips(dir);
                TestMerge(dir, clips);
                TestSplit(dir, clips);
                TestConvert(dir, clips[0]);
                TestTools(dir, clips[0]);
                TestDataStream(dir);
                TestLayout();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine();
                Console.WriteLine("  [HATA]  Test yarida kesildi: " + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine("=== Sonuc: " + _pass + " gecti, " + _fail + " basarisiz ===");
            Console.WriteLine();
            return _fail == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------- test klipleri

        static MediaInfo Generate(string path, string size, int rate, double seconds, bool audio, int freq)
        {
            List<string> a = new List<string>();
            a.Add("-hide_banner"); a.Add("-loglevel"); a.Add("error"); a.Add("-y");
            a.Add("-f"); a.Add("lavfi");
            a.Add("-i"); a.Add("testsrc=size=" + size + ":rate=" + rate + ":duration=" + Util.Num(seconds));
            if (audio)
            {
                a.Add("-f"); a.Add("lavfi");
                a.Add("-i"); a.Add("sine=frequency=" + freq + ":duration=" + Util.Num(seconds));
            }
            a.Add("-c:v"); a.Add("libx264"); a.Add("-preset"); a.Add("ultrafast");
            a.Add("-g"); a.Add(rate.ToString());        // saniyede bir anahtar kare
            a.Add("-pix_fmt"); a.Add("yuv420p");
            if (audio) { a.Add("-c:a"); a.Add("aac"); }
            a.Add(path);

            RunResult r = Runner.Run(FF.FFmpeg, a, seconds, null, null);
            if (!r.Ok) throw new Exception("Test klibi uretilemedi: " + path + "\n" + r.StdErr);
            return MediaInfo.Probe(path);
        }

        static List<MediaInfo> MakeClips(string dir)
        {
            Console.WriteLine("--- Test klipleri uretiliyor ---");
            List<MediaInfo> l = new List<MediaInfo>();
            l.Add(Generate(Path.Combine(dir, "C0001.mp4"), "640x360", 25, 10, true, 440));
            l.Add(Generate(Path.Combine(dir, "C0002.mp4"), "640x360", 25, 10, true, 660));
            // bilerek farkli: cozunurluk, fps ve sessiz
            l.Add(Generate(Path.Combine(dir, "C0003 bosluklu'ad.mp4"), "320x240", 30, 5, false, 0));

            foreach (MediaInfo m in l)
                Check("Klip okundu: " + m.Name, m.Ok && m.Duration > 0,
                      m.Resolution + " " + Util.Num(m.Fps) + "fps " + (m.HasAudio ? "sesli" : "sessiz"));
            return l;
        }

        // ------------------------------------------------------------ birlestirme

        static void TestMerge(string dir, List<MediaInfo> clips)
        {
            Console.WriteLine();
            Console.WriteLine("--- Birlestirme ---");

            List<MediaInfo> same = new List<MediaInfo>();
            same.Add(clips[0]); same.Add(clips[1]);

            Check("Uyumluluk imzasi ayni klipler icin esit", clips[0].CompatKey() == clips[1].CompatKey(), null);
            Check("Uyumluluk imzasi farkli klipler icin farkli", clips[0].CompatKey() != clips[2].CompatKey(), null);

            string outCopy = Path.Combine(dir, "birlesik_kayipsiz.mp4");
            Ops.MergeLossless(same, outCopy, _sink);
            MediaInfo mc = MediaInfo.Probe(outCopy);
            Check("Kayipsiz birlestirme dosyasi olustu", mc.Ok, Util.Size(mc.Size));
            Near("Kayipsiz birlestirme suresi", mc.Duration, 20, 0.5);
            Check("Kayipsiz birlestirmede ses korundu", mc.HasAudio, mc.AudioText);

            string outEnc = Path.Combine(dir, "birlesik_kodlanmis.mp4");
            Ops.MergeReencode(clips, outEnc, 28, false, _sink);
            MediaInfo me = MediaInfo.Probe(outEnc);
            Check("Farkli kliplerin yeniden kodlanarak birlestirilmesi", me.Ok, me.Resolution);
            Near("Yeniden kodlanmis birlestirme suresi", me.Duration, 25, 0.8);
            Check("Sessiz klip icin ses uretildi", me.HasAudio, me.AudioText);
            Check("Cozunurluk ilk klibe esitlendi", me.Width == clips[0].Width && me.Height == clips[0].Height,
                  me.Resolution);

            // Birlestirirken ayni gecise parcalara yazma (uzun cekimler icin):
            // ara dosya olusmadan dogrudan parcalar uretilmeli.
            string segDir = Path.Combine(dir, "birlesik_parcali");
            Directory.CreateDirectory(segDir);
            List<string> segParts = Ops.MergeLossless(same, Path.Combine(segDir, "bp.mp4"), 6, _sink);
            Check("Birlestirirken parcalara ayirma: parca sayisi", segParts.Count == 4,
                  segParts.Count + " parca (20 sn / 6 sn)");
            double segSum = 0;
            bool segOk = true;
            foreach (string f in segParts)
            {
                MediaInfo pm = MediaInfo.Probe(f);
                if (!pm.Ok || !pm.HasAudio) segOk = false;
                segSum += pm.Duration;
            }
            Check("Birlestirirken parcalara ayirma: parcalar saglam", segOk, null);
            Near("Birlestirirken parcalara ayirma: toplam sure", segSum, 20, 0.6);
            Check("Ara dosya birakilmadi", !File.Exists(Path.Combine(segDir, "bp.mp4")),
                  "yalnizca parcalar var");

            // Bosluk ve kesme isareti iceren ad concat listesinde dogru kaciliyor mu
            string list = Ops.WriteConcatList(clips);
            string text = File.ReadAllText(list);
            Check("Concat listesinde ozel karakterler kacirildi", text.Contains("'\\''"), null);
            try { File.Delete(list); }
            catch { }
        }

        // -------------------------------------------------------------- parcalama

        static void TestSplit(string dir, List<MediaInfo> clips)
        {
            Console.WriteLine();
            Console.WriteLine("--- Parcalama ---");
            MediaInfo src = MediaInfo.Probe(Path.Combine(dir, "birlesik_kayipsiz.mp4"));  // 20 sn
            Check("Parcalanacak kaynak hazir", src.Ok && src.Duration > 19, Util.Dur(src.Duration));

            // 1) sureye gore - 5 saniyelik parcalar -> 4 parca
            string d1 = Path.Combine(dir, "sure");
            List<string> p1 = Ops.SplitByDuration(src, 5, d1, "sure", ".mp4", false, 18, _sink);
            Check("Sureye gore parcalama: parca sayisi", p1.Count == 4, p1.Count + " parca");
            double sum = 0;
            bool allOk = true;
            foreach (string f in p1)
            {
                MediaInfo pm = MediaInfo.Probe(f);
                if (!pm.Ok) allOk = false;
                sum += pm.Duration;
            }
            Check("Sureye gore parcalama: hepsi okunabiliyor", allOk, null);
            Near("Sureye gore parcalama: toplam sure korundu", sum, src.Duration, 0.6);

            // 2) esit parca sayisi - 3 parca
            string d2 = Path.Combine(dir, "esit");
            List<string> p2 = Ops.SplitByDuration(src, src.Duration / 3.0, d2, "esit", ".mp4", false, 18, _sink);
            Check("Esit parcalama: 3 parca", p2.Count == 3, p2.Count + " parca");

            // 3) boyuta gore
            string d3 = Path.Combine(dir, "boyut");
            long limit = src.Size / 3;
            List<string> p3 = Ops.SplitBySize(src, limit, d3, "boyut", ".mp4", _sink);
            Check("Boyuta gore parcalama: en az 3 parca", p3.Count >= 3, p3.Count + " parca, sinir " + Util.Size(limit));
            bool underLimit = true;
            double sum3 = 0;
            string sizes = "";
            foreach (string f in p3)
            {
                FileInfo fi = new FileInfo(f);
                if (fi.Length > limit) underLimit = false;
                sizes += Util.Size(fi.Length) + " ";
                sum3 += MediaInfo.Probe(f).Duration;
            }
            Check("Boyuta gore parcalama: hicbir parca siniri gecmedi", underLimit, sizes.Trim());
            // Kayipsiz kesmede parcalar anahtar kareden basladigi icin baslarinda kucuk
            // bir ortusme olur; onemli olan hic icerik atlanmamasi.
            Check("Boyuta gore parcalama: hic icerik atlanmadi", sum3 >= src.Duration - 0.2,
                  "kaynak " + Util.Num(src.Duration) + " sn, parcalar toplami " + Util.Num(Math.Round(sum3, 2)) + " sn");
            Check("Boyuta gore parcalama: ortusme makul", sum3 <= src.Duration + 1.0 * p3.Count,
                  "fazlalik " + Util.Num(Math.Round(sum3 - src.Duration, 2)) + " sn");

            // 4) tam saniyede kesme (yeniden kodlar)
            string d4 = Path.Combine(dir, "tam");
            List<string> p4 = Ops.SplitByDuration(src, 7, d4, "tam", ".mp4", true, 30, _sink);
            Check("Tam saniyede parcalama calisti", p4.Count >= 3, p4.Count + " parca");
            if (p4.Count > 0)
            {
                MediaInfo first = MediaInfo.Probe(p4[0]);
                Near("Tam saniyede parcalama: ilk parca tam 7 sn", first.Duration, 7, 0.35);
            }
        }

        // ------------------------------------------------------------ donusturme

        static void TestConvert(string dir, MediaInfo src)
        {
            Console.WriteLine();
            Console.WriteLine("--- Donusturme onayarlari ---");
            List<Preset> all = Preset.All();
            Check("Onayar sayisi", all.Count >= 20, all.Count + " onayar");

            // Her onayarin arguman uretebildigini dogrula (calistirmadan)
            foreach (Preset p in all)
            {
                try
                {
                    ConvertOptions o = new ConvertOptions();
                    if (p.NeedsTargetSize) o.TargetMB = 5;
                    Plan plan = p.Build(src, o, Path.Combine(dir, "x" + p.Ext));
                    if (plan.Passes.Count == 0) throw new Exception("gecis yok");
                }
                catch (Exception ex)
                {
                    Check("Onayar argumanlari: " + p.Id, false, ex.Message);
                }
            }
            Check("Tum onayarlar arguman uretebildi", true, null);

            // Gercekten calistirilanlar (hizli olanlar secildi)
            string[] ids = new string[] { "remux-mkv", "mp4-small", "mp4-hevc", "dvd-pal", "bluray-720", "mp3", "gif", "fit-size", "audio-copy" };
            foreach (string id in ids)
            {
                Preset p = null;
                foreach (Preset q in all) if (q.Id == id) { p = q; break; }
                if (p == null) { Check("Onayar bulundu: " + id, false, null); continue; }

                ConvertOptions o = new ConvertOptions();
                if (p.NeedsTargetSize) o.TargetMB = 1;
                string outPath = Path.Combine(dir, "d_" + id + p.Ext);
                try
                {
                    Ops.Convert(src, p, o, outPath, _sink);
                    FileInfo fi = new FileInfo(outPath);
                    bool ok = fi.Exists && fi.Length > 1000;
                    string detail = Util.Size(fi.Exists ? fi.Length : 0);
                    if (ok && !p.AudioOnly && p.Id != "gif")
                    {
                        MediaInfo om = MediaInfo.Probe(outPath);
                        if (om.Ok)
                        {
                            detail += ", " + om.Resolution + ", " + Util.Dur(om.Duration);
                            if (Math.Abs(om.Duration - src.Duration) > 1.5) ok = false;
                        }
                    }
                    Check("Donusturme: " + p.Name, ok, detail);
                }
                catch (Exception ex)
                {
                    Check("Donusturme: " + p.Name, false, ex.Message.Replace("\r", " ").Replace("\n", " "));
                }
            }

            // Hedef boyut gercekten tutuyor mu
            string fit = Path.Combine(dir, "d_fit-size.mp4");
            if (File.Exists(fit))
            {
                double mb = new FileInfo(fit).Length / 1048576.0;
                Check("Hedef boyut (1 MB istendi) makul cikti", mb > 0.4 && mb < 2.2, Util.Num(Math.Round(mb, 2)) + " MB");
            }

            // Cozunurluk override
            Preset high = null;
            foreach (Preset q in all) if (q.Id == "mp4-high") { high = q; break; }
            ConvertOptions o2 = new ConvertOptions();
            o2.ScaleW = 320; o2.ScaleH = 180;
            string scaled = Path.Combine(dir, "d_olcekli.mp4");
            Ops.Convert(src, high, o2, scaled, _sink);
            MediaInfo sm = MediaInfo.Probe(scaled);
            Check("Cozunurluk degistirme calisti", sm.Ok && sm.Width == 320, sm.Resolution);
        }

        // ---------------------------------------------------------------- araclar

        static void TestTools(string dir, MediaInfo src)
        {
            Console.WriteLine();
            Console.WriteLine("--- Araclar ---");

            string trim = Path.Combine(dir, "a_kirpilmis.mp4");
            Ops.Trim(src, 2, 6, true, 18, trim, _sink);
            MediaInfo tm = MediaInfo.Probe(trim);
            Check("Kayipsiz kirpma", tm.Ok, Util.Dur(tm.Duration));
            Near("Kirpma suresi", tm.Duration, 4, 1.0);

            string trim2 = Path.Combine(dir, "a_kirpilmis_tam.mp4");
            Ops.Trim(src, 2, 6, false, 30, trim2, _sink);
            MediaInfo tm2 = MediaInfo.Probe(trim2);
            Near("Yeniden kodlayarak kirpma tam sure verdi", tm2.Duration, 4, 0.25);

            string mute = Path.Combine(dir, "a_sessiz.mp4");
            Ops.RemoveAudio(src, mute, _sink);
            MediaInfo mm = MediaInfo.Probe(mute);
            Check("Ses kaldirma", mm.Ok && !mm.HasAudio, mm.AudioText);

            string rot = Path.Combine(dir, "a_dondurulmus.mp4");
            Ops.Rotate(src, 90, 30, rot, _sink);
            MediaInfo rm = MediaInfo.Probe(rot);
            Check("90 derece dondurme en/boy degistirdi",
                  rm.Ok && rm.Width == src.Height && rm.Height == src.Width,
                  src.Resolution + " -> " + rm.Resolution);

            string png = Path.Combine(dir, "a_kare.png");
            Ops.GrabFrame(src, 3, png, _sink);
            Check("Kare yakalama", File.Exists(png) && new FileInfo(png).Length > 1000,
                  File.Exists(png) ? Util.Size(new FileInfo(png).Length) : "yok");

            string info = Ops.InfoDump(src);
            Check("Bilgi dokumu okundu", info != null && info.IndexOf("codec_name") >= 0,
                  info == null ? "bos" : info.Length + " karakter");

            // Zaman ayristirma
            Near("Zaman ayristirma 00:01:30", Util.ParseTime("00:01:30"), 90, 0.001);
            Near("Zaman ayristirma 1:30", Util.ParseTime("1:30"), 90, 0.001);
            Near("Zaman ayristirma 45.5", Util.ParseTime("45.5"), 45.5, 0.001);
            Check("Gecersiz zaman reddedildi", Util.ParseTime("abc") < 0, null);

            // Arguman tirnaklama
            Check("Tirnaklama: bosluklu yol", Util.Quote("C:\\bir klasor\\a.mp4") == "\"C:\\bir klasor\\a.mp4\"", null);
            Check("Tirnaklama: bosluksuz yol degismiyor", Util.Quote("C:\\a.mp4") == "C:\\a.mp4", null);
        }

        // -------------------------------------------- veri akisli dosyalar (XAVC S)

        // Sony XAVC S dosyalarinda (AX100) zaman kodu icin ucuncu bir veri akisi
        // bulunur; bu akis hicbir kaba kopyalanamaz ve "-map 0" kullanilirsa butun
        // islem bastan basarisiz olur. Burada ayni durum "-timecode" ile uretilip
        // kayipsiz islemlerin yine de calistigi dogrulanir.
        static void TestDataStream(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("--- Veri akisli dosya (Sony zaman kodu benzeri) ---");

            string a1 = Path.Combine(dir, "tc1.mp4");
            string a2 = Path.Combine(dir, "tc2.mp4");
            foreach (string p in new string[] { a1, a2 })
            {
                List<string> g = new List<string>();
                g.Add("-hide_banner"); g.Add("-loglevel"); g.Add("error"); g.Add("-y");
                g.Add("-f"); g.Add("lavfi");
                g.Add("-i"); g.Add("testsrc=size=320x240:rate=25:duration=4");
                g.Add("-f"); g.Add("lavfi");
                g.Add("-i"); g.Add("sine=frequency=440:duration=4");
                g.Add("-c:v"); g.Add("libx264"); g.Add("-preset"); g.Add("ultrafast");
                g.Add("-g"); g.Add("25"); g.Add("-pix_fmt"); g.Add("yuv420p");
                g.Add("-c:a"); g.Add("aac");
                g.Add("-timecode"); g.Add("00:00:00:00");
                g.Add(p);
                RunResult rr = Runner.Run(FF.FFmpeg, g, 4, null, null);
                if (!rr.Ok) { Check("Veri akisli test klibi uretildi", false, rr.StdErr); return; }
            }

            // Testin gercekten bir sey denedigini dogrula: dosyada veri akisi olmali
            string dump = Ops.InfoDump(MediaInfo.Probe(a1));
            Check("Test klibinde veri akisi var", dump != null && dump.IndexOf("codec_type=data") >= 0,
                  "zaman kodu akisi");

            List<MediaInfo> two = new List<MediaInfo>();
            two.Add(MediaInfo.Probe(a1));
            two.Add(MediaInfo.Probe(a2));

            string merged = Path.Combine(dir, "tc_birlesik.mp4");
            Ops.MergeLossless(two, merged, _sink);
            MediaInfo mm = MediaInfo.Probe(merged);
            Check("Veri akisli dosyalar kayipsiz birlesti", mm.Ok, Util.Dur(mm.Duration));
            Near("Veri akisli birlestirme suresi", mm.Duration, 8, 0.5);
            Check("Veri akisli birlestirmede ses korundu", mm.HasAudio, mm.AudioText);

            List<string> parts = Ops.SplitByDuration(mm, 3, Path.Combine(dir, "tc_parca"),
                                                     "tc", ".mp4", false, 18, _sink);
            Check("Veri akisli dosya parcalanabildi", parts.Count >= 2, parts.Count + " parca");

            string trimmed = Path.Combine(dir, "tc_kirpilmis.mp4");
            Ops.Trim(mm, 1, 5, true, 18, trimmed, _sink);
            Check("Veri akisli dosya kirpilabildi", MediaInfo.Probe(trimmed).Ok, null);

            string muted = Path.Combine(dir, "tc_sessiz.mp4");
            Ops.RemoveAudio(mm, muted, _sink);
            MediaInfo mu = MediaInfo.Probe(muted);
            Check("Veri akisli dosyadan ses kaldirilabildi", mu.Ok && !mu.HasAudio, null);

            Preset remux = null;
            foreach (Preset q in Preset.All()) if (q.Id == "remux-mkv") { remux = q; break; }
            string rmx = Path.Combine(dir, "tc_remux.mkv");
            Ops.Convert(mm, remux, new ConvertOptions(), rmx, _sink);
            Check("Veri akisli dosya kap degistirebildi", MediaInfo.Probe(rmx).Ok, null);
        }

        // ------------------------------- klasordeki dosyalari birlestirip parcala

        // Cyfan.exe --merge <klasor> <parca_saniye> [cikti_dosyasi]
        // Uygulamanin kendi birlestirme kodunu kullanir.
        public static int RunMerge(string folder, double segSeconds, string outPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + App.Name + " - klasoru birlestir" +
                              (segSeconds > 0 ? " + " + Util.Dur(segSeconds) + " parcalara ayir" : "") + " ===");
            FF.Locate();
            if (!FF.Ready) { Console.WriteLine("ffmpeg yok."); return 2; }
            if (!Directory.Exists(folder)) { Console.WriteLine("Klasor yok: " + folder); return 2; }

            string[] exts = new string[] { ".mp4", ".mts", ".m2ts", ".mov", ".mkv", ".avi", ".mxf", ".ts", ".m4v", ".mpg" };
            List<string> paths = new List<string>();
            foreach (string f in Directory.GetFiles(folder))
                if (Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) paths.Add(f);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            Check("Klasorde video bulundu", paths.Count >= 2, paths.Count + " dosya");
            if (paths.Count < 2) return 1;

            List<MediaInfo> items = new List<MediaInfo>();
            double total = 0;
            long inBytes = 0;
            foreach (string p in paths)
            {
                MediaInfo m = MediaInfo.Probe(p);
                if (!m.Ok) { Check("Okunamadi: " + Path.GetFileName(p), false, m.Error); continue; }
                items.Add(m);
                total += m.Duration;
                inBytes += m.Size;
                Console.WriteLine("      " + m.Name.PadRight(24) + Util.Dur(m.Duration) + "  " +
                                  m.Resolution + "  " + Util.Num(m.Fps) + "fps  " + m.AudioText +
                                  "  [" + m.Layout + "]");
            }
            Console.WriteLine("  Toplam: " + Util.Dur(total) + ", " + Util.Size(inBytes));

            // Bozuk dosyalari ayikla (kopyalama sirasinda yarim kalmis olabilirler)
            List<MediaInfo> healthy = new List<MediaInfo>();
            int bad = 0;
            foreach (MediaInfo m in items)
            {
                string err = Ops.CheckFile(m);
                if (err == null) { healthy.Add(m); continue; }
                bad++;
                Console.WriteLine("      ATLANDI (bozuk): " + m.Name + "  -  " + err);
            }
            Check("Bozuk dosya taramasi", true, bad + " bozuk, " + healthy.Count + " saglam");
            if (healthy.Count < 2) { Check("Yeterli saglam dosya", false, null); return 1; }
            if (bad > 0)
            {
                items = healthy;
                total = 0; inBytes = 0;
                foreach (MediaInfo m in items) { total += m.Duration; inBytes += m.Size; }
                Console.WriteLine("  Saglam dosyalarla devam: " + Util.Dur(total) + ", " + Util.Size(inBytes));
            }

            // Uyumluluk (kayipsiz birlestirme icin)
            bool same = true;
            foreach (MediaInfo m in items) if (m.CompatKey() != items[0].CompatKey()) same = false;
            Check("Butun dosyalar ayni formatta (kayipsiz birlestirilebilir)", same, null);

            if (string.IsNullOrEmpty(outPath))
                outPath = Path.Combine(Path.Combine(folder, "Birlesik"), "birlesik.mp4");
            Ops.EnsureDir(Path.GetDirectoryName(outPath));
            Console.WriteLine("  Cikti: " + outPath);

            // TV'lerde calmayan ses kodegi varsa (XAVC S'te PCM) sesi AAC yap
            bool tv = false;
            foreach (MediaInfo m in items) if (Ops.AudioNeedsTvFix(m)) tv = true;
            if (tv) Console.WriteLine("  Ses " + items[0].ACodec +
                " - TV'ler icin AAC'ye cevrilecek (goruntu dokunulmaz).");

            DateTime t0 = DateTime.Now;
            List<string> made;
            try { made = Ops.MergeLossless(items, outPath, segSeconds, tv, _loud); }
            catch (Exception ex) { Check("Birlestirme", false, ex.Message.Replace("\r", " ").Replace("\n", " ")); return 1; }
            double secs = (DateTime.Now - t0).TotalSeconds;

            double sum = 0;
            long outBytes = 0;
            bool allOk = true, allAudio = true, allFast = true;
            foreach (string f in made)
            {
                MediaInfo m = MediaInfo.Probe(f);
                if (!m.Ok) allOk = false;
                if (!m.HasAudio) allAudio = false;
                if (!MoovAtStart(f)) allFast = false;
                sum += m.Duration;
                outBytes += m.Size;
                Console.WriteLine("      " + Path.GetFileName(f).PadRight(20) + Util.Size(m.Size).PadLeft(10) +
                                  "  " + Util.Dur(m.Duration) + "  " + m.AudioText);
            }

            if (tv && made.Count > 0)
            {
                MediaInfo first = MediaInfo.Probe(made[0]);
                Check("Ses TV uyumlu (AAC) oldu", first.ACodec == "aac", first.AudioText);
                Check("Goruntu hic dokunulmadi (kodek/cozunurluk ayni)",
                      first.VCodec == items[0].VCodec && first.Width == items[0].Width &&
                      first.Height == items[0].Height && Math.Abs(first.Fps - items[0].Fps) < 0.01,
                      items[0].VCodec + " " + items[0].Resolution + " " + Util.Num(items[0].Fps) + "fps  ->  " +
                      first.VCodec + " " + first.Resolution + " " + Util.Num(first.Fps) + "fps");
            }

            int expected = segSeconds > 0 ? (int)Math.Ceiling(total / segSeconds) : 1;
            Check("Parca sayisi", made.Count == expected, made.Count + " parca (beklenen " + expected + ")");
            Check("Parcalarin hepsi okunabiliyor", allOk, null);
            Check("Sesler korundu", allAudio, null);
            Check("Parcalar hizli baslangicli (moov basta)", allFast,
                  "oynaticilar toplam sureyi dogru gosterir");
            Check("Hic icerik atlanmadi", sum >= total - 0.3,
                  "kaynak " + Util.Num(Math.Round(total, 2)) + " sn, cikti " + Util.Num(Math.Round(sum, 2)) + " sn");
            Check("Sure fazladan uzamadi", sum <= total + 0.5, null);
            Console.WriteLine("  Sure: " + Util.Num(Math.Round(secs, 1)) + " sn,  cikti " + Util.Size(outBytes) +
                              " (girdi " + Util.Size(inBytes) + ")");

            Console.WriteLine();
            Console.WriteLine("=== Sonuc: " + _pass + " gecti, " + _fail + " basarisiz ===");
            Console.WriteLine();
            return _fail == 0 ? 0 : 1;
        }

        // MP4 indeksi (moov) dosyanin basinda mi? Basta ise oynaticilar toplam
        // sureyi hemen bilir; sonda ise sure gostergesi oynatma ilerledikce degisir.
        static bool MoovAtStart(string path)
        {
            try
            {
                if (Path.GetExtension(path).ToLowerInvariant() != ".mp4" &&
                    Path.GetExtension(path).ToLowerInvariant() != ".mov") return true;
                byte[] buf = new byte[4096];
                using (FileStream fs = File.OpenRead(path)) fs.Read(buf, 0, buf.Length);
                string s = System.Text.Encoding.ASCII.GetString(buf);
                int i = s.IndexOf("moov", StringComparison.Ordinal);
                return i >= 0 && i < 1024;
            }
            catch { return false; }
        }

        // ------------------------------------------- gercek dosya uzerinde test

        // Verilen gercek video dosyasinda butun islemleri uygulamanin kendi
        // koduyla calistirir.
        public static int RunOnFile(string path, string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + App.Name + " - gercek dosya testi ===");
            FF.Locate();
            if (!FF.Ready) { Console.WriteLine("ffmpeg yok."); return 2; }
            if (!File.Exists(path)) { Console.WriteLine("Dosya yok: " + path); return 2; }
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Path.GetDirectoryName(path), "cyfan_test");
            try
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
            }
            catch (Exception ex) { Console.WriteLine("Klasor hatasi: " + ex.Message); return 2; }

            MediaInfo src = MediaInfo.Probe(path);
            Check("Dosya okundu", src.Ok, src.Name);
            if (!src.Ok) return 1;
            Console.WriteLine("  Sure " + Util.Dur(src.Duration) + ", " + Util.Size(src.Size) + ", " +
                              src.Resolution + " " + Util.Num(src.Fps) + "fps " + src.VCodec +
                              ", ses " + src.AudioText + ", " + Util.Rate(src.EffectiveBitRate()));
            Console.WriteLine("  Cikti: " + outDir);

            try
            {
                // --- parcalama: sureye gore
                Console.WriteLine();
                Console.WriteLine("--- Parcalama: 10 saniyelik parcalar ---");
                string dA = Path.Combine(outDir, "sure10");
                List<string> pA = Ops.SplitByDuration(src, 10, dA, "sure", ".mp4", false, 18, _sink);
                int beklenen = (int)Math.Ceiling(src.Duration / 10.0);
                Check("Parca sayisi", pA.Count == beklenen, pA.Count + " parca (beklenen " + beklenen + ")");
                double sumA = 0; bool okA = true;
                foreach (string f in pA)
                {
                    MediaInfo m = MediaInfo.Probe(f);
                    if (!m.Ok || !m.HasAudio) okA = false;
                    sumA += m.Duration;
                    Console.WriteLine("      " + Path.GetFileName(f) + "  " + Util.Size(m.Size) +
                                      "  " + Util.Dur(m.Duration) + "  " + m.Resolution + "  ses: " + m.AudioText);
                }
                Check("Parcalarin hepsi okunabiliyor ve sesli", okA, null);
                Check("Hic icerik atlanmadi", sumA >= src.Duration - 0.2,
                      "toplam " + Util.Num(Math.Round(sumA, 2)) + " / kaynak " + Util.Num(src.Duration));

                // --- parcalama: boyuta gore
                Console.WriteLine();
                Console.WriteLine("--- Parcalama: en fazla 100 MB ---");
                long limit = 100L * 1024 * 1024;
                string dB = Path.Combine(outDir, "boyut100");
                List<string> pB = Ops.SplitBySize(src, limit, dB, "boyut", ".mp4", _sink);
                bool under = true; double sumB = 0;
                foreach (string f in pB)
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length > limit) under = false;
                    sumB += MediaInfo.Probe(f).Duration;
                    Console.WriteLine("      " + fi.Name + "  " + Util.Size(fi.Length));
                }
                Check("Boyut sinirini hicbir parca gecmedi", under, pB.Count + " parca");
                Check("Boyuta gore: hic icerik atlanmadi", sumB >= src.Duration - 0.2,
                      "toplam " + Util.Num(Math.Round(sumB, 2)) + " sn");

                // --- parcalama: esit 3 parca
                Console.WriteLine();
                Console.WriteLine("--- Parcalama: 3 esit parca ---");
                string dC = Path.Combine(outDir, "esit3");
                List<string> pC = Ops.SplitByDuration(src, src.Duration / 3.0, dC, "esit", ".mp4", false, 18, _sink);
                Check("3 parca olustu", pC.Count == 3, pC.Count + " parca");

                // --- birlestirirken parcalara ayirma (uzun cekim senaryosu)
                Console.WriteLine();
                Console.WriteLine("--- Birlestirirken 15 saniyelik parcalara yazma (dosya iki kez) ---");
                List<MediaInfo> two = new List<MediaInfo>();
                two.Add(src); two.Add(src);
                string dM = Path.Combine(outDir, "birlesik_parcali");
                Directory.CreateDirectory(dM);
                DateTime tm0 = DateTime.Now;
                List<string> pM = Ops.MergeLossless(two, Path.Combine(dM, "cekim.mp4"), 15, _sink);
                double sumM = 0; bool okM = true;
                foreach (string f in pM)
                {
                    MediaInfo m = MediaInfo.Probe(f);
                    if (!m.Ok || !m.HasAudio) okM = false;
                    sumM += m.Duration;
                    Console.WriteLine("      " + Path.GetFileName(f) + "  " + Util.Size(m.Size) +
                                      "  " + Util.Dur(m.Duration) + "  ses: " + m.AudioText);
                }
                int bekM = (int)Math.Ceiling(src.Duration * 2 / 15.0);
                Check("Parca sayisi", pM.Count == bekM, pM.Count + " parca (beklenen " + bekM + ")");
                Check("Parcalar saglam ve sesli", okM, null);
                Check("Toplam sure iki kopyaya esit", Math.Abs(sumM - src.Duration * 2) < 1.0,
                      Util.Num(Math.Round(sumM, 2)) + " / " + Util.Num(src.Duration * 2) + " sn");
                Check("Dev ara dosya olusmadi", !File.Exists(Path.Combine(dM, "cekim.mp4")),
                      Util.Num(Math.Round((DateTime.Now - tm0).TotalSeconds, 1)) + " sn surdu");

                // --- donusturme
                Console.WriteLine();
                Console.WriteLine("--- Donusturme ---");
                string[] ids = new string[] { "remux-mkv", "mp4-small", "mp3", "fit-size" };
                List<Preset> all = Preset.All();
                foreach (string id in ids)
                {
                    Preset p = null;
                    foreach (Preset q in all) if (q.Id == id) { p = q; break; }
                    ConvertOptions o = new ConvertOptions();
                    if (p.NeedsTargetSize) o.TargetMB = 50;
                    string outPath = Path.Combine(outDir, "donus_" + id + p.Ext);
                    DateTime t0 = DateTime.Now;
                    try
                    {
                        Ops.Convert(src, p, o, outPath, _sink);
                        FileInfo fi = new FileInfo(outPath);
                        string detail = Util.Size(fi.Length) + ", " +
                                        Util.Num(Math.Round((DateTime.Now - t0).TotalSeconds, 1)) + " sn surdu";
                        bool ok = fi.Exists && fi.Length > 10000;
                        if (!p.AudioOnly)
                        {
                            MediaInfo om = MediaInfo.Probe(outPath);
                            if (om.Ok) detail += ", " + om.Resolution + ", " + Util.Dur(om.Duration);
                            if (om.Ok && Math.Abs(om.Duration - src.Duration) > 1.5) ok = false;
                        }
                        Check(p.Name, ok, detail);
                    }
                    catch (Exception ex)
                    {
                        Check(p.Name, false, ex.Message.Replace("\r", " ").Replace("\n", " "));
                    }
                }
                string fit = Path.Combine(outDir, "donus_fit-size.mp4");
                if (File.Exists(fit))
                {
                    double mb = new FileInfo(fit).Length / 1048576.0;
                    Check("Hedef 50 MB tutturuldu", mb > 35 && mb < 60, Util.Num(Math.Round(mb, 1)) + " MB");
                }

                // --- araclar
                Console.WriteLine();
                Console.WriteLine("--- Araclar ---");
                string trim = Path.Combine(outDir, "arac_kirpilmis.mp4");
                Ops.Trim(src, 5, 15, true, 18, trim, _sink);
                MediaInfo tm = MediaInfo.Probe(trim);
                Check("Kayipsiz kirpma (5-15 sn)", tm.Ok, Util.Dur(tm.Duration) + ", " + Util.Size(tm.Size));

                string png = Path.Combine(outDir, "arac_kare.png");
                Ops.GrabFrame(src, Math.Min(5, src.Duration / 2), png, _sink);
                Check("Kare yakalama", File.Exists(png) && new FileInfo(png).Length > 10000,
                      File.Exists(png) ? Util.Size(new FileInfo(png).Length) : "yok");

                string mute = Path.Combine(outDir, "arac_sessiz.mp4");
                Ops.RemoveAudio(src, mute, _sink);
                MediaInfo mu = MediaInfo.Probe(mute);
                Check("Ses kaldirma", mu.Ok && !mu.HasAudio, Util.Size(mu.Size));
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine("  [HATA]  " + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine("=== Sonuc: " + _pass + " gecti, " + _fail + " basarisiz ===");
            Console.WriteLine();
            return _fail == 0 ? 0 : 1;
        }

        // Pencereyi gerceginde oldugu gibi olusturup her sekmenin denetim
        // sinirlarini yazar. "Dugme gorunmuyor" turu sorunlari tahminle degil
        // olcumle bulmak icin.
        // TabPage'ler pencere gosterilmeden gercek boyutuna kavusmaz; bu yuzden
        // pencere saydam ve ekran disinda gercekten gosterilir.
        static MainForm ShowOffscreen()
        {
            MainForm f = new MainForm();
            f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            f.Location = new System.Drawing.Point(-30000, -30000);
            f.ShowInTaskbar = false;
            f.Opacity = 0;
            f.Show();
            System.Windows.Forms.Application.DoEvents();
            f.PerformLayout();
            System.Windows.Forms.Application.DoEvents();
            return f;
        }

        public static int DumpLayout()
        {
            MainForm f = ShowOffscreen();
            Console.WriteLine();
            Console.WriteLine("Pencere: " + f.Size + "   istemci alani: " + f.ClientSize);
            foreach (System.Windows.Forms.Control c in f.Controls)
                Console.WriteLine("  " + c.GetType().Name + " dock=" + c.Dock + " " + c.Bounds);

            for (int i = 0; i < f.Tabs.TabPages.Count; i++)
            {
                f.Tabs.SelectedIndex = i;
                f.PerformLayout();
                System.Windows.Forms.Application.DoEvents();
                System.Windows.Forms.TabPage tp = f.Tabs.TabPages[i];
                System.Windows.Forms.Control page = tp.Controls[0];
                Console.WriteLine();
                Console.WriteLine("=== Sekme: " + tp.Text + " ===");
                Console.WriteLine("  TabPage istemci: " + tp.ClientSize + "   sayfa: " + page.Bounds +
                                  "   sayfa istemci: " + page.ClientSize);
                foreach (System.Windows.Forms.Control c in page.Controls)
                {
                    bool off = c.Bottom > page.ClientSize.Height || c.Right > page.ClientSize.Width;
                    string txt = c.Text == null ? "" : c.Text.Replace("\r\n", " ");
                    if (txt.Length > 30) txt = txt.Substring(0, 30);
                    Console.WriteLine("   " + (off ? "DISARIDA " : "         ") +
                                      c.GetType().Name.PadRight(12) + " " + c.Bounds +
                                      " anchor=" + c.Anchor + " gorunur=" + c.Visible +
                                      " etkin=" + c.Enabled + "  \"" + txt + "\"");
                }
            }
            f.Dispose();
            Console.WriteLine();
            return 0;
        }

        // ------------------------------------------------------------ yerlesim

        // Pencereyi olusturur (gostermeden), farkli boyutlarda yerlestirir ve
        // ebeveyninin disina tasan denetim olup olmadigini denetler.
        static void TestLayout()
        {
            Console.WriteLine();
            Console.WriteLine("--- Arayuz yerlesimi ---");
            MainForm f = null;
            try
            {
                // Pencere gercekten gosterilmeli: aksi halde TabPage'ler 200x100
                // varsayilan boyutunda kalir, butun denetimler Visible=false olur ve
                // test hicbir sey denetlemeden "gecer".
                f = ShowOffscreen();
                Check("Pencere olusturuldu", true, f.Tabs.TabPages.Count + " sekme");

                int[][] sizes = new int[][] {
                    new int[] { 1024, 811 },   // varsayilan
                    new int[] { 1024, 661 },   // en kucuk izin verilen
                    new int[] { 1500, 1000 }   // buyutulmus
                };

                foreach (int[] s in sizes)
                {
                    f.ClientSize = new System.Drawing.Size(s[0], s[1]);
                    List<string> bad = new List<string>();
                    for (int i = 0; i < f.Tabs.TabPages.Count; i++)
                    {
                        f.Tabs.SelectedIndex = i;
                        f.PerformLayout();
                        Walk(f, bad);
                    }
                    Check("Yerlesim " + s[0] + "x" + s[1], bad.Count == 0,
                          bad.Count == 0 ? "tasma yok" : string.Join(" | ", bad.ToArray()));
                }
            }
            catch (Exception ex)
            {
                Check("Pencere olusturuldu", false, ex.Message);
            }
            finally
            {
                if (f != null) try { f.Dispose(); } catch { }
            }
        }

        static void Walk(System.Windows.Forms.Control parent, List<string> bad)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                int w = parent.ClientSize.Width, h = parent.ClientSize.Height;

                // Yatay tasma her zaman sorundur. Dusey tasma, ebeveyn kayma
                // cubugu gosterebiliyorsa (sayfalar) sorun degil - kullanici
                // kaydirarak ulasir; grup kutusu icinde ise sorundur.
                System.Windows.Forms.ScrollableControl sc = parent as System.Windows.Forms.ScrollableControl;
                bool scrollable = sc != null && sc.AutoScroll;
                bool over = c.Right > w + 2 || c.Left < -2 || c.Top < -2 ||
                            (c.Bottom > h + 2 && !scrollable);
                if (over && bad.Count < 6)
                {
                    string name = c.GetType().Name;
                    string txt = c.Text == null ? "" : c.Text.Replace("\r\n", " ");
                    if (txt.Length > 22) txt = txt.Substring(0, 22);
                    bad.Add(name + "(" + txt + ") " + c.Bounds + " > " + parent.GetType().Name +
                            " " + w + "x" + h);
                }
                if (c.Controls.Count > 0) Walk(c, bad);
            }
            Overlaps(parent, bad);
        }

        // Ayni kap icindeki denetimlerin birbirinin uzerine binip binmedigini
        // denetler. Ust uste binme, saga yaslanmis denetimlerin yerlesim sirasinda
        // kirpilmasiyla olusur ve ekranda okunamayan bir arayuze yol acar.
        static void Overlaps(System.Windows.Forms.Control parent, List<string> bad)
        {
            List<System.Windows.Forms.Control> vis = new List<System.Windows.Forms.Control>();
            foreach (System.Windows.Forms.Control c in parent.Controls)
                if (c.Visible && c.Width > 0 && c.Height > 0) vis.Add(c);

            for (int i = 0; i < vis.Count; i++)
                for (int j = i + 1; j < vis.Count; j++)
                {
                    System.Drawing.Rectangle r = System.Drawing.Rectangle.Intersect(vis[i].Bounds, vis[j].Bounds);
                    if (r.Width > 2 && r.Height > 2 && bad.Count < 6)
                        bad.Add("USTUSTE " + Desc(vis[i]) + " <-> " + Desc(vis[j]) + " kesisim=" + r);
                }
        }

        static string Desc(System.Windows.Forms.Control c)
        {
            string txt = c.Text == null ? "" : c.Text.Replace("\r\n", " ");
            if (txt.Length > 18) txt = txt.Substring(0, 18);
            return c.GetType().Name + "(" + txt + ")" + c.Bounds;
        }
    }
}
