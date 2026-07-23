import { describe, expect, it } from "vitest";
import {
  createLatestRequestGate,
  isCancelledRequest
} from "./file-compare-request-gate";

describe("file compare latest request gate", () => {
  it("只允许最后开始的请求提交迟到响应", async () => {
    const gate = createLatestRequestGate();
    const state: string[] = [];
    let resolveOld!: (value: string) => void;
    let resolveLatest!: (value: string) => void;
    const oldResult = new Promise<string>(resolve => (resolveOld = resolve));
    const latestResult = new Promise<string>(
      resolve => (resolveLatest = resolve)
    );

    const oldTicket = gate.begin();
    const oldCommit = oldResult.then(value => {
      if (gate.isCurrent(oldTicket)) state.push(value);
    });
    const latestTicket = gate.begin();
    const latestCommit = latestResult.then(value => {
      if (gate.isCurrent(latestTicket)) state.push(value);
    });

    expect(oldTicket.controller.signal.aborted).toBe(true);
    resolveLatest("Sheet B");
    await latestCommit;
    resolveOld("Sheet A");
    await oldCommit;

    expect(state).toEqual(["Sheet B"]);
  });

  it("停用后会取消当前请求并拒绝其响应", () => {
    const gate = createLatestRequestGate();
    const ticket = gate.begin();

    gate.invalidate();

    expect(ticket.controller.signal.aborted).toBe(true);
    expect(gate.isCurrent(ticket)).toBe(false);
    expect(isCancelledRequest({ code: "ERR_CANCELED" })).toBe(true);
    expect(isCancelledRequest(new Error("network"))).toBe(false);
  });
});
