export async function runWithConcurrencyLimit<T>(
  items: readonly T[],
  concurrency: number,
  worker: (item: T, index: number) => Promise<void>
) {
  if (items.length === 0) return;

  const workerCount = Math.min(items.length, Math.max(1, concurrency));
  let nextIndex = 0;
  let failed = false;
  let firstError: unknown;

  const runWorker = async () => {
    while (!failed) {
      const currentIndex = nextIndex;
      nextIndex += 1;
      if (currentIndex >= items.length) return;

      try {
        await worker(items[currentIndex], currentIndex);
      } catch (error) {
        failed = true;
        firstError = error;
      }
    }
  };

  await Promise.all(Array.from({ length: workerCount }, () => runWorker()));
  if (failed) {
    throw firstError;
  }
}
