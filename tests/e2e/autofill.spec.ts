import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

async function createAccount(request: APIRequestContext) {
  const stamp = Date.now().toString(36);
  const login = `e2e${stamp}`;
  const password = `Pw-${stamp}-not-for-logs`;
  const email = `${login}@example.com`;
  const created = await request.post("/api/users", {
    data: {
      login,
      password,
      firstName: "Map",
      lastName: "Test",
      email,
      country: "US",
      isMale: true,
      birthDate: "1990-01-01",
      racingCategoryKey: 0,
      wantsEmails: false
    }
  });
  expect(created.ok(), `create ${created.status()}`).toBeTruthy();
  return { login, password, email };
}

async function eyeInsideInput(page: Page, input: string, button: string): Promise<boolean> {
  return page.evaluate(({ input, button }) => {
    const field = document.querySelector(input)?.getBoundingClientRect();
    const eye = document.querySelector(button)?.getBoundingClientRect();
    if (!field || !eye || field.width === 0 || eye.width === 0)
      return false;
    return eye.left >= field.left && eye.right <= field.right + 1
      && eye.top >= field.top - 1 && eye.bottom <= field.bottom + 1;
  }, { input, button });
}

async function fillWithoutEvents(page: Page, values: Record<string, string>) {
  await page.evaluate((entries) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    if (!setter)
      throw new Error("value setter missing");
    for (const [id, value] of entries) {
      const el = document.getElementById(id);
      if (!(el instanceof HTMLInputElement))
        throw new Error(id);
      setter.call(el, value);
    }
  }, Object.entries(values));
}

test.describe("browser autofill", () => {
  test("a silent fill survives an input event on the user name and signs in", async ({ page, request }) => {
    const account = await createAccount(request);
    await page.goto("/login");
    await page.locator("#signin-login").waitFor();
    await fillWithoutEvents(page, { "signin-login": account.login, "signin-password": account.password });

    await page.locator("#signin-login").evaluate((el) => {
      el.dispatchEvent(new Event("input", { bubbles: true }));
    });

    await expect(page.locator("#signin-login")).toHaveValue(account.login);
    await expect(page.locator("#signin-password")).toHaveValue(account.password);
    await page.locator(".signin-submit").click();
    await page.waitForURL("**/courses");
  });

  test("a silent fill survives Show and a delayed fill still signs in", async ({ page, request }) => {
    const account = await createAccount(request);
    await page.goto("/login");
    await page.locator("#signin-password").waitFor();
    await fillWithoutEvents(page, { "signin-login": account.login, "signin-password": account.password });
    const reveal = page.locator(".signin-reveal");
    await expect(reveal).toHaveAttribute("aria-label", "Show password");
    await expect(reveal).toHaveAttribute("title", "Show password");
    await expect(reveal).toHaveAttribute("aria-pressed", "false");
    await expect(reveal.locator("svg.password-eye")).toBeVisible();
    expect(await eyeInsideInput(page, "#signin-password", ".signin-reveal")).toBeTruthy();
    await reveal.click();
    await expect(page.locator("#signin-password")).toHaveValue(account.password);
    await expect(page.locator("#signin-password")).toHaveAttribute("type", "text");
    await expect(reveal).toHaveAttribute("aria-label", "Hide password");
    await expect(reveal).toHaveAttribute("aria-pressed", "true");
    await reveal.hover();
    await expect.poll(() => reveal.evaluate((el) => getComputedStyle(el, "::after").content)).toContain("Hide password");
    await page.locator(".signin-submit").click();
    await page.waitForURL("**/courses");

    await page.goto("/login");
    await page.locator("#signin-login").waitFor();
    await page.waitForTimeout(400);
    await fillWithoutEvents(page, { "signin-login": account.login, "signin-password": account.password });
    await page.locator(".signin-submit").click();
    await page.waitForURL("**/courses");
  });

  test("signup keeps a silent fill across Show and submits it", async ({ page }) => {
    const stamp = Date.now().toString(36);
    const login = `e2e${stamp}`;
    const password = `Pw-${stamp}-not-for-logs`;
    const email = `${login}@example.com`;
    await page.goto("/signup");
    await page.locator("#signup-password").waitFor();
    await fillWithoutEvents(page, {
      "signup-login": login,
      "signup-password": password,
      "signup-email": email
    });
    await expect(page.locator(".signup-reveal")).toHaveAttribute("aria-label", "Show password");
    expect(await eyeInsideInput(page, "#signup-password", ".signup-reveal")).toBeTruthy();
    await page.locator(".signup-reveal").click();
    await expect(page.locator(".signup-reveal")).toHaveAttribute("aria-label", "Hide password");
    await expect(page.locator("#signup-login")).toHaveValue(login);
    await expect(page.locator("#signup-password")).toHaveValue(password);
    await expect(page.locator("#signup-email")).toHaveValue(email);
    await page.locator(".signup-submit").click();
    await page.waitForURL("**/login");
  });

  test("account passwords survive the profile reload and Show", async ({ page, request }) => {
    const account = await createAccount(request);
    const signed = await request.post("/api/auth/login", { data: { login: account.login, password: account.password } });
    expect(signed.ok()).toBeTruthy();
    const body = await signed.json() as { token?: string; user?: { login?: string } };
    const stored = JSON.stringify({ token: body.token, login: body.user?.login, user: body.user });
    await page.addInitScript((json) => localStorage.setItem("gpstrainer.session", json), stored);

    let releaseProfile: () => void = () => {};
    const profileGate = new Promise<void>((resolve) => { releaseProfile = resolve; });
    await page.route("**/api/users/**", async (route) => {
      if (route.request().method() === "GET")
        await profileGate;
      await route.continue();
    });

    await page.goto("/account");
    await page.locator("#account-current").waitFor();
    const current = "old-secret";
    const next = "new-secret";
    await fillWithoutEvents(page, { "account-current": current, "account-next": next });
    releaseProfile();
    await page.locator("#profile-map").waitFor();
    await expect(page.locator("#account-current")).toHaveValue(current);
    await expect(page.locator("#account-next")).toHaveValue(next);

    await expect(page.locator(".account-reveal").first()).toHaveAttribute("aria-label", "Show password");
    expect(await eyeInsideInput(page, "#account-current", ".account-reveal")).toBeTruthy();
    await page.locator(".account-reveal").first().click();
    await expect(page.locator(".account-reveal").first()).toHaveAttribute("aria-label", "Hide password");
    await expect(page.locator("#account-current")).toHaveValue(current);
    await expect(page.locator("#account-current")).toHaveAttribute("type", "text");
    await expect(page.locator("#account-next")).toHaveValue(next);

    const posted = page.waitForRequest("**/api/auth/password");
    await page.locator(".account-form .account-submit").click();
    const requestBody = (await posted).postDataJSON() as { currentPassword?: string; newPassword?: string };
    expect(requestBody.currentPassword).toBe(current);
    expect(requestBody.newPassword).toBe(next);
  });
});
