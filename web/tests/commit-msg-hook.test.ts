import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

test("commit-msg hook 应识别 Windows 盘符绝对路径", () => {
  const source = readProjectFile("web/.husky/commit-msg");

  assert.match(source, /\[A-Za-z\]:\*/);
});
