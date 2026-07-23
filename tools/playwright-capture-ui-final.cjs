const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");

const baseUrl = process.env.UI_BASE_URL || "http://localhost:8849";
const outputDir = path.resolve("output/playwright/ui-final");

const pages = [
  ["dashboard", "/dashboard"],
  ["smart-fill", "/smart-fill/fill"],
  ["data-import", "/data-import/import"],
  ["batch-reply", "/batch-reply/index"],
  ["file-compare", "/file-compare/compare"],
  ["customers", "/base-data/customers"],
  ["specs", "/base-data/specs"],
  ["audit-logs", "/other/audit-logs"]
];

function layout(theme) {
  return {
    layout: "vertical",
    theme,
    darkMode: theme === "dark",
    sidebarStatus: true,
    epThemeColor: "#7C3AED",
    themeColor: "light",
    overallStyle: theme,
    grey: false,
    weak: false,
    hideTabs: false,
    hideFooter: true,
    showLogo: true,
    multiTagsCache: true
  };
}

async function login(page) {
  await page.goto(`${baseUrl}/login`, { waitUntil: "networkidle" });
  const username = page.locator("input").first();
  const password = page.locator("input[type='password']").first();
  await username.fill("admin");
  await password.fill("admin");
  await page.getByRole("button", { name: /登录|登陆|Login/i }).click();
  await page.waitForURL(url => !url.pathname.includes("/login"), {
    timeout: 15000
  });
}

async function setTheme(page, theme) {
  await page.evaluate(nextLayout => {
    localStorage.setItem("responsive-layout", JSON.stringify(nextLayout));
    document.documentElement.setAttribute("data-theme", "light");
    document.documentElement.classList.toggle("dark", nextLayout.darkMode);
  }, layout(theme));
}

async function captureTheme(context, theme) {
  const page = await context.newPage({ viewport: { width: 1920, height: 1080 } });
  await setTheme(page, theme);

  for (const [name, route] of pages) {
    await setTheme(page, theme);
    await page.goto(`${baseUrl}${route}`, { waitUntil: "networkidle" });
    await page.waitForTimeout(1200);
    await page.screenshot({
      path: path.join(outputDir, `${theme}-${name}.png`),
      fullPage: false
    });
  }

  await page.close();
}

(async () => {
  fs.mkdirSync(outputDir, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
    acceptDownloads: true
  });
  const loginPage = await context.newPage();
  await login(loginPage);
  await loginPage.close();
  await captureTheme(context, "light");
  await captureTheme(context, "dark");
  await browser.close();
  console.log(`captured ${pages.length * 2} screenshots in ${outputDir}`);
})().catch(error => {
  console.error(error);
  process.exit(1);
});
