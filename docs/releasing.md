# Releasing Conduit+ with Yak

This is a local Windows release pipeline. Like HopperGroup, one command bumps,
builds, tests and packages. Like Panel Plus, `Directory.Build.props` is the single
version source and the payload is staged from an explicit list. It does not need
cloud CI or a JavaScript toolchain. Packaging never publishes.

## Quick start

From PowerShell 7 on a clean `main`:

```powershell
./tools/Pack-Yak.ps1 -Version 0.2.0-beta.1   # bump + commit "Release 0.2.0-beta.1", build, test, package
# install artifacts/yak/conduit-plus-0.2.0-beta.1-rh8_20-win.yak in Rhino and try it (below)
./tools/Publish-Yak.ps1 -Server Test         # optional: Yak test server
./tools/Publish-Yak.ps1 -Server Public       # owner's go-ahead only: tag, Yak, GitHub release
```

## Prepare a candidate

1. Merge the intended changes to `main`. For a trial of stacked work, use the
   combined integration checkout; publish from reviewed, committed source.
2. Update release notes/known limitations and commit them.
3. Run `./tools/Pack-Yak.ps1 -Version X.Y.Z[-pre]`. It refuses a dirty tree, a
   version that is not greater than the current one, or an existing `vX.Y.Z` tag.
   It then sets `<Version>` in `Directory.Build.props` and commits only that file
   as `Release X.Y.Z` (the pre-commit hook builds and tests it), then packages.
   Without `-Version`, it packages the version already in `Directory.Build.props`.
   `packaging/manifest.yml` is a template; leave `@VERSION@` there. Versions
   use SemVer, optionally with a prerelease suffix, without build metadata.

Packaging requires a clean Git working tree. For an uncommitted local trial,
use `-AllowDirty`; its validation record explicitly marks that candidate dirty.
Such a candidate is for trying locally, not publishing, and goes into
`artifacts/yak/trials/` so it cannot replace a clean candidate in `artifacts/yak/`.

The script runs the Release solution build (warnings are errors) and tests,
checks all three assemblies' product versions, replaces only the generated
`artifacts/yak/stage` directory, and stages an explicit list of seven files.
Yak builds for **Windows / Rhino 8.20+**. The archive validator checks the exact
inventory, manifest, distribution filename and all six payload hashes against staging.
Yak normalizes the manifest during build, so metadata is checked separately.
Host DLLs, PDBs, build folders and unrelated dependencies cannot silently ship.
The two stable assembly GUIDs are included as package-restore keywords.

Outputs in `artifacts/yak/`:

- `conduit-plus-VERSION-rh8_20-win.yak`: the candidate to test and publish.
- The same filename with `.sha256`: SHA-256 checksum.
- The same filename with `.json`: commit, dirty flag, runtime/platform,
  build/test/archive results, payload hashes and pending manual-validation status.

Rebuilding the same version replaces that version's candidate and sidecars;
other versions stay intact. Keep a copy of an approved candidate before rebuilding.
Logs and checksums stay outside the Yak payload.

Use `-YakPath 'D:\Rhino 8\System\Yak.exe'` or `YAK_PATH` for a custom Rhino
installation. `-StageOnly` builds, tests and stages without requiring Rhino/Yak;
it does not claim archive validation. To check an existing package again:

```powershell
./tools/Test-YakPackage.ps1 -Package './artifacts/yak/conduit-plus-0.1.0-rh8_20-win.yak' -Version 0.1.0
```

Supply `-SourceDirectory ./artifacts/yak/stage` to also compare the six payload
files to that candidate's staging contents. Without it, the command checks
structure/metadata only, not payload provenance. Verify the sidecar SHA-256 when
moving or downloading a candidate.

Exercise the archive gate against the real archive and nine intentionally
damaged copies (missing/extra/changed/empty payload, wrong metadata and wrong tag):

```powershell
./tools/tests/Test-YakPipeline.ps1 -Package './artifacts/yak/conduit-plus-0.1.0-rh8_20-win.yak'
```

## Try the exact archive

Close Rhino. Avoid duplicate developer and Yak installations: move the three
ConduitPlus files out of `%APPDATA%\Grasshopper\Libraries\ConduitPlus` if a
developer install is present. Drop the candidate onto Rhino, accept installation,
restart Rhino and select .NET 8 with `SetDotNetRuntime` if needed.

Record Rhino version/runtime and the candidate checksum with these results:

- `ConduitPlusDemo` and `examples/structural-dashboard.gh`: every card loads.
- Hover/click a card and linked member; shared highlighting and Clear work.
- Scroll/sort the table; drag a card; save/reopen and Undo/Redo the layout.
- PNG capture at 1×, 2× and 4×; restore normal viewport drawing afterwards.
- Experimental PDF output and mixed-monitor DPI if relevant to the release.

Update a release validation record with passed, failed and untested checks.
Existing output caveats are in [capture/PDF verification](verification/capture-pdf.md).
A successful package build does not establish interactive Rhino behavior.

## Publish the tested bytes

Only publish when the owner explicitly requests it. Do not rebuild between
candidate approval and upload. Yak login uses Rhino Accounts; no token belongs
in this repository. Sign in once with `& 'C:\Program Files\Rhino 8\System\Yak.exe' login`.

```powershell
./tools/Publish-Yak.ps1 -Server Test     # Yak test server; cleared nightly
./tools/Publish-Yak.ps1 -Server Public   # asks for confirmation; -WhatIf shows the steps only
```

`Publish-Yak.ps1` publishes the candidate for the version in `Directory.Build.props`
(or `-Version`). It never rebuilds. It first checks that the validation record is
clean, the checksum matches, `HEAD` is the candidate's source commit and the archive
passes `Test-YakPackage.ps1`. `-Server Public` must run on `main`. It then, in order:

1. tags the source commit `vVERSION` (an existing tag must point at that commit);
2. pushes `main` and the tag to GitHub;
3. pushes the `.yak` to the public Yak server and searches for it;
4. creates a GitHub release (prerelease for `-` versions) with the `.yak`, `.sha256`
   and `.json` attached, then downloads the asset again and compares its SHA-256.

Each step stops the script on failure. Public Yak versions cannot be overwritten;
correct a published problem with a new version. Yak and GitHub are separate steps:
a failure in one does not undo the other, so rerun or finish the remaining steps
by hand (the tag is reused when it already points at the candidate commit).
Following McNeel's official [publication guide](https://developer.rhino3d.com/en/guides/yak/pushing-a-package-to-the-server/).

Distribution and metadata references: [McNeel package creation](https://developer.rhino3d.com/en/guides/yak/creating-a-rhino-plugin-package/),
[manifest reference](https://developer.rhino3d.com/en/guides/yak/the-package-manifest/).
