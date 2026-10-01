# Contributing

Thanks for helping! Keep it small and kind.

- **Branches:** work on `feat/<topic>` and open a PR to `main`. CI must pass (`dotnet build` + `dotnet test`).
- **Win32 interop** lives only in `windows/src/KawaiiIsland/Services/Native/`, in small documented wrappers.
- **Mascot art:** edit `design/mascot/mascot.js`, then run `node design/mascot/gen.mjs`. Never hand-edit `Mascots.xaml` or the SVGs.
- **Privacy is a feature:** no telemetry, no cloud calls, no new network endpoints. Data stays in local app-data folders.
- **Never commit secrets:** no passwords, tokens, signing certificates or personal config. Only `config.sample.json` with placeholders.
- **Dependencies:** open an issue before adding a new package.
- **Art:** only original assets. No third-party characters, logos or copyrighted images.
