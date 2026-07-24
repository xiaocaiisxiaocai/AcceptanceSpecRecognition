import test from "node:test";
import assert from "node:assert/strict";

import {
  applyBackfilledItemsToPreviewResults,
  buildExecutionHistoryPreviewTables,
  buildSmartFillExecuteRequest,
  refreshBackfilledExecuteRequest
} from "../src/views/smart-fill/smartFillExecution.helpers.ts";
import type {
  BatchTablePreviewResult,
  MatchConfig,
  MatchResult
} from "../src/api/matching.ts";
import { discardCommittedMatchPreviewOverride } from "../src/views/smart-fill/components/matchPreviewTable.selection.ts";

const createMatchResult = (
  overrides: Partial<MatchResult> = {}
): MatchResult => ({
  specId: 101,
  project: "项目A",
  specification: "规格A",
  acceptance: "验收A",
  remark: "备注A",
  score: 0.98,
  embeddingScore: 0.96,
  scoreDetails: { final: 0.98 },
  decision: "autoApply",
  evidenceSummary: ["项目一致"],
  conflictSummary: [],
  issues: [],
  entities: [],
  topCandidates: [],
  recalledCandidateCount: 1,
  isAmbiguous: false,
  ...overrides
});

const previewResults: BatchTablePreviewResult[] = [
  {
    tableIndex: 0,
    totalMatched: 1,
    highConfidenceCount: 1,
    mediumConfidenceCount: 0,
    lowConfidenceCount: 0,
    ambiguousCount: 0,
    items: [
      {
        rowIndex: 2,
        sourceProject: "项目A",
        sourceSpecification: "规格A",
        bestMatch: createMatchResult({
          topCandidates: [
            {
              ...createMatchResult({
                specId: 102,
                scoreDetails: { final: 0.92 },
                evidenceSummary: ["候选证据"]
              }),
              rank: 1
            }
          ]
        }),
        llmReviewDraft: "复核草稿",
        llmReviewStage: "done",
        hasMatch: true,
        confidenceLevel: "high"
      }
    ]
  },
  {
    tableIndex: 1,
    totalMatched: 0,
    highConfidenceCount: 0,
    mediumConfidenceCount: 0,
    lowConfidenceCount: 0,
    ambiguousCount: 0,
    items: []
  }
];

test("智能填充执行请求 helper 应只包含有选择项的表格并生成预览快照", () => {
  const matchConfig: MatchConfig = {
    minScoreThreshold: 0.9,
    highConfidenceThreshold: 0.8,
    filterEmptySourceRows: false
  };

  const request = buildSmartFillExecuteRequest({
    uploadedFileId: 88,
    scope: { customerId: 1, processId: 2, machineModelId: 3 },
    selectedConfigs: [
      {
        tableIndex: 0,
        acceptanceColumnIndex: 4,
        remarkColumnIndex: 5,
        projectColumnIndex: 1,
        specificationColumnIndex: 2,
        headerRowStart: 1,
        headerRowCount: 1,
        dataStartRow: 2,
        filterEmptySourceRows: false
      },
      {
        tableIndex: 1,
        acceptanceColumnIndex: 4,
        projectColumnIndex: 1,
        specificationColumnIndex: 2
      }
    ],
    allSelections: new Map([
      [
        0,
        [
          {
            rowIndex: 2,
            specId: 101,
            manualConfirmed: true,
            reviewApprovalToken: "token",
            overrideAcceptance: "覆盖验收"
          }
        ]
      ],
      [1, []]
    ]),
    matchConfig,
    highConfidenceThreshold: 0.97,
    previewResults
  });

  assert.ok(request);
  assert.equal(request.fileId, 88);
  assert.equal(request.customerId, 1);
  assert.equal(request.config?.highConfidenceThreshold, 0.97);
  assert.equal(request.config?.filterEmptySourceRows, true);
  assert.equal(request.tables.length, 1);
  assert.equal(request.tables[0].tableIndex, 0);
  assert.equal(request.tables[0].filterEmptySourceRows, true);
  assert.deepEqual(request.tables[0].mappings, [
    {
      rowIndex: 2,
      specId: 101,
      manualConfirmed: true,
      manualFill: undefined,
      reviewApprovalToken: "token",
      overrideAcceptance: "覆盖验收",
      overrideRemark: undefined
    }
  ]);
  assert.equal(request.previewTables?.length, 1);
  assert.equal(request.previewTables?.[0].tableIndex, 0);
  assert.equal(request.previewTables?.[0].items[0].bestMatch?.specId, 101);
});

test("智能填充执行请求 helper 在无文件或无选择项时应返回 null", () => {
  const baseOptions = {
    scope: {},
    selectedConfigs: [
      {
        tableIndex: 0,
        acceptanceColumnIndex: 4,
        projectColumnIndex: 1,
        specificationColumnIndex: 2
      }
    ],
    matchConfig: {},
    highConfidenceThreshold: 0.98,
    previewResults
  };

  assert.equal(
    buildSmartFillExecuteRequest({
      ...baseOptions,
      uploadedFileId: undefined,
      allSelections: new Map([[0, [{ rowIndex: 2, specId: 101 }]]])
    }),
    null
  );
  assert.equal(
    buildSmartFillExecuteRequest({
      ...baseOptions,
      uploadedFileId: 88,
      allSelections: new Map([[0, []]])
    }),
    null
  );
});

test("执行记录预览快照应深拷贝匹配证据，避免后续编辑污染历史", () => {
  const snapshots = buildExecutionHistoryPreviewTables(previewResults, [0]);
  const snapshotBestMatch = snapshots[0].items[0].bestMatch;
  assert.ok(snapshotBestMatch);

  snapshotBestMatch.scoreDetails.final = 0.1;
  snapshotBestMatch.evidenceSummary?.push("新增证据");
  snapshotBestMatch.topCandidates[0].scoreDetails.final = 0.2;

  const originalBestMatch = previewResults[0].items[0].bestMatch;
  assert.equal(originalBestMatch?.scoreDetails.final, 0.98);
  assert.deepEqual(originalBestMatch?.evidenceSummary, ["项目一致"]);
  assert.equal(originalBestMatch?.topCandidates[0].scoreDetails.final, 0.92);
});

test("回填更新现有规格后应同步执行请求，避免继续携带失效 token 和旧预览值", () => {
  const request = buildSmartFillExecuteRequest({
    uploadedFileId: 88,
    scope: { customerId: 1 },
    selectedConfigs: [
      {
        tableIndex: 0,
        acceptanceColumnIndex: 4,
        projectColumnIndex: 1,
        specificationColumnIndex: 2
      }
    ],
    allSelections: new Map([
      [
        0,
        [
          {
            rowIndex: 2,
            specId: 101,
            manualConfirmed: false,
            reviewApprovalToken: "token-before-backfill",
            overrideAcceptance: "覆盖验收",
            overrideRemark: "覆盖备注"
          }
        ]
      ]
    ]),
    matchConfig: {},
    highConfidenceThreshold: 0.98,
    previewResults
  });

  assert.ok(request);
  const refreshed = refreshBackfilledExecuteRequest(request, [
    {
      tableIndex: 0,
      rowIndex: 2,
      specId: 101,
      overrideAcceptance: "覆盖验收",
      overrideRemark: "覆盖备注",
      actionType: "update"
    }
  ]);

  const mapping = refreshed.tables[0].mappings[0];
  assert.equal(mapping.reviewApprovalToken, undefined);
  assert.equal(mapping.manualConfirmed, true);
  assert.equal(mapping.overrideAcceptance, "覆盖验收");
  assert.equal(mapping.overrideRemark, "覆盖备注");
  assert.equal(
    refreshed.previewTables?.[0].items[0].bestMatch?.acceptance,
    "覆盖验收"
  );
  assert.equal(
    refreshed.previewTables?.[0].items[0].bestMatch?.remark,
    "覆盖备注"
  );
  assert.equal(
    refreshed.previewTables?.[0].items[0].bestMatch?.reviewApprovalToken,
    undefined
  );
});

test("回填更新现有规格后应刷新当前匹配预览中的所有引用行", () => {
  const repeatedPreviewResults: BatchTablePreviewResult[] = [
    {
      ...previewResults[0],
      items: [
        previewResults[0].items[0],
        {
          ...previewResults[0].items[0],
          rowIndex: 3,
          bestMatch: createMatchResult({
            acceptance: "旧验收",
            remark: "旧备注",
            reviewApprovalToken: "stale-token"
          })
        }
      ]
    }
  ];

  const refreshed = applyBackfilledItemsToPreviewResults(
    repeatedPreviewResults,
    [
      {
        tableIndex: 0,
        rowIndex: 2,
        specId: 101,
        overrideAcceptance: "业务回复11",
        overrideRemark: "长边进板111",
        actionType: "update"
      }
    ]
  );

  refreshed[0].items.forEach(item => {
    assert.equal(item.bestMatch?.acceptance, "业务回复11");
    assert.equal(item.bestMatch?.remark, "长边进板111");
    assert.equal(item.bestMatch?.reviewApprovalToken, undefined);
  });
  assert.equal(previewResults[0].items[0].bestMatch?.remark, "备注A");
});

test("匹配预览刷新后应清除已经落库的编辑覆盖标记", () => {
  const item = {
    ...previewResults[0].items[0],
    bestMatch: createMatchResult({
      acceptance: "业务回复11",
      remark: "长边进板111"
    })
  };

  assert.equal(
    discardCommittedMatchPreviewOverride(item, {
      overrideAcceptance: "业务回复11",
      overrideRemark: "长边进板111"
    }),
    undefined
  );
  assert.deepEqual(
    discardCommittedMatchPreviewOverride(item, {
      overrideAcceptance: "尚未落库的再次编辑",
      overrideRemark: "长边进板111"
    }),
    {
      overrideAcceptance: "尚未落库的再次编辑",
      overrideRemark: undefined
    }
  );
});
