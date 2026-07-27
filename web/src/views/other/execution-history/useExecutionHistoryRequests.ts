export interface ExecutionHistoryRequestTicket {
  signal: AbortSignal;
  isCurrent: () => boolean;
}

export const createExecutionHistoryRequestGate = () => {
  let controller: AbortController | undefined;
  let version = 0;

  return {
    begin(_requestKey: string): ExecutionHistoryRequestTicket {
      controller?.abort();
      controller = new AbortController();
      const requestVersion = ++version;

      return {
        signal: controller.signal,
        isCurrent: () =>
          requestVersion === version && !controller?.signal.aborted
      };
    },
    cancel() {
      version += 1;
      controller?.abort();
      controller = undefined;
    }
  };
};
