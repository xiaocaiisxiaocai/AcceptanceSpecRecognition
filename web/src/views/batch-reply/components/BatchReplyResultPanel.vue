<script setup lang="ts">
import type {
  BatchReplyExecuteFileResult,
  BatchReplyExecuteResponse
} from "@/api/matching";

defineProps<{
  executeDisabled: boolean;
  executeResult: BatchReplyExecuteResponse | null;
  executableTargetCount: number;
  executing: boolean;
  resultFiles: BatchReplyExecuteFileResult[];
  targetFileCount: number;
}>();

defineEmits<{
  execute: [];
}>();
</script>

<template>
  <el-card class="section-card file-stage-panel" shadow="never">
    <template #header>
      <div class="section-header">
        <span>执行结果</span>
        <span class="section-subtitle"
          >确认可执行文件后，在这里统一执行并查看结果</span
        >
      </div>
    </template>

    <div class="action-row">
      <el-button
        type="success"
        :loading="executing"
        :disabled="executeDisabled"
        @click="$emit('execute')"
      >
        执行已完成目标文件
      </el-button>
      <span class="action-tip">
        当前可执行 {{ executableTargetCount }} /
        {{ targetFileCount }} 份目标文件
      </span>
    </div>

    <el-empty
      v-if="!executeResult"
      description="完成目标文件配置并执行后，结果会展示在这里"
    />
    <template v-else>
      <el-alert
        :title="`执行完成：成功 ${executeResult.successCount} 份，失败 ${executeResult.failedCount} 份`"
        :type="executeResult.failedCount > 0 ? 'warning' : 'success'"
        :closable="false"
        show-icon
      />

      <el-table class="preview-table" :data="resultFiles" border>
        <el-table-column
          prop="fileName"
          label="文件名"
          min-width="min(280px, calc(100vw - 32px))"
        />
        <el-table-column
          label="结果"
          width="min(120px, calc(100vw - 32px))"
          align="center"
        >
          <template #default="{ row }">
            <el-tag :type="row.success ? 'success' : 'danger'">
              {{ row.success ? "成功" : "失败" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="message"
          label="说明"
          min-width="min(320px, calc(100vw - 32px))"
        />
      </el-table>
    </template>
  </el-card>
</template>
