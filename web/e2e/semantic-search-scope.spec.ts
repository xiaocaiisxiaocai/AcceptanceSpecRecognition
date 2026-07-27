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
          nickname: "语义搜索 E2E 管理员",
          roleCode: "admin",
          permissions: [
            "*:*:*",
            "menu:base-data",
            "page:base-data:specs",
            "btn:spec:semantic-search"
          ],
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

const semanticResponse = (scope: "A" | "B") => ({
  queryCount: 1,
  candidateCount: 1,
  embeddingModel: "synthetic-embedding",
  groups: [
    {
      queryIndex: 0,
      queryText: `查询-${scope}`,
      totalHits: 1,
      items: [
        {
          id: scope === "A" ? 101 : 202,
          customerId: scope === "A" ? 1 : 2,
          customerName: `客户${scope}`,
          machineModelId: scope === "A" ? 11 : 22,
          machineModelName: `机型${scope}`,
          processId: scope === "A" ? 111 : 222,
          processName: `制程${scope}`,
          project: `${scope}-语义项目`,
          specification: `${scope}-语义规格`,
          acceptance: `${scope}-验收`,
          remark: `${scope}-备注`,
          score: 0.95,
          importedAt: "2026-07-27T00:00:00Z"
        }
      ]
    }
  ]
});

const clickGroup = async (page: Page, scope: "A" | "B") => {
  const group = page
    .locator(".el-tree-node__content")
    .filter({ hasText: `机型${scope} / 制程${scope}` });
  await expect(group).toBeVisible({ timeout: 10_000 });
  await group.click();
};

const searchCurrentGroup = async (page: Page, query: string) => {
  await page.getByRole("button", { name: "AI搜索", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "AI搜索" });
  await dialog.getByPlaceholder(/示例：/).fill(query);
  await dialog.getByRole("button", { name: "执行搜索", exact: true }).click();
};

test("语义搜索迟到响应与编辑动作始终绑定发起分组", async ({ page }) => {
  await installSyntheticSession(page);
  await page.route(/\/api\/specs\/groups$/, route =>
    fulfillApi(route, [
      {
        customerId: 1,
        customerName: "客户A",
        machineModelId: 11,
        machineModelName: "机型A",
        processId: 111,
        processName: "制程A",
        specCount: 1
      },
      {
        customerId: 2,
        customerName: "客户B",
        machineModelId: 22,
        machineModelName: "机型B",
        processId: 222,
        processName: "制程B",
        specCount: 1
      }
    ])
  );
  await page.route(/\/api\/specs(?:\?.*)?$/, route =>
    fulfillApi(route, {
      items: [],
      total: 0,
      page: 1,
      pageSize: 100,
      totalPages: 0,
      hasNext: false,
      hasPrevious: false
    })
  );

  let releaseFirstA!: () => void;
  const firstARelease = new Promise<void>(resolve => {
    releaseFirstA = resolve;
  });
  let markFirstAStarted!: () => void;
  const firstAStarted = new Promise<void>(resolve => {
    markFirstAStarted = resolve;
  });
  let delayFirstA = true;
  await page.route(/\/api\/specs\/semantic-search$/, async route => {
    const request = route.request().postDataJSON() as { customerId: number };
    const scope = request.customerId === 1 ? "A" : "B";
    if (scope === "A" && delayFirstA) {
      delayFirstA = false;
      markFirstAStarted();
      await firstARelease;
      try {
        await fulfillApi(route, semanticResponse("A"));
      } catch {
        // 切换作用域会取消 A 请求，Playwright 此时拒绝继续回包。
      }
      return;
    }
    await fulfillApi(route, semanticResponse(scope));
  });

  const updatePaths: string[] = [];
  const updateBodies: Array<Record<string, unknown>> = [];
  let releaseUpdate!: () => void;
  const updateRelease = new Promise<void>(resolve => {
    releaseUpdate = resolve;
  });
  let markUpdateStarted!: () => void;
  const updateStarted = new Promise<void>(resolve => {
    markUpdateStarted = resolve;
  });
  await page.route(/\/api\/specs\/\d+$/, async route => {
    if (route.request().method() !== "PUT") {
      return route.continue();
    }
    updatePaths.push(new URL(route.request().url()).pathname);
    updateBodies.push(
      route.request().postDataJSON() as Record<string, unknown>
    );
    markUpdateStarted();
    await updateRelease;
    const id = Number(
      new URL(route.request().url()).pathname.split("/").at(-1)
    );
    return fulfillApi(route, {
      id,
      project: "A-语义项目",
      specification: "A-语义规格",
      acceptance: "A-编辑后验收",
      remark: "A-备注"
    });
  });

  await loginFromUi(page, "admin");
  await page.goto("/#/base-data/specs");
  await expect(page).toHaveURL(/#\/base-data\/specs$/);
  await clickGroup(page, "A");
  await searchCurrentGroup(page, "查询-A");
  await firstAStarted;

  await page
    .getByRole("dialog", { name: "AI搜索" })
    .getByRole("button", { name: "关闭", exact: true })
    .click();
  await clickGroup(page, "B");
  await searchCurrentGroup(page, "查询-B");
  await expect(
    page
      .getByRole("dialog", { name: "AI搜索" })
      .getByText("B-语义项目", { exact: true })
  ).toBeVisible();
  releaseFirstA();
  await expect(page.getByText("A-语义项目", { exact: true })).toHaveCount(0);
  await expect(
    page.getByLabel("当前验收规格范围").getByText("制程B", { exact: true })
  ).toBeVisible();

  await page
    .getByRole("dialog", { name: "AI搜索" })
    .getByRole("button", { name: "关闭", exact: true })
    .click();
  await clickGroup(page, "A");
  await searchCurrentGroup(page, "查询-A");
  const semanticDialog = page.getByRole("dialog", { name: "AI搜索" });
  await expect(
    semanticDialog.getByText("A-语义项目", { exact: true })
  ).toBeVisible();
  await semanticDialog
    .getByRole("button", { name: "编辑", exact: true })
    .click();

  const editDialog = page.getByRole("dialog", { name: "编辑验收规格" });
  await editDialog
    .getByPlaceholder("请输入验收标准（可选）")
    .fill("A-编辑后验收");
  await editDialog.getByRole("button", { name: "确定", exact: true }).click();
  await updateStarted;
  await editDialog.getByRole("button", { name: "取消", exact: true }).click();
  await semanticDialog
    .getByRole("button", { name: "关闭", exact: true })
    .click();
  await clickGroup(page, "B");
  releaseUpdate();

  await expect.poll(() => updatePaths).toEqual(["/api/specs/101"]);
  expect(updateBodies).toEqual([
    {
      project: "A-语义项目",
      specification: "A-语义规格",
      acceptance: "A-编辑后验收",
      remark: "A-备注"
    }
  ]);
  expect(updatePaths).not.toContain("/api/specs/202");
});
