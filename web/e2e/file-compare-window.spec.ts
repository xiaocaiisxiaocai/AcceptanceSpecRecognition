import { expect, test, type Page, type Route } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

const apiOk = (data: unknown) => ({
  status: 200,
  contentType: "application/json",
  body: JSON.stringify({ code: 0, message: "", data })
});

const tableInfos = [
  {
    index: 0,
    name: "Sheet1",
    rowCount: 450,
    columnCount: 120,
    isNested: false,
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 1,
    usedRangeStartColumn: 1
  },
  {
    index: 1,
    name: "Sheet2",
    rowCount: 450,
    columnCount: 120,
    isNested: false,
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 1,
    usedRangeStartColumn: 1
  }
];

const previewWindow = (url: string, fileId: number, tableIndex: number) => {
  const query = new URL(url).searchParams;
  const rowOffset = Number(query.get("rowOffset") ?? 0);
  const columnOffset = Number(query.get("columnOffset") ?? 0);
  const previewRows = Number(query.get("previewRows") ?? 200);
  const previewColumns = Number(query.get("previewColumns") ?? 60);
  const rowCount = Math.min(previewRows, 450 - rowOffset);
  const columnCount = Math.min(previewColumns, 120 - columnOffset);
  const prefix = tableIndex === 0 ? "STALE-SHEET-0" : "SHEET-1";
  const rows = Array.from({ length: rowCount }, (_, rowIndex) =>
    Array.from(
      { length: columnCount },
      (_, columnIndex) =>
        `${prefix}-${fileId}-${rowOffset + rowIndex + 1}-${columnOffset + columnIndex + 1}`
    )
  );
  return {
    tableIndex,
    headers: Array.from(
      { length: columnCount },
      (_, index) => `列 ${columnOffset + index + 1}`
    ),
    rows,
    structuredRows: [],
    totalRows: 450,
    columnCount,
    rowOffset,
    columnOffset,
    totalColumns: 120
  };
};

async function mockCompareApis(page: Page) {
  await page.route("**/api/file-compare/upload", async route => {
    await route.fulfill(
      apiOk({
        fileA: {
          fileId: 101,
          fileName: "large-a.xlsx",
          fileType: 1,
          fileHash: "a",
          isDuplicate: false,
          tableCount: 2,
          tableCountReady: true
        },
        fileB: {
          fileId: 102,
          fileName: "large-b.xlsx",
          fileType: 1,
          fileHash: "b",
          isDuplicate: false,
          tableCount: 2,
          tableCountReady: true
        }
      })
    );
  });

  await page.route("**/api/file-compare/preview", async route => {
    const body = route.request().postDataJSON() as {
      includeUnchanged?: boolean;
    };
    expect(body.includeUnchanged).toBe(false);
    await route.fulfill(
      apiOk({
        fileType: 1,
        items: [
          {
            diffType: "Modified",
            originalText: "旧值-1",
            currentText: "新值-1",
            displayLocation: "Sheet1!A1",
            location: { tableIndex: 0, rowIndex: 1, columnIndex: 1 }
          },
          {
            diffType: "Modified",
            originalText: "旧值-2",
            currentText: "新值-2",
            displayLocation: "Sheet2!A1",
            location: { tableIndex: 1, rowIndex: 1, columnIndex: 1 }
          }
        ],
        hunks: [],
        addedCount: 0,
        removedCount: 0,
        modifiedCount: 2,
        unchangedCount: 53_998,
        totalCount: 54_000
      })
    );
  });

  await page.route(/\/api\/documents\/(101|102)\/tables$/, async route => {
    await route.fulfill(apiOk(tableInfos));
  });

  await page.route(
    /\/api\/documents\/(101|102)\/tables\/(0|1)\/preview/,
    async (route: Route) => {
      const match = route
        .request()
        .url()
        .match(/\/documents\/(101|102)\/tables\/(0|1)\/preview/);
      const fileId = Number(match?.[1]);
      const tableIndex = Number(match?.[2]);
      if (tableIndex === 0) {
        await new Promise(resolve => setTimeout(resolve, 350));
      }
      await route.fulfill(
        apiOk(previewWindow(route.request().url(), fileId, tableIndex))
      );
    }
  );
}

test("大表对比可快速切换工作表、分页、仅看差异并忽略过期预览", async ({
  page
}) => {
  await loginFromUi(page, "admin");
  await mockCompareApis(page);
  await page.goto("/#/file-compare/compare");

  const fileInputs = page.locator('input[type="file"]');
  await fileInputs.nth(0).setInputFiles({
    name: "large-a.xlsx",
    mimeType:
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    buffer: Buffer.from("synthetic-a")
  });
  await fileInputs.nth(1).setInputFiles({
    name: "large-b.xlsx",
    mimeType:
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    buffer: Buffer.from("synthetic-b")
  });

  const firstPreview = page.waitForRequest(request =>
    /\/tables\/0\/preview/.test(request.url())
  );
  await page.getByRole("button", { name: "开始对比" }).click();
  await firstPreview;

  await page.locator(".table-select .el-select__wrapper").click();
  await page.getByText("工作表 2（Sheet2）", { exact: true }).click();

  await expect(
    page.getByText("SHEET-1-101-1-1", { exact: true })
  ).toBeVisible();
  await expect(page.getByText("STALE-SHEET-0", { exact: false })).toHaveCount(
    0
  );
  await expect(page.getByText("行 1-200 / 450", { exact: true })).toBeVisible();
  await expect(page.getByText("列 1-60 / 120", { exact: true })).toBeVisible();

  await page.getByText("仅显示差异", { exact: true }).click();
  await expect(
    page.locator(".compare-pane").nth(0).locator("tbody tr")
  ).toHaveCount(1);
  await page.getByText("仅显示差异", { exact: true }).click();

  const nextRowsRequest = page.waitForRequest(request => {
    const url = new URL(request.url());
    return (
      /\/tables\/1\/preview/.test(url.pathname) &&
      url.searchParams.get("rowOffset") === "200"
    );
  });
  await page.getByRole("button", { name: "下一批行" }).click();
  await nextRowsRequest;
  await expect(
    page.getByText("行 201-400 / 450", { exact: true })
  ).toBeVisible();
  await expect(
    page.getByText("SHEET-1-102-201-1", { exact: true })
  ).toBeVisible();
});
