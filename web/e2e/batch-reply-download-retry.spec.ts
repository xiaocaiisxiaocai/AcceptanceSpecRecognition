import { expect, test, type Page, type Route } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

const fulfillApi = (route: Route, data: unknown) =>
  route.fulfill({
    contentType: "application/json",
    body: JSON.stringify({ code: 0, message: "", data })
  });

const table = {
  index: 0,
  name: "Sheet1",
  rowCount: 2,
  columnCount: 4,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注"],
  hasMergedCells: false,
  usedRangeStartRow: 1,
  usedRangeStartColumn: 1
};

const tableData = {
  tableIndex: 0,
  headers: table.headers,
  rows: [
    ["项目", "规格", "验收", "备注"],
    ["P1", "S1", "", ""]
  ],
  totalRows: 2,
  columnCount: 4
};

const installSyntheticSession = async (page: Page) => {
  await page.route(/\/login$/, route =>
    route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        success: true,
        data: {
          avatar: "",
          username: "admin",
          nickname: "批量回复 E2E 管理员",
          roleCode: "admin",
          permissions: [
            "*:*:*",
            "menu:batch-reply",
            "page:batch-reply:index",
            "api:batch-reply:upload-source",
            "api:batch-reply:upload",
            "btn:batch-reply:preview",
            "btn:batch-reply:execute",
            "api:batch-reply:download"
          ],
          accessToken: "batch-reply-e2e-token",
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

test("批量回复执行成功后下载失败可保留任务并重试", async ({ page }) => {
  await installSyntheticSession(page);
  await page.route(/\/api\/batch-reply\/source\/upload$/, route =>
    fulfillApi(route, {
      sessionId: "batch-session",
      sourceFileName: "source.xlsx",
      sourceFileType: 1,
      tableCount: 1
    })
  );
  await page.route(
    /\/api\/batch-reply\/sessions\/batch-session\/tables$/,
    route => fulfillApi(route, [table])
  );
  await page.route(/\/api\/batch-reply\/targets\/upload$/, route =>
    fulfillApi(route, {
      sessionId: "batch-session",
      files: [
        {
          targetId: "target-1",
          fileName: "target.xlsx",
          fileType: 1,
          tableCount: 1
        }
      ]
    })
  );
  await page.route(
    /\/api\/batch-reply\/sessions\/batch-session\/targets\/target-1\/tables$/,
    route => fulfillApi(route, [table])
  );
  await page.route(
    /\/api\/batch-reply\/sessions\/batch-session\/(?:targets\/target-1\/)?tables\/0\/preview(?:\?.*)?$/,
    route => fulfillApi(route, tableData)
  );
  await page.route(/\/api\/batch-reply\/table-preview$/, route =>
    fulfillApi(route, {
      targetId: "target-1",
      fileName: "target.xlsx",
      tableIndex: 0,
      sourceTableIndex: 0,
      canApply: true,
      errors: [],
      duplicateGroups: [],
      rows: [
        {
          rowIndex: 2,
          project: "P1",
          specification: "S1",
          acceptance: "A1",
          remark: "R1"
        }
      ]
    })
  );
  await page.route(/\/api\/batch-reply\/execute$/, route =>
    fulfillApi(route, {
      taskId: "batch-task-retained",
      successCount: 1,
      failedCount: 0,
      downloadUrl: "/api/batch-reply/download/batch-task-retained",
      downloadFileName: "batch-result.zip",
      files: [
        {
          targetId: "target-1",
          fileName: "target.xlsx",
          success: true,
          message: "执行成功"
        }
      ]
    })
  );

  const downloadTaskIds: string[] = [];
  await page.route(/\/api\/batch-reply\/download\/[^/?]+$/, route => {
    downloadTaskIds.push(
      new URL(route.request().url()).pathname.split("/").at(-1)!
    );
    if (downloadTaskIds.length === 1) {
      return route.fulfill({
        status: 503,
        contentType: "text/plain",
        body: "synthetic download failure"
      });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/zip",
      body: Buffer.from("synthetic zip")
    });
  });

  await loginFromUi(page, "admin");
  await expect(page).toHaveURL(/#\/dashboard$/);
  await page.goto("/#/batch-reply/index");
  await expect(page).toHaveURL(/#\/batch-reply\/index$/);

  await page
    .getByRole("tabpanel", { name: "来源文件" })
    .locator('input[type="file"]')
    .setInputFiles({
      name: "source.xlsx",
      mimeType:
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      buffer: Buffer.from("source")
    });
  await expect(
    page.locator(".source-file-name").filter({ hasText: "source.xlsx" }).first()
  ).toBeVisible();

  await page.getByRole("tab", { name: "目标文件", exact: true }).click();
  await page
    .getByRole("tabpanel", { name: "目标文件" })
    .locator('input[type="file"]')
    .setInputFiles({
      name: "target.xlsx",
      mimeType:
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      buffer: Buffer.from("target")
    });
  await expect(
    page
      .getByRole("tabpanel", { name: "目标文件" })
      .getByText("target.xlsx", { exact: true })
      .first()
  ).toBeVisible();
  await page.getByRole("button", { name: "预览回写", exact: true }).click();
  await expect(
    page.getByText("当前 Sheet/表格可直接写回", { exact: true })
  ).toBeVisible();

  await page.getByRole("tab", { name: "执行结果", exact: true }).click();
  await page
    .getByRole("button", { name: "执行已完成目标文件", exact: true })
    .click();
  await expect(
    page.getByText("执行完成：成功 1 份，失败 0 份", { exact: true })
  ).toBeVisible();
  await expect(
    page.getByText("批量回复已执行成功，但结果下载失败，请重试下载", {
      exact: true
    })
  ).toBeVisible();

  await page.getByRole("button", { name: "重新下载", exact: true }).click();
  await expect
    .poll(() => downloadTaskIds)
    .toEqual(["batch-task-retained", "batch-task-retained"]);
  await expect(
    page.getByText("批量回复已执行成功，但结果下载失败，请重试下载", {
      exact: true
    })
  ).toHaveCount(0);
});
