import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getBatchReplyTablePreview,
  getBatchReplyTargetTablePreview,
  previewBatchReplyTable,
  type BatchReplyDuplicateGroup,
  type BatchReplyDuplicateStrategy
} from "@/api/matching";
import type { TableData } from "@/api/document";
import { ensurePermission } from "@/utils/permission-guard";
import {
  applyDuplicateResolutionState,
  buildDuplicateDialogState,
  updateDuplicateDialogStrategyState,
  type BatchReplyDuplicateDialogState
} from "../batch-reply-duplicates";
import {
  buildBatchReplyPreviewFingerprint,
  createTargetPreviewLoaderResolver
} from "../batch-reply-preview-state";
import {
  toBatchTableConfig,
  type BatchReplyTableConfigItem
} from "../batch-reply-table-config";
import type { BatchReplyTargetState } from "../batch-reply-state";

type PreviewLoaderOptions = {
  previewRows?: number;
  headerRowIndex?: number;
  headerRowCount?: number;
  dataStartRowIndex?: number;
};

type UseBatchReplyPreviewParams = {
  sourceSessionId: ComputedRef<string>;
  selectedSourceConfigs: ComputedRef<BatchReplyTableConfigItem[]>;
  sourceConfigs: Ref<BatchReplyTableConfigItem[]>;
  targetFiles: Ref<BatchReplyTargetState[]>;
};

export const useBatchReplyPreview = (params: UseBatchReplyPreviewParams) => {
  const duplicateDialog = ref<BatchReplyDuplicateDialogState | null>(null);
  const previewRequestStates = new Map<
    string,
    { fingerprint: string; controller: AbortController }
  >();

  const buildPreviewRequestKey = (targetId: string, tableIndex: number) =>
    `${targetId}:${tableIndex}`;

  const parsePreviewRequestKey = (requestKey: string) => {
    const separatorIndex = requestKey.lastIndexOf(":");
    return {
      targetId: requestKey.slice(0, separatorIndex),
      tableIndex: Number(requestKey.slice(separatorIndex + 1))
    };
  };

  const buildCurrentFingerprint = (targetId: string, tableIndex: number) => {
    const config = params.targetFiles.value
      .find(file => file.targetId === targetId)
      ?.configs.find(item => item.tableIndex === tableIndex);
    return buildBatchReplyPreviewFingerprint(
      params.sourceSessionId.value,
      targetId,
      config
    );
  };

  const clearTargetPreviewResults = (
    targetId?: string,
    tableIndex?: number
  ) => {
    params.targetFiles.value = params.targetFiles.value.map(file => {
      if (targetId !== undefined && file.targetId !== targetId) {
        return file;
      }

      if (tableIndex === undefined) {
        return {
          ...file,
          previewResults: {},
          previewLoadingTableIndexes: []
        };
      }

      const previewResults = { ...file.previewResults };
      delete previewResults[tableIndex];
      return {
        ...file,
        previewResults,
        previewLoadingTableIndexes: file.previewLoadingTableIndexes.filter(
          loadingTableIndex => loadingTableIndex !== tableIndex
        )
      };
    });
  };

  const cancelTargetPreviews = (targetId?: string, tableIndex?: number) => {
    previewRequestStates.forEach((requestState, requestKey) => {
      const requestOwner = parsePreviewRequestKey(requestKey);
      if (
        (targetId === undefined || requestOwner.targetId === targetId) &&
        (tableIndex === undefined || requestOwner.tableIndex === tableIndex)
      ) {
        requestState.controller.abort();
        previewRequestStates.delete(requestKey);
      }
    });
    clearTargetPreviewResults(targetId, tableIndex);
  };

  const openDuplicateDialog = (
    targetId: string,
    item: BatchReplyTableConfigItem,
    groups: BatchReplyDuplicateGroup[]
  ) => {
    const targetFile = params.targetFiles.value.find(
      file => file.targetId === targetId
    );
    duplicateDialog.value = buildDuplicateDialogState({
      targetId,
      item,
      groups,
      sourceConfigs: params.sourceConfigs.value,
      targetConfigs: targetFile?.configs
    });
  };

  const closeDuplicateDialog = () => {
    duplicateDialog.value = null;
  };

  const updateDuplicateDialogStrategy = (
    groupId: string,
    strategy: BatchReplyDuplicateStrategy
  ) => {
    duplicateDialog.value = updateDuplicateDialogStrategyState(
      duplicateDialog.value,
      groupId,
      strategy
    );
  };

  const applyDuplicateResolutions = (
    dialog: BatchReplyDuplicateDialogState
  ) => {
    const nextState = applyDuplicateResolutionState({
      dialog,
      sourceConfigs: params.sourceConfigs.value,
      targetFiles: params.targetFiles.value
    });
    params.sourceConfigs.value = nextState.sourceConfigs;
    params.targetFiles.value = nextState.targetFiles;

    if (dialog.groups.some(group => group.duplicateSource === "source")) {
      cancelTargetPreviews();
      return;
    }

    const affectedTableIndexes = new Set([
      dialog.tableIndex,
      ...dialog.groups.map(group => group.tableIndex)
    ]);
    affectedTableIndexes.forEach(tableIndex =>
      cancelTargetPreviews(dialog.targetId, tableIndex)
    );
  };

  const createSourcePreviewLoader = async (
    tableIndex: number,
    options: PreviewLoaderOptions
  ): Promise<TableData> => {
    if (!params.sourceSessionId.value) {
      throw new Error("来源会话不存在");
    }

    const res = await getBatchReplyTablePreview(
      params.sourceSessionId.value,
      tableIndex,
      options
    );
    if (res.code !== 0) {
      throw new Error(res.message || "加载来源表格预览失败");
    }

    return res.data;
  };

  const createTargetPreviewLoader = (targetId: string) => {
    return async (
      tableIndex: number,
      options: PreviewLoaderOptions
    ): Promise<TableData> => {
      if (!params.sourceSessionId.value) {
        throw new Error("来源会话不存在");
      }

      const res = await getBatchReplyTargetTablePreview(
        params.sourceSessionId.value,
        targetId,
        tableIndex,
        options
      );
      if (res.code !== 0) {
        throw new Error(res.message || "加载目标表格预览失败");
      }

      return res.data;
    };
  };

  const getTargetPreviewLoader = createTargetPreviewLoaderResolver(
    createTargetPreviewLoader
  );

  const handleTargetTablePreview = async (
    targetId: string,
    item: BatchReplyTableConfigItem
  ) => {
    if (
      !ensurePermission(
        "btn:batch-reply:preview",
        "权限不足，无法预览当前目标表"
      )
    ) {
      return;
    }

    if (!params.sourceSessionId.value) {
      ElMessage.warning("请先上传来源文件");
      return;
    }

    if (params.selectedSourceConfigs.value.length === 0) {
      ElMessage.warning("请至少选择一个来源表");
      return;
    }

    if (item.sourceTableIndex === undefined) {
      ElMessage.warning("请先为当前目标表选择来源表");
      return;
    }

    const requestKey = buildPreviewRequestKey(targetId, item.tableIndex);
    if (previewRequestStates.has(requestKey)) {
      return;
    }

    clearTargetPreviewResults(targetId, item.tableIndex);
    const requestState = {
      fingerprint: buildBatchReplyPreviewFingerprint(
        params.sourceSessionId.value,
        targetId,
        item
      ),
      controller: new AbortController()
    };
    previewRequestStates.set(requestKey, requestState);
    params.targetFiles.value = params.targetFiles.value.map(file =>
      file.targetId === targetId
        ? {
            ...file,
            previewLoadingTableIndexes: [
              ...file.previewLoadingTableIndexes,
              item.tableIndex
            ]
          }
        : file
    );

    try {
      const res = await previewBatchReplyTable(
        {
          sessionId: params.sourceSessionId.value,
          sourceTables:
            params.selectedSourceConfigs.value.map(toBatchTableConfig),
          targetId,
          targetTable: toBatchTableConfig(item)
        },
        { signal: requestState.controller.signal }
      );

      if (
        requestState.controller.signal.aborted ||
        previewRequestStates.get(requestKey) !== requestState ||
        requestState.fingerprint !==
          buildCurrentFingerprint(targetId, item.tableIndex)
      ) {
        return;
      }

      if (res.code !== 0) {
        ElMessage.error(res.message || "目标表预览失败");
        return;
      }

      params.targetFiles.value = params.targetFiles.value.map(file =>
        file.targetId === targetId
          ? {
              ...file,
              previewResults: {
                ...file.previewResults,
                [item.tableIndex]: res.data
              }
            }
          : file
      );

      if ((res.data.duplicateGroups?.length ?? 0) > 0) {
        openDuplicateDialog(targetId, item, res.data.duplicateGroups);
        ElMessage.warning("当前目标表存在重复项，请先确认处理方式");
      } else if (res.data.canApply) {
        ElMessage.success("当前 Sheet/表格预览通过");
      } else {
        ElMessage.warning("当前目标表仍存在需要处理的问题");
      }
    } catch {
      if (
        requestState.controller.signal.aborted ||
        previewRequestStates.get(requestKey) !== requestState ||
        requestState.fingerprint !==
          buildCurrentFingerprint(targetId, item.tableIndex)
      ) {
        return;
      }

      ElMessage.error("目标表预览失败");
    } finally {
      if (previewRequestStates.get(requestKey) === requestState) {
        previewRequestStates.delete(requestKey);
        params.targetFiles.value = params.targetFiles.value.map(file =>
          file.targetId === targetId
            ? {
                ...file,
                previewLoadingTableIndexes:
                  file.previewLoadingTableIndexes.filter(
                    tableIndex => tableIndex !== item.tableIndex
                  )
              }
            : file
        );
      }
    }
  };

  const confirmDuplicateDialog = async () => {
    const dialog = duplicateDialog.value;
    if (!dialog) {
      return;
    }

    applyDuplicateResolutions(dialog);
    closeDuplicateDialog();

    const targetFile = params.targetFiles.value.find(
      file => file.targetId === dialog.targetId
    );
    const targetConfig = targetFile?.configs.find(
      config => config.tableIndex === dialog.tableIndex
    );
    if (!targetConfig) {
      ElMessage.error("未找到当前目标表配置，无法重新预览");
      return;
    }

    await handleTargetTablePreview(dialog.targetId, targetConfig);
  };

  return {
    cancelTargetPreviews,
    duplicateDialog,
    closeDuplicateDialog,
    confirmDuplicateDialog,
    createSourcePreviewLoader,
    getTargetPreviewLoader,
    handleTargetTablePreview,
    updateDuplicateDialogStrategy
  };
};
