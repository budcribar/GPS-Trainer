import { defineConfig } from "@playwright/test";

const dotnet = process.env.DOTNET ?? `${process.env.HOME}/.dotnet/dotnet`;

export default defineConfig({
  testDir: ".",
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:5097",
    viewport: { width: 1400, height: 900 },
    trace: "retain-on-failure"
  },
  webServer: {
    command: `${dotnet} run --project src/GpsTrainer.Server/GpsTrainer.Server.csproj --launch-profile http`,
    url: "http://127.0.0.1:5097",
    reuseExistingServer: true,
    cwd: "../..",
    timeout: 180_000,
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      PATH: `${process.env.HOME}/.dotnet:${process.env.PATH ?? ""}`
    }
  },
  projects: [
    { name: "local", testMatch: /(?:editor|autofill|account)\.spec\.ts/ },
    {
      name: "live",
      testMatch: /live\.spec\.ts/,
      use: { baseURL: "https://web-production-7e6fc.up.railway.app" }
    },
    {
      name: "live-routing",
      testMatch: /live-routing\.spec\.ts/,
      use: { baseURL: "https://web-production-7e6fc.up.railway.app" }
    }
  ]
});
