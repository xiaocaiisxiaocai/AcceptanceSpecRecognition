<script setup lang="ts">
import { UploadFilled } from "@element-plus/icons-vue";
import type { UploadRequestOptions } from "element-plus";
import type { BatchReplySourceFileState } from "../batch-reply-state";

defineProps<{
  sourceFile: BatchReplySourceFileState | null;
  sourceIsExcel: boolean;
  canUploadSourceFile: boolean;
  sourceUploading: boolean;
  uploadRequest: (options: UploadRequestOptions) => Promise<void>;
}>();

defineEmits<{
  reset: [];
}>();
</script>

<template>
  <el-card class="section-card file-stage-panel" shadow="never">
    <template #header>
      <div class="section-header">
        <span>来源文件</span>
        <el-button v-if="sourceFile" type="danger" link @click="$emit('reset')">
          重新选择
        </el-button>
      </div>
    </template>

    <el-upload
      v-if="canUploadSourceFile && !sourceFile"
      class="upload-area"
      drag
      :show-file-list="false"
      :http-request="uploadRequest"
      accept=".docx,.xlsx"
      :disabled="sourceUploading"
    >
      <el-icon class="el-icon--upload" :size="56">
        <UploadFilled />
      </el-icon>
      <div class="el-upload__text">
        将已回复文档拖到此处，或 <em>点击上传</em>
      </div>
      <template #tip>
        <div class="el-upload__tip">
          仅支持 .docx / .xlsx，文件大小不超过 50MB
        </div>
      </template>
    </el-upload>

    <el-alert
      v-else-if="!sourceFile"
      type="warning"
      :closable="false"
      show-icon
      title="当前账号没有来源文件上传权限"
    />

    <div v-else class="source-summary">
      <div class="source-file-name">{{ sourceFile.sourceFileName }}</div>
      <div class="source-meta">
        <el-tag size="small" type="primary">
          {{ sourceIsExcel ? "Excel" : "Word" }}
        </el-tag>
        <span
          >检测到 {{ sourceFile.tableCount }} 个{{
            sourceIsExcel ? "工作表" : "表格"
          }}</span
        >
        <span>默认会把每张表都带出来，你可以逐表调整或取消参与。</span>
      </div>
    </div>
  </el-card>
</template>
