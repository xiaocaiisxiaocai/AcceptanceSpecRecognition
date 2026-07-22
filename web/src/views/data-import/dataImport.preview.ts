import type { TableData } from "@/api/document";

export const DATA_IMPORT_PREVIEW_WINDOW_ROWS = 500;
export const DATA_IMPORT_PREVIEW_WINDOW_COLUMNS = 100;

export type DataImportPreviewWindowRequest = {
  rowOffset: number;
  previewRows: number;
};

/**
 * 使用服务端允许的有界窗口加载完整待导入行，并在前端按顺序合并。
 * 完整导入仍由后端执行；这里仅用于确认用户可见行及恢复剔除选择。
 */
export const loadBoundedFullTablePreview = async ({
  loadWindow,
  pageSize = DATA_IMPORT_PREVIEW_WINDOW_ROWS
}: {
  loadWindow: (request: DataImportPreviewWindowRequest) => Promise<TableData>;
  pageSize?: number;
}): Promise<TableData> => {
  if (!Number.isInteger(pageSize) || pageSize <= 0) {
    throw new Error("预览分页大小必须为正整数");
  }

  const windows: TableData[] = [];
  let rowOffset = 0;
  let totalRows: number | undefined;

  do {
    const window = await loadWindow({ rowOffset, previewRows: pageSize });
    const resolvedTotalRows = Math.max(0, window.totalRows);
    if (totalRows == null) totalRows = resolvedTotalRows;
    else totalRows = Math.min(totalRows, resolvedTotalRows);

    windows.push(window);
    if (window.rows.length === 0) {
      if (rowOffset < totalRows) {
        throw new Error("完整预览分页未返回剩余数据，请重试");
      }
      break;
    }
    rowOffset += window.rows.length;
  } while (rowOffset < (totalRows ?? 0));

  const first = windows[0];
  const structuredWindows = windows.flatMap(window =>
    window.structuredRows ? [window.structuredRows] : []
  );
  return {
    tableIndex: first?.tableIndex ?? 0,
    headers: first?.headers ?? [],
    rows: windows.flatMap(window => window.rows),
    ...(structuredWindows.length > 0
      ? { structuredRows: structuredWindows.flat() }
      : {}),
    totalRows: totalRows ?? 0,
    columnCount: first?.columnCount ?? 0,
    rowOffset: 0,
    columnOffset: first?.columnOffset ?? 0,
    totalColumns: first?.totalColumns ?? first?.columnCount ?? 0
  };
};
