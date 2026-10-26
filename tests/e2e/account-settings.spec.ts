import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

async function rider(request: APIRequestContext) {
  const stamp = Date.now().toString(36);
  const login = `set${stamp}`;
  const password = `Pw-${stamp}-ok`;
  const created = await request.post("/api/users", {
    data: { login, password, email: `${login}@example.com`, country: "US", isMale: true }
  });
  expect(created.ok()).toBeTruthy();
  const signed = await request.post("/api/auth/login", { data: { login, password } });
  expect(signed.ok()).toBeTruthy();
  const body = await signed.json() as { token: string; user: { login: string } };
  return { login, password, session: JSON.stringify({ token: body.token, login: body.user.login, user: body.user }) };
}

async function signIn(page: Page, login: string, password: string) {
  await page.goto("/login");
  await page.locator("#signin-login").fill(login);
  await page.locator("#signin-password").fill(password);
  await page.locator(".signin-submit").click();
  await page.waitForURL("**/courses");
}

async function openSigned(page: Page, path: string, session: string) {
  await page.goto(path);
  await page.evaluate((stored) => localStorage.setItem("gpstrainer.session", stored), session);
  await page.goto(path);
}

test("password change rejects the wrong current password and the new one signs in", async ({ page, request }) => {
  const account = await rider(request);
  await openSigned(page, "/account", account.session);
  await expect(page.locator("h1")).toHaveText("Account");

  await page.locator("#account-current").fill("wrong-password");
  await page.locator("#account-next").fill("replacement-password");
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.locator("#account-password-note")).toHaveText("The current password was not accepted.");

  await page.locator("#account-current").fill(account.password);
  await page.locator("#account-next").fill("replacement-password");
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.locator("#account-password-note")).toHaveText("Password updated.");

  await page.locator("header button.header-signout").click();
  await expect(page.locator("header a[href='login']")).toHaveText("Sign in");
  await signIn(page, account.login, "replacement-password");
  await expect(page.locator("header a.header-user")).toHaveText(account.login);
});

test("a map click saves the home location and the editor opens there after reload", async ({ page, request }) => {
  test.fail(true, "Known bug: reloading Account leaves the map on Fort Collins instead of the saved home.");
  const account = await rider(request);
  await openSigned(page, "/account", account.session);
  await page.locator("#profile-map.leaflet-container").waitFor();
  const box = await page.locator("#profile-map").boundingBox();
  expect(box).toBeTruthy();
  await page.locator("#profile-map").click({ position: { x: box!.width / 2, y: 36 } });
  await expect(page.locator("#account-location-note")).toHaveText("Map location saved.");
  const saved = await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter());
  expect(saved).toBeTruthy();
  const profile = await request.get(`/api/users/${account.login}`, {
    headers: { authorization: `Bearer ${JSON.parse(account.session).token}` }
  });
  expect(profile.ok()).toBeTruthy();
  const body = await profile.json() as { latitude: number; longitude: number };
  expect(Math.abs(body.latitude - saved!.latitude)).toBeLessThan(0.05);
  expect(Math.abs(body.longitude - saved!.longitude)).toBeLessThan(0.05);
  expect(Math.abs(body.latitude - 40.5853)).toBeGreaterThan(0.01);

  await page.reload();
  await page.locator("#profile-map.leaflet-container").waitFor();
  await expect.poll(async () => (await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter()))!.latitude)
    .toBeCloseTo(body.latitude, 3);
  await expect.poll(async () => (await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter()))!.longitude)
    .toBeCloseTo(body.longitude, 3);

  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor();
  await expect.poll(async () => (await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter()))!.latitude)
    .toBeCloseTo(saved!.latitude, 2);
  await expect.poll(async () => (await page.evaluate(() => window.gpsTrainerCourseEditor.getCenter()))!.longitude)
    .toBeCloseTo(saved!.longitude, 2);
});

test("kilometers survive a reload for that login and a signed-out editor stays on miles", async ({ page, request }) => {
  const account = await rider(request);
  await openSigned(page, "/editor", account.session);
  await page.locator("#course-map.leaflet-container").waitFor();
  await expect(page.locator(".distance-readout")).toContainText(/\d+\.\d+ mi/);
  await expect(page.locator(".units-button")).toHaveText("km");
  await page.locator(".units-button").click();
  await expect(page.locator(".distance-readout")).toContainText(/\d+\.\d+ km/);
  await expect(page.locator(".units-button")).toHaveText("mi");

  await page.reload();
  await page.locator("#course-map.leaflet-container").waitFor();
  await expect(page.locator(".distance-readout")).toContainText(/\d+\.\d+ km/);

  await page.locator("header button.header-signout").click();
  await expect(page.locator("header a[href='login']")).toHaveText("Sign in");
  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor();
  await expect(page.locator(".distance-readout")).toContainText(/\d+\.\d+ mi/);
  await expect(page.locator(".units-button")).toHaveText("km");

  await signIn(page, account.login, account.password);
  await page.goto("/editor");
  await page.locator("#course-map.leaflet-container").waitFor();
  await expect(page.locator(".distance-readout")).toContainText(/\d+\.\d+ km/);
  await expect(page.locator(".units-button")).toHaveText("mi");
});

declare global {
  interface Window {
    gpsTrainerCourseEditor: {
      getCenter: () => { latitude: number; longitude: number } | null;
    };
  }
}
