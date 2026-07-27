import { expect, test, type Page, type Route } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

const smartFillItem = {
  id: 101,
  taskId: "task-smart-fill-a",
  taskType: "smart-fill",
  sourceFileName: "任务-A.xlsx",
  sourceFileType: 1,
  fileCount: 1,
  totalRowCount: 1,
  matchedRowCount: 1,
  adoptedRowCount: 1,
  unmatchedRowCount: 0,
  skippedRowCount: 0,
  notAdoptedRowCount: 0,
  manualSelectedRowCount: 1,
  smartFillSummary: {
    exactMatchedRowCount: 0,
    aiMatchedRowCount: 1,
    manualConfirmedRowCount: 1,
    manualEditedRowCount: 1,
    notUsedRowCount: 0,
    hasPlaybackArchive: true
  },
  createdAt: "2026-07-27T01:00:00Z"
};

const secondSmartFillItem = {
  ...smartFillItem,
  id: 102,
  taskId: "task-smart-fill-b",
  sourceFileName: "任务-B.xlsx",
  createdAt: "2026-07-27T02:00:00Z"
};

const batchReplyItem = {
  ...smartFillItem,
  id: 103,
  taskId: "task-batch-reply",
  taskType: "batch-reply",
  sourceFileName: "批量回复.xlsx",
  smartFillSummary: undefined,
  createdAt: "2026-07-27T03:00:00Z"
};

const createSmartFillDetail = (
  item: typeof smartFillItem,
  finalAcceptance: string
) => ({
  ...item,
  files: [],
  smartFillPlayback: {
    payloadVersion: 1,
    isLegacy: false,
    isSlimmed: true,
    hasFullArchive: true,
    files: [
      {
        fileName: item.sourceFileName,
        fileType: 1,
        sheets: [
          {
            sheetIndex: 0,
            sheetName: "Sheet1",
            rows: [
              {
                rowIndex: 1,
                sourceProject: `项目-${item.id}`,
                sourceSpecification: `规格-${item.id}`,
                status: "adopted",
                matchOrigin: "ai",
                isManualConfirmed: true,
                isManualEdited: true,
                displayTags: ["AI匹配", "人工确认", "人工写入"],
                previewSnapshot: {
                  confidenceLevel: "medium",
                  bestMatch: null
                },
                executionSnapshot: {
                  selectedSpecId: 901,
                  finalAcceptance,
                  finalRemark: `${finalAcceptance}-备注`,
                  manualConfirmed: true,
                  manualEdited: true,
                  status: "adopted"
                }
              }
            ]
          }
        ]
      }
    ]
  }
});

const batchReplyDetail = {
  ...batchReplyItem,
  files: [],
  batchReplyDetail: {
    files: [
      {
        fileName: "批量回复.xlsx",
        fileType: 1,
        sheets: [
          {
            sheetIndex: 0,
            sheetName: "Sheet1",
            rows: [
              {
                rowIndex: 1,
                project: "批量项目",
                specification: "批量规格",
                acceptance: "批量验收-BR",
                remark: "批量备注",
                confidencePercent: 100,
                status: "adopted",
                isManualSelected: false
              }
            ]
          }
        ]
      }
    ]
  }
};

const fullSmartFillRow = {
  rowIndex: 1,
  sourceProject: "项目-102",
  sourceSpecification: "规格-102",
  status: "adopted",
  matchOrigin: "ai",
  isManualConfirmed: true,
  isManualEdited: true,
  displayTags: ["AI匹配", "人工确认", "人工写入"],
  previewSnapshot: {
    confidenceLevel: "medium",
    bestMatch: {
      specId: 901,
      project: "候选-B",
      specification: "候选规格-B",
      acceptance: "候选验收-B",
      remark: "候选备注-B",
      score: 0.82,
      embeddingScore: 0.74,
      scoreDetails: { embedding: 0.74, rerank: 0.82 },
      decision: "manualReview",
      selectionMode: "aiRerank",
      selectionSummary: "AI 复核后建议人工确认",
      matchBasis: "specification",
      evidenceSummary: ["项目一致", "规格语义相近"],
      conflictSummary: ["规格文本不同"],
      issues: [],
      entities: [],
      topCandidates: [
        {
          rank: 1,
          specId: 901,
          project: "候选-B",
          specification: "候选规格-B",
          acceptance: "候选验收-B",
          remark: "候选备注-B",
          score: 0.82,
          embeddingScore: 0.74,
          scoreDetails: { embedding: 0.74, rerank: 0.82 },
          decision: "manualReview",
          selectionMode: "aiRerank",
          matchBasis: "specification",
          evidenceSummary: ["项目一致"],
          conflictSummary: [],
          issues: [],
          entities: []
        }
      ],
      recalledCandidateCount: 2,
      isAmbiguous: true,
      scoreGap: 0.08,
      llmEquivalence: {
        verdict: "equivalent",
        reasonType: "equivalent_expression",
        reason: "语义等价",
        confidence: 0.91
      }
    }
  },
  executionSnapshot: {
    selectedSpecId: 901,
    selectedProject: "候选-B",
    selectedSpecification: "候选规格-B",
    overrideAcceptance: "人工验收-B",
    overrideRemark: "人工备注-B",
    finalAcceptance: "最终验收-B",
    finalRemark: "最终备注-B",
    manualConfirmed: true,
    manualEdited: true,
    status: "adopted"
  }
};

const fulfillApi = (route: Route, data: unknown, status = 200) =>
  route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify({
      code: status === 200 ? 0 : status,
      message: status === 200 ? "" : "完整回放归档不存在",
      data
    })
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
          nickname: "执行历史 E2E 管理员",
          roleCode: "admin",
          permissions: ["*:*:*", "menu:other", "page:other:execution-history"],
          accessToken: "execution-history-e2e-token",
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

const selectTask = async (page: Page, fileName: string) => {
  await page.getByRole("combobox", { name: "任务下拉" }).click();
  await page.getByRole("option", { name: new RegExp(fileName) }).click();
};

test("执行记录使用服务端分页且任务 A 的迟到详情不覆盖任务 B", async ({
  page
}) => {
  await installSyntheticSession(page);
  const requestedPageSizes: number[] = [];
  await page.route(/\/api\/execution-history(?:\?.*)?$/, route => {
    const url = new URL(route.request().url());
    requestedPageSizes.push(Number(url.searchParams.get("pageSize")));
    return fulfillApi(route, {
      items: [smartFillItem, secondSmartFillItem, batchReplyItem],
      total: 3,
      page: Number(url.searchParams.get("page")),
      pageSize: Number(url.searchParams.get("pageSize")),
      totalPages: 1,
      hasNext: false,
      hasPrevious: false
    });
  });

  let releaseTaskA!: () => void;
  const taskARelease = new Promise<void>(resolve => {
    releaseTaskA = resolve;
  });
  let markTaskAStarted!: () => void;
  const taskAStarted = new Promise<void>(resolve => {
    markTaskAStarted = resolve;
  });
  let delayFirstTaskA = true;

  await page.route(/\/api\/execution-history\/\d+$/, async route => {
    const id = Number(
      new URL(route.request().url()).pathname.split("/").at(-1)
    );
    if (id === smartFillItem.id && delayFirstTaskA) {
      delayFirstTaskA = false;
      markTaskAStarted();
      await taskARelease;
      try {
        await fulfillApi(
          route,
          createSmartFillDetail(smartFillItem, "A-迟到验收")
        );
      } catch {
        // 详情 A 已被 AbortSignal 取消时，Playwright 会拒绝继续回包。
      }
      return;
    }

    const detail =
      id === secondSmartFillItem.id
        ? createSmartFillDetail(secondSmartFillItem, "B-最终验收")
        : id === batchReplyItem.id
          ? batchReplyDetail
          : createSmartFillDetail(smartFillItem, "A-最新验收");
    await fulfillApi(route, detail);
  });

  await loginFromUi(page, "admin");
  await page.goto("/#/other/execution-history");
  await taskAStarted;

  await selectTask(page, "任务-B.xlsx");
  await expect(page.getByText("B-最终验收", { exact: true })).toBeVisible();

  releaseTaskA();
  await expect(page.getByText("A-迟到验收", { exact: true })).toHaveCount(0);
  expect(requestedPageSizes).toContain(50);

  await page.locator(".task-control-row > .el-pagination .el-select").click();
  await page
    .locator(".el-select-dropdown:visible")
    .getByRole("option", { name: "100条/页", exact: true })
    .click();
  await expect.poll(() => requestedPageSizes.at(-1)).toBe(100);
});

test("逐行完整回放失败可降级重试并缓存，批量回复仍走原有路径", async ({
  page
}) => {
  await installSyntheticSession(page);
  await page.route(/\/api\/execution-history(?:\?.*)?$/, route =>
    fulfillApi(route, {
      items: [secondSmartFillItem, batchReplyItem],
      total: 2,
      page: 1,
      pageSize: 50,
      totalPages: 1,
      hasNext: false,
      hasPrevious: false
    })
  );
  await page.route(/\/api\/execution-history\/\d+$/, route => {
    const id = Number(
      new URL(route.request().url()).pathname.split("/").at(-1)
    );
    return fulfillApi(
      route,
      id === batchReplyItem.id
        ? batchReplyDetail
        : createSmartFillDetail(secondSmartFillItem, "B-概要验收")
    );
  });

  let rowRequestCount = 0;
  await page.route(
    /\/api\/execution-history\/102\/smart-fill\/rows(?:\?.*)?$/,
    route => {
      rowRequestCount += 1;
      return rowRequestCount === 1
        ? fulfillApi(route, null, 404)
        : fulfillApi(route, fullSmartFillRow);
    }
  );

  await loginFromUi(page, "admin");
  await page.goto("/#/other/execution-history");
  await expect(page.getByText("B-概要验收", { exact: true })).toBeVisible();

  const playbackRow = page
    .locator(".result-table__body .el-table__row")
    .first();
  await playbackRow.click();
  await expect(
    page.getByText(
      "完整逐行回放暂不可用，当前仅展示精简概要。可重试加载该行详情。",
      { exact: true }
    )
  ).toBeVisible();

  await page.getByRole("button", { name: "重试加载该行详情" }).click();
  const rowDetail = page.getByTestId("execution-history-smart-fill-row-detail");
  await expect(rowDetail).toContainText("候选-B");
  await expect(rowDetail).toContainText("项目一致；规格语义相近");
  await expect(rowDetail).toContainText("语义等价");
  await expect(rowDetail).toContainText("人工验收-B");
  await expect(rowDetail).toContainText("最终验收-B");

  await playbackRow.click();
  await page.waitForTimeout(200);
  expect(rowRequestCount).toBe(2);

  await selectTask(page, "批量回复.xlsx");
  await expect(page.getByText("批量验收-BR", { exact: true })).toBeVisible();
});
