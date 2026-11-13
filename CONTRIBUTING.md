# Contributing

Thanks for wanting to help! GamingStack is a small, friendly project. Ideas, bug reports, new app
suggestions and code are all welcome.

## The rules that matter

1. **Disclosed and reversible.** Anything that changes a Windows setting must be shown on screen
   first (with the real current value), need an explicit yes, and be undoable (the app records the
   old value in the undo script). Ideas that can't do that are out of scope.
2. **One process, no helper scripts.** The app is a single self-contained exe. Please don't
   reintroduce a launcher + script split (that design failed once - see `CLAUDE.md`).
3. **Original art only.** No Microsoft logos/boot animations or other people's assets.
4. **Everything is logged.** New install steps should write to the run log.

## Adding an app

Apps live in the `Catalog` in `InstallerEngine.cs`. Each entry says which setup types (`Gm`, `Of`,
`Dv`) offer it. Please check the winget ID with `winget show --id <ID> --exact` before adding it. If
there is no winget package, use a `Link` entry (opens the vendor's https page). Then run
`python tools/gen-catalog.py` to refresh `docs/CATALOG.md`.

## Building

```bash
dotnet build          # must finish with 0 errors and 0 warnings
```

See the README for publishing. Keep the changelog and the version in `GamingStackGUI.csproj` in step
(a new feature bumps the minor version, a fix bumps the patch).

## Code tips

- Fonts: use `Ui.Font(points)`, never `new Font("Consolas", ...)` (see `Ui.cs` for why).
- Desktop/shell code works in virtual pixels (`VW`/`VH`), not `Width`/`Height`.
- `CLAUDE.md` has the architecture notes and project history.
