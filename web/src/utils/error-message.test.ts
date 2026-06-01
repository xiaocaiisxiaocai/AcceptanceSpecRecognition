import { describe, expect, it } from "vitest";
import { getRequestErrorMessage } from "./error-message";

describe("getRequestErrorMessage", () => {
  it("优先返回 Axios response.data.message", () => {
    const error = {
      isAxiosError: true,
      message: "network message",
      response: {
        data: {
          message: "业务错误"
        }
      }
    };

    expect(getRequestErrorMessage(error, "默认错误")).toBe("业务错误");
  });

  it("支持嵌套 error.message", () => {
    const error = {
      isAxiosError: true,
      response: {
        data: {
          error: {
            message: "嵌套错误"
          }
        }
      }
    };

    expect(getRequestErrorMessage(error, "默认错误")).toBe("嵌套错误");
  });

  it("Axios data.message 非字符串时回退到 error.message", () => {
    const error = {
      isAxiosError: true,
      message: "网络错误",
      response: {
        data: {
          message: { text: "结构化错误" }
        }
      }
    };

    expect(getRequestErrorMessage(error, "默认错误")).toBe("网络错误");
  });

  it("支持标准 Error 与字符串错误", () => {
    expect(getRequestErrorMessage(new Error("标准错误"), "默认错误")).toBe(
      "标准错误"
    );
    expect(getRequestErrorMessage("纯文本错误", "默认错误")).toBe("纯文本错误");
  });

  it("无法提取错误时返回调用方兜底文案", () => {
    expect(getRequestErrorMessage(undefined, "兜底错误")).toBe("兜底错误");
  });
});
