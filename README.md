# GPS-Trainer

GPS course training app: Blazor web site, ASP.NET Core server, and .NET MAUI Android app.

| Project | What it is |
|---|---|
| `src/GpsTrainer.Shared` | Domain model, distance and pace formatting, waypoint countdown, local SQLite schema, REST DTOs |
| `src/GpsTrainer.App` | .NET MAUI app (`net10.0-android`, and `net10.0-ios` on macOS) |
| `src/GpsTrainer.Server` | ASP.NET Core API. Hosts the Blazor site. SQLite for development, PostgreSQL for gps-trainer.com |
| `src/GpsTrainer.Web` | Blazor WebAssembly site |
| `tests/GpsTrainer.Shared.Tests` | Distance formatting, countdown loop, schema, speech, sync plan |
| `tests/GpsTrainer.Server.Tests` | API flow: signup, login, course, waypoints, race, positions |
| `tests/GpsTrainer.Web.Tests` | Course editor page against a fake map |

```bash
dotnet test GPS-Trainer.sln
dotnet build src/GpsTrainer.App/GpsTrainer.App.csproj -f net10.0-android
```

The iOS head is in the project and is not built on Linux. Nothing here is signed or published.

## Course map provider

The course editor draws through `ICourseMap`. `Map:Provider` in the web app settings chooses the implementation. An empty value and `Leaflet` both use `LeafletCourseMap`, which calls the existing `wwwroot/course-editor/courseEditor.js` script. The page does not change.

To add a provider such as Google:

1. Add a script under `src/GpsTrainer.Web/wwwroot/course-editor/` and load it from `index.html`.
2. Add a class that implements `ICourseMap` and raises the click, right-click, pin click, drag, and menu events.
3. Return that class from `CourseMaps.Create` when `Map:Provider` is the new name.

Leave `CourseEditor.razor` as it is.

## OpenRouteService

Directions, elevation, and geocoding use the HeiGIT host. The key stays on the server (`Routing__OrsApiKey` or `ORS_API_KEY`) and is sent as the `Authorization` header.

| Key | Default | Path |
|---|---|---|
| `Routing:DirectionsBaseUrl` | `https://api.heigit.org/openrouteservice/` | `v2/directions/{profile}/geojson` |
| `Routing:ElevationBaseUrl` | `https://api.heigit.org/openelevationservice/v0/` | `line` |
| `Routing:GeocodeBaseUrl` | `https://api.heigit.org/pelias/v1/` | `search?size=1&text=` |

These three defaults win. `Routing:BaseUrl` is not set in `appsettings.json`, and a leftover `Routing__BaseUrl` does not replace them. That key is used only when all three bases are unset. If it is `https://api.openrouteservice.org/`, the old paths (`elevation/line` and `geocode/search`) stay in place until that host shuts down (2–6 Nov 2026). Railway should keep `Routing__OrsApiKey` and does not need `Routing__BaseUrl`.
