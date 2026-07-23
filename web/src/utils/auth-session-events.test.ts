import { describe, expect, it, vi } from "vitest";

const posted: unknown[] = [];
const stored: Array<[string, string]> = [];

class FakeBroadcastChannel {
  constructor(_name: string) {}
  addEventListener() {}
  postMessage(value: unknown) {
    posted.push(value);
  }
}

vi.stubGlobal("BroadcastChannel", FakeBroadcastChannel);
vi.stubGlobal("window", {
  addEventListener: vi.fn(),
  localStorage: {
    setItem: (key: string, value: string) => stored.push([key, value]),
    removeItem: vi.fn()
  }
});

const { publishAuthSessionEvent } = await import("./auth-session-events");

describe("cross-tab auth events", () => {
  it("broadcasts session state without any credential fields", () => {
    publishAuthSessionEvent("session-ended");

    expect(posted).toHaveLength(1);
    expect(posted[0]).toMatchObject({ type: "session-ended" });
    const serialized = JSON.stringify(posted[0]);
    expect(serialized).not.toMatch(
      /access.?token|refresh.?token|authorization/i
    );
    expect(stored).toHaveLength(1);
    expect(stored[0]?.[1]).not.toMatch(/access.?token|refresh.?token/i);
  });
});
