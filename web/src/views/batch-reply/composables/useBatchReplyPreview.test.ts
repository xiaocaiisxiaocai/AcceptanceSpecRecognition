import { computed, ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  previewBatchReplyTable,
  type BatchReplyTablePreviewResponse
} from "@/api/matching";
import type { ApiResponse } from "@/api/customer";
import type { BatchReplyTableConfigItem } from "../batch-reply-table-config";
import type { BatchReplyTargetState } from "../batch-reply-state";

const messages = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  warning: vi.fn()
}));
vi.mock("element-plus", () => ({ ElMessage: messages }));
vi.mock("@/utils/permission-guard", () => ({
  ensurePermission: vi.fn(() => true)
}));
vi.mock("@/api/matching", () => ({
  getBatchReplyTablePreview: vi.fn(),
  getBatchReplyTargetTablePreview: vi.fn(),
  previewBatchReplyTable: vi.fn()
}));

import { useBatchReplyPreview } from "./useBatchReplyPreview";

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => {
    resolve = complete;
  });
  return { promise, resolve };
};

const tableInfo = (index: number) => ({
  index,
  name: `Sheet${index + 1}`,
  rowCount: 3,
  columnCount: 4,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注"],
  hasMergedCells: false
});

const tableConfig = (tableIndex: number): BatchReplyTableConfigItem => ({
  tableIndex,
  sourceTableIndex: tableIndex,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2,
  filterEmptySourceRows: true,
  duplicateResolutions: [],
  selected: true,
  tableInfo: tableInfo(tableIndex)
});

const previewResponse = (
  tableIndex: number
): ApiResponse<BatchReplyTablePreviewResponse> => ({
  code: 0,
  message: "",
  data: {
    targetId: "target-1",
    fileName: "target.xlsx",
    tableIndex,
    sourceTableIndex: tableIndex,
    canApply: true,
    errors: [],
    rows: [],
    duplicateGroups: []
  }
});

const createHarness = () => {
  const configs = [tableConfig(0), tableConfig(1)];
  const targetFiles = ref<BatchReplyTargetState[]>([
    {
      targetId: "target-1",
      fileName: "target.xlsx",
      fileType: 1,
      tableCount: 2,
      size: 128,
      signature: "target.xlsx:128:1",
      tables: configs.map(item => item.tableInfo),
      configs,
      previewResults: {},
      previewLoadingTableIndexes: []
    }
  ]);
  const preview = useBatchReplyPreview({
    sourceSessionId: computed(() => "session-1"),
    selectedSourceConfigs: computed(() => configs),
    sourceConfigs: ref(configs),
    targetFiles
  });
  return { configs, preview, targetFiles };
};

describe("useBatchReplyPreview", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("同一目标的表0完成后仍保留表1的预览 loading", async () => {
    const table0 = deferred<ApiResponse<BatchReplyTablePreviewResponse>>();
    const table1 = deferred<ApiResponse<BatchReplyTablePreviewResponse>>();
    vi.mocked(previewBatchReplyTable).mockImplementation(request =>
      request.targetTable.tableIndex === 0 ? table0.promise : table1.promise
    );
    const { configs, preview, targetFiles } = createHarness();

    const pending0 = preview.handleTargetTablePreview("target-1", configs[0]);
    const pending1 = preview.handleTargetTablePreview("target-1", configs[1]);

    expect(targetFiles.value[0].previewLoadingTableIndexes).toEqual(
      expect.arrayContaining([0, 1])
    );
    table0.resolve(previewResponse(0));
    await pending0;

    expect(targetFiles.value[0].previewLoadingTableIndexes).toEqual([1]);
    expect(targetFiles.value[0].previewResults[0]?.canApply).toBe(true);

    table1.resolve(previewResponse(1));
    await pending1;
    expect(targetFiles.value[0].previewLoadingTableIndexes).toEqual([]);
  });

  it("同一目标表在请求进行中重复点击不会发起第二个预览请求", async () => {
    const pendingResponse =
      deferred<ApiResponse<BatchReplyTablePreviewResponse>>();
    vi.mocked(previewBatchReplyTable).mockReturnValue(pendingResponse.promise);
    const { configs, preview, targetFiles } = createHarness();

    const first = preview.handleTargetTablePreview("target-1", configs[0]);
    const duplicate = preview.handleTargetTablePreview("target-1", configs[0]);

    pendingResponse.resolve(previewResponse(0));
    await Promise.all([first, duplicate]);

    expect(previewBatchReplyTable).toHaveBeenCalledTimes(1);
    expect(targetFiles.value[0].previewLoadingTableIndexes).toEqual([]);
  });
});
