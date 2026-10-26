// Reads input values the browser filled without raising input or change.
window.gpsTrainerFields = {
  readOne: function (id) {
    var el = document.getElementById(id);
    return el ? el.value : "";
  },
  read: function (ids) {
    var values = {};
    for (var i = 0; i < ids.length; i++)
      values[ids[i]] = window.gpsTrainerFields.readOne(ids[i]);
    return values;
  }
};
