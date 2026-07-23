import { describe, expect, it } from "vitest";
import { getDistinctAiServiceModel } from "./ai-service-display";

describe("getDistinctAiServiceModel", () => {
  it("服务名与模型名相同时不重复显示模型名", () => {
    expect(getDistinctAiServiceModel("qwen2.5:14b", "qwen2.5:14b")).toBe("");
    expect(getDistinctAiServiceModel(" Qwen2.5:14B ", "qwen2.5:14b")).toBe("");
  });

  it("服务名与模型名不同时保留模型名", () => {
    expect(getDistinctAiServiceModel("本地结构识别", "qwen2.5:14b")).toBe(
      "qwen2.5:14b"
    );
  });
});
