import { describe, expect, it } from "vitest";
import { isRefreshSessionInvalidError } from "./auth-refresh-error";

describe("isRefreshSessionInvalidError", () => {
  it("仅将刷新端点明确返回的 401 视为会话失效", () => {
    expect(isRefreshSessionInvalidError({ response: { status: 401 } })).toBe(
      true
    );
  });

  it.each([
    new TypeError("network unavailable"),
    { code: "ECONNABORTED" },
    { response: { status: 403 } },
    { response: { status: 429 } },
    { response: { status: 500 } },
    { response: { status: 503 } }
  ])("不把网络、限流或服务端瞬态故障视为会话失效", error => {
    expect(isRefreshSessionInvalidError(error)).toBe(false);
  });
});
