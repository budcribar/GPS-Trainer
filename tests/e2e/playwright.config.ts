import { defineConfig } from "@playwright/test";

const dotnet = process.env.DOTNET ?? `${process.env.HOME}/.dotnet/dotnet`;

export default defineConfig({
  testDir: ".",
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ["line"],
    ["html", { open: "never", outputFolder: "playwright-report" }],
    ["junit", { outputFile: "test-results/junit.xml" }]
  ],
  use: {
    baseURL: "http://127.0.0.1:5097",
    viewport: { width: 1400, height: 900 },
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  webServer: {
    command: `${dotnet} run --project src/GpsTrainer.Server/GpsTrainer.Server.csproj --launch-profile http`,
    url: "http://127.0.0.1:5097",
    reuseExistingServer: !process.env.CI,
    cwd: "../..",
    timeout: 180_000,
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      ASPNETCORE_ENVIRONMENT: "Development",
      DATABASE_URL: "",
      ConnectionStrings__GpsTrainer: "Data Source=/tmp/gpstrainer-e2e.db",
      PATH: `${process.env.HOME}/.dotnet:${process.env.PATH ?? ""}`
    }
  },
  projects: [
    {
      name: "local",
      testMatch: /\.spec\.ts$/,
      testIgnore: /live/
    },
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
