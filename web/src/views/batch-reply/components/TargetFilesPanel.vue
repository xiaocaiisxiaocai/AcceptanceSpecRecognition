<script setup lang=”ts”>
import type { UploadFile, UploadFiles, UploadInstance } from “element-plus”;
import type { TableData } from “@/api/document”;
import BatchTableConfigPanel from “@/views/smart-fill/components/BatchTableConfig.vue”;
import type {
  SourceTableOption,
  BatchReplyTableConfigItem
} from “../batch-reply-table-config”;
import { isTargetExecutable } from “../batch-reply-table-config”;
import type {
  BatchReplySourceFileState,
  BatchReplyTargetState
} from “../batch-reply-state”;
import AppUploadZone from “@/components/AppUploadZone.vue”;

defineProps<{
  activeTargetFileId: string;
  canPreviewBatchReply: boolean;
  canUploadTargetFile: boolean;
  formatFileSize: (size: number) => string;
  getTargetPreviewLoader: (targetId: string) => (
    tableIndex: number,
    options: {
      previewRows?: number;
      headerRowIndex?: number;
      headerRowCount?: number;
      dataStartRowIndex?: number;
    }
  ) => Promise<TableData>;
  selectedSourceTableOptions: SourceTableOption[];
  sourceFile: BatchReplySourceFileState | null;
  targetAccept: “.xlsx” | “.docx”;
  targetFiles: BatchReplyTargetState[];
  targetUploading: boolean;
  targetUploadKey: number;
}>();

const targetUploadRef = defineModel<UploadInstance | undefined>(
  “targetUploadRef”
);

defineEmits<{
  “update:activeTargetFileId”: [value: string];
  configChange: [targetId: string, value: BatchReplyTableConfigItem[]];
  fileChange: [uploadFile: UploadFile, uploadFiles: UploadFiles];
  preview: [targetId: string, item: BatchReplyTableConfigItem];
  remove: [targetId: string];
}>();
</script>

<template>
  <el-card class=”section-card file-stage-panel” shadow=”never”>
    <template #header>
      <div class=”section-header”>
        <span>目标文件</span>
        <span class=”section-subtitle”
          >上传后按目标文件和 Sheet/表格逐层配置，并为每个目标表选择来源表</span
        >
      </div>
    </template>

    <el-alert
      v-if=”selectedSourceTableOptions.length === 0”
      type=”warning”
      :closable=”false”
      show-icon
      title=”请先在来源文件步骤里至少保留一个来源表”
    />

    <div v-if=”canUploadTargetFile”>
      <AppUploadZone
        v-show=”targetFiles.length === 0”
        :uploading=”targetUploading”
        :accept=”sourceFile ? targetAccept : '.docx,.xlsx'”
        size=”normal”
        :drag-text=”sourceFile ? '将目标文件拖到此处或' : '请先上传来源文件'”
        :tip-text=”
          sourceFile
            ? `当前仅接受 ${targetAccept} 格式`
            : '来源文件确认后自动限定同格式上传'
        “
        @upload=”
          (options: any) => {
            targetUploadRef?._post(options);
            $emit('fileChange', ...(options as any));
          }
        “
      />
    </div>

    <el-empty
      v-if=”targetFiles.length === 0”
      description=”上传目标文件后，会在下方按文件分 Tab 展开配置”
    />
    <el-tabs
      v-else
      :model-value=”activeTargetFileId”
      class=”target-file-tabs”
      @update:model-value=”$emit('update:activeTargetFileId', String($event))”
    >
      <el-tab-pane
        v-for=”targetFile in targetFiles”
        :key=”targetFile.targetId”
        :label=”targetFile.fileName”
        :name=”targetFile.targetId”
      >
        <div class=”target-file-summary”>
          <div>
            <div class=”target-file-name”>{{ targetFile.fileName }}</div>
            <div class=”target-file-meta”>
              <span>{{ formatFileSize(targetFile.size) }}</span>
              <span
                >共 {{ targetFile.tableCount }} 个{{
                  targetFile.fileType === 1 ? “工作表” : “表格”
                }}</span
              >
              <el-tag
                size=”small”
                :type=”isTargetExecutable(targetFile) ? 'success' : 'warning'”
              >
                {{ isTargetExecutable(targetFile) ? “可执行” : “待预览” }}
              </el-tag>
            </div>
          </div>
          <el-button
            type=”danger”
            link
            @click=”$emit('remove', targetFile.targetId)”
          >
            移除
          </el-button>
        </div>

        <BatchTableConfigPanel
          :model-value=”targetFile.configs”
          :tables=”targetFile.tables”
          :is-excel=”targetFile.fileType === 1”
          :preview-loader=”getTargetPreviewLoader(targetFile.targetId)”
          :source-table-options=”selectedSourceTableOptions”
          source-table-label=”来源表”
          :mapping-previewable=”canPreviewBatchReply”
          :mapping-preview-loading-table-index=”
            targetFile.previewLoadingTableIndex
          “
          :mapping-preview-results=”targetFile.previewResults”
          @update:model-value=”
            $emit('configChange', targetFile.targetId, $event)
          “
          @mapping-preview=”$emit('preview', targetFile.targetId, $event)”
        />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>
