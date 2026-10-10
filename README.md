# HiveBrainComms

HiveBrainComms is a living project. It is a work in progress, and it changes as testers say what helps and what does not.

It is a local command center. Loopback only.

## Hub-first job bus

The method is the point. Agents are subscription-based. There is no API billing.

The bus is plain text. You can read every job and every return. Jobs are signed with HMAC. Each job sits in a lane, and the lane says who owns it.

If a job gets skipped, the agent returns it to the router. The router sends it on, or holds it, so work does not vanish.

Nothing runs without a person. Approve starts a job. Deny is logged the same way.

## Run

`Open-HiveBrainComms-Shell.cmd` starts the hub if it is down, then opens the app. The hub page is http://127.0.0.1:8107/. The app is started with the .NET 8 host.

The app has the hub page, a Build tab, and a read-only routing view. The Build tab holds up to 8 sessions. The first session is the bus watcher. Extra sessions are added only from the button in that tab. Start All turns the hub on if it is down, and if a Build session is already running it focuses that session instead of opening another.

The page also shows two lab panes, the lab bus, an approvals queue, and a read-only session list.

## Bus

Jobs are files in `bus/`. Lead writes `LEAD-TO-BUILD.md`. Grok Bot writes `GROKBOT-TO-BUILD.md`. Results go to the matching return file. This is not the live handoff bus.

A block needs `id`, `from`, `to`, `lane`, `priority`, `job`, and `done-when`. `pane` is `1` or `2`. The `from` line must match the file owner. A bad block is refused and not queued.

## Does the routing help?

Testers: does this routing method help you? Say so in a GitHub Issue. A yes, a no, and a short note about where a job got stuck are all useful.

## How to give feedback

Open a GitHub Issue. Name the job id if you have one, say what you expected, and say what happened.

## Web panels

SuperGrok, X, and Imagine are plain browser views. The shell does not inject scripts, read page content, or strip framing headers. The queue and Build never click or type in those panels. Only the person does.

## Off by default

Freebots is off. HiveBrain game chat is off. This app does not read, relay, or show either one.

## Limits

- Windows only.
- It needs the Grok CLI, SuperGrok, and Grok Bot.
- Some safeguards are rules rather than hard locks. A rule can be missed. A lock cannot.

## License

MIT. See `LICENSE`.

## Credits

Inspired by GodTerm by Daniel Farina, MIT (github.com/daniel-farina/godterm). No upstream file is copied here. This is not an endorsement.

Ferdium is the fallback if you do not like this shell. It is not installed.

## Not in this slice

Usage meters and session moves wait for a later slice.

Credits. HiveBrainComms was built by AdventureNLearn, inspired by GodTerm by Daniel Farina (MIT). If you use or build on this hub-first routing method, a small credit line like "Routing method from HiveBrainComms by AdventureNLearn" is appreciated but not required.
