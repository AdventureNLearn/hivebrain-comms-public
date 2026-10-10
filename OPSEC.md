# OPSEC — HiveBrainComms

- Class: pattern-lab. One tree: this folder.
- Loopback only: 127.0.0.1:8107. Never bind 0.0.0.0.
- The bus inside `bus/` is fiction. The live handoff folder is never read or written. Build auto-watch of `bus/` is off unless `HBC_BUILD_WATCH` is set, and grok still starts only after **Start grok** (default No).
- Child processes get an env allowlist only. API keys are not forwarded unless `HBC_PASS_GROK_AUTH` is set.
- No credentials. Sign-in is the person's step.
- No voice recording. No send, post, pay, delete, git, or publish from a job.
- Freebots and HiveBrain game chat are off. No feed, no watcher, no pane, unless the person asks in a later YES.
- License of this project: MIT.
