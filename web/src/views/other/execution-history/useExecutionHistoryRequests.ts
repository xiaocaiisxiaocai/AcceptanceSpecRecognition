import type { ExecutionHistoryListRequest } from "@/api/execution-history";

export interface ExecutionHistoryRequestTicket {
  signal: AbortSignal;
  isCurrent: () => boolean;
}

export const buildExecutionHistoryListRequest = ({
  page,
  pageSize,
  keyword,
  taskType
}: {
  page: number;
  pageSize: number;
  keyword: string;
  taskType: string;
}): { key: string; params: ExecutionHistoryListRequest } => {
  const normalizedKeyword = keyword.trim();
  const normalizedTaskType = taskType.trim();
  return {
    key: `list:${page}:${pageSize}:${normalizedKeyword}:${normalizedTaskType}`,
    params: {
      page,
      pageSize,
      keyword: normalizedKeyword || undefined,
      taskType: normalizedTaskType || undefined
    }
  };
};

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
