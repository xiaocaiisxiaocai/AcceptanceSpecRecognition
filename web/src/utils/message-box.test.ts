import { describe, expect, it } from "vitest";
import { isMessageBoxCancel } from "./message-box";

describe("isMessageBoxCancel", () => {
  it.each(["cancel", "close"])("把 %s 识别为用户主动取消", action => {
    expect(isMessageBoxCancel(action)).toBe(true);
  });

  it.each([
    "canceled",
    { response: { status: 403 } },
    { response: { status: 500 } },
    new Error("Network Error"),
    { isAxiosError: true, code: "ERR_CANCELED" }
  ])("不把真实请求错误误判为确认框取消", error => {
    expect(isMessageBoxCancel(error)).toBe(false);
  });
});
