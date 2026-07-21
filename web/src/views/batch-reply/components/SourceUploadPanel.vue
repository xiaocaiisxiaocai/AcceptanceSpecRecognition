<script setup lang="ts">
import type { BatchReplySourceFileState } from "../batch-reply-state";
import AppUploadZone from "@/components/AppUploadZone.vue";
import type { AppUploadRequest } from "@/components/useAppUploadTask";

defineProps<{
  sourceFile: BatchReplySourceFileState | null;
  sourceIsExcel: boolean;
  canUploadSourceFile: boolean;
  uploadRequest: AppUploadRequest;
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

    <div v-if="canUploadSourceFile && !sourceFile">
      <AppUploadZone
        :request="uploadRequest"
        accept=".docx,.xlsx"
        size="normal"
        drag-text="将来源文件拖到此处或"
        tip-text="仅支持 .docx / .xlsx，文件大小不超过 50MB"
      />
    </div>

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
