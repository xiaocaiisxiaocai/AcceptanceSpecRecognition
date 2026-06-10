<script setup lang="ts">
import type { TableData, TableInfo } from "@/api/document";
import BatchTableConfigPanel from "@/views/smart-fill/components/BatchTableConfig.vue";
import type { BatchReplySourceFileState } from "../batch-reply-state";
import type { BatchReplyTableConfigItem } from "../batch-reply-table-config";

defineProps<{
  activeTab: string;
  sourceFile: BatchReplySourceFileState | null;
  sourceTables: TableInfo[];
  sourceConfigs: BatchReplyTableConfigItem[];
  sourceIsExcel: boolean;
  previewLoader: (
    tableIndex: number,
    options: {
      previewRows?: number;
      headerRowIndex?: number;
      headerRowCount?: number;
      dataStartRowIndex?: number;
    }
  ) => Promise<TableData>;
}>();

defineEmits<{
  "update:activeTab": [value: string];
  configChange: [value: BatchReplyTableConfigItem[]];
}>();
</script>

<template>
  <el-card class="section-card file-stage-panel" shadow="never">
    <template #header>
      <div class="section-header">
        <span>来源文件工作区</span>
        <span class="section-subtitle"
          >按文件和 Sheet/表格逐层配置行设置与列映射</span
        >
      </div>
    </template>

    <el-empty v-if="!sourceFile" description="请先上传来源文件" />
    <el-tabs
      v-else
      :model-value="activeTab"
      class="source-file-tabs"
      @update:model-value="$emit('update:activeTab', String($event))"
    >
      <el-tab-pane
        :label="sourceFile.sourceFileName"
        :name="sourceFile.sessionId"
      >
        <div class="source-file-summary">
          <div class="source-file-name">{{ sourceFile.sourceFileName }}</div>
          <div class="source-meta">
            <el-tag size="small" type="primary">
              {{ sourceIsExcel ? "Excel" : "Word" }}
            </el-tag>
            <span
              >共 {{ sourceFile.tableCount }} 个{{
                sourceIsExcel ? "工作表" : "表格"
              }}</span
            >
            <span
              >请在对应
              Sheet/表格里直接设置行配置、项目列、规格列、验收列和备注列。</span
            >
          </div>
        </div>
        <BatchTableConfigPanel
          :model-value="sourceConfigs"
          :tables="sourceTables"
          :is-excel="sourceIsExcel"
          :preview-loader="previewLoader"
          @update:model-value="$emit('configChange', $event)"
        />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>
