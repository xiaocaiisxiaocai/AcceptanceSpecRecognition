export interface LatestRequestTicket {
  version: number;
  controller: AbortController;
}

export interface LatestRequestGate {
  begin(): LatestRequestTicket;
  isCurrent(ticket: LatestRequestTicket): boolean;
  invalidate(): void;
}

export const createLatestRequestGate = (): LatestRequestGate => {
  let version = 0;
  let controller: AbortController | undefined;

  return {
    begin() {
      controller?.abort();
      controller = new AbortController();
      version += 1;
      return { version, controller };
    },
    isCurrent(ticket) {
      return (
        ticket.version === version &&
        ticket.controller === controller &&
        !ticket.controller.signal.aborted
      );
    },
    invalidate() {
      controller?.abort();
      controller = undefined;
      version += 1;
    }
  };
};

export const isCancelledRequest = (error: unknown) => {
  if (!error || typeof error !== "object") return false;

  const requestError = error as { name?: string; code?: string };
  return (
    requestError.name === "AbortError" ||
    requestError.name === "CanceledError" ||
    requestError.code === "ERR_CANCELED"
  );
};
