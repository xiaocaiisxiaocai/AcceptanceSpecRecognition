import { expect, type Page } from "@playwright/test";

export const e2eCredentials = {
  admin: {
    username: "admin",
    password: process.env.E2E_ADMIN_PASSWORD ?? "E2eAdminPassword_2026!"
  },
  common: {
    username: "common",
    password: process.env.E2E_COMMON_PASSWORD ?? "E2eCommonPassword_2026!"
  }
} as const;

export async function loginFromUi(
  page: Page,
  account: keyof typeof e2eCredentials
) {
  const credentials = e2eCredentials[account];
  const loginResponse = page.waitForResponse(
    response =>
      response.url().endsWith("/login") &&
      response.request().method() === "POST"
  );

  await page.goto("/#/login");
  await page.getByPlaceholder("账号").fill(credentials.username);
  await page.getByPlaceholder("密码").fill(credentials.password);
  await page.getByRole("button", { name: "登录", exact: true }).click();

  const response = await loginResponse;
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as {
    success: boolean;
    data: { accessToken: string; refreshToken?: string | null };
  };
  expect(body.success).toBeTruthy();
  expect(body.data.refreshToken ?? null).toBeNull();
  await expect(page).not.toHaveURL(/#\/login(?:\?|$)/);
  return body.data.accessToken;
}
