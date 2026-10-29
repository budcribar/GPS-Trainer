import { expect, test } from "@playwright/test";
import { closeHelp, localSession, mapReady } from "./editor.helpers";

async function fits(page: import("@playwright/test").Page) {
  const width = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth
  }));
  expect(width.scroll).toBeLessThanOrEqual(width.client + 1);
}

test.describe("phone widths", () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

  test("public pages fit a 390-wide phone", async ({ page }) => {
    for (const path of ["/", "/download", "/help", "/contact", "/privacy", "/signup", "/login", "/courses"]) {
      await page.goto(path);
      await expect(page.locator("h1")).toBeVisible();
      await fits(page);
    }
  });

  test("help stays on screen and a touch reads elevation", async ({ page, request }) => {
    test.setTimeout(120_000);
    const stored = await localSession(request);
    const session = JSON.parse(stored) as { token?: string; login?: string };
    const created = await request.post("/api/courses", {
      headers: { authorization: `Bearer ${session.token}` },
      data: { name: "Phone climb", login: session.login, activityCode: 1, elapsedDistanceKm: 1.1 }
    });
    expect(created.ok()).toBeTruthy();
    const course = await created.json() as { courseId: string };
    const saved = await request.put(`/api/courses/${course.courseId}/waypoints`, {
      headers: { authorization: `Bearer ${session.token}` },
      data: [
        { courseId: course.courseId, latitude: 40.01, longitude: -105.27, waypointType: 11, altitudeKm: 1.5, elapsedDistanceKm: 0, timestampTicks: 11 },
        { courseId: course.courseId, latitude: 40.02, longitude: -105.27, waypointType: 12, altitudeKm: 1.62, elapsedDistanceKm: 1.11, timestampTicks: 12 }
      ]
    });
    expect(saved.status()).toBe(204);

    await page.addInitScript((json) => localStorage.setItem("gpstrainer.session", json), stored);
    await page.goto(`/editor/${course.courseId}`);
    await page.locator(mapReady).waitFor({ state: "visible", timeout: 60_000 });
    const help = page.locator("#HelpPanel");
    await expect(help).toBeVisible();
    const box = await help.boundingBox();
    const viewport = page.viewportSize();
    expect(box && viewport).toBeTruthy();
    expect(box!.x).toBeGreaterThanOrEqual(-1);
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width + 1);
    await closeHelp(page);

    const chart = page.locator("#course-profile");
    await expect(chart).toBeVisible();
    await chart.tap();
    await expect(page.locator(".profile-hover")).toContainText(/\d/);
    await fits(page);
  });
});

test.describe("narrow phone", () => {
  test.use({ viewport: { width: 360, height: 640 }, isMobile: true, hasTouch: true });

  test("home and download fit 360 pixels", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("h1")).toBeVisible();
    await fits(page);
    await page.goto("/download");
    await expect(page.locator("h1")).toHaveText("Download");
    await fits(page);
  });
});
