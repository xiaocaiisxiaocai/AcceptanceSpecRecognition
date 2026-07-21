import { expect, test, type Page } from "@playwright/test";
import { createServer } from "node:http";
import { once } from "node:events";
import { loginFromUi } from "./helpers/auth";

const startSlowUploadServer = async () => {
  let markUploadStarted!: () => void;
  let markUploadReceived!: () => void;
  const uploadStarted = new Promise<void>(resolve => {
    markUploadStarted = resolve;
  });
  const uploadReceived = new Promise<void>(resolve => {
    markUploadReceived = resolve;
  });
  const responseTimers = new Set<ReturnType<typeof setTimeout>>();
  const server = createServer((request, response) => {
    if (request.method !== "POST") {
      response.writeHead(404).end();
      return;
    }

    markUploadStarted();
    const consumeChunk = () => {
      const chunk = request.read(16 * 1024);
      if (chunk) {
        const timer = setTimeout(() => {
          responseTimers.delete(timer);
          consumeChunk();
        }, 25);
        responseTimers.add(timer);
        return;
      }
      request.once("readable", consumeChunk);
    };
    consumeChunk();
    request.once("end", () => {
      markUploadReceived();
      const timer = setTimeout(() => {
        responseTimers.delete(timer);
        if (response.destroyed) return;
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end(
          JSON.stringify({
            code: 0,
            message: "",
            data: {
              fileId: 902,
              fileName: "synthetic-slow-upload.xlsx",
              fileType: 1,
              fileHash: "synthetic",
              isDuplicate: false,
              tableCount: 1,
              tableCountReady: true
            }
          })
        );
      }, 30_000);
      responseTimers.add(timer);
    });
  });
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const address = server.address();
  if (!address || typeof address === "string") {
    throw new Error("无法启动慢速上传测试服务");
  }

  return {
    url: `http://127.0.0.1:${address.port}/api/documents/upload`,
    uploadStarted,
    uploadReceived,
    close: async () => {
      for (const timer of responseTimers) clearTimeout(timer);
      responseTimers.clear();
      server.closeAllConnections();
      server.close();
      await once(server, "close");
    }
  };
};

const installSyntheticSessionRoutes = async (page: Page) => {
  const permissions = [
    "*:*:*",
    "menu:data-import",
    "page:data-import:index",
    "menu:config",
    "page:config:ai-services",
    "btn:document:upload",
    "btn:document:import",
    "btn:excel-document:import"
  ];
  await page.route(/\/login$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        success: true,
        data: {
          avatar: "",
          username: "admin",
          nickname: "E2E 管理员",
          roleCode: "admin",
          permissions,
          accessToken: "synthetic-e2e-access-token",
          expires: new Date(Date.now() + 60 * 60 * 1000).toISOString()
        }
      })
    })
  );

  const emptyPage = {
    items: [],
    total: 0,
    page: 1,
    pageSize: 200,
    totalPages: 0,
    hasNext: false,
    hasPrevious: false
  };
  for (const endpoint of ["customers", "processes", "machine-models"]) {
    await page.route(new RegExp(`/api/${endpoint}(?:\\?.*)?$`), route =>
      route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({ code: 0, message: "", data: emptyPage })
      })
    );
  }
  await page.route(/\/api\/ai-services(?:\?.*)?$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({ code: 0, message: "", data: emptyPage })
    })
  );
  await page.route(/\/api\/dashboard\/summary(?:\?.*)?$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        code: 0,
        message: "",
        data: {
          periodPreset: "last7",
          periodStart: "2026-07-15T00:00:00Z",
          periodEnd: "2026-07-21T00:00:00Z",
          customerTotal: 0,
          processTotal: 0,
          specTotal: 0,
          importedSpecCount: 0,
          smartFillTaskCount: 0,
          smartFillTotalRows: 0,
          smartFillMatchedRows: 0,
          smartFillAdoptedRows: 0,
          matchingRate: 0,
          adoptionRate: 0,
          dailyTrend: []
        }
      })
    })
  );
  await page.route(/\/api\/execution-history(?:\?.*)?$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({ code: 0, message: "", data: emptyPage })
    })
  );
};

test("从 AI 配置页返回后 keep-alive 页面从不可用、检测中恢复为可用", async ({
  page
}) => {
  let returnedFromConfig = false;
  let checksAfterReturn = 0;
  let selectionRequestCount = 0;
  await installSyntheticSessionRoutes(page);
  await page.route(/\/api\/ai-services\/selection(?:\?.*)?$/, async route => {
    const purpose = new URL(route.request().url()).searchParams.get("purpose");
    selectionRequestCount += 1;

    if (purpose !== "llm") {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          code: 0,
          message: "",
          data: { status: "unavailable", message: "无可用 Embedding 服务" }
        })
      });
      return;
    }

    const status = !returnedFromConfig
      ? "unavailable"
      : checksAfterReturn++ === 0
        ? "checking"
        : "available";
    await route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        code: 0,
        message: "",
        data:
          status === "available"
            ? {
                status,
                serviceId: 901,
                name: "E2E 可用 LLM",
                model: "synthetic-model",
                message: ""
              }
            : {
                status,
                message:
                  status === "checking"
                    ? "正在检测 AI 服务可用性"
                    : "当前没有运行可用的 LLM 服务"
              }
      })
    });
  });
  await loginFromUi(page, "admin");

  await page.goto("/#/data-import/import");
  const aiControl = page.locator(".structure-ai-control").last();
  await expect(aiControl).toContainText("当前没有运行可用的 LLM 服务");

  await aiControl.getByRole("button", { name: "去配置 AI 服务" }).click();
  await expect(page).toHaveURL(/#\/config\/ai-services$/);
  returnedFromConfig = true;

  await page.goBack();
  await expect(page).toHaveURL(/#\/data-import\/import$/);
  await expect(aiControl).toContainText("正在检测 LLM 服务可用性");
  await expect(aiControl).toContainText("E2E 可用 LLM", { timeout: 8_000 });
  await expect(
    aiControl.getByText("synthetic-model", { exact: true })
  ).toBeVisible();
  expect(selectionRequestCount).toBeGreaterThanOrEqual(3);
});

test("慢速上传展示进度与处理阶段，主动取消后不显示失败", async ({ page }) => {
  await installSyntheticSessionRoutes(page);
  await page.route(/\/api\/ai-services\/selection(?:\?.*)?$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        code: 0,
        message: "",
        data: { status: "unavailable", message: "当前没有可用 AI 服务" }
      })
    })
  );
  await loginFromUi(page, "admin");

  const slowUploadServer = await startSlowUploadServer();
  await page.route("**/api/documents/upload", route =>
    route.continue({ url: slowUploadServer.url })
  );

  try {
    await page.goto("/#/data-import/import");
    const cdp = await page.context().newCDPSession(page);
    await cdp.send("Network.enable");
    await cdp.send("Network.emulateNetworkConditions", {
      offline: false,
      latency: 120,
      downloadThroughput: 512 * 1024,
      uploadThroughput: 128 * 1024,
      connectionType: "cellular3g"
    });

    await page.locator('input[type="file"]').setInputFiles({
      name: "synthetic-slow-upload.xlsx",
      mimeType:
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      buffer: Buffer.alloc(1024 * 1024, 0x41)
    });
    await slowUploadServer.uploadStarted;

    const uploadZone = page.locator(".app-upload-zone");
    await expect(
      uploadZone.locator(".upload-progress .el-progress")
    ).toBeVisible();
    await expect(uploadZone.locator(".upload-status__detail")).toHaveText(
      /正在建立上传连接|\d+%|已上传/
    );
    await slowUploadServer.uploadReceived;
    await expect(uploadZone).toContainText("文件已上传，正在解析结构", {
      timeout: 20_000
    });

    await uploadZone.getByRole("button", { name: "停止等待" }).click();
    await expect(uploadZone).toContainText("点击上传");

    await expect(page.locator(".el-message--error")).toHaveCount(0);
    await expect(uploadZone.locator(".upload-status__error")).toHaveCount(0);
    await expect(page.getByText("synthetic-slow-upload.xlsx")).toHaveCount(0);
  } finally {
    await slowUploadServer.close();
  }
});
