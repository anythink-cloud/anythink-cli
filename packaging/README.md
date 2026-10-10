# Packaging

Manifests for distributing the `anythink` CLI through Windows package managers.
The CLI is also available via Homebrew (macOS/Linux), `npx`, the .NET global
tool, and direct binary downloads — see the main [README](../README.md).

The Windows binaries are published on each GitHub release as
`anythink-win-x64.exe` and `anythink-win-arm64.exe` (standalone, self-contained,
no installer or runtime needed).

> Manifests here are pinned to a specific version as a working reference. When
> submitting, target the current latest release and recompute the hashes.
> Compute a hash with `Get-FileHash -Algorithm SHA256 <file>` (PowerShell) or
> `shasum -a 256 <file>` (macOS/Linux).

## winget

`packaging/winget/` holds the three-file manifest set (`InstallerType: portable`,
so `anythink` is registered as a command alias).

Submit to the community repo with [`wingetcreate`](https://github.com/microsoft/winget-create)
(recommended — it downloads the assets, computes hashes, and opens the PR):

```powershell
winget install wingetcreate
wingetcreate new   # paste the two release .exe URLs when prompted
```

For later releases:

```powershell
wingetcreate update Anythink.CLI --version <new-version> `
  --urls <x64-url> <arm64-url> --submit
```

Once merged into `microsoft/winget-pkgs`, users install with:

```powershell
winget install Anythink.CLI
```

## Scoop

`packaging/scoop/anythink.json` is a complete manifest with `checkver`/`autoupdate`,
so once it lives in a bucket it tracks new GitHub releases automatically.

The standard route for a project-owned CLI is a dedicated bucket repo (e.g.
`anythink-cloud/scoop-anythink`) with this file at `bucket/anythink.json`.
Users then install with:

```powershell
scoop bucket add anythink https://github.com/anythink-cloud/scoop-anythink
scoop install anythink
```

Adding the [Excavator action](https://github.com/ScoopInstaller/GithubActions)
to the bucket repo keeps the manifest auto-updated on each release.
