import { chdir, cwd } from "node:process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const testsDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(testsDir, "../..");

if (cwd() !== repoRoot) {
  chdir(repoRoot);
}
