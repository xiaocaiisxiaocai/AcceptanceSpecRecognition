import { describe, expect, it } from "vitest";
import {
  getRuntimeAiSelectionServiceId,
  getRuntimeAiSelectionStatusText,
  isRuntimeAiSelectionAvailable
} from "./runtime-ai-selection";

describe("runtime AI selection", () => {
  it("only accepts an available selection with a service id", () => {
    const selection = {
      status: "available" as const,
      serviceId: 17,
      name: "Primary embedding",
      model: "bge-m3"
    };

    expect(isRuntimeAiSelectionAvailable(selection)).toBe(true);
    expect(getRuntimeAiSelectionServiceId(selection)).toBe(17);
    expect(getRuntimeAiSelectionStatusText(selection, "Embedding")).toBe(
      "自动使用 Primary embedding"
    );
  });

  it.each(["checking", "unavailable"] as const)(
    "does not treat %s as available",
    status => {
      const selection = { status, serviceId: 17 };

      expect(isRuntimeAiSelectionAvailable(selection)).toBe(false);
      expect(getRuntimeAiSelectionServiceId(selection)).toBeUndefined();
    }
  );

  it("uses the server-safe unavailable message", () => {
    expect(
      getRuntimeAiSelectionStatusText(
        { status: "unavailable", message: "服务暂时不可用" },
        "LLM"
      )
    ).toBe("服务暂时不可用");
  });
});
