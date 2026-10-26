import { expect, test, type APIRequestContext } from "@playwright/test";

async function account(request: APIRequestContext, prefix: string) {
  const stamp = Date.now().toString(36);
  const login = `${prefix}${stamp}`;
  const password = `Pw-${stamp}-not-for-logs`;
  const created = await request.post("/api/users", {
    data: { login, password, email: `${login}@example.com`, country: "US", isMale: true }
  });
  expect(created.ok(), `create ${created.status()}`).toBeTruthy();
  const signed = await request.post("/api/auth/login", { data: { login, password } });
  expect(signed.ok(), `login ${signed.status()}`).toBeTruthy();
  const body = await signed.json() as { token?: string; user?: { login?: string } };
  expect(body.token).toBeTruthy();
  return { login, token: body.token!, user: body.user };
}

test("deleting the account removes the ride trail and signs the rider out", async ({ page, request }) => {
  const owner = await account(request, "del");
  const course = await request.post("/api/courses", {
    headers: { authorization: `Bearer ${owner.token}` },
    data: { name: "Delete me", login: owner.login }
  });
  expect(course.ok()).toBeTruthy();
  const courseId = (await course.json()).courseId as string;
  const race = await request.post("/api/races", {
    headers: { authorization: `Bearer ${owner.token}` },
    data: { courseId, login: owner.login, publish: false, comments: "private" }
  });
  expect(race.ok()).toBeTruthy();
  const raceId = (await race.json()).raceId as string;
  const posted = await request.post(`/api/races/${raceId}/positions`, {
    headers: { authorization: `Bearer ${owner.token}` },
    data: [{ timestampTicks: 4, latitude: 40.01, longitude: -105.27 }]
  });
  expect(posted.status()).toBe(204);
  expect((await request.get(`/api/races/${raceId}/positions`)).status()).toBe(401);

  const stored = JSON.stringify({ token: owner.token, login: owner.login, user: owner.user });
  await page.addInitScript((json) => {
    localStorage.setItem("gpstrainer.session", json);
  }, stored);
  await page.goto("/account");
  await expect(page.locator("section.account-delete h2")).toHaveText("Delete account");
  await page.locator(".account-delete-open").click();
  await expect(page.locator("#account-delete-confirm")).toBeVisible();
  await page.screenshot({ path: "/opt/cursor/artifacts/screenshot_delete_account.png" });
  await page.locator("#account-delete-confirm").fill("DELETE");
  await page.locator(".account-delete-submit").click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator("header a[href='login']")).toHaveText("Sign in");
  await expect.poll(() => page.evaluate(() => localStorage.getItem("gpstrainer.session"))).toBeNull();

  const reader = await account(request, "see");
  expect((await request.get(`/api/users/${encodeURIComponent(owner.login)}`, {
    headers: { authorization: `Bearer ${owner.token}` }
  })).status()).toBe(401);
  expect((await request.get(`/api/races/${raceId}/positions`, {
    headers: { authorization: `Bearer ${reader.token}` }
  })).status()).toBe(404);
  expect((await request.get(`/api/courses/${courseId}/waypoints`)).ok()).toBeTruthy();
  expect(await (await request.get(`/api/courses/${courseId}/waypoints`)).json()).toEqual([]);
});

test("the privacy page tells a rider how to delete the account", async ({ page }) => {
  await page.goto("/privacy");
  await expect(page.locator("h1")).toHaveText("Privacy policy");
  await expect(page.locator(".privacy")).toContainText("Account page deletes the account");
  await expect(page.locator(".privacy")).toContainText("courses, waypoints, rides, and location trails");
  await expect(page.locator(".privacy a[href='mailto:support@gpsTrainer.com']").first()).toBeVisible();
});
