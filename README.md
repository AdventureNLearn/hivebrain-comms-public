# HiveBrainComms

## Risks / read first

- The Build tab is a full shell. **+ Build session** and the first session open a plain command shell. Starting grok takes a separate **Start grok** click, then a confirm that says it will run an agent with your user permissions. The default answer is **No**.
- Auto-watch is **off by default**. If you set `HBC_BUILD_WATCH=1` and then confirm **Start grok** on the first session, that session watches the project's `bus/` folder (not `~/.grok/handoff`). The working directory defaults to the project root so `bus/` is visible.
- Child processes get a short env allowlist only: `GROK_HOME`, `PATH`, and on Windows `SYSTEMROOT`, `WINDIR`, `COMSPEC`, `TEMP`, `TMP`, `USERPROFILE`, `LOCALAPPDATA`, `APPDATA`, `PATHEXT`, `SYSTEMDRIVE`, `PROGRAMFILES`, `PROGRAMFILES(X86)`, `PROGRAMDATA`, `HOMEDRIVE`, `HOMEPATH`, `USERNAME`, `COMPUTERNAME`, `PROCESSOR_ARCHITECTURE`, `NUMBER_OF_PROCESSORS`, and `PSModulePath` when present. API keys are not forwarded. If grok needs `GROK_API_KEY` or `XAI_API_KEY`, set `HBC_PASS_GROK_AUTH=1` before starting the hub; that opt-in is required and is off by default.
- The hub session token is stored in plain text in `.session-token` and in the `build.html` URL. Any program running as the same Windows user can read it, type into the shells, or start grok without the confirm dialog. Run it only on an account you trust, or in a VM.
- `tools/Sign-BusJob.ps1` always exits 3 on a bad or missing signature, and also exits 3 if the signing key file is missing. The hub and the Build tab do not enforce signatures on their own in this version, so only use this in a VM or a throwaway test account.
- Failed signature checks are logged to `%TEMP%\bus-sig.log`.
- The launchers run PowerShell with `-ExecutionPolicy Bypass`.

**One window to run your AI team.**

**For testers:** see [TESTERS.md](TESTERS.md) for install steps, requirements, and signing in with your own accounts.

## What it is

HiveBrainComms is a free, open-source (MIT) Windows app that brings your AI agents and tools into one window on your own PC. SuperGrok, X, and Imagine sit in tabs that keep your sign-ins, your Grok build terminal runs inside the app (up to 8 sessions side by side), and a routing view shows where each job went and when.

## The idea: hub-first routing

Many multi-agent setups get messy because every agent talks to every other one and work gets lost. Here, every request goes to one hub first. The hub decides who owns it and passes it on. A job that reaches the wrong agent is sent back to the hub instead of being run quietly.

## How it works

- **Plain-text jobs.** Each job is a readable block in a text file that says who sent it, who it's for, when, and what "done" means. You can read every job and every return.
- **Signed jobs.** `tools/Sign-BusJob.ps1` can attach or check an HMAC-SHA256 `sig` line using a key that only the current Windows account can read. Verify mode always exits 3 if the signature is bad or missing. This version of the hub does not check that `sig` line, and the Build session is not a signature enforcer, so a faked or edited job is not automatically flagged while the app runs.
- **Routed only.** The build agent is instructed to run only jobs the hub has routed, and it writes back pickup and finish times.
- **Start All.** One button turns on the hub if it's down, opens the build session, and opens Grok Bot minimized. A second click focuses what's already open instead of opening copies.

## What it does for you

- **Less window chaos.** Your tools and terminals live in one place.
- **You can see everything.** What was asked, who's on it, and what came back are all in readable files and the routing view.
- **You stay in control.** The agents follow a rule that nothing is sent, posted, pushed, deleted, or made public without your yes, and lab jobs in the hub need your Approve.
- **Work doesn't vanish.** Skipped or misrouted jobs get flagged and rerouted instead of dropped.
- **No API bill.** It runs on the subscriptions you already have (SuperGrok and the Grok CLI), not pay-per-call API keys.

## Security

- The hub listens only on 127.0.0.1, and its API needs a random per-launch token.
- Signing keys are encrypted with Windows DPAPI and locked to your Windows account. They are never written to the bus, the logs, or git.
- Each web tab has its own separate profile. The app never injects scripts, reads page content, or types into web tabs. Only you click and type there.
- Before release it was tested for routing, wrong-owner jobs, spoofed and bad-signature jobs, blocked actions, and leak scans of the repo.

## Who it's for

People running several AI agents or terminals who want them coordinated and easy to watch, without giving up control.

## Status

A living project. It's a work in progress shaped by testers. Setup is in [TESTERS.md](TESTERS.md), and feedback goes in GitHub Issues.

## Run

`Open-HiveBrainComms-Shell.cmd` starts the hub if it's down, then opens the app. The app runs through the .NET 8 host (`dotnet Shell.dll`), which also works on PCs where Smart App Control blocks unsigned programs. The hub page is http://127.0.0.1:8107/.

The app has tabs for SuperGrok, Imagine, X, Freebots, Build, and Hub. The Build tab holds up to 8 sessions in an automatic grid. The first session and **+ Build session** open a plain shell. Extra sessions are added only with the button in that tab. **Start grok** on a pane asks first; the default answer is No. To opt in to auto-watch after that confirm, set `HBC_BUILD_WATCH=1` in the environment, then start or restart the hub. The watcher then polls `LEAD-TO-BUILD.md` and `GROKBOT-TO-BUILD.md` in the project's `bus/` folder. Set `HBC_BUILD_CWD` only if you need a different working directory.

## Bus

The hub's lab bus uses files in `bus/` (this is separate from any live bus you run). Each sender writes its own file, and results go to the matching return file. A job block needs `id`, `from`, `to`, `lane`, `priority`, `time`, `job`, and `done-when`, plus a `routed` line and a `sig` line on a signed bus. The `from` line must match the file owner. A malformed block is refused and not queued. `tools/Sign-BusJob.ps1` signs and verifies jobs.

## Web panels

SuperGrok, X, Imagine, and Freebots are plain browser views. The app doesn't inject scripts, read page content, or strip framing headers, and the queue and build agent never click or type in those panels.

## Off by default

The app doesn't read, relay, or show Freebots or HiveBrain game chat. The Freebots tab is only a plain view of the site.

## Limits

- Windows only.
- It needs the Grok CLI, SuperGrok, and the Grok Bot desktop app. Grok Bot can't be embedded, so it opens beside the app, minimized.
- Some safeguards are rules the agents follow rather than hard locks, such as "routed jobs only" and "no send without a yes". A rule can be missed, but a lock can't.
- The hub does not enforce HMAC signatures. `tools/Sign-BusJob.ps1` fails closed (exit 3) on a bad or missing signature, and on a missing signing key.

## Does the routing help?

Testers: does this routing method help you? Say so in a GitHub Issue. A yes, a no, and a short note about where a job got stuck are all useful.

## How to give feedback

Open a GitHub Issue. Name the job id if you have one, say what you expected, and say what happened.

## License

MIT. See `LICENSE`. Copyright (c) 2026 AdventureNLearn.

## Credits

HiveBrainComms was built by AdventureNLearn, inspired by GodTerm by Daniel Farina (MIT, github.com/daniel-farina/godterm). No upstream file is copied here, and this is not an endorsement.

If you use or build on this hub-first routing method, a small credit line like "Routing method from HiveBrainComms by AdventureNLearn" is appreciated but not required.
