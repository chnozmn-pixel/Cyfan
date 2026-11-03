# Contributing

Thanks for looking. Cyfan is small and deliberately stays that way, so the
most useful contributions are usually bug reports with a real file behind them.

## Reporting a bug

Open an issue and include:

- what you did, what you expected, what happened instead
- the Cyfan version and your ffmpeg version
- **the log**. Every job prints the exact ffmpeg command line and its output in
  the log pane. Paste it. Almost every problem is visible there.
- the camera or source, if it is about particular footage

For a security problem, see [SECURITY.md](SECURITY.md) instead.

## Suggesting a feature

Describe the task you are trying to finish, not only the control you want
added. Cyfan has four tabs on purpose; a new option has to earn its place.

## Building

No SDK required — the C# compiler that ships with Windows is enough.

```powershell
.\build.ps1          # builds Cyfan.exe
.\build.ps1 -Test    # builds, then runs the test suite
```

The code targets **C# 5 / .NET Framework 4.x** deliberately, so it compiles
with `csc.exe` from `C:\Windows\Microsoft.NET\Framework64\v4.0.30319` on any
Windows from 7 onwards, with nothing installed. That means **no string
interpolation, no `?.`, no `nameof`, no `async`**. If the build complains about
a language feature, that is why.

## Tests

```
Cyfan.exe --selftest [folder]
```

It generates its own clips and then really merges, splits, converts and trims
them, measuring every result: 66 checks. It also builds the window off-screen
and verifies at three sizes that no control overflows its parent and no two
controls overlap.

**A pull request has to leave the suite passing**, and GitHub Actions runs it
on a clean Windows machine for every push.

If you fix a bug, add a check that fails without your fix. This matters more
than usual here: several bugs in this program were invisible because ffmpeg
reported a problem and still exited with status 0. A test that only checks the
exit code proves nothing — measure the output.

## Code style

Match the file you are editing. Beyond that:

- Operations go in `src/Ops.cs` and must work without a window, so the test
  suite can call them.
- UI strings go through `L.T("English text")`, and the Turkish translation is
  added to the table in `src/Lang.cs`. Keep the Turkish ASCII-only.
- Comment the *why*, especially where ffmpeg behaves unexpectedly. The existing
  comments about `-map 0`, `-fs` overshoot and `+faststart` are there because
  the code looks wrong without them.

## Pull requests

One change per pull request, with the reasoning in the description. Say how you
tested it, and on what footage.
