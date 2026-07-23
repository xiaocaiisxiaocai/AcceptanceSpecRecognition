import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  logoutApi: vi.fn(),
  removeToken: vi.fn(),
  routerPush: vi.fn(),
  resetRouter: vi.fn(),
  publishAuthSessionEvent: vi.fn(),
  clearAllCachePage: vi.fn(),
  handleTags: vi.fn()
}));

vi.mock("../utils", async () => {
  const { createPinia } = await import("pinia");
  return {
    store: createPinia(),
    router: {
      push: mocks.routerPush,
      replace: vi.fn(),
      currentRoute: { value: { path: "/dashboard", meta: {} } }
    },
    resetRouter: mocks.resetRouter,
    routerArrays: [],
    storageLocal: () => ({ getItem: () => undefined })
  };
});
vi.mock("@/api/user", () => ({
  getLogin: vi.fn(),
  refreshTokenApi: vi.fn(),
  logoutApi: mocks.logoutApi
}));
vi.mock("./multiTags", () => ({
  useMultiTagsStoreHook: () => ({ handleTags: mocks.handleTags })
}));
vi.mock("./permission", () => ({
  usePermissionStoreHook: () => ({
    clearAllCachePage: mocks.clearAllCachePage,
    handleWholeMenus: vi.fn(),
    wholeMenus: []
  })
}));
vi.mock("@/utils/auth", () => ({
  hasBrowserRefreshSession: vi.fn(() => true),
  setToken: vi.fn(),
  removeToken: mocks.removeToken,
  userKey: "user-info"
}));
vi.mock("@/utils/permission", () => ({ hasAnyPermission: vi.fn(() => true) }));
vi.mock("@/utils/auth-session-events", () => ({
  onAuthSessionEvent: vi.fn(),
  publishAuthSessionEvent: mocks.publishAuthSessionEvent
}));
vi.mock("@/utils/auth-refresh-error", () => ({
  isRefreshSessionInvalidError: vi.fn(() => false)
}));

import { useUserStoreHook } from "./user";

describe("user store logout", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    const user = useUserStoreHook();
    user.$reset();
    user.username = "admin";
    user.permissions = ["page:dashboard"];
  });

  it("服务端撤销失败时保留当前登录状态", async () => {
    mocks.logoutApi.mockRejectedValueOnce(new Error("network"));
    const user = useUserStoreHook();

    await expect(user.logout()).resolves.toBe(false);

    expect(user.username).toBe("admin");
    expect(user.permissions).toEqual(["page:dashboard"]);
    expect(mocks.removeToken).not.toHaveBeenCalled();
    expect(mocks.routerPush).not.toHaveBeenCalled();
  });

  it("服务端撤销成功后清理本地会话", async () => {
    mocks.logoutApi.mockResolvedValueOnce(undefined);
    const user = useUserStoreHook();

    await expect(user.logout()).resolves.toBe(true);

    expect(user.username).toBe("");
    expect(user.permissions).toEqual([]);
    expect(mocks.removeToken).toHaveBeenCalledOnce();
    expect(mocks.routerPush).toHaveBeenCalledWith({ path: "/login" });
  });
});
