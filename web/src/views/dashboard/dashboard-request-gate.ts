export interface DashboardRequestTicket {
  signal: AbortSignal;
  isCurrent: () => boolean;
}

export const createDashboardRequestGate = () => {
  let controller: AbortController | undefined;
  let version = 0;

  return {
    begin(): DashboardRequestTicket {
      controller?.abort();
      controller = new AbortController();
      const requestVersion = ++version;
      return {
        signal: controller.signal,
        isCurrent: () => requestVersion === version
      };
    },
    cancel() {
      version += 1;
      controller?.abort();
      controller = undefined;
    }
  };
};
