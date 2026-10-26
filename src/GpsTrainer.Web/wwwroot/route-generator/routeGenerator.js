// Preview map for generated Fort Collins loops. Separate from the course editor.
(function () {
  var map;
  var layer;
  var startMarker;
  var host;

  function ready() {
    return typeof L !== "undefined" && map;
  }

  window.gpsTrainerRouteGenerator = {
    mount: function (id, latitude, longitude, dotnet) {
      if (typeof L === "undefined") {
        return;
      }
      host = dotnet;
      var node = document.querySelector(id);
      if (!node) {
        return;
      }
      if (map) {
        map.remove();
      }
      map = L.map(node).setView([latitude, longitude], 13);
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        maxZoom: 19
      }).addTo(map);
      layer = L.layerGroup().addTo(map);
      startMarker = L.circleMarker([latitude, longitude], { radius: 8, color: "#1b7f3a" }).addTo(map);
      map.on("click", function (event) {
        if (startMarker) {
          startMarker.setLatLng(event.latlng);
        }
        if (host) {
          host.invokeMethodAsync("SetStart", event.latlng.lat, event.latlng.lng);
        }
      });
    },
    setStart: function (latitude, longitude) {
      if (!ready() || !startMarker) {
        return;
      }
      startMarker.setLatLng([latitude, longitude]);
      map.panTo([latitude, longitude]);
    },
    show: function (routes) {
      if (!ready() || !layer) {
        return;
      }
      layer.clearLayers();
      var bounds = [];
      (routes || []).forEach(function (route) {
        var line = (route.points || []).map(function (point) {
          bounds.push(point);
          return point;
        });
        if (line.length > 1) {
          L.polyline(line, { color: route.color || "#1d4e89", weight: 5, opacity: 0.85 }).addTo(layer);
        }
      });
      if (bounds.length > 1) {
        map.fitBounds(bounds, { padding: [24, 24] });
      }
    },
    unmount: function () {
      if (map) {
        map.remove();
        map = undefined;
        layer = undefined;
        startMarker = undefined;
      }
      host = undefined;
    }
  };
})();
