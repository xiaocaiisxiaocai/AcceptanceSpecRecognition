import { describe, expect, it } from "vitest";
import type { BatchTableConfigItem } from "./components/batchTableConfig.types";
import {
  buildSmartFillStructurePreviewRegions,
  resolveSmartFillStructurePreviewConfig
} from "./smartFill.structurePreview";

const createConfig = (
  overrides: Partial<BatchTableConfigItem> = {}
): BatchTableConfigItem => ({
  tableIndex: 0,
  projectColumnIndex: 2,
  specificationColumnIndex: 3,
  acceptanceColumnIndex: 8,
  remarkColumnIndex: 9,
  headerRowStart: 9,
  headerRowCount: 2,
  dataStartRow: 11,
  dataEndRow: 112,
  selected: true,
  tableInfo: {
    index: 0,
    name: "工作表1",
    rowCount: 122,
    columnCount: 10,
    isNested: false,
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 5,
    usedRangeStartColumn: 1
  },
  ...overrides
});

describe("smartFill structure preview", () => {
  it("resolves the active sheet and falls back to the first selected sheet", () => {
    const first = createConfig({ tableIndex: 0 });
    const second = createConfig({ tableIndex: 2 });

    expect(resolveSmartFillStructurePreviewConfig([first, second], 2)).toBe(
      second
    );
    expect(resolveSmartFillStructurePreviewConfig([first, second], 9)).toBe(
      first
    );
  });

  it("converts Excel absolute rows to preview-relative indices", () => {
    const [region] = buildSmartFillStructurePreviewRegions(
      createConfig(),
      true
    );

    expect(region).toMatchObject({
      headerRowIndex: 4,
      headerRowCount: 2,
      dataStartRowIndex: 6,
      dataEndRowIndex: 107,
      previewRows: 102,
      sourceRowNumberStart: 11,
      mapping: {
        projectColumn: 2,
        specificationColumn: 3,
        acceptanceColumn: 8,
        remarkColumn: 9,
        headerRowIndex: 4,
        dataStartRowIndex: 6
      }
    });
  });

  it("converts Word one-based rows and exposes every configured region", () => {
    const regions = buildSmartFillStructurePreviewRegions(
      createConfig({
        tableIndex: 3,
        tableInfo: {
          ...createConfig().tableInfo,
          index: 3,
          name: "表格4",
          usedRangeStartRow: undefined
        },
        regions: [
          {
            regionId: "primary",
            regionIndex: 0,
            projectColumnIndex: 0,
            specificationColumnIndex: 1,
            acceptanceColumnIndex: 2,
            remarkColumnIndex: 3,
            headerRowStart: 1,
            headerRowCount: 1,
            dataStartRow: 2,
            dataEndRow: 8
          },
          {
            regionId: "primary",
            regionIndex: 1,
            projectColumnIndex: 4,
            specificationColumnIndex: 5,
            acceptanceColumnIndex: 6,
            headerRowStart: 10,
            headerRowCount: 2,
            dataStartRow: 12,
            dataEndRow: 20
          }
        ]
      }),
      false
    );

    expect(regions).toHaveLength(2);
    expect(regions[0]).toMatchObject({
      key: "3:0:primary",
      label: "区域 1",
      headerRowIndex: 0,
      dataStartRowIndex: 1,
      dataEndRowIndex: 7,
      previewRows: 7,
      sourceRowNumberStart: 2
    });
    expect(regions[1]).toMatchObject({
      key: "3:1:primary",
      label: "区域 2",
      headerRowIndex: 9,
      dataStartRowIndex: 11,
      dataEndRowIndex: 19,
      previewRows: 9,
      sourceRowNumberStart: 12,
      mapping: {
        projectColumn: 4,
        specificationColumn: 5,
        acceptanceColumn: 6,
        remarkColumn: undefined
      }
    });
    expect(new Set(regions.map(region => region.key)).size).toBe(2);
  });
});
