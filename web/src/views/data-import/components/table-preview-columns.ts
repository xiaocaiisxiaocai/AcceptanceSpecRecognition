type PreviewColumnSource = {
  headers?: string[] | null;
  rows?: string[][] | null;
  columnCount?: number | null;
};

/**
 * 统一计算预览应展示的列数。
 * 优先相信后端返回的 columnCount，同时兜底 headers/rows 的实际长度。
 */
export const resolvePreviewColumnCount = ({
  headers,
  rows,
  columnCount
}: PreviewColumnSource): number => {
  const headerCount = headers?.length ?? 0;
  const rowCount = (rows ?? []).reduce((max, row) => Math.max(max, row?.length ?? 0), 0);

  return Math.max(columnCount ?? 0, headerCount, rowCount);
};

/**
 * 把表头补齐到真实列数。
 * 对于空表头列保留空字符串，由调用方决定显示成“列N”等占位文案。
 */
export const normalizePreviewHeaders = (source: PreviewColumnSource): string[] => {
  const totalColumns = resolvePreviewColumnCount(source);
  const headers = source.headers ?? [];

  return Array.from({ length: totalColumns }, (_, index) => headers[index] ?? "");
};
