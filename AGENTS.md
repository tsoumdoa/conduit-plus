# Conduit+ agent notes

Grasshopper/Rhino 8 plugin (.NET 8, Windows, RhinoCommon/Grasshopper 8.20 NuGet). See `README.md` for features and `docs/` for plans and designs.

## Build and test
```powershell
dotnet build ConduitPlus.sln -c Debug   # warnings are errors
dotnet test ConduitPlus.sln -c Debug
```
There is no cloud CI. The local pre-commit hook (`.githooks/pre-commit`, enable with `git config core.hooksPath .githooks`) builds and tests every commit that touches code. Don't bypass it.

## After every implementation: install it for the user to try
When you finish implementing something (a feature, a fix, a review change), **build and install it so the user can try it in Rhino straight away**, then tell them it's installed and what to try:

```powershell
dotnet build ConduitPlus.sln -c Debug; ./tools/Install-Dev.ps1
```

- Run it from the checkout that contains the work the user should test. When several PRs are open and stacked, that's the local `integration/mvp` branch, which combines all of them; update it first.
- `Install-Dev.ps1` refuses to install while Rhino is running. If so, tell the user to close Rhino and offer to run it again; never kill Rhino.
- Delegated/sub-agents working in their own worktree don't install; the agent that reviews and commits the work does.

## Workflow
- Implementation by Sol (Codex `gpt-6.1-sol`); design reviews by Astra (Codex `gpt-6-astra`); Claude reviews diffs, commits and opens PRs.
- Small feature PRs, squash-merged. Stacked PRs are merged in order and the next one is retargeted to `main`.
- Keep `ConduitPlus.Core` free of Rhino types; pure logic goes there with xUnit tests. Tests can't load RhinoCommon at runtime.
- Component and plug-in GUIDs are stable; never change them. Add new GH inputs after existing ones so saved `.gh` files keep loading.
