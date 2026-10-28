import { expect, test } from "@playwright/test";
import {
  chooseRow,
  chooseSubmenu,
  closeHelp,
  drawTwoPoints,
  freePoint,
  glyphs,
  openEditor,
  openSignedEditor,
  rightClickMap
} from "./editor.helpers";

test("undo on an empty course says there is nothing to undo", async ({ page }) => {
  await openEditor(page);
  await closeHelp(page);
  await page.locator(".map-credit").click();
  await page.keyboard.press("Control+z");
  await expect(page.locator(".notice")).toContainText("Nothing to undo.");
});

test("saving turn text from the map popup reports that it was saved", async ({ page }) => {
  await openEditor(page);
  await closeHelp(page);
  await drawTwoPoints(page);
  await page.getByRole("button", { name: "Close course" }).click();
  await page.locator(".course-pin").first().click({ button: "right", force: true });
  const menu = page.locator("#waypoint-type-menu");
  await expect(menu).toBeVisible();
  await chooseSubmenu(menu, "Connect", "Turn right");
  await expect.poll(() => glyphs(page)).toContain("R");
  await page.locator(".course-pin").filter({ hasText: "R" }).evaluate((pin) => {
    pin.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
  });
  const text = page.locator(".leaflet-popup textarea");
  await expect(text).toBeVisible();
  await text.fill("U-turn <b>here</b>");
  await page.locator(".leaflet-popup button[id^='turn-save-']").click();
  await expect(page.locator(".notice")).toContainText("Turn text saved");
  await expect(page.locator("img[src='x']")).toHaveCount(0);
});

test("the map menu lists every last-point row and not bear left or bear right", async ({ page }) => {
  await openEditor(page);
  await closeHelp(page);
  const spot = await freePoint(page, "end");
  const menu = await rightClickMap(page, spot);
  for (const label of ["CONTINUE", "ROUTE", "TURN RIGHT", "TURN LEFT", "FINISH", "ZOOM", "MOVE"])
    await expect(menu.getByRole("button", { name: label, exact: true })).toBeVisible();
  await expect(menu.getByRole("button", { name: /\d+(\.\d+)? (mi|km) FINISH/ })).toBeVisible();
  await expect(menu.getByRole("button", { name: /BEAR/i })).toHaveCount(0);
});

test.describe("course lifecycle", () => {
  test.beforeEach(async ({ page, request }) => {
    await openSignedEditor(page, request);
    await closeHelp(page);
  });

  test("draw, name, save, list, rename, edit a turn, and delete", async ({ page }) => {
    test.setTimeout(180_000);
    await drawTwoPoints(page);
    await expect(page.locator(".distance-readout")).not.toContainText("0.00");
    await expect(page.locator(".distance-readout")).toContainText("mi");

    const name = page.locator("#PanelDetails input").first();
    await name.fill("North loop");
    await page.locator("#PanelDetails").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".notice")).toContainText("Saved.");

    await page.goto("/courses");
    const row = page.locator("tr", { hasText: "North loop" });
    await expect(row).toBeVisible();
    await expect(row.locator(".courses-distance")).not.toHaveText("0.00");
    const href = await row.locator("a").getAttribute("href");
    expect(href).toMatch(/editor\/[0-9a-f-]{36}/i);
    const courseId = href!.split("/").pop()!;

    const downloadPromise = page.waitForEvent("download");
    await row.getByRole("button", { name: "Download GPX" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/North loop\.gpx$/);
    const xml = await download.createReadStream().then(readAll);
    expect(xml).toContain("<gpx");
    expect(xml).toContain("North loop");

    await page.goto(`/editor/${courseId}`);
    await page.locator("#course-map.leaflet-container").waitFor();
    await closeHelp(page);
    await expect(page.locator("#PanelDetails input").first()).toHaveValue("North loop");
    await page.getByRole("button", { name: "Close course" }).click();
    await page.locator(".course-pin").first().click({ button: "right", force: true });
    const menu = page.locator("#waypoint-type-menu");
    await expect(menu).toBeVisible();
    await chooseSubmenu(menu, "Connect", "Turn right");
    await expect.poll(() => glyphs(page)).toContain("R");

    await page.getByRole("button", { name: "COURSE" }).click();
    await page.locator("#PanelDetails input").first().fill("North loop renamed");
    await page.locator("#PanelDetails").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".notice")).toContainText("Saved.");

    await page.goto("/courses");
    const renamed = page.locator("tr", { hasText: "North loop renamed" });
    await expect(renamed).toBeVisible();
    await expect(page.locator("tr", { hasText: "North loop" }).filter({ hasNotText: "renamed" })).toHaveCount(0);

    await renamed.getByRole("button", { name: "Delete" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toContainText("Delete course North loop renamed? This can't be undone");
    await dialog.getByRole("button", { name: "Cancel" }).click();
    await expect(dialog).toHaveCount(0);
    await expect(renamed).toBeVisible();

    await renamed.getByRole("button", { name: "Delete" }).click();
    await dialog.getByRole("button", { name: "Delete" }).click();
    await expect(page.locator("tr", { hasText: "North loop renamed" })).toHaveCount(0);

    const session = JSON.parse(await page.evaluate(() => localStorage.getItem("gpstrainer.session") || "{}")) as { token?: string };
    const gone = await page.request.get(`/api/courses/${courseId}`, {
      headers: { authorization: `Bearer ${session.token}` }
    });
    expect(gone.status()).toBe(404);
  });

  test("deleting the start promotes the next pin and the map menu can still move a point", async ({ page }) => {
    test.setTimeout(120_000);
    await drawTwoPoints(page);
    const count = await page.locator(".course-pin").count();
    await page.locator(".course-pin").first().click();
    await page.keyboard.press("Delete");
    await expect(page.locator(".notice")).toContainText("Waypoint deleted.");
    await expect(page.locator(".course-pin")).toHaveCount(count - 1);
    await expect(page.locator(".course-pin span").first()).toHaveText("S");

    const before = await page.locator(".course-pin").last().boundingBox();
    const spot = await freePoint(page, "start");
    const menu = await rightClickMap(page, spot);
    await chooseRow(menu, "MOVE");
    await expect(page.locator(".notice")).toContainText(/Waypoint moved\.|straight segment was kept/i);
    const after = await page.locator(".course-pin").last().boundingBox();
    expect(before && after).toBeTruthy();
    expect(Math.hypot(after!.x - before!.x, after!.y - before!.y)).toBeGreaterThan(20);
  });
});

async function readAll(stream: NodeJS.ReadableStream): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of stream)
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  return Buffer.concat(chunks).toString("utf8");
}
