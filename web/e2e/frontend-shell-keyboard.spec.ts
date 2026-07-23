import { expect, test } from "@playwright/test";
import { loginFromUi } from "./helpers/auth";

test("搜索、设置和页面标签可仅使用键盘完成操作", async ({ page }) => {
  await loginFromUi(page, "admin");
  await expect(page).toHaveURL(/#\/dashboard$/);

  const settingsButton = page.getByRole("button", { name: "打开系统配置" });
  await settingsButton.focus();
  await page.keyboard.press("Enter");

  const settingsDialog = page.getByRole("dialog", { name: "系统配置" });
  await expect(settingsDialog).toBeVisible();
  const horizontalNavigation = settingsDialog.getByRole("button", {
    name: "使用顶部导航"
  });
  await horizontalNavigation.focus();
  await page.keyboard.press("Enter");
  await expect(horizontalNavigation).toHaveAttribute("aria-pressed", "true");
  await page.keyboard.press("Escape");
  await expect(settingsDialog).toBeHidden();

  const searchButton = page.getByRole("button", { name: "搜索菜单" });
  await searchButton.focus();
  await page.keyboard.press("Enter");
  const searchInput = page.getByPlaceholder("搜索菜单（支持拼音搜索）");
  await expect(searchInput).toBeFocused();
  await searchInput.pressSequentially("导入数据");
  await expect(
    page.getByRole("button", { name: /导入数据/ }).first()
  ).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/#\/data-import\/import$/);
  await expect(searchInput).toBeHidden();

  const tabList = page.getByRole("tablist", { name: "已打开页面" });
  const importTab = tabList.getByRole("tab", { name: /^导入数据/ });
  await importTab.focus();
  await expect(importTab).toBeFocused();
  await page.keyboard.press("ArrowLeft");
  await expect(tabList.getByRole("tab").first()).toHaveAttribute(
    "aria-selected",
    "true"
  );
  await page.keyboard.press("ArrowRight");
  await expect(importTab).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Delete");
  await expect(importTab).toHaveCount(0);
});
