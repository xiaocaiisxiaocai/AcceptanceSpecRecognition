import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, resolve } from "node:path";

const sourceRoot = resolve(process.cwd(), "web/src");

function collectVueFiles(directory: string): string[] {
  return readdirSync(directory).flatMap(name => {
    const path = join(directory, name);
    return statSync(path).isDirectory()
      ? collectVueFiles(path)
      : path.endsWith(".vue")
        ? [path]
        : [];
  });
}

test("Element Plus 表格列宽必须使用可解析的数值", () => {
  const invalidColumns: string[] = [];

  for (const path of collectVueFiles(sourceRoot)) {
    const source = readFileSync(path, "utf8");
    for (const match of source.matchAll(/<el-table-column\b[\s\S]*?>/g)) {
      if (/\b(?:width|min-width)=["']min\(/.test(match[0])) {
        const line = source.slice(0, match.index).split(/\r?\n/).length;
        invalidColumns.push(
          `${relative(process.cwd(), path)}:${line} ${match[0].replace(/\s+/g, " ")}`
        );
      }
    }
  }

  assert.deepEqual(
    invalidColumns,
    [],
    "el-table-column 会将 width/min-width 转为数字，CSS min(...) 会解析失败"
  );
});
