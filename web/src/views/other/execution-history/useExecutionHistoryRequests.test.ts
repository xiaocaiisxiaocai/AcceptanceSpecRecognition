import { describe, expect, it } from "vitest";
import {
  buildExecutionHistoryListRequest,
  createExecutionHistoryRequestGate
} from "./useExecutionHistoryRequests";

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

  it("页码、页大小和筛选条件共同形成请求参数与请求代次", () => {
    expect(
      buildExecutionHistoryListRequest({
        page: 3,
        pageSize: 100,
        keyword: "  source.xlsx  ",
        taskType: "smart-fill"
      })
    ).toEqual({
      key: "list:3:100:source.xlsx:smart-fill",
      params: {
        page: 3,
        pageSize: 100,
        keyword: "source.xlsx",
        taskType: "smart-fill"
      }
    });
  });

  it("分页或筛选变化会取消旧请求并拒绝旧响应提交", () => {
    const gate = createExecutionHistoryRequestGate();
    const first = buildExecutionHistoryListRequest({
      page: 1,
      pageSize: 50,
      keyword: "",
      taskType: ""
    });
    const second = buildExecutionHistoryListRequest({
      page: 2,
      pageSize: 100,
      keyword: "result",
      taskType: "batch-reply"
    });

    const oldRequest = gate.begin(first.key);
    const currentRequest = gate.begin(second.key);

    expect(oldRequest.signal.aborted).toBe(true);
    expect(oldRequest.isCurrent()).toBe(false);
    expect(currentRequest.isCurrent()).toBe(true);
  });
});
