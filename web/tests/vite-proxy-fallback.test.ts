import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const viteConfig = readFileSync(resolve("web/vite.config.ts"), "utf8");

test("Vite 代理兜底端口和 API 启动端口保持一致", () => {
  assert.match(viteConfig, /http:\/\/localhost:5291/);
  assert.doesNotMatch(viteConfig, /http:\/\/localhost:5843/);
});
