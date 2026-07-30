export type SmartStructureHeaderPreviewRequestOptions = {
  previewRows: number;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  rowOffset?: number;
  columnOffset?: number;
  previewColumns?: number;
};

export type SmartStructureHeaderPreviewResponse = {
  code: number;
  message?: string;
  data: {
    headers: string[];
    rows?: string[][];
    columnCount: number;
  };
};

export type SmartStructureHeaderPreviewInput = {
  regionId: string;
  fileId: number;
  tableIndex: number;
  baseRow: number;
  dataStartRow: number;
  dataEndRow?: number;
  minimumColumnCount: number;
  startValueColumnIndexes?: number[];
};

export type SmartStructureHeaderPreviewResult =
  | {
      status: "applied";
      headers: string[];
      startRowValues: string[];
      endRowValues: string[];
      warning?: string;
    }
  | { status: "stale" }
  | { status: "error"; message: string };

type HeaderPreviewRequest = (
  fileId: number,
  tableIndex: number,
  options: SmartStructureHeaderPreviewRequestOptions
) => Promise<SmartStructureHeaderPreviewResponse>;

export const createSmartStructureHeaderPreviewLoader = (
  request: HeaderPreviewRequest
) => {
  const requestVersions = new Map<string, number>();

  const invalidate = (regionId: string) => {
    requestVersions.set(regionId, (requestVersions.get(regionId) ?? 0) + 1);
  };

  const load = async (
    input: SmartStructureHeaderPreviewInput
  ): Promise<SmartStructureHeaderPreviewResult> => {
    const requestVersion = (requestVersions.get(input.regionId) ?? 0) + 1;
    requestVersions.set(input.regionId, requestVersion);

    try {
      const dataEndRow = input.dataEndRow ?? input.dataStartRow;
      const startResponse = await request(input.fileId, input.tableIndex, {
        previewRows: 1,
        headerRowIndex: input.dataStartRow - input.baseRow - 1,
        headerRowCount: 1,
        dataStartRowIndex: input.dataStartRow - input.baseRow
      });
      if (requestVersions.get(input.regionId) !== requestVersion) {
        return { status: "stale" };
      }
      if (startResponse.code !== 0) {
        return {
          status: "error",
          message: startResponse.message || "加载区域表头失败"
        };
      }

      const endResponse =
        dataEndRow === input.dataStartRow
          ? startResponse
          : await request(input.fileId, input.tableIndex, {
              previewRows: 1,
              headerRowIndex: input.dataStartRow - input.baseRow - 1,
              headerRowCount: 1,
              dataStartRowIndex: dataEndRow - input.baseRow,
              dataEndRowIndex: dataEndRow - input.baseRow
            });
      if (requestVersions.get(input.regionId) !== requestVersion) {
        return { status: "stale" };
      }
      if (endResponse.code !== 0) {
        const columnCount = Math.max(
          input.minimumColumnCount,
          startResponse.data.columnCount,
          startResponse.data.headers.length
        );
        const returnedStartRow = startResponse.data.rows?.[0] ?? [];
        return {
          status: "applied",
          headers: Array.from(
            { length: columnCount },
            (_, index) => startResponse.data.headers[index] ?? ""
          ),
          startRowValues: Array.from(
            { length: columnCount },
            (_, index) => returnedStartRow[index] ?? ""
          ),
          endRowValues: Array.from({ length: columnCount }, () => ""),
          warning: endResponse.message || "加载结束单元格内容失败"
        };
      }

      const columnCount = Math.max(
        input.minimumColumnCount,
        startResponse.data.columnCount,
        startResponse.data.headers.length,
        endResponse.data.columnCount
      );
      const headers = Array.from(
        { length: columnCount },
        (_, index) => startResponse.data.headers[index] ?? ""
      );
      const returnedStartRow = startResponse.data.rows?.[0] ?? [];
      const returnedEndRow = endResponse.data.rows?.[0] ?? [];
      const startRowValues = Array.from(
        { length: columnCount },
        (_, index) => returnedStartRow[index] ?? ""
      );
      const endRowValues = Array.from(
        { length: columnCount },
        (_, index) => returnedEndRow[index] ?? ""
      );
      const missingValueColumns = [
        ...new Set(input.startValueColumnIndexes ?? [])
      ].filter(index => index >= 0 && index < columnCount);
      for (const columnIndex of missingValueColumns.filter(
        index => index >= returnedStartRow.length
      )) {
        const cellResponse = await request(input.fileId, input.tableIndex, {
          previewRows: 1,
          headerRowIndex: input.dataStartRow - input.baseRow - 1,
          headerRowCount: 1,
          dataStartRowIndex: input.dataStartRow - input.baseRow,
          dataEndRowIndex: input.dataStartRow - input.baseRow,
          rowOffset: 0,
          columnOffset: columnIndex,
          previewColumns: 1
        });
        if (requestVersions.get(input.regionId) !== requestVersion) {
          return { status: "stale" };
        }
        if (cellResponse.code !== 0) {
          return {
            status: "error",
            message: cellResponse.message || "加载起始单元格内容失败"
          };
        }
        headers[columnIndex] =
          cellResponse.data.headers[0] ?? headers[columnIndex];
        startRowValues[columnIndex] = cellResponse.data.rows?.[0]?.[0] ?? "";
      }
      if (dataEndRow === input.dataStartRow) {
        endRowValues.splice(0, endRowValues.length, ...startRowValues);
      } else {
        for (const columnIndex of missingValueColumns.filter(
          index => index >= returnedEndRow.length
        )) {
          const cellResponse = await request(input.fileId, input.tableIndex, {
            previewRows: 1,
            headerRowIndex: input.dataStartRow - input.baseRow - 1,
            headerRowCount: 1,
            dataStartRowIndex: dataEndRow - input.baseRow,
            dataEndRowIndex: dataEndRow - input.baseRow,
            rowOffset: 0,
            columnOffset: columnIndex,
            previewColumns: 1
          });
          if (requestVersions.get(input.regionId) !== requestVersion) {
            return { status: "stale" };
          }
          if (cellResponse.code !== 0) {
            return {
              status: "error",
              message: cellResponse.message || "加载结束单元格内容失败"
            };
          }
          endRowValues[columnIndex] = cellResponse.data.rows?.[0]?.[0] ?? "";
        }
      }
      return { status: "applied", headers, startRowValues, endRowValues };
    } catch (error) {
      if (requestVersions.get(input.regionId) !== requestVersion) {
        return { status: "stale" };
      }
      return {
        status: "error",
        message: error instanceof Error ? error.message : "加载区域表头失败"
      };
    }
  };

  return { load, invalidate };
};
