import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => {
  const values = new Map<string, unknown>();
  return {
    values,
    storage: {
      getItem: vi.fn((key: string) => values.get(key)),
      setItem: vi.fn((key: string, value: unknown) => values.set(key, value)),
      removeItem: vi.fn((key: string) => values.delete(key))
    },
    store: {
      SET_AVATAR: vi.fn(),
      SET_USERNAME: vi.fn(),
      SET_NICKNAME: vi.fn(),
      SET_ROLE_CODE: vi.fn(),
      SET_PERMS: vi.fn(),
      permissions: [] as string[]
    }
  };
});

vi.mock("@pureadmin/utils", () => ({
  storageLocal: () => mocks.storage,
  isString: (value: unknown) => typeof value === "string"
}));

vi.mock("@/store/modules/user", () => ({
  useUserStoreHook: () => mocks.store
}));

vi.mock("./permission", () => ({
  hasPermission: vi.fn(),
  hasAllPermissions: vi.fn()
}));

const { getToken, hasBrowserRefreshSession, removeToken, setToken, userKey } =
  await import("./auth");

describe("browser credential storage", () => {
  beforeEach(() => {
    mocks.values.clear();
    removeToken();
  });

  it("keeps the access token only in module memory", () => {
    setToken({
      accessToken: "short-lived-access-token",
      expires: new Date(Date.now() + 60_000),
      username: "admin",
      roleCode: "ADMIN",
      permissions: ["users.read"]
    });

    expect(getToken()).toMatchObject({
      accessToken: "short-lived-access-token",
      username: "admin"
    });
    const persisted = mocks.values.get(userKey) as Record<string, unknown>;
    expect(persisted).not.toHaveProperty("accessToken");
    expect(persisted).not.toHaveProperty("refreshToken");
    expect(persisted).not.toHaveProperty("expires");
  });

  it("scrubs tokens left by a legacy client", () => {
    mocks.values.set(userKey, {
      accessToken: "legacy-access",
      refreshToken: "legacy-refresh",
      expires: Date.now() + 60_000,
      username: "admin"
    });

    setToken({
      accessToken: "new-access",
      expires: new Date(Date.now() + 60_000)
    });

    expect(mocks.values.get(userKey)).toEqual(
      expect.not.objectContaining({
        accessToken: expect.anything(),
        refreshToken: expect.anything()
      })
    );
  });

  it("detects the readable CSRF cookie used as the refresh-session marker", () => {
    expect(hasBrowserRefreshSession("")).toBe(false);
    expect(hasBrowserRefreshSession("unrelated=value")).toBe(false);
    expect(hasBrowserRefreshSession("acceptance-csrf-old=value")).toBe(false);
    expect(
      hasBrowserRefreshSession(
        "theme=light; acceptance-csrf=csrf-value; locale=zh-CN"
      )
    ).toBe(true);
  });
});
