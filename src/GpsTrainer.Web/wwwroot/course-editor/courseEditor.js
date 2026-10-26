// Course editor map. index.html loads this file as window.gpsTrainerCourseEditor.
// courseEditor.ts mirrors the same menu. The line is the waypoints in order and is not closed.
window.gpsTrainerCourseEditor = (function () {
  var map;
  var markers;
  var homeMarker;
  var editorHost;
  var currentPins = [];
  var selectedIndex = -1;
  var pendingMoveIndex = null;
  var suppressClick = false;
  var standardFinishLabel = "1 mi FINISH";
  var mapMenuLast = null;
  var mapMenuEarlier = null;
  var nearStartMeters = 200;
  var samePointMeters = 15;
  var keyHandler = null;
  var popupHandler = null;
  var resizeHandler = null;
  var intervalDistances = ["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"];
  var connectTypes = [
    { type: "Continue", label: "Continue" },
    { type: "TurnRight", label: "Turn right" },
    { type: "TurnLeft", label: "Turn left" },
    { type: "TurnAround", label: "Turn around" },
    { type: "Finish", label: "Finish" }
  ];
  var pinMenuOrder = ["FeedZone", "Summit", "Announcement"];
  var menuTypes = [
    { type: "FeedZone", label: "Feed zone" },
    { type: "Summit", label: "Summit" },
    { type: "Announcement", label: "Announcement" }
  ];
  var attribution = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors | Directions &amp; elevation &copy; <a href="https://openrouteservice.org/">openrouteservice</a> by <a href="https://heigit.org/">HeiGIT</a>';

  function hideMenuOnce(event) {
    var menu = document.getElementById("waypoint-type-menu");
    if (menu && event && menu.contains(event.target)) {
      return;
    }
    hideMenu();
  }

  function hideMenu() {
    document.removeEventListener("click", hideMenuOnce);
    var menu = document.getElementById("waypoint-type-menu");
    if (menu && menu.parentNode) {
      menu.parentNode.removeChild(menu);
    }
  }

  function showMenu(x, y, rows) {
    hideMenu();
    var menu = document.createElement("ul");
    menu.id = "waypoint-type-menu";
    menu.style.visibility = "hidden";
    menu.style.left = "0px";
    menu.style.top = "0px";
    rows.forEach(function (row) {
      menu.appendChild(row);
    });
    document.body.appendChild(menu);
    var width = menu.offsetWidth;
    var height = menu.offsetHeight;
    var subHeight = 0;
    menu.querySelectorAll("ul").forEach(function (list) {
      list.style.display = "block";
      subHeight = Math.max(subHeight, list.offsetHeight);
      list.style.display = "";
    });
    var needed = Math.max(height, subHeight);
    var margin = 8;
    if (x + width > window.innerWidth - margin) {
      x = window.innerWidth - width - margin;
    }
    if (x < margin) {
      x = margin;
    }
    if (y + needed > window.innerHeight - margin) {
      y = y - needed;
    }
    if (y < margin) {
      y = margin;
    }
    if (x + width + width > window.innerWidth - margin) {
      menu.classList.add("menu-open-left");
    }
    menu.style.left = Math.round(x) + "px";
    menu.style.top = Math.round(y) + "px";
    menu.style.visibility = "visible";
    setTimeout(function () {
      document.addEventListener("click", hideMenuOnce);
    }, 0);
  }

  function item(label, onClick) {
    var row = document.createElement("li");
    var button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.addEventListener("click", function (event) {
      event.preventDefault();
      event.stopPropagation();
      hideMenu();
      onClick();
    });
    row.appendChild(button);
    return row;
  }

  function group(label, children) {
    var row = document.createElement("li");
    var button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.addEventListener("click", function (event) {
      event.preventDefault();
      event.stopPropagation();
      var open = row.classList.contains("menu-open");
      var menu = row.parentNode;
      if (menu) {
        menu.querySelectorAll("li.menu-open").forEach(function (openRow) {
          openRow.classList.remove("menu-open");
        });
      }
      if (!open) {
        row.classList.add("menu-open");
      }
    });
    row.appendChild(button);
    var list = document.createElement("ul");
    children.forEach(function (child) {
      list.appendChild(child);
    });
    row.appendChild(list);
    return row;
  }

  function pinIndex(pin, fallback) {
    return pin && (pin.index === 0 || pin.index) ? pin.index : fallback;
  }

  function isInterval(pin) {
    return pin && (pin.type === "StartInterval" || pin.type === "FinishInterval");
  }

  function lastRouteIndex() {
    var found = -1;
    (currentPins || []).forEach(function (pin, i) {
      if (!isInterval(pin)) {
        found = pinIndex(pin, i);
      }
    });
    return found;
  }

  function selectionIndex() {
    var chosen = null;
    (currentPins || []).forEach(function (pin, i) {
      if (pinIndex(pin, i) === selectedIndex) {
        chosen = pin;
      }
    });
    if (chosen && !isInterval(chosen)) {
      return selectedIndex;
    }
    return lastRouteIndex();
  }

  function pinAt(index) {
    var found = null;
    (currentPins || []).forEach(function (pin, i) {
      if (pinIndex(pin, i) === index) {
        found = pin;
      }
    });
    return found;
  }

  function startPin() {
    var found = null;
    (currentPins || []).forEach(function (pin) {
      if (!found && pin.type === "Start") {
        found = pin;
      }
    });
    if (!found) {
      (currentPins || []).forEach(function (pin) {
        if (!found && !isInterval(pin)) {
          found = pin;
        }
      });
    }
    return found;
  }

  function distanceMeters(lat1, lon1, lat2, lon2) {
    var radius = 6371000;
    var p1 = lat1 * Math.PI / 180;
    var p2 = lat2 * Math.PI / 180;
    var dp = (lat2 - lat1) * Math.PI / 180;
    var dl = (lon2 - lon1) * Math.PI / 180;
    var a = Math.sin(dp / 2) * Math.sin(dp / 2)
      + Math.cos(p1) * Math.cos(p2) * Math.sin(dl / 2) * Math.sin(dl / 2);
    return 2 * radius * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  function loopIsOpen() {
    if (routeCount() < 2) {
      return false;
    }
    var start = startPin();
    if (!start) {
      return false;
    }
    var last = null;
    (currentPins || []).forEach(function (pin) {
      if (!isInterval(pin)) {
        last = pin;
      }
    });
    if (!last) {
      return false;
    }
    return distanceMeters(last.latitude, last.longitude, start.latitude, start.longitude) >= samePointMeters;
  }

  function clickNearStart(latitude, longitude) {
    var start = startPin();
    if (!start || !isFinite(latitude) || !isFinite(longitude)) {
      return false;
    }
    return distanceMeters(latitude, longitude, start.latitude, start.longitude) <= nearStartMeters;
  }

  function routeCount() {
    var count = 0;
    (currentPins || []).forEach(function (pin) {
      if (!isInterval(pin)) {
        count++;
      }
    });
    return count;
  }

  function callHost(name, args) {
    if (!editorHost) {
      return;
    }
    editorHost.invokeMethodAsync.apply(editorHost, [name].concat(args || []));
  }

  function zoomBy(delta) {
    if (!map || !map.getZoom) {
      return;
    }
    var next = map.getZoom() + delta;
    if (next < 1) {
      next = 1;
    }
    if (next > 19) {
      next = 19;
    }
    if (map.setZoom) {
      map.setZoom(next, { animate: false });
    } else if (map.getCenter && map.setView) {
      map.setView(map.getCenter(), next);
    }
  }

  function zoomTo(index) {
    var pin = null;
    (currentPins || []).forEach(function (itemPin, i) {
      if (pinIndex(itemPin, i) === index) {
        pin = itemPin;
      }
    });
    if (!map || !pin || !map.setView) {
      return;
    }
    map.setView([pin.latitude, pin.longitude], 19);
  }

  function zoomMenu(index, caps) {
    return group(caps ? "ZOOM" : "Zoom", [
      item(caps ? "IN" : "In", function () { zoomBy(1); }),
      item(caps ? "OUT" : "Out", function () { zoomBy(-1); }),
      item(caps ? "TO SELECTED" : "To selected", function () { zoomTo(index); })
    ]);
  }

  function rule() {
    var row = document.createElement("li");
    row.className = "menu-rule";
    var line = document.createElement("hr");
    row.appendChild(line);
    return row;
  }

  function pointer(event) {
    var original = event && (event.originalEvent || event);
    return {
      x: original && original.clientX ? original.clientX : 0,
      y: original && original.clientY ? original.clientY : 0
    };
  }

  function markerMenuTypes() {
    var byType = {};
    (menuTypes || []).forEach(function (entry) {
      if (entry && pinMenuOrder.indexOf(entry.type) >= 0) {
        byType[entry.type] = entry;
      }
    });
    return pinMenuOrder.map(function (type) {
      return byType[type] || { type: type, label: type };
    });
  }

  function openMarkerMenu(x, y, index, interval) {
    var rows = [item("Delete", function () { callHost("DeleteWaypoint", [index]); })];
    var pin = pinAt(index);
    if (!interval && pin && pin.type === "Start" && loopIsOpen()) {
      rows.push(item("FINISH", function () { callHost("CloseLoop", []); }));
    }
    var selected = selectionIndex();
    if (!interval && selected >= 0 && index !== selected && selected === lastRouteIndex()) {
      rows.push(group("Connect", connectTypes.map(function (entry) {
        return item(entry.label, function () {
          callHost("ConnectWaypoint", [selected, index, entry.type]);
        });
      })));
    }
    if (!interval) {
      markerMenuTypes().forEach(function (entry) {
        rows.push(item(entry.label, function () {
          if (entry.type === "Summit") {
            callHost("PlaceSummit", [index]);
          } else {
            callHost("SetWaypointType", [index, entry.type]);
          }
        }));
      });
    }
    if (!interval && selected === lastRouteIndex() && routeCount() >= 2) {
      rows.push(group("Interval", intervalDistances.map(function (distance) {
        return item(distance, function () { callHost("PlaceInterval", [index, distance]); });
      })));
    }
    rows.push(zoomMenu(index, false));
    showMenu(x, y, rows);
  }

  function openMapMenu(x, y, latitude, longitude) {
    var selected = selectionIndex();
    var last = lastRouteIndex();
    var spec = selected >= 0 && last >= 0 && selected !== last ? mapMenuEarlier : mapMenuLast;
    var rows = [];
    (spec || []).forEach(function (row) {
      if (row.action === "rule") {
        rows.push(rule());
      } else if (row.action === "zoom") {
        rows.push(zoomMenu(selected, true));
      } else if (row.action === "move") {
        rows.push(item(row.label, function () { callHost("MoveWaypoint", [selected, latitude, longitude]); }));
      } else if (row.action === "finish") {
        rows.push(item(row.label, function () { callHost("StandardFinish", [selected, latitude, longitude]); }));
      } else if (row.action === "route") {
        rows.push(item(row.label, function () { callHost("InsertWaypoint", [row.type || "Continue", latitude, longitude, true]); }));
      } else if (row.type === "Finish" && routeCount() >= 2 && clickNearStart(latitude, longitude)) {
        rows.push(item(row.label, function () { callHost("CloseLoop", []); }));
      } else {
        rows.push(item(row.label, function () { callHost("InsertWaypoint", [row.type, latitude, longitude, false]); }));
      }
    });
    var offersFinish = (spec || []).some(function (row) { return row.type === "Finish"; });
    if (!offersFinish && routeCount() >= 2 && clickNearStart(latitude, longitude)) {
      rows.unshift(item("FINISH", function () { callHost("CloseLoop", []); }));
    }
    showMenu(x, y, rows);
  }

  function stopMarkerEvent(event) {
    if (event && event.originalEvent && event.originalEvent.preventDefault) {
      event.originalEvent.preventDefault();
    }
    if (typeof L !== "undefined" && L.DomEvent) {
      if (L.DomEvent.stop) {
        L.DomEvent.stop(event);
      } else if (L.DomEvent.stopPropagation) {
        L.DomEvent.stopPropagation(event);
      }
    }
  }

  function placeArmedMove(latlng) {
    if (pendingMoveIndex === null || pendingMoveIndex === undefined || !latlng) {
      return false;
    }
    var moveIndex = pendingMoveIndex;
    pendingMoveIndex = null;
    callHost("MoveWaypoint", [moveIndex, latlng.lat, latlng.lng]);
    return true;
  }

  function armDrag(marker, index) {
    marker.on("mousedown", function (event) {
      stopMarkerEvent(event);
      if (pendingMoveIndex !== null && pendingMoveIndex !== undefined) {
        if (map.dragging && map.dragging.disable) {
          map.dragging.disable();
        }
        function onPlace(ev) {
          if (map.off) {
            map.off("mouseup", onPlace);
          }
          if (map.dragging && map.dragging.enable) {
            map.dragging.enable();
          }
          placeArmedMove(ev && ev.latlng);
        }
        map.on("mouseup", onPlace);
        return;
      }
      if (map.dragging && map.dragging.disable) {
        map.dragging.disable();
      }
      var moved = false;
      function onMove(ev) {
        moved = true;
        if (marker.setLatLng && ev.latlng) {
          marker.setLatLng(ev.latlng);
        }
      }
      function onUp(ev) {
        if (map.off) {
          map.off("mousemove", onMove);
          map.off("mouseup", onUp);
        }
        if (map.dragging && map.dragging.enable) {
          map.dragging.enable();
        }
        if (moved && ev && ev.latlng) {
          suppressClick = true;
          callHost("MoveWaypoint", [index, ev.latlng.lat, ev.latlng.lng]);
        }
      }
      map.on("mousemove", onMove);
      map.on("mouseup", onUp);
    });
  }

  function applyOptions(options) {
    if (!options) {
      return;
    }
    if (options.standardFinish) {
      standardFinishLabel = options.standardFinish;
    }
    if (options.intervals && options.intervals.length) {
      intervalDistances = options.intervals;
    }
    if (options.connect && options.connect.length) {
      connectTypes = options.connect;
    }
    if (options.mapMenuLast && options.mapMenuLast.length) {
      mapMenuLast = options.mapMenuLast;
    }
    if (options.mapMenuEarlier && options.mapMenuEarlier.length) {
      mapMenuEarlier = options.mapMenuEarlier;
    }
    if (typeof options.nearStartMeters === "number" && options.nearStartMeters > 0) {
      nearStartMeters = options.nearStartMeters;
    }
    if (typeof options.samePointMeters === "number" && options.samePointMeters > 0) {
      samePointMeters = options.samePointMeters;
    }
  }

  function draw(waypoints) {
    if (!map || typeof L === "undefined") {
      return;
    }
    hideMenu();
    currentPins = waypoints || [];
    var stillThere = false;
    currentPins.forEach(function (pin, i) {
      if (pinIndex(pin, i) === selectedIndex && !isInterval(pin)) {
        stillThere = true;
      }
    });
    if (!stillThere) {
      selectedIndex = -1;
    }
    if (markers) {
      markers.clearLayers();
    } else {
      markers = L.layerGroup().addTo(map);
    }
    var line = [];
    currentPins.forEach(function (pin, index) {
      var at = pinIndex(pin, index);
      var interval = isInterval(pin);
      var marker = L.marker([pin.latitude, pin.longitude], { icon: markerIcon(pin), draggable: false })
        .addTo(markers);
      if (pin.type === "TurnLeft" || pin.type === "TurnRight" || pin.type === "Announcement") {
        marker.bindPopup(turnPopup(pin, at));
      }
      if (pin.type === "Start" && marker.setZIndexOffset) {
        marker.setZIndexOffset(1000);
      }
      marker.on("click", function (event) {
        if (placeArmedMove(event && event.latlng)) {
          stopMarkerEvent(event);
          return;
        }
        stopMarkerEvent(event);
        if (!interval) {
          selectedIndex = at;
        }
        if ((pin.type === "TurnLeft" || pin.type === "TurnRight" || pin.type === "Announcement") && marker.openPopup) {
          marker.openPopup();
        }
      });
      marker.on("contextmenu", function (event) {
        stopMarkerEvent(event);
        var point = pointer(event);
        openMarkerMenu(point.x, point.y, at, interval);
      });
      armDrag(marker, at);
      if (!interval) {
        line.push([pin.latitude, pin.longitude]);
      }
    });
    if (line.length > 1) {
      L.polyline(line, { color: "#1d4e89" }).addTo(markers);
    }
  }

  function sharesFinish(start) {
    var found = false;
    (currentPins || []).forEach(function (pin) {
      if (pin.type === "Finish"
        && distanceMeters(pin.latitude, pin.longitude, start.latitude, start.longitude) < samePointMeters) {
        found = true;
      }
    });
    return found;
  }

  function markerIcon(pin) {
    var type = pin && pin.type;
    var glyph = "•";
    var color = "#1d4e89";
    var startFinish = type === "Start" && sharesFinish(pin);
    if (type === "Start") { glyph = startFinish ? "S/F" : "S"; color = "#1b7f3a"; }
    else if (type === "Finish") { glyph = "F"; color = "#8b1e3f"; }
    else if (type === "TurnLeft" || type === "BearLeft") { glyph = "L"; }
    else if (type === "TurnRight" || type === "BearRight") { glyph = "R"; }
    else if (type === "TurnAround") { glyph = "U"; }
    else if (type === "Summit") { glyph = "▲"; color = "#000000"; }
    else if (type === "FeedZone") { glyph = "Z"; color = "#6b4f1d"; }
    else if (type === "Announcement") { glyph = "A"; color = "#585880"; }
    else if (type === "StartInterval" || type === "FinishInterval") { glyph = "I"; color = "#e67e22"; }
    var width = startFinish ? 36 : 22;
    var mark = startFinish ? " class=\"start-finish\"" : "";
    return L.divIcon({
      className: "course-pin",
      html: "<span" + mark + " style=\"background:" + color + "\">" + glyph + "</span>",
      iconSize: [width, 22],
      iconAnchor: [width / 2, 11]
    });
  }

  function turnPopup(pin, index) {
    var text = pin.text ? String(pin.text) : "";
    var safe = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
    var html = "<div class=\"turn-popup\"><p>" + (pin.type || "Waypoint") + "</p>"
      + "<textarea id=\"turn-text-" + index + "\">" + safe + "</textarea>"
      + "<button type=\"button\" id=\"turn-save-" + index + "\">Save</button></div>";
    return html;
  }

  function tileLayer() {
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: attribution,
      maxZoom: 19
    }).addTo(map);
  }

  function mount(selector, waypoints, host, center, types, options) {
    var element = document.querySelector(selector);
    if (!element || typeof L === "undefined") {
      return;
    }
    unmount();
    editorHost = host;
    if (types && types.length) {
      menuTypes = types;
    }
    applyOptions(options);
    var first = waypoints && waypoints.length ? waypoints[0] : null;
    var fallback = center && isFinite(center.latitude) && isFinite(center.longitude)
      ? [center.latitude, center.longitude]
      : [40.5853, -105.0844];
    // A course that already has points stays on that course. The saved home is only the empty-map center.
    var view = first ? [first.latitude, first.longitude] : fallback;
    map = L.map(element.id, { zoomControl: false, keyboard: false }).setView(view, first ? 14 : 13);
    tileLayer();
    if (map.getContainer) {
      map.getContainer().addEventListener("contextmenu", function (event) {
        event.preventDefault();
      });
    }
    draw(waypoints);
    map.on("click", function (event) {
      hideMenu();
      if (suppressClick) {
        suppressClick = false;
        return;
      }
      if (pendingMoveIndex !== null && pendingMoveIndex !== undefined) {
        var index = pendingMoveIndex;
        pendingMoveIndex = null;
        callHost("MoveWaypoint", [index, event.latlng.lat, event.latlng.lng]);
        return;
      }
      callHost("AddWaypoint", [event.latlng.lat, event.latlng.lng]);
    });
    map.on("contextmenu", function (event) {
      stopMarkerEvent(event);
      var point = pointer(event);
      openMapMenu(point.x, point.y, event.latlng.lat, event.latlng.lng);
    });
    // Leaflet stops clicks inside a popup before they bubble, so Save is bound on the button itself.
    map.on("popupopen", function (event) {
      var root = event.popup && event.popup.getElement ? event.popup.getElement() : null;
      if (!root) {
        return;
      }
      var button = root.querySelector("button[id^='turn-save-']");
      if (!button || button.getAttribute("data-bound") === "1") {
        return;
      }
      button.setAttribute("data-bound", "1");
      var saveTurn = function (domEvent) {
        if (domEvent) {
          domEvent.preventDefault();
          domEvent.stopPropagation();
        }
        var index = Number(button.id.slice("turn-save-".length));
        var box = root.querySelector("#turn-text-" + index);
        if (!box || !isFinite(index)) {
          return;
        }
        callHost("SetWaypointText", [index, box.value]);
      };
      button.addEventListener("pointerdown", saveTurn);
      button.addEventListener("click", saveTurn);
    });
    var lastTurnSave = 0;
    popupHandler = function (event) {
      var target = event.target;
      var button = target && target.closest ? target.closest("button[id^='turn-save-']") : null;
      if (!button) {
        return;
      }
      var now = Date.now();
      if (now - lastTurnSave < 400) {
        return;
      }
      lastTurnSave = now;
      var index = Number(button.id.slice("turn-save-".length));
      var box = document.getElementById("turn-text-" + index);
      if (!box || !isFinite(index)) {
        return;
      }
      event.preventDefault();
      callHost("SetWaypointText", [index, box.value]);
    };
    // A real press never becomes a click: the map swallows it. pointerdown still fires.
    document.addEventListener("pointerdown", popupHandler, true);
    document.addEventListener("click", popupHandler, true);
    keyHandler = function (event) {
      var tag = event.target && event.target.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
        return;
      }
      if ((event.ctrlKey || event.metaKey) && (event.key === "z" || event.key === "Z")) {
        event.preventDefault();
        callHost("UndoLastSegment");
        return;
      }
      if (event.key === "+" || event.key === "=" || event.key === "Add") {
        event.preventDefault();
        zoomBy(1);
        return;
      }
      if (event.key === "-" || event.key === "_" || event.key === "Subtract") {
        event.preventDefault();
        zoomBy(-1);
        return;
      }
      if (event.key === "Delete" && selectedIndex >= 0) {
        event.preventDefault();
        callHost("DeleteWaypoint", [selectedIndex]);
      }
    };
    document.addEventListener("keydown", keyHandler);
    watchResize();
    if (window.requestAnimationFrame) {
      window.requestAnimationFrame(fitMap);
    } else {
      fitMap();
    }
  }

  function fitMap() {
    if (map && map.invalidateSize) {
      map.invalidateSize();
    }
  }

  function watchResize() {
    if (resizeHandler) {
      return;
    }
    resizeHandler = function () { fitMap(); };
    window.addEventListener("resize", resizeHandler);
  }

  function mountLocationPicker(selector, latitude, longitude, host) {
    var element = document.querySelector(selector);
    if (!element || typeof L === "undefined") {
      return;
    }
    unmount();
    var lat = isFinite(latitude) ? latitude : 40.5853;
    var lng = isFinite(longitude) ? longitude : -105.0844;
    map = L.map(element.id).setView([lat, lng], 13);
    tileLayer();
    homeMarker = L.circleMarker([lat, lng], { radius: 8, color: "#1d4e89" }).addTo(map);
    map.on("click", function (event) {
      setLocation(event.latlng.lat, event.latlng.lng);
      host.invokeMethodAsync("SetHomeLocation", event.latlng.lat, event.latlng.lng);
    });
  }

  function setLocation(latitude, longitude) {
    if (!map || typeof L === "undefined") {
      return;
    }
    map.setView([latitude, longitude], Math.max(map.getZoom(), 13), { animate: false });
    if (homeMarker) {
      homeMarker.setLatLng([latitude, longitude]);
    }
  }

  function setWaypoints(waypoints, options) {
    applyOptions(options);
    draw(waypoints);
  }

  function elevationUnits(accountKey, sharedKey) {
    try {
      if (accountKey) {
        var account = localStorage.getItem(accountKey);
        if (account === "metric" || account === "english") {
          return account;
        }
      }
      var shared = localStorage.getItem(sharedKey || "gpstrainer.elevationUnits");
      if (shared === "metric" || shared === "english") {
        return shared;
      }
    } catch (err) {
      return "english";
    }
    return "english";
  }

  function rememberElevationUnits(key, value) {
    try {
      if (!key || (value !== "metric" && value !== "english")) {
        return;
      }
      localStorage.setItem(key, value);
    } catch (err) {
      return;
    }
  }

  function showProfile() {
    try {
      return localStorage.getItem("gpstrainer.showProfile") !== "false";
    } catch (err) {
      return true;
    }
  }

  function rememberShowProfile(open) {
    try {
      localStorage.setItem("gpstrainer.showProfile", open ? "true" : "false");
    } catch (err) {
      return;
    }
  }

  function openProfileMenu(x, y, clientX, clientY) {
    showMenu(x, y, [
      group("Interval", intervalDistances.map(function (distance) {
        return item(distance, function () { callHost("ProfileInterval", [distance, clientX, clientY]); });
      })),
      item("Turn Around", function () { callHost("ProfilePlace", ["TurnAround", clientX, clientY]); }),
      item("Feed Zone", function () { callHost("ProfilePlace", ["FeedZone", clientX, clientY]); }),
      item("Summit", function () { callHost("ProfilePlace", ["Summit", clientX, clientY]); }),
      item("Announcement", function () { callHost("ProfilePlace", ["Announcement", clientX, clientY]); })
    ]);
  }

  function profileViewX(id, clientX, clientY) {
    var svg = document.getElementById(id);
    if (!svg || !svg.createSVGPoint || !svg.getScreenCTM) {
      return 68;
    }
    var point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    var matrix = svg.getScreenCTM();
    if (!matrix || !matrix.inverse) {
      return 68;
    }
    var local = point.matrixTransform(matrix.inverse());
    return local && isFinite(local.x) ? local.x : 68;
  }

  function download(name, text) {
    var blob = new Blob([text], { type: "application/gpx+xml" });
    var url = URL.createObjectURL(blob);
    var link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  }

  function setView(latitude, longitude) {
    if (!map || !map.setView || !isFinite(latitude) || !isFinite(longitude)) {
      return;
    }
    var zoom = map.getZoom ? map.getZoom() : 13;
    map.setView([latitude, longitude], Math.max(zoom, 14));
  }

  function unmount() {
    if (resizeHandler) {
      window.removeEventListener("resize", resizeHandler);
      resizeHandler = null;
    }
    hideMenu();
    if (keyHandler) {
      document.removeEventListener("keydown", keyHandler);
      keyHandler = null;
    }
    if (popupHandler) {
      document.removeEventListener("pointerdown", popupHandler, true);
      document.removeEventListener("click", popupHandler, true);
      popupHandler = null;
    }
    pendingMoveIndex = null;
    selectedIndex = -1;
    currentPins = [];
    if (map) {
      map.remove();
      map = undefined;
      markers = undefined;
      homeMarker = undefined;
    }
  }

  return {
    mount: mount,
    mountLocationPicker: mountLocationPicker,
    setLocation: setLocation,
    setView: setView,
    unmount: unmount,
    setWaypoints: setWaypoints,
    download: download,
    profileViewX: profileViewX,
    elevationUnits: elevationUnits,
    rememberElevationUnits: rememberElevationUnits,
    showProfile: showProfile,
    rememberShowProfile: rememberShowProfile,
    openProfileMenu: openProfileMenu,
    fit: fitMap,
    getZoom: function () { return map && map.getZoom ? map.getZoom() : null; },
    getCenter: function () {
      if (!map || !map.getCenter) {
        return null;
      }
      var center = map.getCenter();
      return center ? { latitude: center.lat, longitude: center.lng } : null;
    }
  };
})();
