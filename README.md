<p align="center"><img src="assets/banner.svg" alt="SAM Enhanced by leanz" width="100%"></p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-x86-38bdf8?style=for-the-badge" alt="Windows x86">
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-7c3aed?style=for-the-badge" alt=".NET Framework 4.8">
  <img src="https://img.shields.io/badge/License-zlib-22c55e?style=for-the-badge" alt="zlib license">
  <a href="https://discord.gg/JaRSAWTUMx"><img src="https://img.shields.io/badge/Discord-leanz-5865f2?style=for-the-badge" alt="leanz Discord"></a>
</p>

# SAM Enhanced

**[Quick start](#quick-start) · [Availability](#availability) · [Build](#build-from-source) · [Credits](#credits-and-license)**

<p align="center"><strong>Achievement availability at a glance. One reviewed action across multiple games.</strong><br>Credits: <strong>leanz</strong> · <a href="https://discord.gg/JaRSAWTUMx">Discord community</a></p>

Version **7.0.41.2**. An independently modified edition of [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager), based on the supplied 7.0.41 source archive. Original software by Rick / gibbed; enhanced edition prepared for **leanz**. Community: https://discord.gg/JaRSAWTUMx

![Availability preview](assets/sam-enhanced-preview.png)

### What you get

| Feature | How it works |
| --- | --- |
| **Availability at a glance** | Colored labels distinguish editable, mixed, protected and unknown states. |
| **One action, multiple games** | Unlock or relock editable achievements across checked games. |
| **Review before applying** | Read-only inspection produces the exact plan for confirmation. |
| **Protected entries skipped** | Parent permissions and trusted-server flags are respected. |
| **Per-game results** | Rejected entries and unconfirmed saves are reported separately. |
| **Manual control** | Add App IDs or Store links; open separate original editors. |

### Quick start

Download **SAMEnhanced-v7.0.41.2-windows-x86.zip** from this repository’s **Releases** section. GitHub’s automatic source-code ZIP contains source, not the runnable app.

1. Extract the entire Windows ZIP into one folder. Keep both executables and all DLLs together.
2. Open Steam and sign in.
3. Run **Start.cmd** or **SAM.Picker.exe**. Requires Windows and .NET Framework 4.8. The app is built for x86, including on 64-bit Windows.
4. Check the games you want, then choose **Check games**, **Unlock all**, or **Lock all**.
5. Unlock/lock first reads each game, then shows a review of the proposed changes. Confirming applies that plan.

**Check games** checks selected games, or the complete account list when none are selected. It never changes achievements. A live result shows achievement counts even when schema permissions are missing; those entries remain excluded from writes.

**Unlock all** and **Lock all** unlock or relock **all editable achievements** in the selected games that need that change. For individual achievements, choose **Open editor(s)**. Multiple selected games can have separate editors; close those editors before applying a bulk action to the same games.

### Availability

| State | Meaning |
| --- | --- |
| Green — Client-editable | Cached permissions or a live read show editable candidates. Steam can still reject a save. |
| Amber — Partly protected | Some entries are protected or unreadable. Only readable, unprotected entries are eligible. |
| Red — Protected / not editable | All schema entries are protected; the bulk action skips them. |
| No achievements | No achievement entries are reported. |
| Achievements loaded / permissions unknown | Live achievements were read, but permission data is missing. Counts are shown; these entries are not changed. |
| Unknown / unavailable | Missing cache, failed read, or timeout. This does **not** mean the game is protected. Retry **Check games**. |

The initial list maps **only the active account’s valid local license packages** to games. Global artwork and other accounts’ folders are not treated as ownership. Cancelled, expired, pending, and unactivated licenses and the global free-to-play package are excluded. Metadata supplies names, not ownership. Account changes require refreshing the list. Cached achievement schemas are inspected without changing achievements. A fresh stats read occurs before every bulk preview and again before applying the approved IDs. An incomplete or stale license/package cache may omit titles; open Steam’s Library and refresh. Manual App IDs / Store links remain available. Missing account caches produce an explicit error instead of an inflated global list. A usable Steam license is required. This edition does not bypass ownership or server restrictions.

### Bulk behavior

- Up to two isolated game workers operate simultaneously; additional games are queued.
- The game and Steam account must still match the reviewed plan.
- Parent/container permission flags and trusted-server flags are respected.
- Already matching entries are skipped. One rejected game/entry does not stop other games.
- Statistics are not reset or changed by the bulk actions.
- A successful save requires Steam's asynchronous save response and a matching readback.
- The **Last result** column reports each game's outcome. An unconfirmed save may still be retried by Steam; do not assume no changes happened.
- **Cancel queued** stops pending games. Active saves finish; read-only checks can be stopped. Closing during a batch waits for active work.

### Build from source

GitHub Actions also builds and tests on Windows, then attaches a runnable ZIP and checksums to the workflow run. It does not publish a release automatically.

Install a .NET SDK supporting this SDK-style net48 project, then run on Windows:

```powershell
.\Build.ps1
```

Use `./Build.ps1 -Package` to also create a release ZIP and checksum file.

The script builds both apps, runs synthetic regression tests, and copies a runnable bundle to `dist/windows-x86`. Dependencies are restored from NuGet. No Steam credentials or API key are required for the build or synthetic tests.

The tests cover protection inheritance, unknown schemas, account/game mismatch, approved-ID scope, unlock/lock, partial failures, unconfirmed saves, and XML request/result handling. They use a fake store and do not change real account achievements.

Optional UI preview with synthetic data:

```powershell
.\dist\windows-x86\SAM.Picker.exe --preview
```

An explicitly invoked developer `--live-probe` test reads stats from an owned sample game without invoking writes. Real bulk saves were not tested against an account during preparation.

### Publish on GitHub

See [the upload guide](docs/GITHUB-UPLOAD-DE.md) for the exact files and steps. Release text is ready in [RELEASE_NOTES.md](RELEASE_NOTES.md).

Upload this source folder's contents to your repository. Include the original `LICENSE.txt`, `README.UPSTREAM.md`, and `NOTICE.txt`. Put the Windows ZIP in **Releases**, rather than committing build outputs. Do not upload account data, Steam caches, logs, temporary requests, `bin`, or `obj`.

Suggested repository description: **Steam Achievement Manager enhanced with achievement availability indicators, reviewed bulk unlock/lock actions, and per-game results.**

### Credits and license

Original SAM: **Rick / gibbed**. This is an altered, unofficial edition, not the upstream release. Enhanced edition credits: **leanz** — https://discord.gg/JaRSAWTUMx

Distributed under the included zlib license. Original copyright and source notices are retained.
