const status = document.querySelector("#status");

function field(chunk, name) {
  return (chunk.match(new RegExp("^" + name + ": (.+)$", "m")) || [])[1] || "";
}

function minutes(start, end) {
  const a = Date.parse(start.replace(" ET", " GMT-0400"));
  const b = Date.parse(end.replace(" ET", " GMT-0400"));
  if (Number.isNaN(a) || Number.isNaN(b)) return "";
  return Math.round((b - a) / 60000) + " min";
}

function hubRows(bus) {
  const rows = [];
  const returns = {
    Lead: bus["BUILD-TO-LEAD.md"] || "",
    "Grok Bot": bus["BUILD-TO-GROKBOT.md"] || "",
  };
  for (const [name, owner] of [["LEAD-TO-BUILD.md", "Lead"], ["GROKBOT-TO-BUILD.md", "Grok Bot"]]) {
    for (const chunk of (bus[name] || "").split(/\n(?=id: )/)) {
      const id = field(chunk, "id");
      if (!id) continue;
      const back = (returns[owner].split(/\n(?=id: )/).find((part) => field(part, "id") === id)) || "";
      const arrived = field(chunk, "time");
      const routed = field(chunk, "routed") || arrived;
      const pickup = field(back, "pickup");
      const returned = field(back, "returned");
      rows.push({
        id,
        from: owner,
        route: owner + " -> Build",
        status: back ? "returned" : "waiting",
        wait: [
          arrived && routed ? minutes(arrived, routed) : "",
          routed && pickup ? minutes(routed, pickup) : "",
          pickup && returned ? minutes(pickup, returned) : "",
        ].filter(Boolean).join(" / ") || "no times",
      });
    }
  }
  return rows;
}

async function load() {
  const token = window.HBC_TOKEN || "";
  const response = await fetch("/api/state?token=" + encodeURIComponent(token));
  const data = await response.json();
  document.querySelector("#flag").textContent = data.live ? "LIVE - stop" : "LAB - not the live bus";
  const sessions = document.querySelector("#sessions");
  sessions.textContent = data.sessions.length
    ? data.sessions.map((row) => `${row.time} pane ${row.pane} ${row.id}: ${row.result}`).join("\n")
    : "No sessions yet.";
  document.querySelector("#log").textContent = data.log || "No decisions yet.";
  const body = document.querySelector("#hub tbody");
  body.replaceChildren();
  const head = document.createElement("tr");
  for (const label of ["id", "from", "route", "status", "wait"]) {
    const cell = document.createElement("th");
    cell.textContent = label;
    head.append(cell);
  }
  body.append(head);
  for (const row of hubRows(data.bus || {})) {
    const line = document.createElement("tr");
    for (const value of [row.id, row.from, row.route, row.status, row.wait]) {
      const cell = document.createElement("td");
      cell.textContent = value;
      line.append(cell);
    }
    body.append(line);
  }
  const bus = data.bus || {};
  document.querySelector("#bus").textContent = Object.keys(bus).length
    ? Object.entries(bus).map(([name, text]) => `## ${name}\n${text}`).join("\n")
    : "No bus files.";
  const list = document.querySelector("#queue");
  list.replaceChildren();
  if (data.queue.length === 0) {
    const item = document.createElement("li");
    item.textContent = "No jobs.";
    list.append(item);
    return;
  }
  for (const job of data.queue) {
    const item = document.createElement("li");
    const title = document.createElement("strong");
    title.textContent = `${job.id} (${job.owner}, pane ${job.pane || "?"}, ${job.priority || "?"})`;
    item.append(title, document.createElement("br"));
    item.append(job.job || "(empty)");
    if (job.status === "ready") {
      for (const decision of ["approve", "deny"]) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = decision === "approve" ? "Approve" : "Deny";
        button.addEventListener("click", () => decide(job.id, decision));
        item.append(" ", button);
      }
    } else {
      const note = document.createElement("div");
      note.textContent = `Held: ${job.reason}`;
      item.append(note);
    }
    list.append(item);
  }
}

async function decide(id, decision) {
  status.textContent = "Working...";
  const response = await fetch("/api/decide", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Hbc-Token": token },
    body: JSON.stringify({ id, decision }),
  });
  const data = await response.json();
  status.textContent = data.error || data.result || "Done.";
  await load();
}

function attach(pane) {
  const term = new Terminal({ cursorBlink: true, fontSize: 14, rows: 12, cols: 80 });
  term.open(document.querySelector("#term-" + pane));
  const socket = new WebSocket("ws://127.0.0.1:8107/term/" + pane + "?token=" + encodeURIComponent(window.HBC_TOKEN || ""));
  socket.binaryType = "arraybuffer";
  socket.addEventListener("message", (event) => {
    const text = typeof event.data === "string" ? event.data : new TextDecoder().decode(event.data);
    term.write(text);
  });
  term.onData((data) => { if (socket.readyState === 1) socket.send(data); });
  socket.addEventListener("close", () => term.write("\r\n[pane closed]\r\n"));
}

attach("1");
attach("2");

for (const button of document.querySelectorAll("[data-open]")) {
  button.addEventListener("click", () => decide_open("/api/open", { name: button.dataset.open }));
}
async function decide_open(url, payload) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Hbc-Token": window.HBC_TOKEN || "" },
    body: JSON.stringify(payload),
  });
  const data = await response.json();
  status.textContent = data.error || `Opened ${data.opened}.`;
}

load().catch(() => { status.textContent = "The lab server is not answering."; });
