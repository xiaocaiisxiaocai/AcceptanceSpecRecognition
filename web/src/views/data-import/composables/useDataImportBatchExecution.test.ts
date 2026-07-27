import { describe, expect, it } from "vitest";
import { getDataImportExecutionErrorMessage } from "./data-import-error-message";

describe("数据导入错误呈现", () => {
  it("导入和继续导入均优先呈现服务端真实错误", () => {
    const error = {
      isAxiosError: true,
      message: "Request failed with status code 409",
      response: {
        data: {
          message: "数据库写入冲突"
        }
      }
    };

    expect(getDataImportExecutionErrorMessage(error, "import")).toBe(
      "数据库写入冲突"
    );
    expect(getDataImportExecutionErrorMessage(error, "continue")).toBe(
      "数据库写入冲突"
    );
  });

  it("没有可用错误信息时按执行阶段返回明确兜底文案", () => {
    expect(getDataImportExecutionErrorMessage(undefined, "import")).toBe(
      "导入失败，请稍后重试"
    );
    expect(getDataImportExecutionErrorMessage(undefined, "continue")).toBe(
      "继续导入失败，请稍后重试"
    );
  });
});
