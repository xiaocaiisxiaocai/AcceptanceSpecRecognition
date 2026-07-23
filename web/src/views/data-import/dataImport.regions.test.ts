import { describe, expect, it } from "vitest";
import {
  buildImportDifferenceDecisionKey,
  captureExcludedRowIdentities,
  getExcludedRowIndexesForRegion,
  mergeExcelRegionPreviews,
  replaceExcelRegionMapping,
  resolveExcludedCombinedIndexes
} from "./dataImport.regions";
import type { ExcelRegionMapping } from "./dataImport.types";

const mapping = (
  regionIndex: number,
  dataStartRow: number,
  dataEndRow: number
): ExcelRegionMapping => ({
  regionId: `table-0-region-${regionIndex}`,
  regionIndex,
  isSpecificationOnly: false,
  projectColumn: 3,
  specificationColumn: 4,
  acceptanceColumn: 9,
  remarkColumn: 10,
  headerRowStart: dataStartRow - 1,
  headerRowCount: 1,
  dataStartRow,
  dataEndRow
});

describe("data import multi-region preview", () => {
  it("合并所有区域的预览总数并保留真实行号", () => {
    const first = mapping(0, 9, 112);
    const second = mapping(1, 128, 143);
    const merged = mergeExcelRegionPreviews(0, [
      {
        mapping: first,
        preview: {
          tableIndex: 0,
          headers: ["", "", "项目", "规格"],
          rows: Array.from({ length: 104 }, () => ["a"]),
          totalRows: 104,
          columnCount: 10
        }
      },
      {
        mapping: second,
        preview: {
          tableIndex: 0,
          headers: ["", "", "细项", "规格"],
          rows: Array.from({ length: 16 }, () => ["b"]),
          totalRows: 16,
          columnCount: 10
        }
      }
    ]);

    expect(merged.previewData.totalRows).toBe(120);
    expect(merged.previewData.rows).toHaveLength(120);
    expect(merged.rowLocations[104]).toMatchObject({
      regionId: "table-0-region-1",
      relativeRowIndex: 0,
      displayRowNumber: 128
    });
  });

  it("删除某个区域的行时不会把相同行号套到其他区域", () => {
    const merged = mergeExcelRegionPreviews(0, [
      {
        mapping: mapping(0, 9, 10),
        preview: {
          tableIndex: 0,
          headers: [],
          rows: [["first-0"], ["first-1"]],
          totalRows: 2,
          columnCount: 1
        }
      },
      {
        mapping: mapping(1, 128, 129),
        preview: {
          tableIndex: 0,
          headers: [],
          rows: [["second-0"], ["second-1"]],
          totalRows: 2,
          columnCount: 1
        }
      }
    ]);

    expect(getExcludedRowIndexesForRegion([2], merged.rowLocations, 0)).toEqual(
      []
    );
    expect(getExcludedRowIndexesForRegion([2], merged.rowLocations, 1)).toEqual(
      [0]
    );
  });

  it("相同重复键在不同区域使用独立决策键", () => {
    expect(
      buildImportDifferenceDecisionKey({
        tableIndex: 0,
        regionId: "region-1",
        key: "项目|规格"
      })
    ).toBe("0:region-1:项目|规格");
  });

  it("部分预览升级全量预览后仍按区域内稳定坐标剔除", () => {
    const first = mapping(0, 9, 112);
    const second = mapping(1, 128, 143);
    const partial = mergeExcelRegionPreviews(0, [
      {
        mapping: first,
        preview: {
          tableIndex: 0,
          headers: ["项目"],
          rows: Array.from({ length: 10 }, (_, index) => [`first-${index}`]),
          totalRows: 104,
          columnCount: 1
        }
      },
      {
        mapping: second,
        preview: {
          tableIndex: 0,
          headers: ["细项"],
          rows: Array.from({ length: 10 }, (_, index) => [`second-${index}`]),
          totalRows: 16,
          columnCount: 1
        }
      }
    ]);
    const identities = captureExcludedRowIdentities([10], partial.rowLocations);

    const full = mergeExcelRegionPreviews(0, [
      {
        mapping: first,
        preview: {
          tableIndex: 0,
          headers: ["项目"],
          rows: Array.from({ length: 104 }, (_, index) => [`first-${index}`]),
          totalRows: 104,
          columnCount: 1
        }
      },
      {
        mapping: second,
        preview: {
          tableIndex: 0,
          headers: ["细项"],
          rows: Array.from({ length: 16 }, (_, index) => [`second-${index}`]),
          totalRows: 16,
          columnCount: 1
        }
      }
    ]);

    const remapped = resolveExcludedCombinedIndexes(
      identities,
      full.rowLocations
    );
    expect(remapped).toEqual([104]);
    expect(
      getExcludedRowIndexesForRegion(remapped, full.rowLocations, 0)
    ).toEqual([]);
    expect(
      getExcludedRowIndexesForRegion(remapped, full.rowLocations, 1)
    ).toEqual([0]);
  });

  it("高级映射只更新目标区域并保留其余离散区域", () => {
    const first = mapping(0, 9, 112);
    const second = mapping(1, 128, 143);

    const updated = replaceExcelRegionMapping({
      regions: [first, second],
      previousMapping: { ...second },
      mapping: {
        ...second,
        specificationColumn: 5,
        dataEndRow: 145
      }
    });

    expect(updated).toHaveLength(2);
    expect(updated[0]).toEqual(first);
    expect(updated[1]).toEqual({
      ...second,
      specificationColumn: 5,
      dataEndRow: 145
    });
  });

  it("高级映射没有明确目标时更新主区域但不丢失后续区域", () => {
    const first = mapping(0, 9, 112);
    const second = mapping(1, 128, 143);

    const updated = replaceExcelRegionMapping({
      regions: [first, second],
      mapping: { ...first, projectColumn: 4 }
    });

    expect(updated).toEqual([{ ...first, projectColumn: 4 }, second]);
  });
});
