"""HiveBrainComms lab. Loopback only. Lab bus only. No live handoff."""

import asyncio
import hmac
import json
import os
import re
import secrets
import subprocess
from datetime import datetime
from pathlib import Path

import winpty
from aiohttp import web

HOST = "127.0.0.1"
PORT = 8107
ROOT = Path(__file__).resolve().parent
BUS = ROOT / "bus"
LOG = ROOT / "approvals.jsonl"
SEEN = ROOT / ".seen-ids.txt"
GROK = Path.home() / ".grok" / "bin" / "grok.exe"

BUILD_CWD = os.environ.get("HBC_BUILD_CWD", str(Path(ROOT.anchor) / "AOS"))
OWNERS = {"LEAD-TO-BUILD.md": "Lead", "GROKBOT-TO-BUILD.md": "Grok Bot"}
RETURNS = {"Lead": "BUILD-TO-LEAD.md", "Grok Bot": "BUILD-TO-GROKBOT.md"}
FIELDS = ("id", "from", "to", "lane", "priority", "job", "done-when")
BAD = re.compile(r"send|post|pay|delete|git push|publish", re.I)


def now():
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def record(row):
    with LOG.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(row) + "\n")


def seen_ids():
    if not SEEN.is_file():
        return set()
    return {line.strip() for line in SEEN.read_text(encoding="utf-8").splitlines() if line.strip()}


def mark_seen(job_id):
    with SEEN.open("a", encoding="utf-8") as handle:
        handle.write(job_id + "\n")


def blocks(text):
    chunks = re.split(r"\n(?=id: )", text)
    found = []
    for chunk in chunks:
        if "id:" not in chunk:
            continue
        fields = {}
        for line in chunk.splitlines():
            if ": " in line:
                key, value = line.split(": ", 1)
                fields[key.strip()] = value.strip()
        found.append(fields)
    return found


def check(fields, owner):
    missing = [name for name in FIELDS if not fields.get(name)]
    if missing:
        return f"MALFORMED missing {', '.join(missing)}"
    if fields["priority"] not in ("urgent", "normal"):
        return "MALFORMED priority"
    if fields["from"] != owner:
        return "SPOOFED"
    if fields["id"] in seen_ids():
        return "DUPLICATE"
    if BAD.search(fields["job"]):
        return "REFUSED no send, post, pay, delete, git, or publish"
    if fields.get("pane") not in ("1", "2"):
        return "MALFORMED pane must be 1 or 2"
    return ""


def queue():
    items = []
    for name, owner in OWNERS.items():
        path = BUS / name
        if not path.is_file():
            continue
        for fields in blocks(path.read_text(encoding="utf-8")):
            reason = check(fields, owner)
            items.append({
                "id": fields.get("id", ""),
                "owner": owner,
                "pane": fields.get("pane", ""),
                "priority": fields.get("priority", ""),
                "job": fields.get("job", ""),
                "status": "ready" if not reason else "held",
                "reason": reason,
            })
    items.sort(key=lambda item: item["priority"] != "urgent")
    return items


def find_job(job_id):
    for name, owner in OWNERS.items():
        for fields in blocks((BUS / name).read_text(encoding="utf-8")):
            if fields.get("id") == job_id:
                return fields, owner
    return None, None


def append_return(owner, text):
    path = BUS / RETURNS[owner]
    path.write_text(path.read_text(encoding="utf-8") + "\n" + text + "\n", encoding="utf-8")


def run_pane(pane, command):
    home = ROOT / "panes" / f"pane-{pane}"
    home.mkdir(parents=True, exist_ok=True)
    if not GROK.is_file():
        return 127, "", "grok.exe not found"
    completed = subprocess.run(
        [str(GROK), "-p", command, "--permission-mode", "plan", "--max-turns", "1",
         "--cwd", str(home), "--disable-web-search"],
        cwd=str(home),
        capture_output=True,
        text=True,
        timeout=120,
        env={"GROK_HOME": str(home), "PATH": str(GROK.parent)},
    )
    return completed.returncode, completed.stdout[-2000:], completed.stderr[-1000:]


LINKS = {
    "x": "https://x.com/",
    "imagine": "https://grok.com/imagine",
    "grok": "https://grok.com/",
}


def bus_view():
    view = {}
    for path in sorted(BUS.glob("*.md")):
        view[path.name] = path.read_text(encoding="utf-8")[-4000:]
    return view


def sessions():
    path = ROOT / ".sessions.json"
    if not path.is_file():
        return []
    return json.loads(path.read_text(encoding="utf-8"))


def remember(pane, job_id, result):
    rows = sessions()
    rows.append({"time": now(), "pane": pane, "id": job_id, "result": result[:240]})
    (ROOT / ".sessions.json").write_text(json.dumps(rows[-20:], indent=2), encoding="utf-8")


PANES = {}
TOKEN = secrets.token_hex(16)
(ROOT / ".session-token").write_text(TOKEN, encoding="utf-8")


def allowed(request):
    origin = request.headers.get("Origin", "")
    if origin and origin != f"http://{HOST}:{PORT}":
        return False
    if request.path.startswith("/term/") or request.path.startswith("/api/"):
        supplied = request.query.get("token") or request.headers.get("X-Hbc-Token", "")
        return hmac.compare_digest(supplied, TOKEN)
    return True


async def term_socket(request):
    if not allowed(request):
        return web.Response(status=403, text="refused")
    pane = request.match_info["pane"]
    if pane not in ("1", "2", "build") and not (pane.startswith("build") and pane[5:].isdigit() and 2 <= int(pane[5:]) <= 8):
        return web.Response(status=404, text="unknown pane")
    socket = web.WebSocketResponse()
    await socket.prepare(request)
    pty = PANES.get(pane)
    if pty is None or not pty.isalive():
        home = ROOT / "panes" / f"pane-{pane}"
        home.mkdir(parents=True, exist_ok=True)
        pty = winpty.PTY(120, 32)
        child_env = os.environ.copy()
        is_build = pane.startswith("build")
        if not is_build:
            child_env["GROK_HOME"] = str(home)
        child_env["PATH"] = str(GROK.parent) + os.pathsep + child_env.get("PATH", "")
        child_env["PROMPT"] = f"pane{pane}$G"
        env_block = "\0".join(f"{key}={value}" for key, value in child_env.items()) + "\0\0"
        pty.spawn("cmd.exe", cmdline=None, cwd=(BUILD_CWD if is_build else str(home)), env=env_block)
        if pane == "build":
            prompt = (
                "Resume the handoff-bus watch. Poll the handoff folder every 5 seconds "
                "under BUS-PROTOCOL rules 1-13. Watch LEAD-TO-BUILD.md and GROKBOT-TO-BUILD.md. "
                "Reply only in the sender return file."
            )
            pty.write("grok --continue \"" + prompt + "\"\r\n")
        elif is_build:
            pty.write("grok\r\n")
        PANES[pane] = pty
        print(f"pane open {pane}", flush=True)

    async def pump():
        while pty.isalive():
            try:
                chunk = await asyncio.to_thread(pty.read, 4096)
            except EOFError:
                break
            if chunk:
                await socket.send_str(chunk)
        await socket.close()

    task = asyncio.create_task(pump())
    try:
        async for message in socket:
            if message.type == web.WSMsgType.TEXT:
                data = message.data
                if data.startswith("\x00resize:"):
                    try:
                        cols, rows = (int(v) for v in data[8:].split(","))
                        if 20 <= cols <= 400 and 5 <= rows <= 200:
                            size = (cols, rows)
                            if getattr(pty, "hbc_size", None) != size:
                                pty.set_size(cols, rows)
                                pty.hbc_size = size
                    except Exception:
                        pass
                    continue
                pty.write(data)
    finally:
        task.cancel()
    return socket


async def page(request):
    name = "index.html" if request.path == "/" else request.path.lstrip("/")
    if ".." in name or "\\" in name or name.startswith(("bus", "panes")):
        return web.Response(status=404, text="no")
    path = (ROOT / name).resolve()
    if not str(path).startswith(str(ROOT)) or not path.is_file():
        return web.Response(status=404, text="no")
    kind = {".html": "text/html", ".css": "text/css", ".js": "text/javascript"}.get(path.suffix, "text/plain")
    body = path.read_bytes()
    if path.name == "index.html":
        body = body.replace(b"{{TOKEN}}", TOKEN.encode("ascii"))
    return web.Response(body=body, content_type=kind, headers={"Cache-Control": "no-store"})


def state_payload():
    return {
        "class": "pattern-lab",
        "live": False,
        "freebots": "off",
        "hivebrain_game_chat": "off",
        "queue": queue(),
        "sessions": sessions(),
        "log": LOG.read_text(encoding="utf-8") if LOG.is_file() else "",
        "bus": bus_view(),
        "panes": ["1", "2"],
    }


async def state(request):
    if not allowed(request):
        return web.json_response({"error": "refused"}, status=403)
    return web.json_response(state_payload())


async def open_link(request):
    if not allowed(request):
        return web.json_response({"error": "refused"}, status=403)
    body = await request.json()
    name = body.get("name", "")
    if name == "grokbot":
        link = Path.home() / "AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Grok Bot.lnk"
        if not link.is_file():
            return web.json_response({"error": "Grok Bot shortcut not found"}, status=404)
        subprocess.Popen(["cmd", "/c", "start", "", str(link)])
        return web.json_response({"opened": "grokbot"})
    url = LINKS.get(name)
    if not url:
        return web.json_response({"error": "unknown link"}, status=400)
    subprocess.Popen(["cmd", "/c", "start", "", url])
    return web.json_response({"opened": name})


async def decide(request):
    if not allowed(request):
        return web.json_response({"error": "refused"}, status=403)
    body = await request.json()
    job_id = body.get("id", "")
    decision = body.get("decision", "")
    fields, owner = find_job(job_id)
    if not fields or decision not in ("approve", "deny"):
        return web.json_response({"error": "unknown job or decision"}, status=400)
    reason = check(fields, owner)
    if reason:
        record({"time": now(), "id": job_id, "decision": "held", "reason": reason})
        return web.json_response({"error": reason}, status=403)
    if decision == "deny":
        record({"time": now(), "id": job_id, "decision": "deny", "pane": fields["pane"]})
        mark_seen(job_id)
        append_return(owner, f"id: {job_id}\nresult: denied\nstop")
        return web.json_response({"result": "denied"})
    record({"time": now(), "id": job_id, "decision": "approve", "pane": fields["pane"]})
    code, out, err = run_pane(fields["pane"], fields["job"])
    result = out.strip() or err.strip() or f"exit {code}"
    mark_seen(job_id)
    remember(fields["pane"], job_id, result)
    append_return(owner, f"id: {job_id}\npane: {fields['pane']}\nexit: {code}\nresult: {result}\nstop")
    return web.json_response({"result": result, "exit": code})


def main():
    app = web.Application()
    app.router.add_get("/api/state", state)
    app.router.add_get("/term/{pane}", term_socket)
    app.router.add_post("/api/open", open_link)
    app.router.add_post("/api/decide", decide)
    app.router.add_get("/", page)
    app.router.add_get("/{name}", page)
    web.run_app(app, host=HOST, port=PORT, print=lambda msg: print(msg))


def _retired():
    return


class Handler_retired:
    def log_message(self, fmt, *args):
        return

    def send_json(self, code, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > 100_000:
            return None
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def do_GET(self):
        if self.path == "/api/state":
            self.send_json(200, {
                "class": "pattern-lab",
                "live": False,
                "freebots": "off",
                "hivebrain_game_chat": "off",
                "queue": queue(),
                "sessions": sessions(),
                "log": LOG.read_text(encoding="utf-8") if LOG.is_file() else "",
                "bus": bus_view(),
                "panes": ["1", "2"],
            })
            return
        rel = "index.html" if self.path in ("/", "") else self.path.lstrip("/")
        if ".." in rel or "\\" in rel or rel.startswith("bus") or rel.startswith("panes"):
            self.send_error(404)
            return
        path = (ROOT / rel).resolve()
        if not str(path).startswith(str(ROOT)) or not path.is_file():
            self.send_error(404)
            return
        kind = {".html": "text/html", ".css": "text/css", ".js": "text/plain"}.get(path.suffix, "text/plain")
        data = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", f"{kind}; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        if self.path == "/api/open":
            self.open_link()
            return
        if self.path == "/api/signin":
            self.sign_in()
            return
        if self.path != "/api/decide":
            self.send_error(404)
            return
        self.decide()

    def open_link(self):
        body = self.read_json() or {}
        name = body.get("name", "")
        if name == "grokbot":
            link = Path.home() / "AppData/Roaming/Microsoft/Windows/Start Menu/Programs/Grok Bot.lnk"
            if not link.is_file():
                self.send_json(404, {"error": "Grok Bot shortcut not found"})
                return
            subprocess.Popen(["cmd", "/c", "start", "", str(link)])
            self.send_json(200, {"opened": "grokbot"})
            return
        url = LINKS.get(name)
        if not url:
            self.send_json(400, {"error": "unknown link"})
            return
        subprocess.Popen(["cmd", "/c", "start", "", url])
        self.send_json(200, {"opened": name})

    def sign_in(self):
        pane = (self.read_json() or {}).get("pane", "")
        if pane not in ("1", "2"):
            self.send_json(400, {"error": "pane must be 1 or 2"})
            return
        home = ROOT / "panes" / f"pane-{pane}"
        home.mkdir(parents=True, exist_ok=True)
        if not GROK.is_file():
            self.send_json(404, {"error": "grok.exe not found"})
            return
        subprocess.Popen(
            ["cmd", "/c", "start", "HiveBrainComms sign-in", "cmd", "/k",
             f"set GROK_HOME={home}&& \"{GROK}\" login"],
        )
        record({"time": now(), "id": f"signin-{pane}", "decision": "signin-opened", "pane": pane})
        self.send_json(200, {"opened": pane})

    def decide(self):
        body = self.read_json() or {}
        job_id = body.get("id", "")
        decision = body.get("decision", "")
        fields, owner = find_job(job_id)
        if not fields or decision not in ("approve", "deny"):
            self.send_json(400, {"error": "unknown job or decision"})
            return
        reason = check(fields, owner)
        if reason:
            record({"time": now(), "id": job_id, "decision": "held", "reason": reason})
            self.send_json(403, {"error": reason})
            return
        if decision == "deny":
            record({"time": now(), "id": job_id, "decision": "deny", "pane": fields["pane"]})
            mark_seen(job_id)
            append_return(owner, f"id: {job_id}\nresult: denied\nstop")
            self.send_json(200, {"result": "denied"})
            return
        record({"time": now(), "id": job_id, "decision": "approve", "pane": fields["pane"]})
        code, out, err = run_pane(fields["pane"], fields["job"])
        result = out.strip() or err.strip() or f"exit {code}"
        mark_seen(job_id)
        remember(fields["pane"], job_id, result)
        append_return(owner, f"id: {job_id}\npane: {fields['pane']}\nexit: {code}\nresult: {result}\nstop")
        self.send_json(200, {"result": result, "exit": code})


if __name__ == "__main__":
    main()
