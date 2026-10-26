import { expect, test, type Page } from "@playwright/test";

// Read-only checks against production, which has the routing key.
// No sign-in, no save, no import. Run only after that deploy is live:
//   npx playwright test --project=live-routing

test.use({
  storageState: { cookies: [], origins: [] }
});

test.beforeEach(async ({ page }) => {
  const writes: string[] = [];
  page.on("request", (request) => {
    if (request.method() === "GET" || request.method() === "HEAD")
      return;
    if (request.url().includes("/api/routing/"))
      return;
    writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  await page.addInitScript(() => localStorage.removeItem("gpstrainer.session"));
  (page as Page & { routingWrites?: string[] }).routingWrites = writes;
});

test("unsigned clicks around Fort Collins follow roads and the profile fills in", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor({ timeout: 60_000 });
  await expect(page.locator(".editor-signin, .notice")).toContainText(/sign in/i);

  const points = await freeSpots(page, 3);
  expect(points.length).toBe(3);
  for (const point of points) {
    const before = await page.locator(".course-pin").count();
    await page.locator("#course-map").click({ position: point });
    await expect.poll(() => page.locator(".course-pin").count(), { timeout: 40_000 }).toBeGreaterThan(before);
  }

  await expect.poll(() => lineVertices(page, "#course-map"), { timeout: 20_000 }).toBeGreaterThan(11);
  const notices = (await page.locator(".notice").allTextContents()).join("\n");
  expect(notices).not.toMatch(/sign in to follow roads|straight segment/i);
  await expect(page.locator("#PanelProfile")).toBeVisible();
  await expect(page.locator("#PanelProfile")).not.toContainText("No elevation");
  await expect(page.locator("#course-profile polyline")).toHaveAttribute("points", /\d/);
  expect(writesOf(page)).toEqual([]);
});

test("FINISH on the start pin closes the loop on the road and shows S/F", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor({ timeout: 60_000 });
  for (const point of await freeSpots(page, 3)) {
    const before = await page.locator(".course-pin").count();
    await page.locator("#course-map").click({ position: point });
    await expect.poll(() => page.locator(".course-pin").count(), { timeout: 40_000 }).toBeGreaterThan(before);
  }

  await expect.poll(() => lineVertices(page, "#course-map"), { timeout: 20_000 }).toBeGreaterThan(11);
  const startPin = page.locator(".course-pin").filter({ hasText: "S" }).first();
  await startPin.click({ button: "right", force: true });
  const menu = page.locator("#waypoint-type-menu");
  await expect(menu).toBeVisible();
  await menu.getByRole("button", { name: "FINISH", exact: true }).click();
  await expect.poll(() => page.locator(".course-pin span").allTextContents(), { timeout: 40_000 }).toContain("S/F");
  await expect.poll(() => lineVertices(page, "#course-map"), { timeout: 20_000 }).toBeGreaterThan(11);
  const notices = (await page.locator(".notice").allTextContents()).join("\n");
  expect(notices).not.toMatch(/routable|could not find|straight segment|350/i);
  expect(notices).toMatch(/loop closed at the start/i);
  const centers = await page.locator(".course-pin").evaluateAll((els) =>
    els.map((el) => {
      const box = el.getBoundingClientRect();
      return { text: (el.textContent ?? "").trim(), x: box.x + box.width / 2, y: box.y + box.height / 2 };
    })
  );
  const start = centers.find((pin) => pin.text === "S/F");
  const finish = centers.find((pin) => pin.text === "F");
  expect(start && finish).toBeTruthy();
  expect(Math.hypot(start!.x - finish!.x, start!.y - finish!.y)).toBeLessThan(8);
  expect(writesOf(page)).toEqual([]);
});

test("the loop generator draws a road route without an account", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/generate");
  const form = page.locator(".generate-form");
  const rendered = await form.waitFor({ timeout: 20_000 }).then(() => true).catch(() => false);
  if (!rendered) {
    test.skip(true, "The loop generator is not on this deploy.");
  }
  await page.locator("#generate-map.leaflet-container").waitFor({ timeout: 60_000 });
  await page.getByRole("button", { name: "Generate" }).click();
  await expect(page.locator(".loop-card").first()).toBeVisible({ timeout: 60_000 });
  await expect.poll(() => lineVertices(page, "#generate-map"), { timeout: 20_000 }).toBeGreaterThan(11);
  await expect(page.locator(".loop-card").getByRole("button", { name: "Open in editor" }).first()).toBeVisible();
  expect(writesOf(page)).toEqual([]);
});

function writesOf(page: Page): string[] {
  return (page as Page & { routingWrites?: string[] }).routingWrites ?? [];
}

async function lineVertices(page: Page, mapSelector: string): Promise<number> {
  return page.locator(mapSelector).evaluate((map) => {
    const paths = [...map.querySelectorAll(".leaflet-overlay-pane path")];
    let count = 0;
    for (const path of paths) {
      const drawn = path.getAttribute("d") ?? "";
      count += drawn.match(/-?\d+(?:\.\d+)?[\s,]+-?\d+(?:\.\d+)?/g)?.length ?? 0;
    }
    return count;
  });
}

async function freeSpots(page: Page, count: number): Promise<{ x: number; y: number }[]> {
  return page.evaluate((needed) => {
    const map = document.querySelector("#course-map, #generate-map")!.getBoundingClientRect();
    const blockers = [...document.querySelectorAll(".editor-dialog, .editor-bar, .editor-menu, header, .generate-form")].map((el) =>
      el.getBoundingClientRect()
    );
    const free: { x: number; y: number }[] = [];
    for (let y = 36; y < map.height - 36; y += 36) {
      for (let x = 36; x < map.width - 36; x += 36) {
        const vx = map.left + x;
        const vy = map.top + y;
        const covered = blockers.some((box) => vx >= box.left && vx <= box.right && vy >= box.top && vy <= box.bottom);
        const hit = document.elementFromPoint(vx, vy);
        const onPin = !!hit && !!(hit as Element).closest(".course-pin, .leaflet-marker-icon");
        if (!covered && !onPin)
          free.push({ x, y });
      }
    }
    if (free.length === 0)
      return [{ x: map.width / 2, y: map.height / 2 }];
    const chosen = [free[0]];
    while (chosen.length < needed && chosen.length < free.length) {
      let best = free[0];
      let bestScore = -1;
      for (const candidate of free) {
        const score = Math.min(...chosen.map((point) => (point.x - candidate.x) ** 2 + (point.y - candidate.y) ** 2));
        if (score > bestScore) {
          bestScore = score;
          best = candidate;
        }
      }
      if (bestScore < 80 * 80)
        break;
      chosen.push(best);
    }
    return chosen;
  }, count);
}

declare global {
  interface Window {
    gpsTrainerCourseEditor: { getZoom: () => number | null };
  }
}
