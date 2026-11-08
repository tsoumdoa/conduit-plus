# Conduit+ agent notes

Grasshopper/Rhino 8 plugin (.NET 8, Windows, RhinoCommon/Grasshopper 8.20 NuGet). See `README.md` for features and `docs/` for plans and designs.

## Build and test
```powershell
dotnet build ConduitPlus.sln -c Debug   # warnings are errors
dotnet test ConduitPlus.sln -c Debug
```
There is no cloud CI. The local pre-commit hook (`.githooks/pre-commit`, enable with `git config core.hooksPath .githooks`) builds and tests every commit that touches code. Don't bypass it.

## After every implementation: give the user a Yak package to try
Yak is the only way Conduit+ gets installed, including during development. Don't copy build output into Grasshopper's Libraries folder; a second copy clashes with the Yak install.

When you finish implementing something (a feature, a fix, a review change), **package it so the user can try it in Rhino straight away**, then give them the `.yak` path and say what to try:

```powershell
./tools/Pack-Yak.ps1 -Version 0.2.0-beta.N   # next unused prerelease number; bumps, commits, builds, tests, packages
```

- Run it from a clean checkout that contains the work the user should test. When several PRs are open and stacked, that's the local `integration/mvp` branch, which combines all of them; update it first.
- The user drops the `.yak` onto Rhino and restarts it. Never kill Rhino.
- Packaging is local. Publishing is separate: only run `tools/Publish-Yak.ps1 -Server Public` when the user asks for that version, because public versions can't be overwritten.
- Delegated/sub-agents working in their own worktree don't package; the agent that reviews and commits the work does.

## Workflow
- Implementation by Sol (Codex `gpt-6.1-sol`); design reviews by Astra (Codex `gpt-6-astra`); Claude reviews diffs, commits and opens PRs.
- Small feature PRs, squash-merged. Stacked PRs are merged in order and the next one is retargeted to `main`.
- Keep `ConduitPlus.Core` free of Rhino types; pure logic goes there with xUnit tests. Tests can't load RhinoCommon at runtime.
- Component and plug-in GUIDs are stable; never change them. Add new GH inputs after existing ones so saved `.gh` files keep loading.
