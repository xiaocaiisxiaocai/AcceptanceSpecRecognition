import { describe, expect, it } from "vitest";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  applySmartStructureFieldSelectionsToDraft,
  applySmartStructureFieldSelectionsToTable,
  collectSmartStructureFieldConflicts,
  createUnresolvedSmartStructureFieldSelections,
  getSmartStructureRecommendedColumnIndex
} from "./smart-structure-field-conflicts";

const table = (): SmartConfigRecognizedTable => ({
  tableIndex: 0,
  tableName: "工作表1",
  headers: ["项目", "规格", "OK/NG", "Remark", "備註"],
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  dataEndRowIndex: 4,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  isSpecificationOnly: false,
  confidence: 0.99,
  source: "RuleBased",
  decision: "NeedConfirm",
  fields: [
    {
      field: "Remark",
      columnIndex: 3,
      header: "Remark",
      confidence: 0.99,
      source: "RuleBased"
    }
  ],
  regions: [
    {
      regionId: "table-0-region-0",
      regionIndex: 0,
      headers: ["项目", "规格", "OK/NG", "Remark", "備註"],
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 4,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      isSpecificationOnly: false,
      confidence: 0.99,
      source: "RuleBased",
      decision: "NeedConfirm",
      issues: [
        {
          code: "AmbiguousFieldCandidates",
          severity: "Warning",
          field: "Remark",
          message: "备注列存在多个同分高置信候选，请选择最终列"
        }
      ],
      fields: [
        {
          field: "Remark",
          columnIndex: 3,
          header: "Remark",
          confidence: 0.99,
          source: "RuleBased"
        }
      ],
      fieldConflicts: [
        {
          field: "Remark",
          recommendedColumnIndex: 3,
          candidates: [
            {
              columnIndex: 3,
              header: "Remark",
              confidence: 0.99,
              isRecommended: true,
              samples: ["厂商说明"]
            },
            {
              columnIndex: 4,
              header: "備註",
              confidence: 0.99,
              isRecommended: false,
              samples: ["整表说明"]
            }
          ]
        }
      ]
    }
  ]
});

const draft = (): SmartConfigConfirmRequest => ({
  customerId: 1,
  fileId: 2,
  tableIndex: 0,
  headers: ["项目", "规格", "OK/NG", "Remark", "備註"],
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  dataEndRowIndex: 4,
  isSpecificationOnly: false,
  learnedColumns: [{ header: "Remark", targetField: 4 }],
  regions: [
    {
      regionId: "table-0-region-0",
      regionIndex: 0,
      headers: ["项目", "规格", "OK/NG", "Remark", "備註"],
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 4,
      isSpecificationOnly: false
    }
  ]
});

describe("smart-structure-field-conflicts", () => {
  it("字段冲突保留系统推荐信息但不应把推荐列当成人工选择", () => {
    const conflict = collectSmartStructureFieldConflicts([table()], [0])[0];

    expect(getSmartStructureRecommendedColumnIndex(conflict)).toBe(3);
    expect(createUnresolvedSmartStructureFieldSelections([conflict])).toEqual({
      [conflict.key]: undefined
    });
    expect(
      getSmartStructureRecommendedColumnIndex({
        ...conflict,
        recommendedColumnIndex: null,
        candidates: conflict.candidates.map(candidate => ({
          ...candidate,
          isRecommended: candidate.columnIndex === 4
        }))
      })
    ).toBe(4);
  });

  it("只收集已选 Sheet 的未解决字段冲突", () => {
    const ignored = { ...table(), tableIndex: 1, tableName: "工作表2" };

    const conflicts = collectSmartStructureFieldConflicts(
      [table(), ignored],
      [0]
    );

    expect(conflicts).toHaveLength(1);
    expect(conflicts[0]).toMatchObject({
      tableIndex: 0,
      regionIndex: 0,
      field: "Remark",
      fieldLabel: "备注列"
    });
  });

  it("按工作表顺序收集多 Sheet 冲突，并跳过无冲突 Sheet", () => {
    const first = table();
    const second = {
      ...table(),
      tableIndex: 1,
      tableName: "工作表2",
      regions: table().regions?.map(region => ({
        ...region,
        regionId: "table-1-region-0"
      }))
    };
    const resolved = {
      ...table(),
      tableIndex: 2,
      tableName: "工作表3",
      fieldConflicts: [],
      regions: table().regions?.map(region => ({
        ...region,
        regionId: "table-2-region-0",
        fieldConflicts: []
      }))
    };

    const conflicts = collectSmartStructureFieldConflicts(
      [first, second, resolved],
      [0, 1, 2]
    );

    expect(conflicts.map(conflict => conflict.tableIndex)).toEqual([0, 1]);
    expect(conflicts.map(conflict => conflict.key)).toEqual([
      "0:table-0-region-0:Remark",
      "1:table-1-region-0:Remark"
    ]);
  });

  it("选择備註后同步更新页面结构并移除已解决冲突", () => {
    const selection = {
      key: "0:table-0-region-0:Remark",
      tableIndex: 0,
      regionId: "table-0-region-0",
      regionIndex: 0,
      field: "Remark",
      columnIndex: 4
    };

    const updated = applySmartStructureFieldSelectionsToTable(table(), [
      selection
    ]);

    expect(updated.remarkColumnIndex).toBe(4);
    expect(updated.regions?.[0].remarkColumnIndex).toBe(4);
    expect(updated.regions?.[0].fields[0]).toMatchObject({
      columnIndex: 4,
      header: "備註",
      source: "UserConfirmed"
    });
    expect(updated.regions?.[0].fieldConflicts).toEqual([]);
    expect(updated.regions?.[0].issues).toEqual([]);
  });

  it("选择结果写入确认草稿和学习字段", () => {
    const selection = {
      key: "0:table-0-region-0:Remark",
      tableIndex: 0,
      regionId: "table-0-region-0",
      regionIndex: 0,
      field: "Remark",
      columnIndex: 4
    };

    const updated = applySmartStructureFieldSelectionsToDraft(
      draft(),
      table(),
      [selection]
    );

    expect(updated.remarkColumnIndex).toBe(4);
    expect(updated.regions?.[0].remarkColumnIndex).toBe(4);
    expect(updated.learnedColumns).toContainEqual({
      header: "備註",
      targetField: 4
    });
    expect(updated.userModifiedStructure).toBe(true);
  });
});
