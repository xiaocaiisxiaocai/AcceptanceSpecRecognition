import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { gzipSync } from "node:zlib";
import { fileURLToPath } from "node:url";
import {
  dirname,
  extname,
  isAbsolute,
  join,
  relative,
  resolve
} from "node:path";

const KIB = 1024;
const MANIFEST_RELATIVE_PATH = ".vite/manifest.json";
const MAIN_ENTRY_SOURCE = "index.html";
const DASHBOARD_SOURCE = "src/views/dashboard/index.vue";

export const DEFAULT_BUDGETS = Object.freeze({
  maxJavaScriptGzip: 450 * KIB,
  maxStylesheetGzip: 80 * KIB,
  maxTotalGzip: 1100 * KIB,
  maxMainEntryGzip: 500 * KIB,
  maxDashboardGzip: 100 * KIB
});

function collectAssets(directory) {
  return readdirSync(directory).flatMap(name => {
    const path = join(directory, name);
    return statSync(path).isDirectory() ? collectAssets(path) : [path];
  });
}

function formatKib(bytes) {
  return `${(bytes / KIB).toFixed(1)} KiB`;
}

function normalizeManifestSource(value) {
  return typeof value === "string"
    ? value.replaceAll("\\", "/").replace(/^\.\//, "")
    : "";
}

function findManifestChunk(manifest, source, predicate) {
  const normalizedSource = normalizeManifestSource(source);
  const candidates = Object.entries(manifest).filter(([key, chunk]) => {
    if (!chunk || typeof chunk !== "object" || !predicate(chunk)) return false;
    return (
      normalizeManifestSource(key) === normalizedSource ||
      normalizeManifestSource(chunk.src) === normalizedSource
    );
  });

  return candidates.length === 1 ? candidates[0][1] : null;
}

function resolveManifestAsset(distDirectory, manifestAsset) {
  const normalizedAsset = normalizeManifestSource(manifestAsset);
  if (
    !normalizedAsset ||
    isAbsolute(normalizedAsset) ||
    normalizedAsset.split("/").includes("..")
  ) {
    throw new Error(`manifest 包含无效资源路径: ${String(manifestAsset)}`);
  }

  const absolutePath = resolve(distDirectory, normalizedAsset);
  const relativePath = relative(distDirectory, absolutePath);
  if (relativePath.startsWith("..") || isAbsolute(relativePath)) {
    throw new Error(`manifest 资源超出 dist 目录: ${normalizedAsset}`);
  }
  if (!existsSync(absolutePath)) {
    throw new Error(`manifest 引用的资源不存在: ${normalizedAsset}`);
  }

  return absolutePath;
}

function collectChunkResourcePaths(distDirectory, chunk) {
  const resourceNames = [
    chunk.file,
    ...(Array.isArray(chunk.css) ? chunk.css : [])
  ];
  return [...new Set(resourceNames)].map(resourceName =>
    resolveManifestAsset(distDirectory, resourceName)
  );
}

function gzipBytes(path) {
  return gzipSync(readFileSync(path), { level: 9 }).length;
}

export function evaluateBundleBudget(distDirectory, budgets = DEFAULT_BUDGETS) {
  const resolvedDistDirectory = resolve(distDirectory);
  const findings = [];
  if (!existsSync(resolvedDistDirectory)) {
    return {
      findings: ["dist 目录不存在，请先运行 pnpm build"],
      assets: [],
      totalGzip: 0
    };
  }

  const manifestPath = join(resolvedDistDirectory, MANIFEST_RELATIVE_PATH);
  if (!existsSync(manifestPath)) {
    return {
      findings: [`缺少 ${MANIFEST_RELATIVE_PATH}，无法定位受控入口`],
      assets: [],
      totalGzip: 0
    };
  }

  let manifest;
  try {
    manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  } catch (error) {
    return {
      findings: [`无法读取 Vite manifest: ${error.message}`],
      assets: [],
      totalGzip: 0
    };
  }

  const assets = collectAssets(resolvedDistDirectory)
    .filter(path => [".js", ".css"].includes(extname(path)))
    .map(path => ({
      path: relative(resolvedDistDirectory, path).replaceAll("\\", "/"),
      absolutePath: path,
      extension: extname(path),
      gzipBytes: gzipBytes(path)
    }));

  const totalGzip = assets.reduce((total, asset) => total + asset.gzipBytes, 0);
  for (const asset of assets) {
    const limit =
      asset.extension === ".js"
        ? budgets.maxJavaScriptGzip
        : budgets.maxStylesheetGzip;
    if (asset.gzipBytes > limit) {
      findings.push(
        `${asset.path}: ${formatKib(asset.gzipBytes)} > ${formatKib(limit)}`
      );
    }
  }

  if (totalGzip > budgets.maxTotalGzip) {
    findings.push(
      `total JavaScript/CSS gzip: ${formatKib(totalGzip)} > ${formatKib(budgets.maxTotalGzip)}`
    );
  }

  const controlledChunks = [
    {
      label: "main entry",
      source: MAIN_ENTRY_SOURCE,
      limit: budgets.maxMainEntryGzip,
      chunk: findManifestChunk(
        manifest,
        MAIN_ENTRY_SOURCE,
        chunk => chunk.isEntry === true
      )
    },
    {
      label: "Dashboard async chunk",
      source: DASHBOARD_SOURCE,
      limit: budgets.maxDashboardGzip,
      chunk: findManifestChunk(
        manifest,
        DASHBOARD_SOURCE,
        chunk => chunk.isDynamicEntry === true
      )
    }
  ];

  const controlled = [];
  for (const item of controlledChunks) {
    if (!item.chunk) {
      findings.push(`Vite manifest 中未唯一定位 ${item.label}: ${item.source}`);
      continue;
    }

    try {
      const resources = collectChunkResourcePaths(
        resolvedDistDirectory,
        item.chunk
      );
      const size = resources.reduce(
        (total, path) => total + gzipBytes(path),
        0
      );
      const resourceNames = resources.map(path =>
        relative(resolvedDistDirectory, path).replaceAll("\\", "/")
      );
      controlled.push({
        label: item.label,
        gzipBytes: size,
        resources: resourceNames
      });
      if (size > item.limit) {
        findings.push(
          `${item.label}: ${formatKib(size)} > ${formatKib(item.limit)} (${resourceNames.join(", ")})`
        );
      }
    } catch (error) {
      findings.push(`${item.label}: ${error.message}`);
    }
  }

  return { findings, assets, totalGzip, controlled };
}

export function runBundleBudgetCheck(distDirectory = resolve("dist")) {
  const result = evaluateBundleBudget(distDirectory);
  if (result.findings.length > 0) {
    console.error("Bundle budget failed:");
    for (const finding of result.findings) console.error(`- ${finding}`);
    return 1;
  }

  const largestAssets = result.assets
    .sort((left, right) => right.gzipBytes - left.gzipBytes)
    .slice(0, 5)
    .map(asset => `${asset.path}=${formatKib(asset.gzipBytes)}`)
    .join(", ");
  const controlled = result.controlled
    .map(item => `${item.label}=${formatKib(item.gzipBytes)}`)
    .join(", ");

  console.log(
    `Bundle budget passed: total=${formatKib(result.totalGzip)}; ${controlled}; largest: ${largestAssets}`
  );
  return 0;
}

const currentFile = fileURLToPath(import.meta.url);
const invokedFile = process.argv[1] ? resolve(process.argv[1]) : "";
if (invokedFile === currentFile) {
  process.exitCode = runBundleBudgetCheck(
    resolve(dirname(currentFile), "..", "dist")
  );
}
