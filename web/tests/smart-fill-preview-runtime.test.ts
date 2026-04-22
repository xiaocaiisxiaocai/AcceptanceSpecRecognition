import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = (() => {
  const cwd = process.cwd();
  if (existsSync(resolve(cwd, "web/package.json"))) {
    return cwd;
  }

  return resolve(cwd, "..");
})();

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(repositoryRoot, relativePath), "utf8");

test("MatchPreviewTable 应先声明 getReviewStatus，再在其他计算逻辑中引用它", () => {
  const source = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  const reviewStatusIndex = source.indexOf("const getReviewStatus =");
  const reviewInFlightIndex = source.indexOf("const isReviewInFlight =");

  assert.notEqual(reviewStatusIndex, -1, "应存在 getReviewStatus 定义");
  assert.notEqual(reviewInFlightIndex, -1, "应存在 isReviewInFlight 定义");
  assert.ok(
    reviewStatusIndex < reviewInFlightIndex,
    "getReviewStatus 必须先于 isReviewInFlight 定义，避免 setup 阶段触发暂时性死区"
  );
});

test("smart-fill 第四步在没有预览结果时应显示明确空状态，避免出现空白页", () => {
  const source = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(source, /v-else-if="!loading && batchPreviewResults\.length === 0"/);
  assert.match(source, /当前没有预览结果/);
  assert.match(source, /页面状态可能已失效，请返回上一步重新匹配/);
});
