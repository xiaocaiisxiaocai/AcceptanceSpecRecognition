import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import type { UploadRequestOptions } from "element-plus";
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
import type {
  BatchReplySourceFileState,
  BatchReplyTargetState
} from "../batch-reply-state";
import {
  createTargetFileSignature,
  decideTargetUpload
} from "../target-upload";
import type { AppUploadRequestContext } from "@/components/useAppUploadTask";
import {
  isUploadRequestCancelled,
  throwIfUploadCancelled,
  type UploadTransportOptions
} from "@/utils/upload-request";

type UseBatchReplyTargetUploadsParams = {
  sourceFile: Ref<BatchReplySourceFileState | null>;
  targetFiles: Ref<BatchReplyTargetState[]>;
  sourceSessionId: ComputedRef<string>;
  targetAccept: ComputedRef<string>;
  selectedSourceTableOptions: ComputedRef<SourceTableOption[]>;
  activeRootTab: Ref<string>;
  /** 由调用方提供的目标文件上传执行函数，接收 sourceSessionId 和待上传文件列表 */
  onUploadTargets?: (
    sessionId: string,
    files: File[],
    options: UploadTransportOptions
  ) => Promise<Awaited<ReturnType<typeof uploadBatchReplyTargets>>>;
};

export const useBatchReplyTargetUploads = (
  params: UseBatchReplyTargetUploadsParams
) => {
  const targetUploading = ref(false);
  const targetUploadKey = ref(0);
  const activeTargetFileId = ref("");
  const pendingTargetUploadFiles = ref<File[]>([]);
  let targetUploadVersion = 0;

  const throwIfTargetUploadStale = (uploadVersion: number) => {
    if (uploadVersion === targetUploadVersion) return;
    throw new DOMException("目标文件上传状态已重置", "AbortError");
  };

  const appendUploadedTarget = async (
    uploadedFile: BatchReplyUploadedTargetFile,
    rawFile: File,
    signal: AbortSignal,
    uploadVersion: number
  ) => {
    throwIfTargetUploadStale(uploadVersion);
    throwIfUploadCancelled(signal);
    const tablesResp = await getBatchReplyTargetTables(
      params.sourceSessionId.value,
      uploadedFile.targetId
    );
    throwIfTargetUploadStale(uploadVersion);
    throwIfUploadCancelled(signal);
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
          resolveDefaultSourceTableIndex(
            table.index,
            params.selectedSourceTableOptions.value
          )
        )
      ),
      previewResults: {}
    };

    throwIfTargetUploadStale(uploadVersion);
    throwIfUploadCancelled(signal);
    params.targetFiles.value = [...params.targetFiles.value, targetState];
    if (!activeTargetFileId.value) {
      activeTargetFileId.value = uploadedFile.targetId;
    }
  };

  const handleTargetFileChange = async (
    options: UploadRequestOptions,
    context: AppUploadRequestContext
  ) => {
    const rawFile = options.file;
    if (!rawFile) {
      throw new Error("未读取到目标文件");
    }

    const decision = decideTargetUpload({
      hasSourceFile: !!params.sourceFile.value,
      accept: params.targetAccept.value,
      existingSignatures: params.targetFiles.value.map(item => item.signature),
      file: rawFile
    });

    if (decision.status === "rejected") {
      if (decision.level === "warning") {
        ElMessage.warning(decision.message);
      } else {
        ElMessage.error(decision.message);
      }
      throw new Error(decision.message);
    }

    if (
      !ensurePermission("api:batch-reply:upload", "权限不足，无法上传目标文件")
    ) {
      throw new Error("权限不足，无法上传目标文件");
    }

    if (targetUploading.value) throw new Error("已有目标文件正在上传");

    const uploadVersion = ++targetUploadVersion;
    const releaseCancelledUpload = () => {
      if (uploadVersion !== targetUploadVersion) return;
      targetUploading.value = false;
      pendingTargetUploadFiles.value = [];
    };
    context.signal.addEventListener("abort", releaseCancelledUpload, {
      once: true
    });
    pendingTargetUploadFiles.value = [rawFile];
    targetUploading.value = true;
    try {
      throwIfTargetUploadStale(uploadVersion);
      throwIfUploadCancelled(context.signal);
      const transportOptions = {
        signal: context.signal,
        onUploadProgress: context.onUploadProgress
      };
      const res = await (params.onUploadTargets
        ? params.onUploadTargets(
            params.sourceSessionId.value,
            [rawFile],
            transportOptions
          )
        : uploadBatchReplyTargets(
            params.sourceSessionId.value,
            [rawFile],
            transportOptions
          ));
      throwIfTargetUploadStale(uploadVersion);
      throwIfUploadCancelled(context.signal);
      if (res.code !== 0 || res.data.files.length === 0)
        throw new Error(res.message || "目标文件上传失败");

      const uploadedFile = res.data.files[0];
      throwIfTargetUploadStale(uploadVersion);
      await appendUploadedTarget(
        uploadedFile,
        rawFile,
        context.signal,
        uploadVersion
      );
      throwIfTargetUploadStale(uploadVersion);
      throwIfUploadCancelled(context.signal);
      params.activeRootTab.value = "target";
      ElMessage.success(`${rawFile.name} 上传成功`);
    } catch (error) {
      if (isUploadRequestCancelled(error)) throw error;
      ElMessage.error("目标文件上传失败");
      throw error;
    } finally {
      context.signal.removeEventListener("abort", releaseCancelledUpload);
      if (uploadVersion === targetUploadVersion) {
        targetUploading.value = false;
        pendingTargetUploadFiles.value = [];
      }
    }
  };

  const resetTargetUploadState = () => {
    targetUploadVersion += 1;
    targetUploading.value = false;
    activeTargetFileId.value = "";
    pendingTargetUploadFiles.value = [];
    targetUploadKey.value++;
  };

  return {
    activeTargetFileId,
    targetUploading,
    targetUploadKey,
    handleTargetFileChange,
    resetTargetUploadState,
    pendingTargetUploadFiles
  };
};
