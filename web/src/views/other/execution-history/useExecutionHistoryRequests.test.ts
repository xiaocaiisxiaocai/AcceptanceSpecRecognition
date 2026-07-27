import { describe, expect, it } from "vitest";
import { createExecutionHistoryRequestGate } from "./useExecutionHistoryRequests";

describe("执行历史请求闸门", () => {
  it("任务 A 的迟到响应不能覆盖已选中的任务 B", () => {
    const gate = createExecutionHistoryRequestGate();
    const taskA = gate.begin("detail:1");
    const taskB = gate.begin("detail:2");

    expect(taskA.isCurrent()).toBe(false);
    expect(taskA.signal.aborted).toBe(true);
    expect(taskB.isCurrent()).toBe(true);
    expect(taskB.signal.aborted).toBe(false);
  });

  it("页面卸载后当前响应不能再提交", () => {
    const gate = createExecutionHistoryRequestGate();
    const request = gate.begin("list:1:50");

    gate.cancel();

    expect(request.isCurrent()).toBe(false);
    expect(request.signal.aborted).toBe(true);
  });
});
