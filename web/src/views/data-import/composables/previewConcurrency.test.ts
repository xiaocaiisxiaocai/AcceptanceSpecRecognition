import { describe, expect, it } from "vitest";
import { runWithConcurrencyLimit } from "./previewConcurrency";

const deferred = () => {
  let resolve!: () => void;
  const promise = new Promise<void>(done => {
    resolve = done;
  });
  return { promise, resolve };
};

describe("runWithConcurrencyLimit", () => {
  it("最多同时执行指定数量并等待所有任务完成", async () => {
    const gates = [deferred(), deferred(), deferred(), deferred()];
    const started: number[] = [];
    const completed: number[] = [];
    let active = 0;
    let maximumActive = 0;

    const running = runWithConcurrencyLimit([0, 1, 2, 3], 2, async item => {
      started.push(item);
      active += 1;
      maximumActive = Math.max(maximumActive, active);
      await gates[item].promise;
      active -= 1;
      completed.push(item);
    });

    await Promise.resolve();
    expect(started).toEqual([0, 1]);
    expect(maximumActive).toBe(2);

    gates[0].resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(started).toEqual([0, 1, 2]);

    gates[1].resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(started).toEqual([0, 1, 2, 3]);

    gates[2].resolve();
    gates[3].resolve();
    await running;

    expect(completed).toHaveLength(4);
    expect(maximumActive).toBe(2);
  });

  it("任务失败后不再启动队列中的新任务", async () => {
    const gate = deferred();
    const started: number[] = [];

    const running = runWithConcurrencyLimit([0, 1, 2], 2, async item => {
      started.push(item);
      if (item === 0) {
        throw new Error("预览失败");
      }
      await gate.promise;
    });

    await Promise.resolve();
    expect(started).toEqual([0, 1]);
    gate.resolve();

    await expect(running).rejects.toThrow("预览失败");
    expect(started).toEqual([0, 1]);
  });

  it("任务抛出 undefined 时仍应按失败处理", async () => {
    await expect(
      runWithConcurrencyLimit([0], 1, async () => {
        throw undefined;
      })
    ).rejects.toBeUndefined();
  });
});
