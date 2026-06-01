import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import type { UploadFile, UploadFiles, UploadInstance } from "element-plus";
import {
  getBatchReplyTargetTables,
  uploadBatchReplyTargets,
  type BatchReplyUploadedTargetFile
} from "@/api/matching";
import { ensurePermission } from "@/utils/permission-guard";
import {
  buildTableConfig,
  resolveDefaultSourceTableIndex,
  type SourceTableOption
} from "../batch-reply-table-config";
import type { BatchReplySourceFileState, BatchReplyTargetState } from "../batch-reply-state";
import { createTargetFileSignature, decideTargetUpload } from "../target-upload";

type UseBatchReplyTargetUploadsParams = {
  sourceFile: Ref<BatchReplySourceFileState | null>;
  targetFiles: Ref<BatchReplyTargetState[]>;
  sourceSessionId: ComputedRef<string>;
  targetAccept: ComputedRef<string>;
  selectedSourceTableOptions: ComputedRef<SourceTableOption[]>;
  activeRootTab: Ref<string>;
};

export const useBatchReplyTargetUploads = (params: UseBatchReplyTargetUploadsParams) => {
  const targetUploading = ref(false);
  const targetUploadKey = ref(0);
  const targetUploadRef = ref<UploadInstance>();
  const activeTargetFileId = ref("");
  const pendingTargetUploadFiles = ref<File[]>([]);
  const pendingTargetUploadSignatures = ref<string[]>([]);
  let targetUploadFlushTimer: number | undefined;

  const appendUploadedTarget = async (
    uploadedFile: BatchReplyUploadedTargetFile,
    rawFile: File
  ) => {
    const tablesResp = await getBatchReplyTargetTables(
      params.sourceSessionId.value,
      uploadedFile.targetId
    );
    if (tablesResp.code !== 0) {
      throw new Error(tablesResp.message || "加载目标表格失败");
    }

    const targetState: BatchReplyTargetState = {
      targetId: uploadedFile.targetId,
      fileName: uploadedFile.fileName,
      fileType: uploadedFile.fileType,
      tableCount: uploadedFile.tableCount,
      size: rawFile.size,
      signature: createTargetFileSignature(rawFile),
      tables: tablesResp.data,
      configs: tablesResp.data.map(table =>
        buildTableConfig(
          table,
          uploadedFile.fileType === 1,
          true,
          resolveDefaultSourceTableIndex(table.index, params.selectedSourceTableOptions.value)
        )
      ),
      previewResults: {}
    };

    params.targetFiles.value = [...params.targetFiles.value, targetState];
    if (!activeTargetFileId.value) {
      activeTargetFileId.value = uploadedFile.targetId;
    }
  };

  const schedulePendingTargetUploadFlush = () => {
    if (targetUploadFlushTimer !== undefined) {
      return;
    }

    targetUploadFlushTimer = window.setTimeout(() => {
      targetUploadFlushTimer = undefined;
      void flushPendingTargetUploads();
    }, 0);
  };

  const flushPendingTargetUploads = async () => {
    if (targetUploading.value || pendingTargetUploadFiles.value.length === 0) {
      return;
    }

    const pendingFiles = [...pendingTargetUploadFiles.value];
    pendingTargetUploadFiles.value = [];
    pendingTargetUploadSignatures.value = [];

    targetUploading.value = true;
    try {
      const res = await uploadBatchReplyTargets(params.sourceSessionId.value, pendingFiles);
      if (res.code !== 0 || res.data.files.length === 0) {
        ElMessage.error(res.message || "目标文件上传失败");
        return;
      }

      for (let index = 0; index < res.data.files.length; index++) {
        const uploadedFile = res.data.files[index];
        const rawFile = pendingFiles[index];
        if (!uploadedFile || !rawFile) {
          continue;
        }

        await appendUploadedTarget(uploadedFile, rawFile);
      }

      params.activeRootTab.value = "target";
      ElMessage.success(
        pendingFiles.length === 1
          ? `${pendingFiles[0].name} 上传成功`
          : `已上传 ${pendingFiles.length} 个目标文件`
      );
    } catch {
      ElMessage.error("目标文件上传失败");
    } finally {
      targetUploading.value = false;
      if (pendingTargetUploadFiles.value.length > 0) {
        schedulePendingTargetUploadFlush();
      }
    }
  };

  const handleTargetFileChange = (uploadFile: UploadFile, _uploadFiles: UploadFiles) => {
    const rawFile = uploadFile.raw;
    if (!rawFile) {
      targetUploadRef.value?.handleRemove(uploadFile);
      return;
    }

    const decision = decideTargetUpload({
      hasSourceFile: !!params.sourceFile.value,
      accept: params.targetAccept.value,
      existingSignatures: [
        ...params.targetFiles.value.map(item => item.signature),
        ...pendingTargetUploadSignatures.value
      ],
      file: rawFile
    });

    if (decision.status === "rejected") {
      if (decision.level === "warning") {
        ElMessage.warning(decision.message);
      } else {
        ElMessage.error(decision.message);
      }
      targetUploadRef.value?.handleRemove(uploadFile);
      return;
    }

    if (!ensurePermission("api:batch-reply:upload", "权限不足，无法上传目标文件")) {
      targetUploadRef.value?.handleRemove(uploadFile);
      return;
    }

    pendingTargetUploadFiles.value = [...pendingTargetUploadFiles.value, rawFile];
    pendingTargetUploadSignatures.value = [
      ...pendingTargetUploadSignatures.value,
      createTargetFileSignature(rawFile)
    ];
    schedulePendingTargetUploadFlush();
    targetUploadRef.value?.handleRemove(uploadFile);
  };

  const resetTargetUploadState = () => {
    activeTargetFileId.value = "";
    pendingTargetUploadFiles.value = [];
    pendingTargetUploadSignatures.value = [];
    if (targetUploadFlushTimer !== undefined) {
      window.clearTimeout(targetUploadFlushTimer);
      targetUploadFlushTimer = undefined;
    }
    targetUploadKey.value++;
  };

  return {
    activeTargetFileId,
    targetUploading,
    targetUploadKey,
    targetUploadRef,
    handleTargetFileChange,
    resetTargetUploadState
  };
};
