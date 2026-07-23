import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import {
  decideTargetUpload,
  createTargetFileSignature
} from "../src/views/batch-reply/target-upload.ts";

const createFileLike = (
  name: string,
  size = 1024,
  lastModified = 1710000000000
) => ({
  name,
  size,
  lastModified
});

test("未上传来源文件时拒绝添加目标文件", () => {
  const result = decideTargetUpload({
    hasSourceFile: false,
    accept: ".xlsx",
    existingSignatures: [],
    file: createFileLike("target.xlsx")
  });

  assert.deepEqual(result, {
    status: "rejected",
    message: "请先上传来源文件",
    level: "warning"
  });
});

test("扩展名不匹配时拒绝添加目标文件", () => {
  const result = decideTargetUpload({
    hasSourceFile: true,
    accept: ".xlsx",
    existingSignatures: [],
    file: createFileLike("target.docx")
  });

  assert.deepEqual(result, {
    status: "rejected",
    message: "目标文件仅支持 .xlsx 格式",
    level: "error"
  });
});

test("重复目标文件会被拦截", () => {
  const file = createFileLike("target.xlsx", 2048, 1710001234567);
  const result = decideTargetUpload({
    hasSourceFile: true,
    accept: ".xlsx",
    existingSignatures: [createTargetFileSignature(file)],
    file
  });

  assert.deepEqual(result, {
    status: "rejected",
    message: "target.xlsx 已在列表中",
    level: "warning"
  });
});

test("有效目标文件会生成待添加项", () => {
  const file = createFileLike("target.xlsx", 4096, 1710009999999);
  const result = decideTargetUpload({
    hasSourceFile: true,
    accept: ".xlsx",
    existingSignatures: [],
    file
  });

  assert.equal(result.status, "accepted");
  assert.equal(result.item?.id, createTargetFileSignature(file));
  assert.equal(result.item?.file, file);
});

test("已有目标文件后仍保留继续添加入口并通过 key 重建上传任务", () => {
  const panelSource = readFileSync(
    new URL(
      "../src/views/batch-reply/components/TargetFilesPanel.vue",
      import.meta.url
    ),
    "utf8"
  );

  assert.match(panelSource, /:key="targetUploadKey"/);
  assert.match(panelSource, /reset-after-success/);
  assert.match(panelSource, /继续添加目标文件/);
  assert.doesNotMatch(panelSource, /v-show="targetFiles\.length === 0"/);
});
