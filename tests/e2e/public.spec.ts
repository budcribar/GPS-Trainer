import { expect, test } from "@playwright/test";

test.describe("public pages", () => {
  test("home, download, help, contact, and privacy render", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("h1")).toHaveText("Endurance sports software");
    await expect(page.locator("a[href='download'], a[href='about']").first()).toBeVisible();

    await page.goto("/download");
    await expect(page.locator("h1")).toHaveText("Download");
    await expect(page.getByRole("link", { name: "Your courses" })).toBeVisible();
    await expect(page.locator(".store-badges")).toBeVisible();

    await page.goto("/help");
    await expect(page.locator("h1")).toHaveText("Support");
    await expect(page.getByRole("main").locator("a[href='privacy']")).toBeVisible();

    await page.goto("/contact");
    await expect(page.locator("h1")).toHaveText("Contact");
    await expect(page.locator("a[href='mailto:support@gpsTrainer.com']")).toBeVisible();
    await expect(page.locator("a[href='mailto:info@gpsTrainer.com']")).toBeVisible();
    await expect(page.getByRole("main").locator("a[href='privacy']")).toBeVisible();

    await page.goto("/privacy");
    await expect(page.locator("h1")).toHaveText("Privacy policy");
    await expect(page.locator(".privacy")).toContainText("published course that an admin has approved");
    await expect(page.locator(".privacy")).toContainText("Account page deletes the account");
  });

  test("sign-up rejects an empty form, a bad email, and a taken user name", async ({ page, request }) => {
    const stamp = Date.now().toString(36);
    const login = `up${stamp}`;
    const created = await request.post("/api/users", {
      data: { login, password: `Pw-${stamp}-ok`, email: `${login}@example.com`, country: "US" }
    });
    expect(created.ok()).toBeTruthy();

    await page.goto("/signup");
    await page.locator(".signup-submit").click();
    await expect(page.locator("#signup-login-error")).toHaveText("Enter a user name.");
    await expect(page.locator("#signup-password-error")).toHaveText("Enter a password.");
    await expect(page.locator("#signup-email-error")).toHaveText("Enter a valid email address.");

    await page.locator("#signup-login").fill(login + "b");
    await page.locator("#signup-password").fill(`Pw-${stamp}-ok`);
    await page.locator("#signup-email").fill("not-an-email");
    await page.locator(".signup-submit").click();
    await expect(page.locator("#signup-email-error")).toHaveText("Enter a valid email address.");

    await page.locator("#signup-login").fill(login);
    await page.locator("#signup-email").fill(`${login}b@example.com`);
    await page.locator(".signup-submit").click();
    await expect(page.locator("#signup-login-error")).toHaveText("That user name is already registered.");

    await page.locator("#signup-login").fill(login + "c");
    await page.locator("#signup-email").fill(`${login}@example.com`);
    await page.locator(".signup-submit").click();
    await expect(page.locator("#signup-email-error")).toHaveText("That email is already registered.");
  });

  test("sign-in rejects a wrong password and accepts the account", async ({ page, request }) => {
    const stamp = Date.now().toString(36);
    const login = `in${stamp}`;
    const password = `Pw-${stamp}-ok`;
    expect((await request.post("/api/users", {
      data: { login, password, email: `${login}@example.com`, country: "US" }
    })).ok()).toBeTruthy();

    await page.goto("/login");
    await page.locator("#signin-login").fill(login);
    await page.locator("#signin-password").fill("wrong-password");
    await page.locator(".signin-submit").click();
    await expect(page.locator("#signin-form-error")).toHaveText("The user name or password is incorrect.");

    await page.locator("#signin-password").fill(password);
    await page.locator(".signin-submit").click();
    await page.waitForURL("**/courses");
    await expect(page.locator("h1")).toHaveText("Courses");
  });
});
