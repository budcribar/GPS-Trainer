// Browser copies of the sign-in session and an unsaved course.
window.gpsTrainerSession = {
  load: function () {
    try { return localStorage.getItem("gpstrainer.session"); } catch (err) { return null; }
  },
  save: function (json) {
    try { localStorage.setItem("gpstrainer.session", json); } catch (err) { return; }
  },
  clear: function () {
    try { localStorage.removeItem("gpstrainer.session"); } catch (err) { return; }
  }
};

window.gpsTrainerDraft = {
  load: function () {
    try { return localStorage.getItem("gpstrainer.draftCourse"); } catch (err) { return null; }
  },
  save: function (json) {
    try { localStorage.setItem("gpstrainer.draftCourse", json); } catch (err) { return; }
  },
  clear: function () {
    try { localStorage.removeItem("gpstrainer.draftCourse"); } catch (err) { return; }
  }
};

// Drag the course and help dialogs by their headers. Positions are clamped here
// while the pointer moves; C# decides whether a saved spot should reset.
window.gpsTrainerDialogs = (function () {
  var dotNet = null;
  var drag = null;
  var onResize = null;
  var margin = 8;

  function controlBox(el) {
    if (!el || !el.getBoundingClientRect)
      return null;
    var rect = el.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1)
      return null;
    return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
  }

  function blockedControls() {
    var boxes = [];
    [document.querySelector(".editor-find button"), document.querySelector(".gpx-tools button"), document.getElementById("PanelProfile")].forEach(function (el) {
      var box = controlBox(el);
      if (box)
        boxes.push(box);
    });
    return boxes;
  }

  function clamp(left, top, width, height) {
    var vw = window.innerWidth;
    var vh = window.innerHeight;
    if (width + margin * 2 > vw)
      left = margin;
    else
      left = Math.min(Math.max(left, margin), vw - width - margin);
    if (height + margin * 2 > vh)
      top = margin;
    else
      top = Math.min(Math.max(top, margin), vh - height - margin);
    return { left: left, top: top };
  }

  function headerOf(target) {
    if (!target || !target.closest)
      return null;
    return target.closest(".editor-dialog-header");
  }

  function onPointerDown(event) {
    if (event.button !== undefined && event.button !== 0)
      return;
    var header = headerOf(event.target);
    if (!header)
      return;
    if (event.target.closest && event.target.closest("button"))
      return;
    var dialog = header.closest(".editor-dialog");
    if (!dialog)
      return;
    var rect = dialog.getBoundingClientRect();
    drag = {
      dialog: dialog,
      name: dialog.getAttribute("data-dialog"),
      pointerId: event.pointerId,
      offsetX: event.clientX - rect.left,
      offsetY: event.clientY - rect.top
    };
    if (header.setPointerCapture)
      header.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  function onPointerMove(event) {
    if (!drag || event.pointerId !== drag.pointerId)
      return;
    var rect = drag.dialog.getBoundingClientRect();
    var next = clamp(event.clientX - drag.offsetX, event.clientY - drag.offsetY, rect.width, rect.height);
    drag.dialog.style.left = next.left + "px";
    drag.dialog.style.top = next.top + "px";
    drag.dialog.style.right = "auto";
    drag.dialog.style.bottom = "auto";
  }

  function onPointerUp(event) {
    if (!drag || event.pointerId !== drag.pointerId)
      return;
    var name = drag.name;
    var left = parseFloat(drag.dialog.style.left);
    var top = parseFloat(drag.dialog.style.top);
    drag = null;
    if (dotNet && name && !isNaN(left) && !isNaN(top))
      dotNet.invokeMethodAsync("RememberDialog", name, left, top);
  }

  return {
    load: function (key) {
      try { return localStorage.getItem(key); } catch (err) { return null; }
    },
    save: function (key, json) {
      try { localStorage.setItem(key, json); } catch (err) { return; }
    },
    measure: function (id) {
      var el = document.getElementById(id);
      if (!el)
        return null;
      var rect = el.getBoundingClientRect();
      return {
        width: rect.width,
        height: rect.height,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        blocked: blockedControls()
      };
    },
    watch: function (ref) {
      this.unwatch();
      dotNet = ref;
      document.addEventListener("pointerdown", onPointerDown);
      document.addEventListener("pointermove", onPointerMove);
      document.addEventListener("pointerup", onPointerUp);
      document.addEventListener("pointercancel", onPointerUp);
      onResize = function () {
        if (dotNet)
          dotNet.invokeMethodAsync("ReflowDialogs");
      };
      window.addEventListener("resize", onResize);
    },
    unwatch: function () {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("pointermove", onPointerMove);
      document.removeEventListener("pointerup", onPointerUp);
      document.removeEventListener("pointercancel", onPointerUp);
      if (onResize)
        window.removeEventListener("resize", onResize);
      onResize = null;
      dotNet = null;
      drag = null;
    }
  };
})();
