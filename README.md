# Cyfan

**Merge, split and convert video — a small ffmpeg front end for Windows.**

*Cyfan* is Welsh for *whole, complete*: it makes parts whole again, and splits the
whole into the parts you want.

One executable, about 170 KB, no installer and no runtime to install. It drives
ffmpeg, which it can download for you on first run.

[Türkçe belgeler →](README.tr.md)

---

## Why it exists

Camcorders split long recordings into chunks — a Sony AX100 starts a new file every
4 GB — and putting them back together is surprisingly awkward. tsMuxeR refuses XAVC S
MP4 files. Most GUI tools re-encode when they do not have to. And the command line
works, until the day it quietly does the wrong thing.

Cyfan is built around the cases that actually go wrong with camera footage, and it
refuses to produce a result it cannot verify.

## What it does

### Merge

Drag the clips in, check the order, press **MERGE**. The file list shows length,
resolution, fps, codecs and size, and a line underneath says whether a lossless merge
is safe:

- **green** — all clips share the same format, stream copy is safe (seconds, no quality loss)
- **orange** — something differs, use **Re-encode** (it evens out resolution, fps and
  audio settings, and generates silent audio for clips that have none)

Two options worth knowing:

- **Split while merging** — for long recordings. Writes the result directly as parts of
  the length you choose, in a single pass, so a four-hour shoot never needs a 90 GB
  intermediate file. Measured: 92 seconds of 1080p50 XAVC S into 7 parts in 1.3 seconds.
- **Make audio TV-friendly (AAC)** — see [TV audio](#tv-audio) below. Ticked
  automatically when the source audio is one televisions choke on.

### Split

Three ways, lossless by default:

| Mode | Use it for |
|---|---|
| By length | "ten minutes per part" |
| By size | "at most 4 GB per part" — with preset limits for e-mail, messaging, CD, FAT32, DVD-5, DVD-9, Blu-ray 25/50 |
| Equal parts | "cut this into three" |

The size limit is a guarantee, not an estimate: every part is measured after it is
written, and if it came out over the limit the length is reduced and the part written
again. (ffmpeg's own `-fs` option can overshoot by up to half a megabyte because of the
write buffer, so it is not trusted on its own.)

Tick **cut exactly at the given second** to re-encode and get frame-accurate cuts;
otherwise cuts snap to the nearest keyframe, which is instant and lossless.

### Convert

25 presets, batch capable:

- **Disc** — DVD-Video PAL/NTSC, SVCD, Blu-ray compliant 1080p/720p, AVCHD
- **MP4** — archive (CRF 16), high quality (CRF 18), H.265 at half the size, web/YouTube,
  phone/tablet, shrink for messaging, and **fit to a target size** (enter MB, it computes
  the bit rate and encodes in two passes)
- **Editing** — Apple ProRes 422, Avid DNxHR HQ
- **Audio** — MP3 320k, AAC 256k, WAV, or extract the audio stream untouched
- **Container** — lossless remux into MP4 / MKV / MOV / MPEG-TS, plus the TV audio fix
- **Other** — animated GIF

Plus resolution override and hardware encoding (NVENC / QSV / AMF) where available.

> The disc presets produce standard-compliant **streams**, not a burnable disc
> structure. For Blu-ray the `.m2ts` output is exactly what tsMuxeR expects.

### Tools

Trim (lossless or frame-accurate), rotate, remove audio, grab a frame as PNG, and a
full ffprobe dump of the file.

---

## Install

1. Download `Cyfan.exe` from [Releases](../../releases).
2. Run it.
3. If ffmpeg is missing, the program says so and offers to install it.

Windows 7 or newer. .NET Framework 4.x ships with Windows, so nothing else is needed.

### ffmpeg

Three ways, offered in a dialog:

| Method | What it does |
|---|---|
| **Download automatically** (recommended) | ~110 MB from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), unpacked next to the executable. No administrator rights. |
| **winget** | `winget install Gyan.FFmpeg`, installed system-wide. |
| **From a zip you already have** | For machines without internet. |

Automatic installation puts ffmpeg **next to the executable**, so copying the folder to
a USB stick carries ffmpeg along with it. If that folder is read-only, it falls back to
`%LOCALAPPDATA%\Cyfan\ffmpeg`.

Search order: executable folder → `%LOCALAPPDATA%\Cyfan\ffmpeg` → `PATH` →
winget/chocolatey locations. If you already have ffmpeg, nothing is downloaded.

### Language

English by default. **Tools → Language → Türkçe** switches the interface; the choice is
remembered.

---

## Things that bite with camera footage

These are the failure modes Cyfan was built to handle. All of them are silent by
default — ffmpeg reports a problem and still exits with status 0.

<a name="tv-audio"></a>
### PCM audio plays on a PC but not on a TV

XAVC S records **uncompressed PCM audio** (1536 kbps). Computers play it; almost no
television can play PCM inside MP4 — you get picture and no sound.

**Make audio TV-friendly** converts only the audio to AAC 320k and copies the video
stream untouched. Verified by comparing the video stream hash before and after:

```
source : MD5=41ec6773e4629c149fd8aec06672f58e
output : MD5=41ec6773e4629c149fd8aec06672f58e
```

Identical, same video bit rate. Only the audio changed; a 286 MB file becomes 279 MB.

### The hidden third stream

Sony XAVC S files carry a **data stream** for timecode alongside video and audio. It
cannot be copied into MP4 or MKV, so `-map 0` (copy every stream) fails before writing
anything. Cyfan selects streams by type instead, keeping all audio and subtitle tracks
and dropping data streams.

Worse: if *some* inputs have that stream and some do not, the concat demuxer breaks
halfway and **ffmpeg still returns 0**. In one real folder that turned 7 minutes 41
seconds of footage into 1 minute 43 seconds without a word of complaint. Cyfan
normalises mismatched inputs first, and checks the output length afterwards.

### A file that is a few bytes short

A copy interrupted partway through looks completely normal — right size, right length,
plays fine for a while. It derails the merge at the damaged point, silently.

Before merging, every input is parsed without decoding (~0.3 s per file, regardless of
size). If anything is broken the job does not start and the offending files are named.

### Parts whose duration display keeps moving

An MP4 whose index (`moov`) sits at the end of the file leaves players guessing the
total length, so the time display drifts during playback. Every output, single file or
part, is written with `+faststart`.

---

## Build from source

No SDK required — the C# compiler that ships with Windows is enough.

```powershell
.\build.ps1          # builds Cyfan.exe (and generates the icon)
.\build.ps1 -Test    # builds, then runs the test suite
```

The code targets C# 5 / .NET Framework 4.x on purpose, so it compiles with
`csc.exe` from `C:\Windows\Microsoft.NET\Framework64\v4.0.30319` on any Windows
machine from 7 onwards.

| File | Contents |
|---|---|
| `src/Core.cs` | Settings, helpers, ffmpeg discovery and installation, ffprobe |
| `src/Ops.cs` | The actual operations — UI independent, testable |
| `src/Presets.cs` | The 25 conversion presets |
| `src/MainForm.cs` | Window, job plumbing (progress/log/cancel), ffmpeg setup flow |
| `src/MergePage.cs` `SplitPage.cs` `ConvertPage.cs` `ToolsPage.cs` | The tabs |
| `src/Lang.cs` | English source strings, Turkish translation table |
| `src/Ui.cs` | Small UI helpers, page base class |
| `src/SelfTest.cs` | The test suite |

## Testing

```
Cyfan.exe --selftest [folder]        # generates clips and exercises everything
Cyfan.exe --merge <folder> <sec>     # merge a folder, optionally into parts
Cyfan.exe --testfile <video>         # run every operation on a real file
Cyfan.exe --dumplayout               # print control geometry per tab
```

`--selftest` makes its own clips and then really runs merging, splitting (all three
modes), the conversion presets and the tools, measuring the results: 66 checks.

It also builds the window off-screen and verifies the layout at three sizes — that no
control overflows its parent and no two controls overlap. Both of those checks exist
because a bug got past an earlier version of them: the window was created without being
shown, which left every control invisible, so the test passed without checking anything.

## Licence

MIT — see [LICENSE](LICENSE).

ffmpeg is not bundled. It is downloaded on request or taken from the system, and is
separate free software under its own licence: <https://ffmpeg.org>
