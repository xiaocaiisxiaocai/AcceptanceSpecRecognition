import { expect, test } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";
import { createSyntheticDocx } from "./helpers/synthetic-docx";

type ApiEnvelope<T> = { code: number; data: T; message?: string };

async function readApi<T>(response: {
  ok(): boolean;
  json(): Promise<unknown>;
}) {
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as ApiEnvelope<T>;
  expect(body.code, body.message).toBe(0);
  return body.data;
}

test("数据导入页面使用合成 Word 完成上传和导入", async ({ page }) => {
  const accessToken = await loginFromUi(page, "admin");
  const headers = { Authorization: `Bearer ${accessToken}` };
  const suffix = Date.now().toString(36);
  const source = createSyntheticDocx([
    ["项目", "规格", "验收", "备注"],
    ["IMPORT-P1", "IMPORT-S1", "IMPORT-OK", "synthetic"]
  ]);

  await page.goto("/#/data-import/import");
  await expect(page.getByText("数据导入", { exact: true })).toBeVisible();
  const uploadResponse = page.waitForResponse(
    response =>
      response.url().endsWith("/api/documents/upload") &&
      response.request().method() === "POST"
  );
  await page.locator('input[type="file"]').setInputFiles({
    name: `synthetic-import-${suffix}.docx`,
    mimeType:
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    buffer: Buffer.from(source)
  });
  const upload = await readApi<{ fileId: number }>(await uploadResponse);
  await expect(
    page
      .locator(".file-name")
      .filter({ hasText: `synthetic-import-${suffix}.docx` })
      .first()
  ).toBeVisible();

  const customer = await readApi<{ id: number }>(
    await page.request.post("/api/customers", {
      headers,
      data: { name: `E2E-IMPORT-C-${suffix}` }
    })
  );
  const process = await readApi<{ id: number }>(
    await page.request.post("/api/processes", {
      headers,
      data: { name: `E2E-IMPORT-P-${suffix}` }
    })
  );
  const imported = await readApi<{ successCount: number }>(
    await page.request.post("/api/documents/import", {
      headers,
      data: {
        fileId: upload.fileId,
        tableIndex: 0,
        customerId: customer.id,
        processId: process.id,
        mapping: {
          projectColumn: 0,
          specificationColumn: 1,
          acceptanceColumn: 2,
          remarkColumn: 3,
          headerRowIndex: 0,
          dataStartRowIndex: 1
        }
      }
    })
  );
  expect(imported.successCount).toBe(1);
});

test("合成 Word 完成上传、智能填充确认与结果下载契约", async ({ page }) => {
  const accessToken = await loginFromUi(page, "admin");
  const headers = { Authorization: `Bearer ${accessToken}` };
  const suffix = Date.now().toString(36);
  const source = createSyntheticDocx([
    ["项目", "规格", "验收", "备注"],
    ["E2E-P1", "E2E-S1", "", ""]
  ]);

  await page.goto("/#/smart-fill/fill");
  await expect(page.getByText("智能填充", { exact: true })).toBeVisible();
  const uploadResponse = page.waitForResponse(
    response =>
      response.url().endsWith("/api/documents/upload") &&
      response.request().method() === "POST"
  );
  await page.locator('input[type="file"]').setInputFiles({
    name: `synthetic-smart-fill-${suffix}.docx`,
    mimeType:
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    buffer: Buffer.from(source)
  });
  const upload = await readApi<{ fileId: number }>(await uploadResponse);
  await expect(
    page
      .locator(".file-name")
      .filter({ hasText: `synthetic-smart-fill-${suffix}.docx` })
      .first()
  ).toBeVisible();
  const customer = await readApi<{ id: number }>(
    await page.request.post("/api/customers", {
      headers,
      data: { name: `E2E-C-${suffix}` }
    })
  );
  const process = await readApi<{ id: number }>(
    await page.request.post("/api/processes", {
      headers,
      data: { name: `E2E-P-${suffix}` }
    })
  );
  await readApi(
    await page.request.post("/api/specs", {
      headers,
      data: {
        customerId: customer.id,
        processId: process.id,
        project: "E2E-P1",
        specification: "E2E-S1",
        acceptance: "E2E-OK",
        remark: "synthetic"
      }
    })
  );
  const preview = await readApi<{
    tables: Array<{
      items: Array<{ rowIndex: number; bestMatch: { specId: number } }>;
    }>;
  }>(
    await page.request.post("/api/matching/batch-preview", {
      headers,
      data: {
        fileId: upload.fileId,
        customerId: customer.id,
        processId: process.id,
        config: { minScoreThreshold: 0 },
        tables: [
          {
            tableIndex: 0,
            projectColumnIndex: 0,
            specificationColumnIndex: 1,
            acceptanceColumnIndex: 2,
            remarkColumnIndex: 3
          }
        ]
      }
    })
  );
  expect(preview.tables[0]?.items).toHaveLength(1);
  const item = preview.tables[0]!.items[0]!;
  const execution = await readApi<{ taskId: string }>(
    await page.request.post("/api/matching/batch-execute", {
      headers,
      data: {
        fileId: upload.fileId,
        customerId: customer.id,
        processId: process.id,
        config: { minScoreThreshold: 0, highConfidenceThreshold: 0.95 },
        tables: [
          {
            tableIndex: 0,
            projectColumnIndex: 0,
            specificationColumnIndex: 1,
            acceptanceColumnIndex: 2,
            remarkColumnIndex: 3,
            mappings: [
              { rowIndex: item.rowIndex, specId: item.bestMatch.specId }
            ]
          }
        ]
      }
    })
  );
  const download = await page.request.get(
    `/api/matching/download/${execution.taskId}`,
    { headers }
  );
  expect(download.ok()).toBeTruthy();
  expect((await download.body()).length).toBeGreaterThan(0);
});

test("合成 Word 完成 BatchReply 预览、执行与下载契约", async ({ page }) => {
  const accessToken = await loginFromUi(page, "admin");
  const headers = { Authorization: `Bearer ${accessToken}` };
  const source = createSyntheticDocx([
    ["项目", "规格", "验收", "备注"],
    ["B-P1", "B-S1", "B-OK", "B-R"]
  ]);
  const target = createSyntheticDocx([
    ["项目", "规格", "验收", "备注"],
    ["B-P1", "B-S1", "", ""]
  ]);
  await page.goto("/#/batch-reply/index");
  await expect(page.getByText("批量回复", { exact: true })).toBeVisible();
  const sourceUploadResponse = page.waitForResponse(
    response =>
      response.url().endsWith("/api/batch-reply/source/upload") &&
      response.request().method() === "POST"
  );
  await page
    .getByRole("tabpanel", { name: "来源文件" })
    .locator('input[type="file"]')
    .setInputFiles({
      name: "synthetic-batch-source.docx",
      mimeType:
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      buffer: Buffer.from(source)
    });
  const sourceUpload = await readApi<{ sessionId: string }>(
    await sourceUploadResponse
  );
  await expect(
    page.getByRole("tab", {
      name: "synthetic-batch-source.docx",
      exact: true
    })
  ).toBeVisible();
  const tableConfigsJson = JSON.stringify([
    {
      tableIndex: 0,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      filterEmptySourceRows: true
    }
  ]);
  const preview = await readApi<{ readyCount: number }>(
    await page.request.post("/api/batch-reply/preview", {
      headers,
      multipart: {
        sessionId: sourceUpload.sessionId,
        tableConfigsJson,
        targetFiles: {
          name: "synthetic-batch-target.docx",
          mimeType:
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
          buffer: Buffer.from(target)
        }
      }
    })
  );
  expect(preview.readyCount).toBe(1);
  const execution = await readApi<{ taskId: string; successCount: number }>(
    await page.request.post("/api/batch-reply/execute", {
      headers,
      data: { sessionId: sourceUpload.sessionId }
    })
  );
  expect(execution.successCount).toBe(1);
  const download = await page.request.get(
    `/api/batch-reply/download/${execution.taskId}`,
    { headers }
  );
  expect(download.ok()).toBeTruthy();
  expect((await download.body()).length).toBeGreaterThan(0);
});
