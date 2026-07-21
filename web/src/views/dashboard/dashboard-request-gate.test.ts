import { describe, expect, it } from "vitest";
import { createDashboardRequestGate } from "./dashboard-request-gate";

describe("createDashboardRequestGate", () => {
  it("aborts and invalidates an older dashboard response", () => {
    const gate = createDashboardRequestGate();
    const older = gate.begin();
    const latest = gate.begin();

    expect(older.signal.aborted).toBe(true);
    expect(older.isCurrent()).toBe(false);
    expect(latest.signal.aborted).toBe(false);
    expect(latest.isCurrent()).toBe(true);
  });

  it("invalidates the active request when the page unmounts", () => {
    const gate = createDashboardRequestGate();
    const active = gate.begin();

    gate.cancel();

    expect(active.signal.aborted).toBe(true);
    expect(active.isCurrent()).toBe(false);
  });
});
