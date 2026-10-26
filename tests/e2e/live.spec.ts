import { expect, test } from "@playwright/test";
import { freePoint } from "./editor.helpers";

// Read-only checks against the hosted site. These do not sign in, save, import, or generate a route.
test("the live home page keeps the logo subtitle out of the menu", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("header .domain-name")).toHaveText("gps-trainer");
  await expect(page.locator("header .domain-com")).toHaveText(".com");
  await expect(page.locator("header nav")).not.toContainText("gps-trainer");
  await expect(page.locator("header")).not.toContainText("gpsTrainer");
});

test("the live editor shows the bar, map, and floating dialogs without writing a course", async ({ page }) => {
  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor({ timeout: 60_000 });
  await expect(page.locator(".editor-bar").getByRole("button", { name: "Zoom To" })).toBeVisible();
  await expect(page.locator(".editor-bar .domain-name")).toHaveText("gps-trainer");
  await expect(page.locator("#PanelDetails")).toBeVisible();
  await expect(page.locator("#HelpPanel")).toBeVisible();
  await expect(page.locator("header .site-brand")).toHaveCount(0);
  const writes = [];
  page.on("request", (request) => {
    if (request.method() !== "GET" && request.method() !== "HEAD")
      writes.push(`${request.method()} ${request.url()}`);
  });
  const point = await freePoint(page, "end");
  await page.locator("#course-map").click({ button: "right", position: point });
  await expect(page.locator("#waypoint-type-menu")).toContainText("CONTINUE");
  expect(writes.filter((line) => !line.includes("/api/routing/"))).toEqual([]);
});
