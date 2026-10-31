# Cyfan

> Arayüz varsayılan olarak İngilizce açılır. Türkçe için: **Tools → Language → Türkçe**.
> Bu belge Türkçe sürümüdür; İngilizcesi [README.md](README.md) dosyasındadır.

**Cyfan** Galce'de "bütün, tam" demektir. Parçaları birleştirip bütün yapar, bütünü
istediğiniz parçalara böler.

Sony AX100 ve diğer kameraların bölünmüş kliplerini birleştiren, istediğiniz süre veya
boyutta parçalayan, DVD / Blu-ray / MP4 / ses formatlarına dönüştüren bir Windows
programı. Bütün video işlerini ffmpeg yapar.

## Kurulum

1. `Cyfan.exe` dosyasını hedef bilgisayara kopyalayın. Tek dosya, kurulum gerektirmez.
2. Çift tıklayın.
3. ffmpeg kurulu değilse program sorar; **Evet** deyin, gerisini kendisi yapar.

Windows 7 ve sonrası çalışır. .NET Framework 4.x Windows ile gelir, ayrıca bir şey
kurmanız gerekmez.

### ffmpeg kurulum yöntemleri

Üç seçenek sunar (menüden **Araçlar → ffmpeg kurulumu** ile sonradan da açılır):

| Yöntem | Açıklama |
|---|---|
| **Otomatik indir ve kur** | gyan.dev'den ~110 MB iner, exe'nin yanına açılır. Yönetici yetkisi gerekmez. |
| **winget ile kur** | `winget install Gyan.FFmpeg`. Sisteme kurar. |
| **Elimdeki zip'ten kur** | İnternetsiz bilgisayar için; zip'i USB ile götürün. |

Otomatik kurulum ffmpeg'i **exe'nin yanına** koyar; klasörü USB'ye kopyalayıp başka
bilgisayarda çalıştırdığınızda ffmpeg de beraber gider. Exe'nin bulunduğu yer yazma
korumalıysa `%LOCALAPPDATA%\Cyfan\ffmpeg` klasörüne kurar.

Arama sırası: exe klasörü → `%LOCALAPPDATA%\Cyfan\ffmpeg` → PATH → winget/chocolatey.
Sisteminizde ffmpeg varsa hiçbir şey indirmez.

---

## Birleştir sekmesi

Dosyaları sürükleyip bırakın ya da **Dosya Ekle** ile seçin. Her dosyanın süresi,
çözünürlüğü, fps'i, kodekleri ve boyutu listede görünür.

**Sıra listedeki sıradır.** `İsme Göre Sırala` AX100'ün `C0001.MP4`, `C0002.MP4`
adlandırmasında doğru sonucu verir; gerekirse `Yukarı` / `Aşağı` ile düzeltin.

Listenin altındaki satır uyumluluğu söyler:

- **Yeşil** → bütün dosyalar aynı formatta, **Kayıpsız** birleştirin.
- **Turuncu** → farklılık var, **Yeniden kodla** seçeneğini kullanın.

| | Kayıpsız | Yeniden kodla |
|---|---|---|
| Hız | Saniyeler | Gerçek süreden yavaş |
| Kalite | Hiç kayıp yok | CRF 16-18'de göz ile ayırt edilemez |
| Koşul | Klipler aynı kodek/çözünürlük/fps/ses ayarında olmalı | Farklılıkları eşitler |

Aynı kayıttan bölünmüş parçalar (AX100 4 GB'ta yeni dosya açar) her zaman aynı ayardadır,
yani kayıpsız çalışır. Sessiz bir klip varsa yeniden kodlama ona sessiz ses kanalı üretir,
böylece ses kayması olmaz.

`.MTS` / AVCHD karışımı varsa çıktı olarak **.mkv** seçin — AVCHD'nin AC-3 sesi MP4
kabında sorun çıkarabilir.

### Birleştirirken parçalara ayırma

Uzun çekimler için: **"Birleştirirken parçalara ayır"** kutusunu işaretleyip bir süre
girin (`00:30:00` ya da sadece `30` = dakika). Program dosyaları birleştirip sonucu
doğrudan o sürede parçalar hâlinde yazar — `birlesik_001.mp4`, `birlesik_002.mp4` ...

Bu, "önce birleştir, sonra parçala"dan farklıdır: **tek geçişte** olur, yani 4 saatlik
bir çekimde önce 90 GB'lık ara dosya yazılmaz. Kayıpsız modda disk hızında çalışır
(ölçüm: 92 saniyelik 1080p50 XAVC S, 7 parça, 1,3 saniye).

Çıktı dosyası olarak ne yazarsanız parçalar ona göre adlandırılır; `birlesik.mp4`
yazdıysanız `birlesik_001.mp4` diye gider. Yeniden kodlama modunda da çalışır; o modda
parça sınırlarına anahtar kare zorlanır, yani kesimler tam istenen saniyede olur.

### TV'de ses gelmiyorsa

XAVC S'in sesi **PCM**'dir (sıkıştırılmamış, 1536 kbps). Bilgisayarlar bunu oynatır ama
televizyonların neredeyse hiçbiri MP4 içindeki PCM sesi desteklemez — görüntü gelir, ses
gelmez.

Çözüm: **"Sesi TV uyumlu yap (AAC)"** kutusu. Program kaynaklarda PCM (ya da DTS, FLAC,
TrueHD) sesi görürse bu kutuyu kendiliğinden işaretler ve uyarı satırında söyler.

Görüntüye **hiç dokunulmaz** — video akışı birebir kopyalanır, yalnızca ses yeniden
kodlanır. Doğrulandı: işlem öncesi ve sonrası video akışının MD5 özeti aynı çıkıyor
(`41ec6773...`), bit hızı aynı. Dosya biraz küçülür (PCM 1536 kbps → AAC 320 kbps);
286 MB'lık bir dosya 279 MB oluyor.

Aynı işi tek dosya için Dönüştür sekmesindeki **"Sesi TV uyumlu yap (video dokunulmaz)"**
önayarı yapar.

### Bozuk dosya taraması

Birleştirmeden önce her dosya, çözülmeden akış ayrıştırılarak taranır (dosya başına
~0,3 saniye, boyuttan bağımsız). Bozuk bir dosya varsa işlem hiç başlamaz ve hangi
dosyanın ne hatası olduğu söylenir.

Bu önemli: ffmpeg bozuk bir girdide demux hatası verir ama **çıkış kodu 0 döner**. Yani
tarama olmasa program "başarıyla bitti" sanıp sessizce eksik çıktı üretir. Kopyalama
sırasında yarım kalmış dosyalar (birkaç bayt eksik) bu duruma yol açar ve gözle fark
edilmez.

İşlem bittikten sonra ayrıca çıktının toplam süresi beklenenle karşılaştırılır.

### Sony XAVC S dosyaları hakkında

AX100'ün XAVC S dosyalarında videonun ve sesin yanında **üçüncü bir veri akışı** bulunur
(zaman kodu / üstveri). Bu akış hiçbir kaba kopyalanamaz; ffmpeg'e "bütün akışları kopyala"
dendiğinde (`-map 0`) işlem hiç başlamadan başarısız olur. Cyfan akışları türlerine göre
açıkça seçer: video, bütün ses akışları ve altyazılar korunur, veri akışları atılır.
Sonuç olarak birleşik dosya kaynakların toplamından biraz küçük çıkar — fark tam olarak bu
veri akışı kadardır, görüntü ve sesten hiçbir şey kaybolmaz.

XAVC S'in sesi ayrıca **PCM** (sıkıştırılmamış, 1536 kbps) olabilir; bu ses MP4 kabına
kayıpsız olarak aktarılır, yeniden kodlanmaz.

---

## Parçala sekmesi

Üç yöntem:

**Süreye göre** — her parça 10 dakika gibi. `00:10:00` ya da sadece `10` (dakika) yazın.

**Boyuta göre** — her parça en fazla 4 GB gibi. Hazır sınırlar listesinden seçebilirsiniz:
e-posta eki 25 MB, mesaj 64 MB, CD 700 MB, FAT32/SD sınırı 4 GB, DVD-5 4,7 GB,
DVD-9 8,5 GB, Blu-ray 25/50 GB.

**Eşit parça sayısı** — "3 parçaya böl".

Varsayılan olarak kayıpsız keser (yeniden kodlamaz, saniyeler sürer) ve kesme noktası en
yakın anahtar kareye yuvarlanır. Tam istenen saniyede kesmek isterseniz **"Tam istenen
saniyede kes"** kutusunu işaretleyin — bu videoyu yeniden kodlar, yavaştır.

İki davranışı bilmekte fayda var:

- **Boyut sınırı garantidir.** Program her parçayı yazdıktan sonra gerçekten ölçer; sınır
  aşılmışsa süreyi küçültüp yeniden yazar. (ffmpeg'in kendi `-fs` sınırı yazma arabelleği
  yüzünden yarım megabayta kadar aşabiliyor, bu yüzden ona güvenmiyor.)
- **Kayıpsız kesmede parçaların başında kısa bir örtüşme olur.** Her parça kendisinden
  önceki anahtar kareden başlamak zorunda olduğu için birkaç saniyeye kadar tekrar
  olabilir. Hiçbir içerik kaybolmaz — tersi (boşluk) olmaz.

---

## Dönüştür sekmesi

Birden fazla dosya seçip hepsini aynı önayarla işleyebilirsiniz. 24 önayar var:

**Disk**
- DVD-Video PAL (720x576, 25p) ve NTSC (720x480, 29.97p) — MPEG-2 + AC-3
- SVCD PAL
- Blu-ray uyumlu 1080p (20 Mbps) ve 720p (12 Mbps) — H.264 + AC-3, `.m2ts`
- AVCHD uyumlu 1080 (16 Mbps)

> Bu önayarlar **uyumlu akış** üretir, oynatılabilir disk yapısı (VIDEO_TS / BDMV) değil.
> Diske yazmak için DVDStyler, ImgBurn ya da — Blu-ray için — tsMuxeR kullanın;
> `.m2ts` çıktısı tam olarak tsMuxeR'ın beklediği girdidir.

**MP4**
- 4K/kaynak arşiv (CRF 16), yüksek kalite (CRF 18), H.265/HEVC yarım boyut (CRF 24)
- Web/YouTube 1080p, telefon/tablet 720p, e-posta için küçült (480p)
- **Hedef boyuta sığdır** — istediğiniz MB değerini girin, program bit hızını hesaplayıp
  iki geçişli kodlar. Diske veya e-posta ekine sığdırmak için.

**Kurgu** — Apple ProRes 422, Avid DNxHR HQ (ikisi de çok büyük dosya üretir)

**Ses** — MP3 320k, AAC 256k, WAV, sesi kayıpsız çıkar (.mka)

**Kap değiştir (kayıpsız)** — MP4 / MKV / MOV / MPEG-TS. Saniyeler sürer, kalite kaybı yok.

**Diğer** — animasyonlu GIF

Seçenekler: çözünürlük değiştirme, donanım hızlandırma (NVIDIA NVENC / Intel QSV / AMD AMF
varsa), sesi mümkünse yeniden kodlamadan geçirme.

---

## Araçlar sekmesi

- **Kırp** — başlangıç/bitiş zamanı; kayıpsız (hızlı, anahtar kareye yuvarlar) veya tam
- **Döndür** — 90 / 180 / 270 derece (yeniden kodlar)
- **Sesi kaldır** — kayıpsız
- **Kare yakala** — istediğiniz andaki kareyi PNG olarak kaydet
- **Dosya bilgisi** — ffprobe'un verdiği tüm teknik bilgiler

---

## Sorun giderme

**"ffmpeg kurulu değil"** — alttaki **ffmpeg KUR** düğmesi. Otomatik indirme çalışmazsa
(ağ kısıtlaması, vekil sunucu) zip'i başka bilgisayardan indirip
**Elimdeki zip'ten kur** seçeneğini kullanın.

**Dosya listeye eklenmiyor** — ffprobe dosyayı açamadı. AVCHD'yi kameradan doğrudan değil,
`PRIVATE\AVCHD\BDMV\STREAM` klasöründen kopyalamanız gerekir.

**Kayıpsız birleştirme/dönüştürme hata veriyor** — çıktıyı `.mkv` yapıp tekrar deneyin;
MKV her kodeki kabul eder. Olmazsa yeniden kodlamaya geçin.

**Birleşik dosyada ses kayıyor** — klipler arasında zaman damgası atlaması var; yeniden
kodlayarak birleştirin.

**Çıktı çok büyük** — 4K XAVC S 100 Mbps'tir, dakikada ~750 MB. Birleştirme boyutu
azaltmaz; küçültmek için Dönüştür sekmesindeki H.265 veya "hedef boyuta sığdır".

---

## Geliştirici notları

Kaynak kod `src` klasöründe, C# ile yazılmış (dil sürümü 5). Windows ile gelen
`csc.exe` ile derlenir, ayrıca bir SDK gerekmez.

```powershell
.\build.ps1          # derler (simgeyi de üretir)
.\build.ps1 -Test    # derler ve kendi kendini test eder
```

`Cyfan.exe --selftest <klasör>` arayüzü açmadan çalışır: test klipleri üretip
birleştirme, parçalama (üç yöntem), dönüştürme önayarları, araçlar ve pencere
yerleşimini gerçekten çalıştırıp ölçer. 54 denetim yapar.

Dosya düzeni:

| Dosya | İçerik |
|---|---|
| `Core.cs` | Ayarlar, yardımcılar, ffmpeg bulma/kurma, ffprobe ile medya bilgisi |
| `Ops.cs` | Asıl işlemler — arayüzden bağımsız, test edilebilir |
| `Presets.cs` | 24 dönüştürme önayarı |
| `MainForm.cs` | Ana pencere, iş altyapısı (ilerleme/kayıt/iptal), ffmpeg kurulum akışı |
| `MergePage.cs` `SplitPage.cs` `ConvertPage.cs` `ToolsPage.cs` | Sekmeler |
| `Ui.cs` | Arayüz yardımcıları, sayfa temeli |
| `SelfTest.cs` | Otomatik testler |

ffmpeg ayrı bir özgür yazılımdır (https://ffmpeg.org) ve bu programla birlikte
dağıtılmaz; istendiğinde indirilir.
