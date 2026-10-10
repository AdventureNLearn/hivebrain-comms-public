# Testers

HiveBrainComms is a local command center. It listens on 127.0.0.1 only.

## Requirements

- Windows
- WebView2 runtime
- .NET 8 SDK
- Python 3
- Grok CLI

## Install

Clone the repo and open the folder. From that folder, run `Open-HiveBrainComms-Shell.cmd`. The launcher starts the hub if it is down, then opens the app. Paths inside the launchers are relative to the folder they sit in.

The hub page is http://127.0.0.1:8107/. The app window is HiveBrainComms. Closing the console that started the launcher does not close the app.

The .NET 8 SDK is required because the app runs through the `dotnet` host. Start All turns the hub on when it is down and focuses an existing Build session instead of opening a second one. If the hub is down, or a tab fails to load, the app says so on its status line.

## Build sessions (plain shell by default)

**+ Build session** opens a plain command shell. It does not start grok. Each pane has **Start grok**. That button asks first: it will run an agent with your user permissions. The default answer is No. If you confirm, grok starts in that pane.

The Build tab does not watch the bus unless you opt in. Set `HBC_BUILD_WATCH=1` in the environment, restart the hub, then confirm **Start grok** on the first session. The watcher then polls the project's `bus/` folder (`LEAD-TO-BUILD.md` and `GROKBOT-TO-BUILD.md`) from the project root. Leave the variable unset for a plain shell. The routing view in the app also reads that same `bus/` folder, not a home-directory handoff path.

Child shells do not inherit the parent environment. They get `GROK_HOME`, `PATH`, and the usual Windows path, drive, and identity variables when present (the full list is in the README Risks section). API keys are not passed. If grok needs `GROK_API_KEY` or `XAI_API_KEY`, set `HBC_PASS_GROK_AUTH=1` before you start the hub. That opt-in is off by default.

`tools/Sign-BusJob.ps1` exits 3 if the signature is bad or missing, and also if the signing key file is missing.

## Sign-in

Each tester signs in with their own accounts. None are shared. The app does not see the password.
