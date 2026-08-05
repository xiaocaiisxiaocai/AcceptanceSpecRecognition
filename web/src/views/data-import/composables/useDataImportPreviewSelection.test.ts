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

  it("同一 Excel 工作表的多个区域应拆分为独立待导入 Sheet", () => {
    const firstRegionMapping = {
      regionId: "region-1",
      regionIndex: 0,
      isSpecificationOnly: false,
      projectColumn: 1,
      specificationColumn: 2,
      acceptanceColumn: 3,
      remarkColumn: 4,
      headerRowStart: 8,
      headerRowCount: 1,
      dataStartRow: 9,
      dataEndRow: 9
    };
    const secondRegionMapping = {
      ...firstRegionMapping,
      regionId: "region-2",
      regionIndex: 1,
      headerRowStart: 126,
      dataStartRow: 127,
      dataEndRow: 127
    };
    const config: TableImportConfig = {
      tableIndex: 0,
      tableInfo: {
        index: 0,
        name: "工作表1",
        rowCount: 127,
        columnCount: 4,
        isNested: false,
        headers: ["项目", "规格", "验收", "备注"],
        hasMergedCells: false
      },
      excelMapping: firstRegionMapping,
      recognizedExcelMappings: [firstRegionMapping, secondRegionMapping],
      excelPreviewRowLocations: [
        {
          regionId: firstRegionMapping.regionId,
          regionIndex: firstRegionMapping.regionIndex,
          relativeRowIndex: 0,
          displayRowNumber: 9,
          headers: ["项目", "规格", "验收", "备注"],
          mapping: firstRegionMapping
        },
        {
          regionId: secondRegionMapping.regionId,
          regionIndex: secondRegionMapping.regionIndex,
          relativeRowIndex: 0,
          displayRowNumber: 127,
          headers: ["项目", "规格", "验收", "备注"],
          mapping: secondRegionMapping
        }
      ],
      previewData: {
        tableIndex: 0,
        headers: ["项目", "规格", "验收", "备注"],
        rows: [
          ["P1", "S1", "OK", "R1"],
          ["P2", "S2", "NG", "R2"]
        ],
        totalRows: 2,
        columnCount: 4
      }
    };
    const state = useDataImportPreviewSelection({
      isExcelFile: computed(() => true),
      tableConfigs: ref([config])
    });

    expect(state.importPreviewGroups.value).toHaveLength(2);
    expect(
      state.importPreviewGroups.value.map(group => ({
        key: group.key,
        label: group.label,
        rows: group.rows.map(row => row.displayRowNumber)
      }))
    ).toEqual([
      {
        key: "0:region-1",
        label: "工作表 1（工作表1）· 区域 1",
        rows: [9]
      },
      {
        key: "0:region-2",
        label: "工作表 1（工作表1）· 区域 2",
        rows: [127]
      }
    ]);

    const [firstGroup, secondGroup] = state.importPreviewGroups.value;
    state.handleImportPreviewSelectionChange(
      firstGroup.tableIndex,
      firstGroup.rows,
      firstGroup.rows
    );
    state.handleImportPreviewSelectionChange(
      secondGroup.tableIndex,
      secondGroup.rows,
      secondGroup.rows
    );
    expect(state.importPreviewSelectionKeys.value).toEqual([
      firstGroup.rows[0].key,
      secondGroup.rows[0].key
    ]);

    state.excludeImportPreviewRows(firstGroup.rows);
    expect(
      state.importPreviewGroups.value.map(group => group.rows.length)
    ).toEqual([0, 1]);
  });

  it("选中无关项只勾选验收和备注同时为空的行，取消时保留其他手工选择", () => {
    const config: TableImportConfig = {
      tableIndex: 0,
      tableInfo: {
        index: 0,
        name: "工作表1",
        rowCount: 5,
        columnCount: 4,
        isNested: false,
        headers: ["项目", "规格", "验收", "备注"],
        hasMergedCells: false
      },
      excelMapping: {
        projectColumn: 1,
        specificationColumn: 2,
        acceptanceColumn: 3,
        remarkColumn: 4,
        headerRowStart: 1,
        headerRowCount: 1,
        dataStartRow: 2,
        dataEndRow: 5
      },
      previewData: {
        tableIndex: 0,
        headers: ["项目", "规格", "验收", "备注"],
        rows: [
          ["P1", "S1", "", ""],
          ["P2", "S2", " ", "\t"],
          ["P3", "S3", "OK", ""],
          ["P4", "S4", "", "已有备注"]
        ],
        totalRows: 4,
        columnCount: 4
      }
    };
    const state = useDataImportPreviewSelection({
      isExcelFile: computed(() => true),
      tableConfigs: ref([config])
    });
    const rows = state.importPreviewGroups.value[0].rows;

    state.handleImportPreviewSelectionChange(0, [rows[2]]);
    state.handleSelectIrrelevantRowsChange(true);

    expect(state.irrelevantPreviewRowCount.value).toBe(2);
    expect(state.allIrrelevantPreviewRowsSelected.value).toBe(true);
    expect(state.someIrrelevantPreviewRowsSelected.value).toBe(false);
    expect(state.importPreviewSelectionKeys.value).toEqual([
      rows[2].key,
      rows[0].key,
      rows[1].key
    ]);

    state.handleSelectIrrelevantRowsChange(false);

    expect(state.allIrrelevantPreviewRowsSelected.value).toBe(false);
    expect(state.importPreviewSelectionKeys.value).toEqual([rows[2].key]);

    state.handleImportPreviewSelectionChange(0, [rows[0]], [rows[0], rows[1]]);
    expect(state.importPreviewSelectionKeys.value).toEqual([
      rows[2].key,
      rows[0].key
    ]);

    state.handleImportPreviewSelectionChange(0, [rows[3]], [rows[2], rows[3]]);
    expect(state.importPreviewSelectionKeys.value).toEqual([
      rows[0].key,
      rows[3].key
    ]);
  });
});
