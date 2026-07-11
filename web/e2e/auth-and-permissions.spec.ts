import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

type SessionCookie = { name: string; value: string };

async function getSessionCookies(context: BrowserContext) {
  const cookies = await context.cookies();
  const refresh = cookies.find(cookie =>
    cookie.name.includes("acceptance-refresh")
  );
  const csrf = cookies.find(cookie => cookie.name.includes("acceptance-csrf"));
  expect(refresh).toBeDefined();
  expect(csrf).toBeDefined();
  return { refresh: refresh!, csrf: csrf! };
}

function cookieHeader(...cookies: SessionCookie[]) {
  return cookies.map(cookie => `${cookie.name}=${cookie.value}`).join("; ");
}

async function refreshFromBrowser(page: Page) {
  return page.evaluate(async () => {
    const csrf = document.cookie
      .split(";")
      .map(value => value.trim())
      .find(value => value.startsWith("acceptance-csrf="))
      ?.slice("acceptance-csrf=".length);
    const response = await fetch("/refresh-token", {
      method: "POST",
      credentials: "include",
      headers: csrf ? { "X-CSRF-Token": decodeURIComponent(csrf) } : {}
    });
    return { status: response.status, body: await response.json() };
  });
}

test("登录写入 HttpOnly 会话 Cookie，并能在清空浏览器存储后恢复", async ({
  context,
  page
}) => {
  await loginFromUi(page, "admin");
  await expect(page.getByText("系统概览", { exact: true })).toBeVisible();

  const refreshCookie = (await context.cookies()).find(cookie =>
    cookie.name.includes("acceptance-refresh")
  );
  expect(refreshCookie).toBeDefined();
  expect(refreshCookie?.httpOnly).toBeTruthy();

  await page.evaluate(() => {
    localStorage.clear();
    sessionStorage.clear();
  });
  const refreshResponse = page.waitForResponse(
    response =>
      response.url().endsWith("/refresh-token") &&
      response.request().method() === "POST"
  );
  await page.reload();
  expect((await refreshResponse).ok()).toBeTruthy();
  await expect(page).toHaveURL(/#\/dashboard$/);
  await expect(page.getByText("系统概览", { exact: true })).toBeVisible();
});

test("普通用户看不到 RBAC 页面，受限 API 返回 403", async ({ page }) => {
  const accessToken = await loginFromUi(page, "common");
  await page.goto("/#/rbac/system-users");
  await expect(page).not.toHaveURL(/#\/rbac\/system-users/);
  await expect(page.getByText("系统用户", { exact: true })).toHaveCount(0);

  const response = await page.request.get("/api/system-users", {
    headers: { Authorization: `Bearer ${accessToken}` }
  });
  expect(response.status()).toBe(403);
});

test("Refresh Cookie 每次成功刷新都会轮换，旧 token 重放会撤销后继会话", async ({
  context,
  page,
  request
}) => {
  await loginFromUi(page, "admin");
  const original = await getSessionCookies(context);

  const rotation = await refreshFromBrowser(page);
  expect(rotation.status).toBe(200);
  const replacement = await getSessionCookies(context);
  expect(replacement.refresh.value).not.toBe(original.refresh.value);

  const trustedOrigin = new URL(page.url()).origin;
  const replay = await request.post("/refresh-token", {
    headers: {
      Origin: trustedOrigin,
      Cookie: cookieHeader(original.refresh, replacement.csrf),
      "X-CSRF-Token": replacement.csrf.value
    },
    data: {}
  });
  expect(replay.status()).toBe(401);
  expect((await replay.json()).message).toContain("重放");

  const revokedDescendant = await refreshFromBrowser(page);
  expect(revokedDescendant.status).toBe(401);
});

test("缺少 CSRF 或使用恶意 Origin 会被拒绝，且不会消耗 refresh token", async ({
  context,
  page,
  request
}) => {
  await loginFromUi(page, "admin");
  const original = await getSessionCookies(context);

  const missingCsrf = await page.evaluate(async () => {
    const response = await fetch("/refresh-token", {
      method: "POST",
      credentials: "include"
    });
    return response.status;
  });
  expect(missingCsrf).toBe(403);

  const maliciousOrigin = await request.post("/refresh-token", {
    headers: {
      Origin: "https://attacker.invalid",
      Cookie: cookieHeader(original.refresh, original.csrf),
      "X-CSRF-Token": original.csrf.value
    },
    data: {}
  });
  expect(maliciousOrigin.status()).toBe(403);

  const validRefresh = await refreshFromBrowser(page);
  expect(validRefresh.status).toBe(200);
  const replacement = await getSessionCookies(context);
  expect(replacement.refresh.value).not.toBe(original.refresh.value);
});

test("两个标签页会同步用户主动登出并清除服务端会话 Cookie", async ({
  context,
  page
}) => {
  await loginFromUi(page, "admin");
  const secondPage = await context.newPage();
  await secondPage.goto("/#/dashboard");
  await expect(secondPage.getByText("系统概览", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "管理员", exact: true }).click();
  await page.getByText("退出系统", { exact: true }).click();

  await expect(page).toHaveURL(/#\/login/);
  await expect(secondPage).toHaveURL(/#\/login/);
  const remainingRefresh = (await context.cookies()).find(cookie =>
    cookie.name.includes("acceptance-refresh")
  );
  expect(remainingRefresh).toBeUndefined();
});

test("服务端判定会话重放后，一个标签页的恢复失败会使另一个标签页同步失效", async ({
  context,
  page,
  request
}) => {
  await loginFromUi(page, "admin");
  const original = await getSessionCookies(context);
  const secondPage = await context.newPage();
  await secondPage.goto("/#/dashboard");
  await expect(secondPage.getByText("系统概览", { exact: true })).toBeVisible();
  const replacement = await getSessionCookies(context);
  expect(replacement.refresh.value).not.toBe(original.refresh.value);

  const replay = await request.post("/refresh-token", {
    headers: {
      Origin: new URL(page.url()).origin,
      Cookie: cookieHeader(original.refresh, replacement.csrf),
      "X-CSRF-Token": replacement.csrf.value
    },
    data: {}
  });
  expect(replay.status()).toBe(401);

  await page.reload();
  await expect(page).toHaveURL(/#\/login/);
  await expect(secondPage).toHaveURL(/#\/login/);
});

test("十个并发 401 在真实浏览器中只触发一次 refresh，并统一重放", async ({
  page
}) => {
  const originalAccessToken = await loginFromUi(page, "admin");
  let originalRequestCount = 0;
  let replayRequestCount = 0;
  let refreshRequestCount = 0;
  let releaseOriginalRequests!: () => void;
  const allOriginalRequestsArrived = new Promise<void>(resolve => {
    releaseOriginalRequests = resolve;
  });

  page.on("request", request => {
    if (request.url().endsWith("/refresh-token")) refreshRequestCount++;
  });
  await page.route("**/api/e2e-single-flight**", async route => {
    const authorization = route.request().headers().authorization ?? "";
    if (authorization === `Bearer ${originalAccessToken}`) {
      originalRequestCount++;
      if (originalRequestCount === 10) releaseOriginalRequests();
      await allOriginalRequestsArrived;
      await route.fulfill({
        status: 401,
        contentType: "application/json",
        body: JSON.stringify({ message: "synthetic expired access token" })
      });
      return;
    }

    replayRequestCount++;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ code: 0, data: { ok: true } })
    });
  });

  const results = await page.evaluate(async () => {
    // @ts-expect-error Vite 在浏览器中解析绝对 /src 模块；Node 侧 tsc 无该 URL 模块。
    const module = await import("/src/utils/http/index.ts");
    const { http } = module as unknown as {
      http: {
        request(
          method: string,
          url: string,
          params?: unknown
        ): Promise<{ code: number; data: { ok: boolean } }>;
      };
    };
    return Promise.all(
      Array.from({ length: 10 }, (_, index) =>
        http.request("get", "/api/e2e-single-flight", {
          params: { index }
        })
      )
    );
  });

  expect(results).toHaveLength(10);
  expect(results.every(result => result.data.ok)).toBeTruthy();
  expect(originalRequestCount).toBe(10);
  expect(replayRequestCount).toBe(10);
  expect(refreshRequestCount).toBe(1);
});
