import { expect, test, type Page, type Route } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

const fulfillApi = (route: Route, data: unknown) =>
  route.fulfill({
    contentType: "application/json",
    body: JSON.stringify({ code: 0, message: "", data })
  });

const installSyntheticSession = async (page: Page) => {
  await page.route(/\/login$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        success: true,
        data: {
          avatar: "",
          username: "admin",
          nickname: "智能填充激活 E2E 管理员",
          roleCode: "admin",
          permissions: [
            "*:*:*",
            "menu:smart-fill",
            "page:smart-fill:index",
            "btn:document:upload",
            "btn:matching:preview-batch",
            "btn:matching-fill:execute-batch",
            "btn:matching:download"
          ],
          accessToken: "smart-fill-activation-e2e-token",
          expires: new Date(Date.now() + 60 * 60 * 1000).toISOString()
        }
      })
    })
  );
  await page.route(/\/api\/dashboard\/summary(?:\?.*)?$/, route =>
    fulfillApi(route, {
      periodPreset: "last7",
      customerTotal: 0,
      processTotal: 0,
      specTotal: 0,
      smartFillTaskCount: 0,
      smartFillTotalRows: 0,
      smartFillMatchedRows: 0,
      smartFillAdoptedRows: 0,
      matchingRate: 0,
      adoptionRate: 0,
      dailyTrend: []
    })
  );
};

test("智能填充 keep-alive 返回时先核对任务状态并按状态恢复", async ({
  page
}) => {
  await installSyntheticSession(page);
  await page.route(/\/api\/ai-services\/selection(?:\?.*)?$/, route => {
    const purpose = new URL(route.request().url()).searchParams.get("purpose");
    return fulfillApi(
      route,
      purpose === "embedding"
        ? {
            status: "available",
            serviceId: 901,
            name: "E2E Embedding",
            model: "synthetic-embedding",
            message: ""
          }
        : {
            status: "available",
            serviceId: 902,
            name: "E2E LLM",
            model: "synthetic-llm",
            message: ""
          }
    );
  });

  let releaseScopeRequests!: () => void;
  const scopeRelease = new Promise<void>(resolve => {
    releaseScopeRequests = resolve;
  });
  const scopeRequestCounts = new Map<string, number>();
  const abortedScopeRequests = new Set<string>();
  page.on("requestfailed", request => {
    const pathname = new URL(request.url()).pathname;
    if (
      pathname === "/api/customers" ||
      pathname === "/api/processes" ||
      pathname === "/api/machine-models"
    ) {
      abortedScopeRequests.add(pathname);
    }
  });
  await page.route(
    /\/api\/(?:customers|processes|machine-models)(?:\?.*)?$/,
    async route => {
      const pathname = new URL(route.request().url()).pathname;
      const requestCount = (scopeRequestCounts.get(pathname) ?? 0) + 1;
      scopeRequestCounts.set(pathname, requestCount);
      if (requestCount === 1) await scopeRelease;
      try {
        const items =
          pathname === "/api/customers" ? [{ id: 1, name: "E2E 客户" }] : [];
        await fulfillApi(route, {
          items,
          total: items.length,
          page: 1,
          pageSize: 100,
          totalPages: items.length === 0 ? 0 : 1,
          hasNext: false,
          hasPrevious: false
        });
      } catch {
        // keep-alive 失活会取消页面拥有的作用域选项请求。
      }
    }
  );
  await page.route(/\/api\/documents\/upload$/, route =>
    fulfillApi(route, {
      fileId: 9,
      fileName: "retained.xlsx",
      fileType: 1,
      fileHash: "synthetic",
      isDuplicate: false,
      tableCount: 1,
      tableCountReady: true
    })
  );
  await page.route(/\/api\/documents\/9\/tables$/, route =>
    fulfillApi(route, [
      {
        index: 0,
        name: "目标表1",
        rowCount: 2,
        columnCount: 4,
        isNested: false,
        headers: ["项目", "规格", "验收", "备注"],
        hasMergedCells: false,
        usedRangeStartRow: 1,
        usedRangeStartColumn: 1
      }
    ])
  );
  await page.route(/\/api\/smart-config\/recognize$/, route =>
    fulfillApi(route, {
      fileId: 9,
      tables: [
        {
          tableIndex: 0,
          tableName: "目标表1",
          headers: ["项目", "规格", "验收", "备注"],
          headerRowIndex: 0,
          headerRowCount: 1,
          dataStartRowIndex: 1,
          projectColumnIndex: 0,
          specificationColumnIndex: 1,
          acceptanceColumnIndex: 2,
          remarkColumnIndex: 3,
          isSpecificationOnly: false,
          confidence: 1,
          source: "Rule",
          decision: "AutoApply",
          recommendation: "Recommended",
          fields: []
        }
      ]
    })
  );
  await page.route(/\/api\/matching\/batch-preview$/, route =>
    fulfillApi(route, {
      tables: [
        {
          tableIndex: 0,
          items: [
            {
              rowIndex: 1,
              sourceProject: "保留任务项目",
              sourceSpecification: "保留任务规格",
              hasMatch: true,
              confidenceLevel: "high",
              bestMatch: {
                specId: 88,
                project: "保留任务项目",
                specification: "保留任务规格",
                acceptance: "保留任务验收",
                remark: "",
                score: 1,
                embeddingScore: 1,
                scoreDetails: {},
                decision: "autoApply",
                topCandidates: []
              }
            }
          ],
          totalMatched: 1,
          highConfidenceCount: 1,
          mediumConfidenceCount: 0,
          lowConfidenceCount: 0,
          ambiguousCount: 0
        }
      ],
      totalMatched: 1,
      highConfidenceCount: 1,
      mediumConfidenceCount: 0,
      lowConfidenceCount: 0,
      ambiguousCount: 0
    })
  );
  await page.route(/\/api\/matching\/batch-preview-progress\/[^/?]+$/, route =>
    fulfillApi(route, {
      requestId: "synthetic-preview",
      status: "completed",
      stage: "completed",
      stageText: "已完成",
      completedItems: 1,
      totalItems: 1,
      progressPercent: 100,
      startedAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      elapsedMs: 1
    })
  );
  await page.route(/\/api\/matching\/batch-execute$/, route =>
    fulfillApi(route, {
      taskId: "retained-smart-fill-task",
      filledCount: 1,
      skippedCount: 0,
      downloadUrl: "/api/matching/download/retained-smart-fill-task"
    })
  );

  const statusSequence: string[] = [];
  await page.route(
    /\/api\/matching\/tasks\/retained-smart-fill-task\/status$/,
    route => {
      const status = statusSequence.length === 0 ? "running" : "completed";
      statusSequence.push(status);
      return fulfillApi(route, {
        taskId: "retained-smart-fill-task",
        status,
        canDownload: status === "completed",
        updatedAt: new Date().toISOString()
      });
    }
  );
  let downloadCount = 0;
  await page.route(
    /\/api\/matching\/download\/retained-smart-fill-task$/,
    route => {
      downloadCount++;
      if (downloadCount === 1) {
        return route.fulfill({
          status: 503,
          contentType: "application/json",
          body: JSON.stringify({
            message: "synthetic initial download failure"
          })
        });
      }
      return route.fulfill({
        status: 200,
        contentType:
          "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        body: Buffer.from("synthetic xlsx")
      });
    }
  );

  await loginFromUi(page, "admin");
  await page
    .getByRole("menubar")
    .getByRole("link", { name: "智能填充", exact: true })
    .click();
  await expect(page).toHaveURL(/#\/smart-fill\/fill$/);
  await expect(page.locator(".smart-fill")).toBeVisible();
  await expect
    .poll(() =>
      ["/api/customers", "/api/processes", "/api/machine-models"].every(
        pathname => (scopeRequestCounts.get(pathname) ?? 0) >= 1
      )
    )
    .toBe(true);

  await page
    .getByRole("menubar")
    .getByRole("link", { name: "仪表盘", exact: true })
    .click();
  await expect(page).toHaveURL(/#\/dashboard$/);
  await expect
    .poll(() =>
      ["/api/customers", "/api/processes", "/api/machine-models"].every(
        pathname => abortedScopeRequests.has(pathname)
      )
    )
    .toBe(true);
  releaseScopeRequests();

  await page
    .getByRole("menubar")
    .getByRole("link", { name: "智能填充", exact: true })
    .click();
  await expect(page).toHaveURL(/#\/smart-fill\/fill$/);
  await expect
    .poll(() =>
      ["/api/customers", "/api/processes", "/api/machine-models"].every(
        pathname => (scopeRequestCounts.get(pathname) ?? 0) >= 2
      )
    )
    .toBe(true);
  await page.locator('input[type="file"]').setInputFiles({
    name: "retained.xlsx",
    mimeType:
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    buffer: Buffer.from("synthetic xlsx")
  });
  await expect(
    page.locator(".file-name").filter({ hasText: "retained.xlsx" }).first()
  ).toBeVisible();
  await page.getByRole("combobox", { name: "* 客户" }).click();
  await page.getByRole("option", { name: "E2E 客户", exact: true }).click();
  await page
    .getByRole("button", { name: "识别并进入确认", exact: true })
    .click();
  await page
    .getByRole("button", {
      name: "确认所选 Sheet、学习并进入匹配配置",
      exact: true
    })
    .click();
  await page
    .getByRole("button", { name: "下一步：预览确认", exact: true })
    .click();
  await page.getByRole("button", { name: "执行填充", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "重新下载", exact: true })
  ).toBeVisible();
  expect(downloadCount).toBe(1);

  await page
    .getByRole("menubar")
    .getByRole("link", { name: "仪表盘", exact: true })
    .click();
  await expect(page).toHaveURL(/#\/dashboard$/);
  await page
    .getByRole("menubar")
    .getByRole("link", { name: "智能填充", exact: true })
    .click();
  await expect(page).toHaveURL(/#\/smart-fill\/fill$/);
  await expect.poll(() => statusSequence).toEqual(["running"]);
  expect(downloadCount).toBe(1);

  await expect.poll(() => statusSequence).toEqual(["running", "completed"]);
  await expect(
    page.getByRole("button", { name: "重新下载", exact: true })
  ).toBeVisible();
  expect(downloadCount).toBe(1);
  await page.getByRole("button", { name: "重新下载", exact: true }).click();
  await expect.poll(() => downloadCount).toBe(2);
});
