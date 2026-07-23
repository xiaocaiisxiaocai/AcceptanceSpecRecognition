import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  DEFAULT_BUDGETS,
  evaluateBundleBudget
} from "./assert-bundle-budget.mjs";

function createFixture({ includeDashboard = true } = {}) {
  const directory = mkdtempSync(join(tmpdir(), "bundle-budget-"));
  mkdirSync(join(directory, ".vite"), { recursive: true });
  mkdirSync(join(directory, "static", "js"), { recursive: true });
  mkdirSync(join(directory, "static", "css"), { recursive: true });

  writeFileSync(join(directory, "static", "js", "index-a1b2.js"), "main();");
  writeFileSync(
    join(directory, "static", "js", "Dashboard-c3d4.js"),
    "dashboard();"
  );
  writeFileSync(join(directory, "static", "css", "Dashboard-e5f6.css"), ".a{}");

  const manifest = {
    "index.html": {
      file: "static/js/index-a1b2.js",
      src: "index.html",
      isEntry: true
    }
  };
  if (includeDashboard) {
    manifest["src/views/dashboard/index.vue"] = {
      file: "static/js/Dashboard-c3d4.js",
      src: "src/views/dashboard/index.vue",
      isDynamicEntry: true,
      css: ["static/css/Dashboard-e5f6.css"]
    };
  }
  writeFileSync(
    join(directory, ".vite", "manifest.json"),
    JSON.stringify(manifest)
  );

  return directory;
}

test("uses stable manifest source keys instead of hashed output names", t => {
  const directory = createFixture();
  t.after(() => rmSync(directory, { recursive: true, force: true }));

  const result = evaluateBundleBudget(directory);

  assert.deepEqual(result.findings, []);
  assert.deepEqual(
    result.controlled.map(item => item.resources),
    [
      ["static/js/index-a1b2.js"],
      ["static/js/Dashboard-c3d4.js", "static/css/Dashboard-e5f6.css"]
    ]
  );
});

test("fails independently when the main entry or Dashboard budget is exceeded", t => {
  const directory = createFixture();
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const oneByteControlledBudgets = {
    ...DEFAULT_BUDGETS,
    maxMainEntryGzip: 1,
    maxDashboardGzip: 1
  };

  const result = evaluateBundleBudget(directory, oneByteControlledBudgets);

  assert.ok(result.findings.some(finding => finding.startsWith("main entry:")));
  assert.ok(
    result.findings.some(finding =>
      finding.startsWith("Dashboard async chunk:")
    )
  );
});

test("fails closed when the Dashboard manifest entry is missing", t => {
  const directory = createFixture({ includeDashboard: false });
  t.after(() => rmSync(directory, { recursive: true, force: true }));

  const result = evaluateBundleBudget(directory);

  assert.ok(
    result.findings.includes(
      "Vite manifest 中未唯一定位 Dashboard async chunk: src/views/dashboard/index.vue"
    )
  );
});
