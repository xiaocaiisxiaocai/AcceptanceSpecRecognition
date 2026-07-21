import { getFileTables, type TableInfo } from "@/api/document";

const resolvedTables = new Map<number, TableInfo[]>();
const pendingLoads = new Map<number, Promise<TableInfo[]>>();

export const loadFileTablesOnce = (
  fileId: number,
  options: { force?: boolean } = {}
): Promise<TableInfo[]> => {
  if (options.force) {
    resolvedTables.delete(fileId);
  }

  const resolved = resolvedTables.get(fileId);
  if (resolved) {
    return Promise.resolve(resolved);
  }

  const pending = pendingLoads.get(fileId);
  if (pending) {
    return pending;
  }

  const request = getFileTables(fileId)
    .then(response => {
      if (response.code !== 0) {
        throw new Error(response.message || "读取表格结构失败");
      }

      const tables = response.data ?? [];
      resolvedTables.set(fileId, tables);
      return tables;
    })
    .finally(() => {
      pendingLoads.delete(fileId);
    });

  pendingLoads.set(fileId, request);
  return request;
};

export const invalidateFileTables = (fileId?: number) => {
  if (fileId == null) {
    resolvedTables.clear();
    return;
  }

  resolvedTables.delete(fileId);
};
