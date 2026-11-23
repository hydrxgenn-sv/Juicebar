# Security Policy

## Supported versions

Only the latest release of Juicebar receives security fixes.

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Report them privately through
GitHub: open the repository's **Security** tab and choose **Report a vulnerability**.

Include what you found, the steps to reproduce it, the affected version and system, and its
possible impact. You'll get an answer as soon as possible, and credit in the release notes if you
want it.

## What Juicebar does, and doesn't do

- Runs as a normal user and never asks for administrator rights.
- Talks to your peripherals locally over HID. Commands only **read** battery and device state; it
  never writes settings or firmware.
- Never sends your readings anywhere. The only network request is the optional update check, an
  anonymous call to the public GitHub API for this repository's latest release.
- Downloads an update only when you click **Update now**, only from this repository's release
  assets, and installs it only if its SHA-256 matches the release's `SHA256SUMS.txt`. If the check
  fails nothing is replaced, and the previous copy is kept as a backup until the next start.
- Opens only `https` links that point to this repository.
- Runs external tools (`nvidia-smi`, `headsetcontrol`, `upower`, `system_profiler`...) only from
  system folders and absolute `PATH` entries, never from its own folder or the working directory.
- On Linux, the udev rule grants access to the exact supported receivers only, not to whole
  vendors, so keyboards' raw input stays protected.

## Verifying a download

Every release includes `SHA256SUMS.txt`. Compare it with the file you downloaded:

```sh
sha256sum Juicebar-linux-x64.tar.gz                    # Linux
shasum -a 256 Juicebar-osx-arm64.zip                   # macOS
Get-FileHash Juicebar-win-x64.zip -Algorithm SHA256    # Windows (PowerShell)
```

Releases are also signed with a build-provenance attestation, which proves the package was
built by this repository's GitHub workflow. With the [GitHub CLI](https://cli.github.com/):

```sh
gh attestation verify Juicebar-win-x64.zip --repo hydrxgenn-sv/Juicebar
```
