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

## Build auto-watch (off by default)

The Build tab does not watch the bus unless you opt in. Set `HBC_BUILD_WATCH=1` in the environment, then start or restart the hub. The watcher then polls the project's `bus/` folder (`LEAD-TO-BUILD.md` and `GROKBOT-TO-BUILD.md`). Leave the variable unset for a plain shell. The routing view in the app also reads that same `bus/` folder, not a home-directory handoff path.

## Sign-in

Each tester signs in with their own accounts. None are shared. The app does not see the password.
