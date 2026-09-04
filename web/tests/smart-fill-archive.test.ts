import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

test("填充存档页面接入独立路由、查询和下载接口", () => {
  const manifest = readFileSync(
    resolve(process.cwd(), "shared/navigation/navigation-manifest.json"),
    "utf8"
  );
  const route = readFileSync(
    resolve(process.cwd(), "web/src/router/modules/other.ts"),
    "utf8"
  );
  const api = readFileSync(
    resolve(process.cwd(), "web/src/api/execution-history.ts"),
    "utf8"
  );

  assert.match(manifest, /other-smart-fill-archives/);
  assert.match(route, /SmartFillArchives/);
  assert.match(api, /getSmartFillArchiveList/);
  assert.match(api, /downloadSmartFillArchive/);
});

test("填充存档使用当前页摘要且不使用抽屉", () => {
  const page = readFileSync(
    resolve(process.cwd(), "web/src/views/other/smart-fill-archives/index.vue"),
    "utf8"
  );

  assert.match(page, /当前页/);
  assert.match(page, /hasResultArchive/);
  assert.match(page, /downloadSmartFillArchive/);
  assert.doesNotMatch(page, /el-drawer/);
});
