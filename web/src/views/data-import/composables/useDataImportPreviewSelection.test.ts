import { computed, ref } from "vue";
import { describe, expect, it, vi } from "vitest";

vi.mock("element-plus", () => ({
  ElMessage: { success: vi.fn(), warning: vi.fn() },
  ElMessageBox: { confirm: vi.fn() }
}));

import { useDataImportPreviewSelection } from "./useDataImportPreviewSelection";
import type { TableImportConfig } from "../dataImport.types";

describe("useDataImportPreviewSelection", () => {
  it("Word 预览行号包含 0-based 数据起始行偏移", () => {
    const config: TableImportConfig = {
      tableIndex: 0,
      tableInfo: {
        index: 0,
        name: "Word表格",
        rowCount: 10,
        columnCount: 4,
        isNested: false,
        headers: ["项目", "规格", "验收", "备注"],
        hasMergedCells: false
      },
      wordMapping: {
        projectColumn: 0,
        specificationColumn: 1,
        acceptanceColumn: 2,
        remarkColumn: 3,
        headerRowIndex: 4,
        dataStartRowIndex: 5
      },
      previewData: {
        tableIndex: 0,
        headers: ["项目", "规格", "验收", "备注"],
        rows: [["P", "S", "A", "R"]],
        totalRows: 1,
        columnCount: 4
      }
    };
    const state = useDataImportPreviewSelection({
      isExcelFile: computed(() => false),
      tableConfigs: ref([config])
    });

    expect(state.importPreviewGroups.value[0].rows[0].displayRowNumber).toBe(6);
  });
});
