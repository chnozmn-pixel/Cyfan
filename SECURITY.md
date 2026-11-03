# Security Policy

## Supported versions

Only the latest release is supported. Fixes go into a new release; older
builds are not patched.

| Version | Supported |
|---|---|
| 1.0.x | yes |
| older | no |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Use GitHub's private reporting instead:
**[Report a vulnerability](https://github.com/chnozmn-pixel/Cyfan/security/advisories/new)**
(repository → Security → Report a vulnerability). Only the maintainers see it.

Useful details:

- what the problem is and what an attacker could do with it
- the steps to reproduce it, and the Cyfan version (Help → About)
- your ffmpeg version, if it is involved

You can expect an acknowledgement within a week. If the report is confirmed,
a fix is released and you are credited in the release notes unless you prefer
otherwise.

## What is in scope

Cyfan is a desktop program with no server component and no network service. The
realistic risks are:

- **Command injection through file names.** Cyfan builds ffmpeg command lines
  from paths you choose. Arguments are passed as an array, never through a
  shell, but a case that escapes that is a genuine bug — report it.
- **The ffmpeg download.** The automatic installer fetches a build over HTTPS
  from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/). A way to make it fetch
  or run something else is in scope.
- **Where files are written.** Output paths that escape the folder you picked.

## What is not in scope

- Vulnerabilities in ffmpeg itself — report those to
  [the ffmpeg project](https://ffmpeg.org/security.html). Cyfan does not bundle
  ffmpeg; it uses the copy on your machine.
- SmartScreen warnings on the downloaded executable. The release is unsigned;
  verify it with the SHA-256 published in the release notes.
