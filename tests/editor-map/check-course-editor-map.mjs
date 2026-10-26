// Headless check for the course editor map.
// A map click asks the page to add a waypoint. The page always follows roads.
// Map MOVE moves the selected pin immediately.
// The marker menu is Delete, Connect, Feed zone, Summit, Announcement, Interval, and Zoom.
// FINISH on the map beside the start asks to close the loop.
// The Leaflet +/- control is absent. + and - keys still zoom.
// Click and right-click on the elevation chart open the profile menu inside the window,
// and each row asks the page to insert that point.

import http from "http";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../src/GpsTrainer.Web/wwwroot");
const editorJs = fs.readFileSync(path.join(root, "course-editor/courseEditor.js"), "utf8");

const html = `<!doctype html>
<html><head>
<meta charset="utf-8" />
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
<style>
  html, body { margin: 0; }
  .course-map-frame { position: relative; width: 800px; }
  #course-map { height: 800px; width: 800px; position: relative; z-index: 1; background: #d9e4dc; }
  .profile-panel { position: absolute; left: 0; right: 0; bottom: 0; z-index: 20; background: #EAEAEA; pointer-events: auto; }
  #course-profile { width: 100%; height: 180px; display: block; background: #F8F7F4; }
  #waypoint-type-menu, #waypoint-type-menu ul { z-index: 2000; margin: 0; padding: 0.2rem 0; list-style: none; background: #fff; border: 1px solid #585880; min-width: 9.5rem; }
  #waypoint-type-menu { position: fixed; }
  #waypoint-type-menu ul { position: absolute; left: 100%; top: 0; display: none; }
  #waypoint-type-menu li { position: relative; }
  #waypoint-type-menu li:hover > ul, #waypoint-type-menu li:focus-within > ul, #waypoint-type-menu li.menu-open > ul { display: block; }
  #waypoint-type-menu.menu-open-left > li > ul { left: auto; right: 100%; }
  #waypoint-type-menu button { display: block; width: 100%; text-align: left; background: transparent; border: 0; padding: 0.28rem 0.75rem; color: #585880; }
  .leaflet-div-icon.course-pin { background: transparent; border: 0; }
  .course-pin span { display: flex; width: 22px; height: 22px; border-radius: 50%; color: #fff; background: #1d4e89; }
</style>
</head><body>
<div class="course-map-frame">
<div id="course-map"></div>
<div id="PanelProfile" class="profile-panel">
  <svg id="course-profile" viewBox="0 0 760 230">
    <rect x="0" y="0" width="760" height="230" fill="transparent"></rect>
    <polyline points="68,180 400,80 742,140" fill="none" stroke="#585880" stroke-width="2"></polyline>
  </svg>
</div>
</div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script>${editorJs}</script>
<script>
window.calls = [];
window.host = {
  invokeMethodAsync: function (name) {
    var args = Array.prototype.slice.call(arguments, 1);
    window.calls.push({ name: name, args: args });
    return Promise.resolve();
  }
};
var pins = [
  { index: 0, latitude: 40.5853, longitude: -105.0844, type: "Start", text: "" },
  { index: 1, latitude: 40.5900, longitude: -105.0760, type: "TurnLeft", text: "" }
];
window.gpsTrainerCourseEditor.mount("#course-map", pins, window.host, { latitude: 40.5853, longitude: -105.0844 }, null, {
  nearStartMeters: 200,
  samePointMeters: 15,
  mapMenuLast: [
    { label: "CONTINUE", action: "insert", type: "Continue" },
    { label: "ROUTE", action: "route", type: "Continue" },
    { label: "FINISH", action: "insert", type: "Finish" },
    { label: "MOVE", action: "move", type: null }
  ],
  mapMenuEarlier: [
    { label: "CONTINUE", action: "insert", type: "Continue" },
    { label: "MOVE", action: "move", type: null }
  ]
});
var chart = document.getElementById("course-profile");
function openChartMenu(ev) {
  ev.preventDefault();
  window.gpsTrainerCourseEditor.openProfileMenu(ev.clientX, ev.clientY, ev.clientX, ev.clientY);
}
chart.addEventListener("click", openChartMenu);
chart.addEventListener("contextmenu", openChartMenu);
</script>
</body></html>`;

function getJson(url) {
  return new Promise((resolve, reject) => {
    http.get(url, (res) => {
      let data = "";
      res.on("data", (chunk) => (data += chunk));
      res.on("end", () => resolve(JSON.parse(data)));
    }).on("error", reject);
  });
}

class Cdp {
  constructor(ws) {
    this.ws = ws;
    this.next = 1;
    this.pending = new Map();
    ws.addEventListener("message", (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id && this.pending.has(msg.id)) {
        const waiter = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (msg.error) waiter.reject(new Error(JSON.stringify(msg.error)));
        else waiter.resolve(msg.result);
      }
    });
  }
  send(method, params = {}, sessionId) {
    const id = this.next++;
    const payload = { id, method, params };
    if (sessionId) payload.sessionId = sessionId;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify(payload));
    });
  }
}

const server = http.createServer((req, res) => {
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
  res.end(html);
});

await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const port = server.address().port;
const version = await getJson("http://127.0.0.1:9333/json/version");
const ws = new WebSocket(version.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  ws.addEventListener("open", resolve);
  ws.addEventListener("error", reject);
});
const cdp = new Cdp(ws);
const created = await cdp.send("Target.createTarget", { url: "about:blank" });
const attached = await cdp.send("Target.attachToTarget", { targetId: created.targetId, flatten: true });
const send = (method, params) => cdp.send(method, params, attached.sessionId);
await send("Page.enable");
await send("Runtime.enable");
await send("Page.navigate", { url: `http://127.0.0.1:${port}/` });

async function evalJs(expression) {
  const result = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails).slice(0, 600));
  return result.result?.value;
}

async function waitFor(expression, label) {
  const start = Date.now();
  while (Date.now() - start < 15000) {
    try {
      const value = await evalJs(expression);
      if (value) return value;
    } catch {
      // The page is still loading Leaflet.
    }
    await new Promise((r) => setTimeout(r, 200));
  }
  throw new Error("timeout " + label);
}

async function click(xRatio, yRatio, button = "left") {
  const box = JSON.parse(await evalJs(`(() => {
    const r = document.querySelector("#course-map").getBoundingClientRect();
    return JSON.stringify({ left: r.left, top: r.top, width: r.width, height: r.height });
  })()`));
  const x = box.left + box.width * xRatio;
  const y = box.top + box.height * yRatio;
  await send("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button, clickCount: 1, buttons: button === "right" ? 2 : 1 });
  await send("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button, clickCount: 1, buttons: 0 });
}

async function clickElement(selector, which) {
  const point = JSON.parse(await evalJs(`(() => {
    const nodes = [...document.querySelectorAll(${JSON.stringify(selector)})];
    const el = nodes[${which}];
    const r = el.getBoundingClientRect();
    return JSON.stringify({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
  })()`));
  await send("Input.dispatchMouseEvent", { type: "mousePressed", x: point.x, y: point.y, button: "left", clickCount: 1, buttons: 1 });
  await send("Input.dispatchMouseEvent", { type: "mouseReleased", x: point.x, y: point.y, button: "left", clickCount: 1, buttons: 0 });
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function clickAt(x, y, button = "left") {
  await send("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button, clickCount: 1, buttons: button === "right" ? 2 : 1 });
  await send("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button, clickCount: 1, buttons: 0 });
}

async function topMenuLabels() {
  const text = await evalJs(`JSON.stringify([...document.querySelectorAll("#waypoint-type-menu > li > button")].filter(b => b.getBoundingClientRect().height > 2).map(b => b.textContent.trim()))`);
  return JSON.parse(text);
}

async function menuLabels() {
  const text = await evalJs(`JSON.stringify([...document.querySelectorAll("#waypoint-type-menu button")].map(b => b.textContent.trim()))`);
  return JSON.parse(text);
}

async function clickMenu(label) {
  const point = JSON.parse(await evalJs(`(() => {
    const buttons = [...document.querySelectorAll("#waypoint-type-menu button")].filter(b => b.textContent.trim() === ${JSON.stringify(label)});
    const button = buttons.find(b => b.getBoundingClientRect().height > 2) || null;
    if (!button) return "null";
    const r = button.getBoundingClientRect();
    return JSON.stringify({ x: r.left + r.width / 2, y: r.top + r.height / 2, h: r.height, w: r.width });
  })()`));
  assert(point && point.h > 2 && point.w > 2, "menu row is not on screen: " + label + " " + JSON.stringify(point));
  await clickAt(point.x, point.y);
}

async function clickChart(button, yRatio) {
  const point = JSON.parse(await evalJs(`(() => {
    const r = document.getElementById("course-profile").getBoundingClientRect();
    return JSON.stringify({ x: r.left + r.width * 0.45, y: r.top + r.height * ${yRatio} });
  })()`));
  await clickAt(point.x, point.y, button);
  await new Promise((r) => setTimeout(r, 200));
}

let failed = null;
try {
  await waitFor("window.L && document.querySelector('#course-map .leaflet-marker-icon')", "map");
  await evalJs("window.calls = []");
  await click(0.25, 0.2);
  await new Promise((r) => setTimeout(r, 300));
  const added = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(added.some((call) => call.name === "AddWaypoint" && call.args.length === 2), "map click did not ask to add a waypoint: " + JSON.stringify(added));

  await evalJs("window.calls = []");
  await click(0.7, 0.25, "right");
  await new Promise((r) => setTimeout(r, 300));
  const movedFromMap = await evalJs(`(() => {
    const button = [...document.querySelectorAll("#waypoint-type-menu button")].find(b => b.textContent.trim() === "MOVE");
    if (!button) return "missing";
    button.click();
    return "ok";
  })()`);
  assert(movedFromMap === "ok", "map menu did not show MOVE");
  await new Promise((r) => setTimeout(r, 200));
  const mapMove = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(mapMove.some((call) => call.name === "MoveWaypoint" && call.args.length === 3), "map MOVE did not move a pin: " + JSON.stringify(mapMove));

  assert(await evalJs(`document.querySelector(".leaflet-control-zoom") ? "yes" : "no"`) === "no", "the Leaflet +/- zoom control is still on the map");
  const zoomBefore = await evalJs("window.gpsTrainerCourseEditor.getZoom()");
  await evalJs(`document.dispatchEvent(new KeyboardEvent("keydown", { key: "+", bubbles: true }))`);
  assert(await evalJs("window.gpsTrainerCourseEditor.getZoom()") === zoomBefore + 1, "+ key did not zoom in");
  await evalJs(`document.dispatchEvent(new KeyboardEvent("keydown", { key: "-", bubbles: true }))`);
  assert(await evalJs("window.gpsTrainerCourseEditor.getZoom()") === zoomBefore, "- key did not zoom back out");

  await evalJs("window.calls = []");
  await clickElement("#course-map .leaflet-marker-icon", 0);
  const first = JSON.parse(await evalJs(`(() => {
    const el = document.querySelectorAll("#course-map .leaflet-marker-icon")[0];
    const r = el.getBoundingClientRect();
    return JSON.stringify({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
  })()`));
  await send("Input.dispatchMouseEvent", { type: "mousePressed", x: first.x, y: first.y, button: "right", clickCount: 1, buttons: 2 });
  await send("Input.dispatchMouseEvent", { type: "mouseReleased", x: first.x, y: first.y, button: "right", clickCount: 1, buttons: 0 });
  await new Promise((r) => setTimeout(r, 300));
  const selectedStart = await topMenuLabels();
  assert(JSON.stringify(selectedStart) === JSON.stringify(["Delete", "Feed zone", "Summit", "Announcement", "Zoom"]), "start pin menu is not the 2013 rows: " + JSON.stringify(selectedStart));
  assert(!(await menuLabels()).includes("Move"), "pin menu still offers Move");
  await clickMenu("Feed zone");
  await new Promise((r) => setTimeout(r, 200));
  const fed = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(fed.some((call) => call.name === "SetWaypointType" && call.args[1] === "FeedZone"), "Feed zone did not set the marker: " + JSON.stringify(fed));

  await clickElement("#course-map .leaflet-marker-icon", 1);
  await evalJs(`document.querySelector(".leaflet-popup-close-button")?.click()`);
  await clickAt(first.x, first.y, "right");
  await new Promise((r) => setTimeout(r, 200));
  const withLastSelected = await topMenuLabels();
  assert(JSON.stringify(withLastSelected) === JSON.stringify(["Delete", "Connect", "Feed zone", "Summit", "Announcement", "Interval", "Zoom"]), "pin menu with the last point selected is not the 2013 rows: " + JSON.stringify(withLastSelected));
  await evalJs(`document.getElementById("waypoint-type-menu")?.remove()`);

  await evalJs("window.calls = []");
  const beside = JSON.parse(await evalJs(`(() => {
    const el = document.querySelectorAll("#course-map .leaflet-marker-icon")[0];
    const r = el.getBoundingClientRect();
    return JSON.stringify({ x: r.right + 6, y: r.top + r.height / 2 });
  })()`));
  await clickAt(beside.x, beside.y, "right");
  await new Promise((r) => setTimeout(r, 200));
  assert((await menuLabels()).includes("FINISH"), "map menu beside the start did not offer FINISH: " + JSON.stringify(await menuLabels()));
  await clickMenu("FINISH");
  await new Promise((r) => setTimeout(r, 200));
  const closedFromMap = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(closedFromMap.some((call) => call.name === "CloseLoop"), "FINISH beside the start did not close the loop: " + JSON.stringify(closedFromMap));

  await clickElement("#course-map .leaflet-marker-icon", 1);
  await evalJs(`document.querySelector(".leaflet-popup-close-button")?.click()`);
  await evalJs("window.calls = []");
  await click(0.72, 0.22, "right");
  await new Promise((r) => setTimeout(r, 200));
  await clickMenu("FINISH");
  await new Promise((r) => setTimeout(r, 200));
  const farFinish = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(farFinish.some((call) => call.name === "InsertWaypoint" && call.args[0] === "Finish"), "FINISH away from the start did not insert a finish: " + JSON.stringify(farFinish));
  assert(!farFinish.some((call) => call.name === "CloseLoop"), "FINISH away from the start closed the loop: " + JSON.stringify(farFinish));

  await evalJs("window.calls = []");
  await click(0.72, 0.3, "right");
  await new Promise((r) => setTimeout(r, 200));
  await clickMenu("ROUTE");
  await new Promise((r) => setTimeout(r, 200));
  const routed = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
  assert(routed.some((call) => call.name === "InsertWaypoint" && call.args[0] === "Continue" && call.args[3] === true), "ROUTE did not ask for a road route: " + JSON.stringify(routed));

  const hit = await evalJs(`(() => {
    const r = document.getElementById("course-profile").getBoundingClientRect();
    const el = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
    return el && (el.id === "course-profile" || el.closest("#course-profile") || el.closest("#PanelProfile")) ? "chart" : (el ? el.tagName + "." + el.className : "none");
  })()`);
  assert(hit === "chart", "the elevation chart is not the click target: " + hit);

  const profileLabels = ["Interval", "100m", "200m", "400m", "500m", "800m", "1000m", "1mi", "Turn Around", "Feed Zone", "Summit", "Announcement"];
  for (const button of ["left", "right"]) {
    await clickChart(button, 0.4);
    const labels = await menuLabels();
    for (const label of profileLabels) {
      assert(labels.includes(label), button + " click on the chart missed " + label + ": " + JSON.stringify(labels));
    }
    await clickMenu("Interval");
    await new Promise((r) => setTimeout(r, 100));
    const interval = JSON.parse(await evalJs(`(() => {
      const button = [...document.querySelectorAll("#waypoint-type-menu button")].find(b => b.textContent.trim() === "100m");
      const r = button.getBoundingClientRect();
      return JSON.stringify({ h: r.height, top: r.top, bottom: r.bottom, inner: window.innerHeight });
    })()`));
    assert(interval.h > 2 && interval.top >= 0 && interval.bottom <= interval.inner + 1, "Interval distances are not visible: " + JSON.stringify(interval));
    await evalJs(`document.getElementById("waypoint-type-menu")?.remove()`);
  }

  const inserts = [
    ["100m", "ProfileInterval", "100m", true],
    ["200m", "ProfileInterval", "200m", true],
    ["400m", "ProfileInterval", "400m", true],
    ["500m", "ProfileInterval", "500m", true],
    ["800m", "ProfileInterval", "800m", true],
    ["1000m", "ProfileInterval", "1000m", true],
    ["1mi", "ProfileInterval", "1mi", true],
    ["Turn Around", "ProfilePlace", "TurnAround", false],
    ["Feed Zone", "ProfilePlace", "FeedZone", false],
    ["Summit", "ProfilePlace", "Summit", false],
    ["Announcement", "ProfilePlace", "Announcement", false]
  ];
  for (const [label, name, arg, interval] of inserts) {
    await evalJs("window.calls = []");
    await clickChart("left", 0.45);
    if (interval) {
      await clickMenu("Interval");
      await new Promise((r) => setTimeout(r, 80));
    }
    await clickMenu(label);
    await new Promise((r) => setTimeout(r, 150));
    const calls = JSON.parse(await evalJs("JSON.stringify(window.calls)"));
    assert(calls.some((call) => call.name === name && call.args[0] === arg), label + " did not insert a point: " + JSON.stringify(calls));
  }

  await send("Emulation.setDeviceMetricsOverride", { width: 1000, height: 640, deviceScaleFactor: 1, mobile: false });
  await evalJs(`document.getElementById("course-profile").scrollIntoView({ block: "end" })`);
  await new Promise((r) => setTimeout(r, 200));
  await clickChart("right", 0.9);
  const placed = JSON.parse(await evalJs(`(() => {
    const menu = document.getElementById("waypoint-type-menu");
    const r = menu.getBoundingClientRect();
    return JSON.stringify({ top: r.top, bottom: r.bottom, left: r.left, right: r.right, inner: window.innerHeight, width: window.innerWidth });
  })()`));
  assert(placed.top >= 0 && placed.bottom <= placed.inner + 1 && placed.left >= 0 && placed.right <= placed.width + 1, "profile menu opened outside the window: " + JSON.stringify(placed));
  console.log("editor map checks passed");
} catch (err) {
  failed = err;
  console.error(err);
} finally {
  if (server.closeAllConnections) server.closeAllConnections();
  server.close();
  ws.close();
}
process.exit(failed ? 1 : 0);
