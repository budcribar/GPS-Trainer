import { expect, type APIRequestContext, type Locator, type Page } from "@playwright/test";

export const mapReady = "#course-map.leaflet-container";

export async function openEditor(page: Page): Promise<void> {
  await page.goto("/editor");
  await page.locator(mapReady).waitFor({ state: "visible", timeout: 60_000 });
}

/** The default HELP dialog sits on the toolbar. Tests that click those controls close it first. */
export async function closeHelp(page: Page): Promise<void> {
  const close = page.getByRole("button", { name: "Close help" });
  if (await close.count())
    await close.click();
  await expect(page.locator("#HelpPanel")).toHaveCount(0);
}

/** A point on the map that is not covered by a floating dialog. Coordinates are relative to #course-map. */
export async function freePoint(page: Page, prefer: "start" | "end"): Promise<{ x: number; y: number }> {
  const point = await page.evaluate((which) => {
    const map = document.querySelector("#course-map")!.getBoundingClientRect();
    const blockers = [...document.querySelectorAll(".editor-dialog, .editor-bar, .editor-menu, header")].map((el) =>
      el.getBoundingClientRect()
    );
    const free: { x: number; y: number }[] = [];
    for (let y = 24; y < map.height - 24; y += 28) {
      for (let x = 24; x < map.width - 24; x += 28) {
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
      return { x: map.width / 2, y: map.height / 2 };
    if (which === "start")
      return free[0];
    const origin = free[0];
    return free.reduce((far, next) => {
      const farD = (far.x - origin.x) ** 2 + (far.y - origin.y) ** 2;
      const nextD = (next.x - origin.x) ** 2 + (next.y - origin.y) ** 2;
      return nextD > farD ? next : far;
    }, free[0]);
  }, prefer);
  return point;
}

export async function clickMap(page: Page, point: { x: number; y: number }): Promise<void> {
  await page.locator("#course-map").click({ position: point });
}

export async function rightClickMap(page: Page, point: { x: number; y: number }): Promise<Locator> {
  await page.locator("#course-map").click({ button: "right", position: point });
  const menu = page.locator("#waypoint-type-menu");
  await expect(menu).toBeVisible();
  return menu;
}

export async function chooseRow(menu: Locator, label: string): Promise<void> {
  await menu.getByRole("button", { name: label, exact: true }).click();
}

export async function chooseSubmenu(menu: Locator, parent: string, child: string): Promise<void> {
  const row = menu.locator("li").filter({ has: menu.page().getByRole("button", { name: parent, exact: true }) }).first();
  await row.getByRole("button", { name: parent, exact: true }).click();
  await row.getByRole("button", { name: child, exact: true }).click();
}

/** Two clicks far enough apart that the segment is a real leg, not "too close". */
export async function drawTwoPoints(page: Page): Promise<void> {
  const start = await freePoint(page, "start");
  const end = await freePoint(page, "end");
  await clickMap(page, start);
  await expect(page.locator(".course-pin")).toHaveCount(1);
  await clickMap(page, end);
  await expect(page.locator(".course-pin").nth(1)).toBeVisible({ timeout: 30_000 });
}

export async function glyphs(page: Page): Promise<string[]> {
  return page.locator(".course-pin span").allTextContents();
}

let signedIn: string | undefined;

/** A throwaway local account. The password and token stay in memory and are never written to a log.
 *  Pass false when the test will sign out, so it does not revoke the account the other tests reuse. */
export async function localSession(request: APIRequestContext, reuse = true): Promise<string> {
  if (reuse && signedIn)
    return signedIn;
  const stamp = Date.now().toString(36);
  const login = `e2e${stamp}`;
  const password = `Pw-${stamp}-not-for-logs`;
  const created = await request.post("/api/users", {
    data: {
      login,
      password,
      firstName: "Map",
      lastName: "Test",
      email: `${login}@example.com`,
      country: "US",
      isMale: true,
      birthDate: "1990-01-01",
      racingCategoryKey: 0,
      wantsEmails: false
    }
  });
  if (!created.ok())
    throw new Error(`Could not create the local test account (${created.status()}).`);
  const signed = await request.post("/api/auth/login", { data: { login, password } });
  if (!signed.ok())
    throw new Error(`Could not sign in the local test account (${signed.status()}).`);
  const body = await signed.json() as { token?: string; user?: { login?: string } };
  if (!body.token || !body.user?.login)
    throw new Error("The local sign-in response had no session.");
  const json = JSON.stringify({ token: body.token, login: body.user.login, user: body.user });
  if (reuse)
    signedIn = json;
  return json;
}

export async function openSignedEditor(page: Page, request: APIRequestContext): Promise<void> {
  const json = await localSession(request);
  await page.addInitScript((stored) => {
    localStorage.setItem("gpstrainer.session", stored);
  }, json);
  await openEditor(page);
  await expect(page.locator(".editor-signin")).toHaveCount(0);
}
