// Course editor map for the Blazor site. index.html loads courseEditor.js, which is the
// same menu: Delete, Connect, the marker types, Interval, Zoom (In, Out, To selected),
// Move, and the map menu's Move, Zoom, Summit, and standard-distance finish.
// Summit searches for the high point; the other types only relabel the marker.
// The line is the waypoints in order and is not closed.
// Basemap: OpenStreetMap. Directions: OpenRouteService. Both are credited on the map.

interface WaypointPin {
  index?: number;
  latitude: number;
  longitude: number;
  type?: string;
  text?: string;
}

interface MenuType {
  type: string;
  label: string;
}

interface MapCenter {
  latitude: number;
  longitude: number;
}

interface EditorHost {
  invokeMethodAsync(name: string, ...args: unknown[]): Promise<void>;
}

interface LeafletEvent {
  latlng: { lat: number; lng: number };
  originalEvent?: { clientX: number; clientY: number; preventDefault(): void };
}

interface LeafletLayer {
  addTo(target: unknown): LeafletLayer;
  bindPopup(text: string): LeafletLayer;
  setLatLng(latlng: [number, number]): void;
  on(event: string, handler: (event: LeafletEvent) => void): LeafletLayer;
}

interface LeafletMap {
  setView(center: [number, number], zoom: number): LeafletMap;
  getZoom(): number;
  getCenter?(): [number, number];
  setZoom?(zoom: number): void;
  getContainer(): HTMLElement;
  on(event: string, handler: (e: LeafletEvent) => void): void;
  off?(event: string, handler: (e: LeafletEvent) => void): void;
  remove(): void;
  dragging?: { disable(): void; enable(): void };
}

interface LeafletNamespace {
  map(id: string): LeafletMap;
  tileLayer(url: string, options: { attribution: string; maxZoom: number }): { addTo(map: LeafletMap): void };
  layerGroup(): { addTo(map: LeafletMap): { clearLayers(): void } };
  circleMarker(latlng: [number, number], options: { radius: number; color: string }): LeafletLayer;
  polyline(latlngs: [number, number][], options: { color: string }): { addTo(target: unknown): void };
  DomEvent?: { stop(event: LeafletEvent): void };
}

declare const L: LeafletNamespace;

const attribution = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors | Directions &amp; elevation &copy; <a href="https://openrouteservice.org/">openrouteservice</a> by <a href="https://heigit.org/">HeiGIT</a>';
const fortCollins: [number, number] = [40.5853, -105.0844];

let map: LeafletMap | undefined;
let markers: { clearLayers(): void } | undefined;
let homeMarker: LeafletLayer | undefined;
let editorHost: EditorHost | undefined;
let menuTypes: MenuType[] = [
  { type: "Continue", label: "Continue" },
  { type: "TurnRight", label: "Turn right" },
  { type: "TurnLeft", label: "Turn left" },
  { type: "TurnAround", label: "Turn around" },
  { type: "Finish", label: "Finish" },
  { type: "FeedZone", label: "Feed zone" },
  { type: "Summit", label: "Summit" },
  { type: "Announcement", label: "Announcement" }
];
let currentPins: WaypointPin[] = [];
let selectedIndex = -1;
let pendingMoveIndex: number | null = null;
let standardFinishLabel = "1 mi FINISH";
let intervalDistances = ["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"];
let connectTypes: MenuType[] = [
  { type: "Continue", label: "Continue" },
  { type: "TurnRight", label: "Turn right" },
  { type: "TurnLeft", label: "Turn left" },
  { type: "TurnAround", label: "Turn around" },
  { type: "Finish", label: "Finish" }
];

function hideMenu(): void {
  document.getElementById("waypoint-type-menu")?.remove();
}

function menuButton(label: string, onClick: () => void): HTMLLIElement {
  const row = document.createElement("li");
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = label;
  button.addEventListener("click", (event) => {
    event.preventDefault();
    event.stopPropagation();
    hideMenu();
    onClick();
  });
  row.appendChild(button);
  return row;
}

function menuGroup(label: string, children: HTMLLIElement[]): HTMLLIElement {
  const row = document.createElement("li");
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = label;
  button.addEventListener("click", (event) => {
    event.preventDefault();
    event.stopPropagation();
  });
  row.appendChild(button);
  const list = document.createElement("ul");
  for (const child of children) {
    list.appendChild(child);
  }
  row.appendChild(list);
  return row;
}

function showMenu(x: number, y: number, rows: HTMLLIElement[]): void {
  hideMenu();
  const menu = document.createElement("ul");
  menu.id = "waypoint-type-menu";
  menu.style.left = x + "px";
  menu.style.top = y + "px";
  for (const row of rows) {
    menu.appendChild(row);
  }
  document.body.appendChild(menu);
}

function pinIndex(pin: WaypointPin, fallback: number): number {
  return pin.index === 0 || pin.index ? pin.index : fallback;
}

function intervalPin(pin: WaypointPin): boolean {
  return pin.type === "StartInterval" || pin.type === "FinishInterval";
}

function lastRouteIndex(): number {
  let found = -1;
  currentPins.forEach((pin, index) => {
    if (!intervalPin(pin)) {
      found = pinIndex(pin, index);
    }
  });
  return found;
}

function selectionIndex(): number {
  const chosen = currentPins.find((pin, index) => pinIndex(pin, index) === selectedIndex);
  if (chosen && !intervalPin(chosen)) {
    return selectedIndex;
  }
  return lastRouteIndex();
}

function zoomTo(index: number): void {
  const pin = currentPins.find((item, itemIndex) => pinIndex(item, itemIndex) === index);
  if (!map || !pin) {
    return;
  }
  map.setView([pin.latitude, pin.longitude], 19);
}

function openMarkerMenu(x: number, y: number, index: number, interval: boolean): void {
  const rows: HTMLLIElement[] = [
    menuButton("Delete", () => void editorHost?.invokeMethodAsync("DeleteWaypoint", index))
  ];
  const selected = selectionIndex();
  if (!interval && selected >= 0 && index !== selected && selected === lastRouteIndex()) {
    rows.push(menuGroup("Connect", connectTypes.map((entry) => menuButton(entry.label, () => {
      void editorHost?.invokeMethodAsync("ConnectWaypoint", selected, index, entry.type);
    }))));
  }
  if (!interval) {
    for (const entry of menuTypes) {
      rows.push(menuButton(entry.label, () => {
        if (entry.type === "Summit") {
          void editorHost?.invokeMethodAsync("PlaceSummit", index);
        } else {
          void editorHost?.invokeMethodAsync("SetWaypointType", index, entry.type);
        }
      }));
    }
  }
  if (!interval && selected === lastRouteIndex() && currentPins.filter((pin) => !intervalPin(pin)).length >= 2) {
    rows.push(menuGroup("Interval", intervalDistances.map((distance) => menuButton(distance, () => {
      void editorHost?.invokeMethodAsync("PlaceInterval", index, distance);
    }))));
  }
  rows.push(menuGroup("Zoom", [
    menuButton("In", () => map?.setView(map.getCenter?.() ?? [0, 0], Math.min(19, map.getZoom() + 1))),
    menuButton("Out", () => map?.setView(map.getCenter?.() ?? [0, 0], Math.max(1, map.getZoom() - 1))),
    menuButton("To selected", () => zoomTo(index))
  ]));
  if (!interval) {
    rows.push(menuButton("Move", () => { pendingMoveIndex = index; }));
  }
  showMenu(x, y, rows);
}

function openMapMenu(x: number, y: number, latitude: number, longitude: number): void {
  const selected = selectionIndex();
  showMenu(x, y, [
    menuButton("Move", () => void editorHost?.invokeMethodAsync("MoveWaypoint", selected, latitude, longitude)),
    menuGroup("Zoom", [
      menuButton("In", () => map?.setView(map.getCenter?.() ?? [0, 0], Math.min(19, map.getZoom() + 1))),
      menuButton("Out", () => map?.setView(map.getCenter?.() ?? [0, 0], Math.max(1, map.getZoom() - 1))),
      menuButton("To selected", () => zoomTo(selected))
    ]),
    menuButton("Summit", () => void editorHost?.invokeMethodAsync("PlaceSummitAt", latitude, longitude)),
    menuButton(standardFinishLabel, () => void editorHost?.invokeMethodAsync("StandardFinish", selected, latitude, longitude))
  ]);
}

function draw(waypoints: WaypointPin[]): void {
  if (!map || typeof L === "undefined") {
    return;
  }
  hideMenu();
  currentPins = waypoints || [];
  if (markers) {
    markers.clearLayers();
  } else {
    markers = L.layerGroup().addTo(map);
  }
  const line: [number, number][] = [];
  (waypoints || []).forEach((pin, index) => {
    const interval = pin.type === "StartInterval" || pin.type === "FinishInterval";
    const color = interval ? "orange" : pin.type === "Summit" ? "#000000" : "blue";
    const marker = L.circleMarker([pin.latitude, pin.longitude], { radius: interval ? 9 : 6, color })
      .addTo(markers)
      .bindPopup((pin.type ?? "Waypoint") + (pin.text ? " " + pin.text : ""));
    marker.on("click", (event) => {
      L.DomEvent?.stop(event);
      if (!interval) {
        selectedIndex = pin.index === 0 || pin.index ? pin.index : index;
      }
    });
    marker.on("contextmenu", (event) => {
      L.DomEvent?.stop(event);
      openMarkerMenu(event.originalEvent?.clientX ?? 0, event.originalEvent?.clientY ?? 0, pin.index === 0 || pin.index ? pin.index : index, interval);
    });
    if (!interval) {
      line.push([pin.latitude, pin.longitude]);
    }
  });
  if (line.length > 1) {
    L.polyline(line, { color: "#1d4e89" }).addTo(markers);
  }
}

export function mount(selector: string, waypoints: WaypointPin[], host: EditorHost, center?: MapCenter, types?: MenuType[], options?: { standardFinish?: string; intervals?: string[]; connect?: MenuType[] }): void {
  const element = document.querySelector(selector);
  if (!element || typeof L === "undefined") {
    return;
  }
  unmount();
  editorHost = host;
  if (types && types.length) {
    menuTypes = types;
  }
  if (options?.standardFinish) {
    standardFinishLabel = options.standardFinish;
  }
  if (options?.intervals?.length) {
    intervalDistances = options.intervals;
  }
  if (options?.connect?.length) {
    connectTypes = options.connect;
  }
  const id = element.id;
  const first = waypoints[0];
  const fallback: [number, number] = center && Number.isFinite(center.latitude) && Number.isFinite(center.longitude)
    ? [center.latitude, center.longitude]
    : fortCollins;
  const view = first ? [first.latitude, first.longitude] as [number, number] : fallback;
  map = L.map(id).setView(view, first ? 14 : 13);
  L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    attribution,
    maxZoom: 19
  }).addTo(map);
  map.getContainer().addEventListener("contextmenu", (event) => event.preventDefault());
  draw(waypoints);
  map.on("click", (event) => {
    hideMenu();
    if (pendingMoveIndex !== null) {
      const index = pendingMoveIndex;
      pendingMoveIndex = null;
      void host.invokeMethodAsync("MoveWaypoint", index, event.latlng.lat, event.latlng.lng);
      return;
    }
    void host.invokeMethodAsync("AddWaypoint", event.latlng.lat, event.latlng.lng);
  });
  map.on("contextmenu", (event) => {
    L.DomEvent?.stop(event);
    openMapMenu(event.originalEvent?.clientX ?? 0, event.originalEvent?.clientY ?? 0, event.latlng.lat, event.latlng.lng);
  });
}

export function mountLocationPicker(selector: string, latitude: number, longitude: number, host: EditorHost): void {
  const element = document.querySelector(selector);
  if (!element || typeof L === "undefined") {
    return;
  }
  unmount();
  const lat = Number.isFinite(latitude) ? latitude : fortCollins[0];
  const lng = Number.isFinite(longitude) ? longitude : fortCollins[1];
  map = L.map(element.id).setView([lat, lng], 13);
  L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    attribution,
    maxZoom: 19
  }).addTo(map);
  homeMarker = L.circleMarker([lat, lng], { radius: 8, color: "#1d4e89" }).addTo(map);
  map.on("click", (event) => {
    setLocation(event.latlng.lat, event.latlng.lng);
    void host.invokeMethodAsync("SetHomeLocation", event.latlng.lat, event.latlng.lng);
  });
}

export function setLocation(latitude: number, longitude: number): void {
  if (!map) {
    return;
  }
  map.setView([latitude, longitude], Math.max(map.getZoom(), 13));
  homeMarker?.setLatLng([latitude, longitude]);
}

export function setWaypoints(waypoints: WaypointPin[], options?: { standardFinish?: string }): void {
  if (options?.standardFinish) {
    standardFinishLabel = options.standardFinish;
  }
  draw(waypoints);
}

export function profileViewX(id: string, clientX: number, clientY: number): number {
  const svg = document.getElementById(id);
  if (!svg || !("createSVGPoint" in svg)) {
    return 68;
  }
  const element = svg as SVGSVGElement;
  const point = element.createSVGPoint();
  point.x = clientX;
  point.y = clientY;
  const matrix = element.getScreenCTM();
  if (!matrix) {
    return 68;
  }
  const local = point.matrixTransform(matrix.inverse());
  return Number.isFinite(local.x) ? local.x : 68;
}

export function unmount(): void {
  hideMenu();
  map?.remove();
  map = undefined;
  markers = undefined;
  homeMarker = undefined;
}
