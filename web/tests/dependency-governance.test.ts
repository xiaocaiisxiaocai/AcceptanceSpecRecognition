import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  existsSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

const repositoryRoot = process.cwd();
const readSource = (relativePath: string) =>
  readFileSync(join(repositoryRoot, relativePath), "utf8");

const shellExecutable = (() => {
  if (process.platform !== "win32") return "sh";
  const candidates = [
    "D:/Software/Git/bin/sh.exe",
    `${process.env.ProgramFiles ?? "C:/Program Files"}/Git/bin/sh.exe`
  ];
  return candidates.find(existsSync) ?? "sh";
})();

const runShell = (script: string, args: string[]) =>
  spawnSync(shellExecutable, [script, ...args], {
    cwd: repositoryRoot,
    encoding: "utf8"
  });

const validateRegistry = (registry: string) =>
  spawnSync(shellExecutable, ["deploy/validate-npm-registry.sh"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: { ...process.env, NPM_REGISTRY: registry }
  });

const writeProductionEnv = (
  directory: string,
  adminPassword: string,
  commonPassword = "com1"
) => {
  const envPath = join(directory, `production-${adminPassword.length}.env`);
  writeFileSync(
    envPath,
    [
      "MYSQL_ROOT_PASSWORD=RootPassword_2026!",
      "MYSQL_PASSWORD=AppPassword_2026!",
      "JWT_SIGNING_KEY=SigningKey_AtLeast32Characters_2026!",
      `AUTH_SEED_ADMIN_PASSWORD=${adminPassword}`,
      `AUTH_SEED_COMMON_PASSWORD=${commonPassword}`,
      "API_IMAGE=acceptance-api:test-20260727",
      "WEB_IMAGE=acceptance-web:test-20260727",
      "CORS_ALLOWED_ORIGIN=https://acceptance.example.invalid",
      "BROWSER_AUTH_ALLOW_INSECURE_HTTP=false",
      "BROWSER_AUTH_REFRESH_COOKIE_NAME=__Host-acceptance-refresh",
      "BROWSER_AUTH_COOKIE_SECURE=true",
      "BROWSER_AUTH_COOKIE_SAME_SITE=Strict",
      "BROWSER_AUTH_COOKIE_DOMAIN="
    ].join("\n"),
    "utf8"
  );
  return envPath;
};

test("生产环境脚本在 3、4、200、201 位边界执行同一密码契约", () => {
  const directory = mkdtempSync(join(tmpdir(), "acceptance-password-"));
  try {
    for (const [length, accepted] of [
      [3, false],
      [4, true],
      [200, true],
      [201, false]
    ] as const) {
      const result = runShell("deploy/validate-production-env.sh", [
        writeProductionEnv(directory, "a".repeat(length))
      ]);
      assert.equal(
        result.status === 0,
        accepted,
        `${length} 位密码结果不符：${result.stderr}`
      );
    }
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("npm registry 只接受不含凭据的 HTTP(S) URL", () => {
  for (const registry of [
    "https://registry.npmjs.org/",
    "http://registry.internal.example:4873/"
  ]) {
    const result = validateRegistry(registry);
    assert.equal(result.status, 0, result.stderr);
  }

  for (const registry of [
    "",
    "ftp://registry.npmjs.org/",
    "https://user:token@registry.npmjs.org/",
    "https://registry npmjs.org/",
    "https://registry.npmjs.org/\nnext"
  ]) {
    const result = validateRegistry(registry);
    assert.notEqual(result.status, 0, `非法 registry 被接受：${registry}`);
  }
});

test("所有 v4 GitHub Actions 固定到已解析提交并保留版本注释", () => {
  const workflow = readSource(".github/workflows/ci.yml");
  const expected = new Map([
    ["actions/checkout", "11d5960a326750d5838078e36cf38b85af677262"],
    ["actions/setup-node", "49933ea5288caeca8642d1e84afbd3f7d6820020"],
    ["actions/setup-dotnet", "67a3573c9a986a3f9c594539f4ab511d57bb3ce9"],
    ["actions/upload-artifact", "ea165f8d65b6e75b540449e92b4886f43607fa02"],
    ["pnpm/action-setup", "b906affcce14559ad1aafd4ab0e942779e9f58b1"]
  ]);
  const actionLines = workflow
    .split(/\r?\n/)
    .filter(line => /^\s*uses:\s*/.test(line));

  assert.equal(actionLines.length, 17);
  for (const line of actionLines) {
    const match = /^\s*uses:\s*([^@\s]+)@([0-9a-f]{40})\s+# v4\s*$/.exec(line);
    assert.ok(match, `Action 未使用 40 位 SHA 和 # v4 注释：${line}`);
    assert.equal(
      match[2],
      expected.get(match[1]),
      `Action pin 不符：${match[1]}`
    );
  }
});

test("solution 与 API Docker 恢复入口强制消费 7 项目 NuGet 锁", () => {
  const workflow = readSource(".github/workflows/ci.yml");
  const solutionRestores =
    workflow.match(/dotnet restore AcceptanceSpecSystem\.sln[^\r\n]*/g) ?? [];
  assert.equal(solutionRestores.length, 3);
  solutionRestores.forEach(command => assert.match(command, /--locked-mode/));

  const dockerfile = readSource("src/AcceptanceSpecSystem.Api/Dockerfile");
  for (const project of [
    "AcceptanceSpecSystem.Api",
    "AcceptanceSpecSystem.Application",
    "AcceptanceSpecSystem.Core",
    "AcceptanceSpecSystem.Data"
  ]) {
    assert.ok(
      dockerfile.includes(
        `COPY src/${project}/packages.lock.json src/${project}/`
      ),
      `${project} lock 未复制到 Docker restore 上下文`
    );
  }
  assert.match(dockerfile, /dotnet restore[\s\S]*--locked-mode/);

  const solutionLocks = [
    "src/AcceptanceSpecSystem.Api/packages.lock.json",
    "src/AcceptanceSpecSystem.Application/packages.lock.json",
    "src/AcceptanceSpecSystem.Core/packages.lock.json",
    "src/AcceptanceSpecSystem.Data/packages.lock.json",
    "tests/AcceptanceSpecSystem.Api.Tests/packages.lock.json",
    "tests/AcceptanceSpecSystem.Core.Tests/packages.lock.json",
    "tests/AcceptanceSpecSystem.Data.Tests/packages.lock.json"
  ];
  solutionLocks.forEach(relativePath =>
    assert.equal(
      existsSync(join(repositoryRoot, relativePath)),
      true,
      relativePath
    )
  );
  assert.equal(
    existsSync(
      join(
        repositoryRoot,
        "tools/AcceptanceSpecSystem.DbDump/packages.lock.json"
      )
    ),
    false
  );
});

test("生产部署材料不建议 12 位或随机初始化密码", () => {
  const deploymentMaterials = [
    ".deploy/README.md",
    ".deploy/Publish-DockerImageRelease.ps1",
    ".deploy/production.env.example",
    ".github/workflows/ci.yml"
  ]
    .map(readSource)
    .join("\n");

  assert.doesNotMatch(
    deploymentMaterials,
    /至少\s*12\s*位|随机\s*(?:12\+?|密码|代码)|at_least_12_chars_(?:admin|common)_password/
  );
});
