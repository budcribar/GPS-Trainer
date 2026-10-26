import { expect, test } from "@playwright/test";
import {
  chooseRow,
  chooseSubmenu,
  closeHelp,
  drawTwoPoints,
  freePoint,
  glyphs,
  localSession,
  openEditor,
  openSignedEditor,
  rightClickMap
} from "./editor.helpers";

test.describe("unsigned editor", () => {
  test.beforeEach(async ({ page }) => {
    await openEditor(page);
  });

  test("the editor bar shows the logo, subtitle, and Zoom To", async ({ page }) => {
    const bar = page.locator(".editor-bar");
    await expect(bar.locator("img.site-logo")).toBeVisible();
    await expect(bar.locator(".domain-name")).toHaveText("gps-trainer");
    await expect(bar.locator(".domain-com")).toHaveText(".com");
    const name = await bar.locator(".domain-name").evaluate((el) => getComputedStyle(el).color);
    const com = await bar.locator(".domain-com").evaluate((el) => getComputedStyle(el).color);
    expect(name).toBe("rgb(0, 118, 189)");
    expect(com).toBe("rgb(232, 184, 54)");
    await expect(bar.getByRole("button", { name: "Zoom To" })).toBeVisible();
    await expect(page.locator("header")).not.toContainText("gpsTrainer");
    await expect(page.locator("header .site-brand")).toHaveCount(0);
  });

  test("course and help dialogs float over the map and remember where they were dragged", async ({ page }) => {
    const course = page.locator("#PanelDetails");
    const help = page.locator("#HelpPanel");
    await expect(course).toBeVisible();
    await expect(help).toBeVisible();
    const courseBox = await course.boundingBox();
    const helpBox = await help.boundingBox();
    const mapBox = await page.locator("#course-map").boundingBox();
    expect(courseBox && helpBox && mapBox).toBeTruthy();
    expect(courseBox!.y).toBeLessThan(mapBox!.y + mapBox!.height);
    expect(helpBox!.x).toBeLessThan(courseBox!.x);

    const before = courseBox!.x;
    const header = course.locator(".editor-dialog-header");
    const box = await header.boundingBox();
    expect(box).not.toBeNull();
    await page.mouse.move(box!.x + 40, box!.y + 10);
    await page.mouse.down();
    await page.mouse.move(box!.x + 40 - 160, box!.y + 10 + 50, { steps: 8 });
    await page.mouse.up();
    const after = await course.evaluate((el) => el.getBoundingClientRect().left);
    expect(after).toBeLessThan(before - 40);
    await page.reload();
    await page.locator("#course-map.leaflet-container").waitFor();
    const restored = await page.locator("#PanelDetails").evaluate((el) => el.getBoundingClientRect().left);
    expect(Math.abs(restored - after)).toBeLessThan(3);

    await page.getByRole("button", { name: "Close course" }).click();
    await page.getByRole("button", { name: "Close help" }).click();
    await expect(page.locator("#PanelDetails")).toHaveCount(0);
    await expect(page.locator("#HelpPanel")).toHaveCount(0);
    await page.getByRole("button", { name: "COURSE" }).click();
    await page.getByRole("button", { name: "HELP" }).click();
    await expect(page.locator("#PanelDetails")).toBeVisible();
    await expect(page.locator("#HelpPanel")).toBeVisible();
  });

  test("HELP sits beside the map and leaves Zoom To, Undo, and the profile clear", async ({ page }) => {
    await drawTwoPoints(page);
    await expectHelpClear(page);
    expect(await page.locator("#HelpPanel").evaluate((el) => el.getBoundingClientRect().x)).toBeGreaterThan(790);
    expect(await page.locator("#HelpPanel").evaluate((el) => el.getBoundingClientRect().x)).toBeLessThan(820);
    expect(await page.locator("#HelpPanel").evaluate((el) => el.getBoundingClientRect().y)).toBeGreaterThan(520);
    expect(await page.locator("#HelpPanel").evaluate((el) => el.getBoundingClientRect().y)).toBeLessThan(570);
  });

  test("a saved HELP position over the toolbar is put back beside the map", async ({ page }) => {
    await page.evaluate(() => localStorage.setItem("gpstrainer.helpDialog", JSON.stringify({ left: 8, top: 8 })));
    await page.reload();
    await page.locator("#course-map.leaflet-container").waitFor();
    await drawTwoPoints(page);
    await expectHelpClear(page);
    const spot = await page.evaluate(() => JSON.parse(localStorage.getItem("gpstrainer.helpDialog") || "{}"));
    expect(spot.left).toBeGreaterThan(790);
    expect(spot.top).toBeGreaterThan(520);
  });

  test("a right-click on the empty map offers the 2013 last-point rows", async ({ page }) => {
    const point = await freePoint(page, "end");
    const menu = await rightClickMap(page, point);
    for (const label of ["CONTINUE", "ROUTE", "TURN RIGHT", "TURN LEFT", "FINISH", "ZOOM", "MOVE"])
      await expect(menu.getByRole("button", { name: label, exact: true })).toBeVisible();
    await expect(menu).toContainText(/(\d+(\.\d+)? (mi|km) FINISH)/);
    await chooseSubmenu(menu, "ZOOM", "IN");
    await expect(page.locator("#waypoint-type-menu")).toHaveCount(0);
  });

  test("clicks without an account still drop a point, undo removes it, and units switch", async ({ page }) => {
    await closeHelp(page);
    const point = await freePoint(page, "end");
    await page.locator("#course-map").click({ position: point });
    await expect(page.locator(".course-pin").first()).toBeVisible();
    await expect(page.locator(".course-pin span").first()).toHaveText("S");

    const second = await freePoint(page, "start");
    await page.locator("#course-map").click({ position: second });
    await expect(page.locator(".course-pin").nth(1)).toBeVisible();
    const notices = (await page.locator(".notice").allTextContents()).join("\n");
    expect(notices).not.toMatch(/sign in to follow roads/i);

    await page.getByRole("button", { name: "Undo last segment" }).click();
    await expect(page.locator(".course-pin")).toHaveCount(1);
    await expect(page.locator(".notice")).toContainText("Removed the last segment.");
    await page.keyboard.press("Control+z");
    await expect(page.locator(".course-pin")).toHaveCount(0);

    await expect(page.locator(".distance-readout")).toContainText("mi");
    await page.locator(".units-button").click();
    await expect(page.locator(".distance-readout")).toContainText("km");
    await expect(page.locator(".units-button")).toHaveText("mi");
  });

  test("an empty Zoom To asks for an address and the profile stays closed until the course has distance", async ({ page }) => {
    await closeHelp(page);
    await page.getByRole("button", { name: "Zoom To" }).click();
    await expect(page.locator(".notice")).toContainText("Enter an address");
    await expect(page.locator("#PanelProfile")).toHaveCount(0);
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Download GPX" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.gpx$/i);
  });

  test("saving or importing without an account explains that a sign-in is required", async ({ page }) => {
    await page.locator("#PanelDetails").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".notice")).toContainText("Sign in first.");
    await page.getByRole("button", { name: "Import GPX" }).click();
    await page.locator("input[type=file]").setInputFiles({
      name: "loop.gpx",
      mimeType: "application/gpx+xml",
      buffer: Buffer.from(sampleGpx)
    });
    await expect(page.locator(".notice")).toContainText("Sign in before importing.");
  });
});

test.describe("signed-in header", () => {
  test("sign out sits beside the user name and returns home", async ({ page, request }) => {
    const json = await localSession(request, false);
    const login = JSON.parse(json).login as string;
    await page.addInitScript((stored) => {
      localStorage.setItem("gpstrainer.session", stored);
    }, json);
    await page.goto("/");
    const user = page.locator("header a.header-user");
    const signOut = page.locator("header button.header-signout");
    await expect(user).toHaveText(login);
    await expect(user).toHaveAttribute("href", "account");
    await expect(signOut).toBeVisible();

    await page.setViewportSize({ width: 390, height: 800 });
    await expect(signOut).toBeVisible();
    const narrow = await signOut.boundingBox();
    expect(narrow).toBeTruthy();
    expect(narrow!.width).toBeGreaterThan(40);
    expect(narrow!.x).toBeGreaterThanOrEqual(0);
    expect(narrow!.x + narrow!.width).toBeLessThanOrEqual(390);

    await user.click();
    await expect(page.locator("h1")).toHaveText("Account");
    await expect(page.locator("header button.header-signout")).toBeVisible();
    await page.locator("header button.header-signout").click();
    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator("header a[href='login']")).toHaveText("Sign in");
    await expect(page.locator("header button.header-signout")).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => localStorage.getItem("gpstrainer.session"))).toBeNull();
  });
});

test.describe("signed-in editor", () => {
  test.beforeEach(async ({ page, request }) => {
    await openSignedEditor(page, request);
    await closeHelp(page);
  });

  test("clicks add points and say when the road route is missing", async ({ page }) => {
    await drawTwoPoints(page);
    const notice = page.locator(".notice");
    if (await notice.count()) {
      const text = (await notice.textContent()) ?? "";
      expect(text).not.toMatch(/sign in/i);
      if (!/straight/i.test(text))
        expect(await page.locator(".course-pin").count()).toBeGreaterThan(2);
    }
    await expect(page.locator(".distance-readout")).not.toContainText("0.00");
    await page.getByRole("button", { name: "Undo last segment" }).click();
    await expect(page.locator(".course-pin")).toHaveCount(1);
    await expect(page.locator(".course-pin span")).toHaveText("S");
  });

  test("every map menu insert, the standard finish, MOVE, and ZOOM change the course", async ({ page }) => {
    test.setTimeout(180_000);
    await drawTwoPoints(page);
    const spot = await freePoint(page, "end");

    for (const [label, glyph] of [["TURN RIGHT", "R"], ["TURN LEFT", "L"], ["FINISH", "F"]] as const) {
      const before = await page.locator(".course-pin").count();
      const menu = await rightClickMap(page, spot);
      await chooseRow(menu, label);
      await expect(page.locator(".course-pin").nth(before)).toBeVisible({ timeout: 30_000 });
      await expect.poll(() => glyphs(page)).toContain(glyph);
      await page.getByRole("button", { name: "Undo last segment" }).click();
      await expect(page.locator(".course-pin")).toHaveCount(before);
    }

    const continueMenu = await rightClickMap(page, spot);
    await chooseRow(continueMenu, "CONTINUE");
    await expect.poll(async () => page.locator(".course-pin").count()).toBeGreaterThan(2);
    await page.getByRole("button", { name: "Undo last segment" }).click();

    const routeMenu = await rightClickMap(page, spot);
    await chooseRow(routeMenu, "ROUTE");
    await expect.poll(async () => (await page.locator(".notice").textContent()) ?? "pins").not.toMatch(/sign in/i);
    await page.getByRole("button", { name: "Undo last segment" }).click();

    const zoomBefore = await page.evaluate(() => window.gpsTrainerCourseEditor.getZoom());
    const zoomMenu = await rightClickMap(page, spot);
    await chooseSubmenu(zoomMenu, "ZOOM", "IN");
    await expect.poll(() => page.evaluate(() => window.gpsTrainerCourseEditor.getZoom())).toBeGreaterThan(zoomBefore ?? 0);
    const outMenu = await rightClickMap(page, spot);
    await chooseSubmenu(outMenu, "ZOOM", "OUT");

    const pinsBeforeMove = await page.locator(".course-pin").count();
    const moving = page.locator(".course-pin").last();
    const beforeMove = await moving.boundingBox();
    const moveSpot = await freePoint(page, "start");
    const moveMenu = await rightClickMap(page, moveSpot);
    await chooseRow(moveMenu, "MOVE");
    await expect(page.locator(".notice")).toContainText(/Waypoint moved\.|straight segment was kept/i);
    await expect(page.locator(".course-pin")).toHaveCount(pinsBeforeMove);
    const afterMove = await page.locator(".course-pin").last().boundingBox();
    expect(beforeMove && afterMove).toBeTruthy();
    expect(Math.hypot(afterMove!.x - beforeMove!.x, afterMove!.y - beforeMove!.y)).toBeGreaterThan(40);

    const finishMenu = await rightClickMap(page, spot);
    const standard = finishMenu.getByRole("button", { name: /\d+(\.\d+)? (mi|km) FINISH/ });
    await expect(standard).toBeVisible();
    await standard.click();
    await expect(page.locator(".notice")).toContainText(/Standard finish added\.|straight segment was kept/i);
    await expect.poll(() => glyphs(page)).toContain("F");
  });

  test("the pin menu deletes, connects, sets every marker type, and places every interval", async ({ page }) => {
    test.setTimeout(180_000);
    await drawTwoPoints(page);
    await page.locator(".course-pin").first().click({ button: "right" });
    const menu = page.locator("#waypoint-type-menu");
    await expect(menu).toBeVisible();
    for (const label of ["Delete", "Connect", "Feed zone", "Summit", "Announcement", "Interval", "Zoom"])
      await expect(menu.getByRole("button", { name: label, exact: true })).toBeVisible();
    await menu.getByRole("button", { name: "Connect", exact: true }).click();
    for (const label of ["Continue", "Turn right", "Turn left", "Turn around", "Finish"])
      await expect(menu.getByRole("button", { name: label, exact: true })).toBeVisible();
    await page.locator(".map-credit").click();
    await expect(page.locator("#waypoint-type-menu")).toHaveCount(0);

    const connect = await openPinMenu(page, 0);
    await chooseSubmenu(connect, "Connect", "Turn right");
    await expect(page.locator(".notice")).toContainText(/Connected\.|straight segment was kept/i);
    await expect.poll(() => glyphs(page)).toContain("R");

    const feed = await openPinMenu(page, 0);
    await chooseRow(feed, "Feed zone");
    await expect.poll(() => glyphs(page)).toContain("Z");
    await expect(page.locator(".notice")).toContainText("Feed zone");

    const announce = await openPinMenu(page, 0);
    await chooseRow(announce, "Announcement");
    await expect.poll(() => glyphs(page)).toContain("A");

    const summit = await openPinMenu(page, 0);
    await chooseRow(summit, "Summit");
    await expect(page.locator(".notice")).toContainText(/Summit placed at the high point|No elevation was found|too short/i);
    await expect(page.locator(".notice")).not.toContainText(/sign in/i);

    for (const distance of ["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"]) {
      const before = await page.locator(".course-pin").count();
      const interval = await openPinMenu(page, 0);
      await chooseSubmenu(interval, "Interval", distance);
      await expect(page.locator(".notice")).not.toContainText("too short");
      await expect.poll(() => glyphs(page)).toContain("I");
      await page.getByRole("button", { name: "Undo last segment" }).click();
      await expect(page.locator(".course-pin")).toHaveCount(before);
    }

    const deleting = await openPinMenu(page, 0);
    const count = await page.locator(".course-pin").count();
    await chooseRow(deleting, "Delete");
    await expect(page.locator(".notice")).toContainText("Waypoint deleted.");
    await expect(page.locator(".course-pin")).toHaveCount(count - 1);
  });

  test("FINISH beside the start pin closes the loop", async ({ page }) => {
    test.setTimeout(120_000);
    await drawTwoPoints(page);
    await page.locator(".course-pin").first().click();
    const away = await freePoint(page, "end");
    const zoom = await rightClickMap(page, away);
    await chooseSubmenu(zoom, "ZOOM", "TO SELECTED");
    await expect.poll(() => page.evaluate(() => window.gpsTrainerCourseEditor.getZoom())).toBe(19);

    const spot = await page.evaluate(() => {
      const pin = document.querySelector(".course-pin")!.getBoundingClientRect();
      const candidates = [
        { x: pin.right + 24, y: pin.top + pin.height / 2 },
        { x: pin.left - 24, y: pin.top + pin.height / 2 },
        { x: pin.left + pin.width / 2, y: pin.bottom + 24 },
        { x: pin.left + pin.width / 2, y: pin.top - 24 }
      ];
      for (const candidate of candidates) {
        const el = document.elementFromPoint(candidate.x, candidate.y);
        if (el && (el as Element).closest("#course-map") && !(el as Element).closest(".course-pin") && !(el as Element).closest(".editor-dialog"))
          return candidate;
      }
      return null;
    });
    expect(spot).not.toBeNull();
    await page.mouse.click(spot!.x, spot!.y, { button: "right" });
    const menu = page.locator("#waypoint-type-menu");
    await expect(menu).toBeVisible();
    await chooseRow(menu, "FINISH");
    await expect.poll(() => glyphs(page)).toContain("F");
    const centers = await page.locator(".course-pin").evaluateAll((els) =>
      els.map((el) => {
        const box = el.getBoundingClientRect();
        return { text: el.textContent ?? "", x: box.x + box.width / 2, y: box.y + box.height / 2 };
      })
    );
    const start = centers.find((pin) => pin.text.includes("S"));
    const finish = centers.find((pin) => pin.text.includes("F"));
    expect(start && finish).toBeTruthy();
    expect(Math.hypot(start!.x - finish!.x, start!.y - finish!.y)).toBeLessThan(8);
  });

  test("right-click FINISH on the start pin closes there and shows S/F", async ({ page }) => {
    test.setTimeout(120_000);
    await drawTwoPoints(page);
    const menu = await openPinMenu(page, 0);
    await expect(menu.getByRole("button", { name: "FINISH", exact: true })).toBeVisible();
    await chooseRow(menu, "FINISH");
    await expect(page.locator(".notice")).toContainText("Loop closed at the start.");
    await expect(page.locator(".notice")).not.toContainText(/routable|could not find|350/i);
    await expect.poll(() => glyphs(page)).toContain("S/F");
  });

  test("the profile chart opens its 2013 menu and a hover popup when elevation exists", async ({ page }) => {
    await drawTwoPoints(page);
    await expect(page.locator("#PanelProfile")).toBeVisible();
    const chartText = page.locator(".profile-chart");
    const target = (await page.locator("#course-profile").count())
      ? page.locator("#course-profile")
      : chartText.getByText("No elevation");
    await target.click({ button: "right" });
    const menu = page.locator("#waypoint-type-menu");
    await expect(menu).toBeVisible();
    for (const label of ["Interval", "Turn Around", "Feed Zone", "Summit", "Announcement"])
      await expect(menu.getByRole("button", { name: label, exact: true })).toBeVisible();
    await menu.getByRole("button", { name: "Interval", exact: true }).click();
    for (const distance of ["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"])
      await expect(menu.getByRole("button", { name: distance, exact: true })).toBeVisible();
    await chooseRow(menu, "Turn Around");
    await expect(page.locator(".notice")).toContainText(/TurnAround placed|too short|Could not read/i);
    if (await page.locator("#course-profile").count()) {
      await page.locator("#course-profile").hover({ position: { x: 200, y: 80 } });
      await expect(page.locator(".profile-hover")).toHaveText(/\d+\.\d+(mi|k),\d+(ft|m)/);
    } else {
      await expect(page.locator("#PanelProfile")).toContainText("No elevation");
    }
  });

  test("GPX download and import replace the course for the signed-in rider", async ({ page }) => {
    await drawTwoPoints(page);
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "Download GPX" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.gpx$/i);
    const xml = await download.createReadStream().then(readAll);
    expect(xml).toContain("<gpx");

    await page.getByRole("button", { name: "Import GPX" }).click();
    await expect(page.locator("input[type=number]")).toHaveValue("10");
    await page.locator("input[type=file]").setInputFiles({
      name: "imported.gpx",
      mimeType: "application/gpx+xml",
      buffer: Buffer.from(sampleGpx)
    });
    await expect(page.locator(".notice")).toContainText("Review the imported course");
    await expect(page.locator("#PanelDetails input").first()).toHaveValue(/Imported/);
    await expect(page.locator(".course-pin").first()).toBeVisible();
  });

  test("saving a course and opening another redraws the saved one", async ({ page }) => {
    test.setTimeout(180_000);
    await drawTwoPoints(page);
    const alphaPins = await page.locator(".course-pin").count();
    await page.locator("#PanelDetails input").first().fill("Alpha loop");
    await page.locator("#PanelDetails").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".notice")).toContainText("Saved.");
    const alphaHref = await page.getByRole("link", { name: "Interval times" }).getAttribute("href");
    expect(alphaHref).toMatch(/intervals\/[0-9a-f-]{36}/i);
    const alphaId = alphaHref!.split("/").pop();

    await page.goto("/editor");
    await page.locator("#course-map.leaflet-container").waitFor();
    await expect(page.locator("#PanelDetails input").first()).toHaveValue("");
    const fresh = await freePoint(page, "end");
    await page.locator("#course-map").click({ position: fresh });
    await expect(page.locator(".course-pin")).toHaveCount(1);
    await page.locator("#PanelDetails input").first().fill("Beta loop");
    await page.locator("#PanelDetails").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".notice")).toContainText("Saved.");

    await page.goto(`/editor/${alphaId}`);
    await page.locator("#course-map.leaflet-container").waitFor();
    await expect(page.locator("#PanelDetails input").first()).toHaveValue("Alpha loop");
    await expect(page.locator(".course-pin")).toHaveCount(alphaPins);
    const switched = await page.locator(".notice").allTextContents();
    expect(switched.join(" ")).not.toContain("Restored the course you were drawing.");

    await page.reload();
    await page.locator("#course-map.leaflet-container").waitFor();
    await expect(page.locator("#PanelDetails input").first()).toHaveValue("Alpha loop");
    await expect(page.locator(".course-pin")).toHaveCount(alphaPins);
  });
});

test.describe("saved home", () => {
  test("a blank editor opens on the saved home and an existing course stays on the course", async ({ page, request }) => {
    const json = await localSession(request, false);
    const session = JSON.parse(json) as { token: string; login: string; user: { login: string } };
    const home = { latitude: 37.7749, longitude: -122.4194 };
    const auth = { Authorization: `Bearer ${session.token}` };
    const located = await request.put(`/api/users/${encodeURIComponent(session.login)}/location`, {
      headers: auth,
      data: home
    });
    expect(located.ok(), await located.text()).toBeTruthy();

    const created = await request.post("/api/courses", {
      headers: auth,
      data: { login: session.login, name: "Far loop", activityCode: 3 }
    });
    if (!created.ok())
      expect(created.status(), await created.text()).toBe(200);
    const course = await created.json() as { courseId: string };
    const stored = await request.put(`/api/courses/${course.courseId}/waypoints`, {
      headers: auth,
      data: [
        { latitude: 41.5, longitude: -107.2, waypointType: 11, timestampTicks: 1 },
        { latitude: 41.55, longitude: -107.1, waypointType: 12, timestampTicks: 2 }
      ]
    });
    expect(stored.ok(), await stored.text()).toBeTruthy();

    await page.addInitScript((storedSession) => {
      localStorage.setItem("gpstrainer.session", storedSession);
    }, JSON.stringify({
      token: session.token,
      login: session.login,
      user: { login: session.login, latitude: home.latitude, longitude: home.longitude }
    }));

    await page.goto("/editor");
    await page.locator("#course-map.leaflet-container").waitFor();
    await expect.poll(async () => (await mapCenter(page)).latitude).toBeCloseTo(home.latitude, 2);
    await expect.poll(async () => (await mapCenter(page)).longitude).toBeCloseTo(home.longitude, 2);
    const blank = await mapCenter(page);
    expect(Math.abs(blank.latitude - 40.5853)).toBeGreaterThan(1);

    await page.goto(`/editor/${course.courseId}`);
    await page.locator("#course-map.leaflet-container").waitFor();
    await expect(page.locator(".course-pin").first()).toBeVisible();
    await expect.poll(async () => (await mapCenter(page)).latitude).toBeCloseTo(41.5, 2);
    await expect.poll(async () => (await mapCenter(page)).longitude).toBeCloseTo(-107.2, 2);
    const opened = await mapCenter(page);
    expect(Math.abs(opened.latitude - home.latitude)).toBeGreaterThan(1);
  });
});

async function mapCenter(page: import("@playwright/test").Page): Promise<{ latitude: number; longitude: number }> {
  const center = await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter());
  expect(center).toBeTruthy();
  return center!;
}

async function openPinMenu(page: import("@playwright/test").Page, index: number) {
  await page.locator(".course-pin").nth(index).click({ button: "right", force: true });
  const menu = page.locator("#waypoint-type-menu");
  await expect(menu).toBeVisible();
  return menu;
}

async function readAll(stream: NodeJS.ReadableStream): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of stream)
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  return Buffer.concat(chunks).toString("utf8");
}

async function expectHelpClear(page: import("@playwright/test").Page) {
  const help = await page.locator("#HelpPanel").boundingBox();
  const zoom = await page.getByRole("button", { name: "Zoom To" }).boundingBox();
  const undo = await page.getByRole("button", { name: "Undo last segment" }).boundingBox();
  const profile = await page.locator("#PanelProfile").boundingBox();
  expect(help && zoom && undo && profile).toBeTruthy();
  expect({
    zoom: boxesOverlap(help!, zoom!),
    undo: boxesOverlap(help!, undo!),
    profile: boxesOverlap(help!, profile!)
  }).toEqual({ zoom: false, undo: false, profile: false });
}

function boxesOverlap(
  a: { x: number; y: number; width: number; height: number },
  b: { x: number; y: number; width: number; height: number }
): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

const sampleGpx = `<?xml version="1.0" encoding="UTF-8"?>
<gpx version="1.1" creator="gpsTrainer-test">
  <trk><name>Imported loop</name><trkseg>
    <trkpt lat="40.5853" lon="-105.0844"></trkpt>
    <trkpt lat="40.5950" lon="-105.0700"></trkpt>
    <trkpt lat="40.5800" lon="-105.0900"></trkpt>
  </trkseg></trk>
</gpx>`;

declare global {
  interface Window {
    gpsTrainerCourseEditor: {
      getZoom: () => number | null;
      getCenter: () => { latitude: number; longitude: number } | null;
    };
  }
}
