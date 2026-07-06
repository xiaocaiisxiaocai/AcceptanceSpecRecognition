<script setup lang="ts">
import type { FileUploadResponse, TableData } from "@/api/document";
import type { ExcelSheetMapping, TableImportConfig } from "../dataImport.types";

defineProps<{
  isExcelFile: boolean;
  uploadedFile: FileUploadResponse | null;
  tableConfigs: TableImportConfig[];
  canPasteClipboard: boolean;
  mappingRulesCount: number;
  loadingMappingRules: boolean;
  mappingClipboardSourceIndex: number | null;
  activeTableIndex: number | null;
  getExcelPreviewOptions: (cfg: TableImportConfig) => {
    headerRowIndex: number;
    headerRowCount: number;
    dataStartRowIndex: number;
    dataEndRowIndex: number;
  };
}>();

const emit = defineEmits<{
  (e: "copyMapping"): void;
  (e: "pasteMapping"): void;
  (e: "update:activeTableIndex", value: number | null): void;
  (e: "tabRemove", value: string | number): void;
  (e: "previewLoaded", payload: { tableIndex: number; data: TableData }): void;
  (
    e: "updateExcelMapping",
    payload: { tableIndex: number; value: ExcelSheetMapping }
  ): void;
  (e: "restoreTables"): void;
  (e: "goPrev"): void;
  (e: "reloadRules"): void;
  (e: "reapplyRules"): void;
}>();
</script>

<template>
  <div class="step-panel">
    <div v-if="!isExcelFile" class="mapping-rule-actions">
      <el-tag size="small" type="info"
        >列映射规则：{{ mappingRulesCount }} 条</el-tag
      >
      <el-button
        size="small"
        :loading="loadingMappingRules"
        @click="emit('reloadRules')"
      >
        重新加载规则
      </el-button>
      <el-button
        size="small"
        type="primary"
        :disabled="mappingRulesCount === 0"
        @click="emit('reapplyRules')"
      >
        重新应用自动预填
      </el-button>
    </div>
    <div
      v-if="uploadedFile && tableConfigs.length > 0"
      class="mapping-quick-actions"
    >
      <el-button size="small" @click="emit('copyMapping')">
        复制当前{{ isExcelFile ? "工作表" : "表格" }}字段配置
      </el-button>
      <el-button
        size="small"
        type="primary"
        :disabled="tableConfigs.length < 2 || !canPasteClipboard"
        @click="emit('pasteMapping')"
      >
        应用到其他{{ isExcelFile ? "工作表" : "表格" }}
      </el-button>
      <span
        v-if="mappingClipboardSourceIndex !== null"
        class="mapping-clipboard-tip"
      >
        已复制{{ isExcelFile ? "工作表" : "表格" }}
        {{ mappingClipboardSourceIndex + 1 }} 的字段配置
      </span>
    </div>

    <slot />

    <div
      v-if="!tableConfigs.length && uploadedFile"
      class="mapping-empty-state"
    >
      <el-empty
        :description="`至少需要保留一个${isExcelFile ? '工作表' : '表格'}才能继续配置映射`"
      >
        <el-button type="primary" @click="emit('restoreTables')">
          重新载入{{ isExcelFile ? "工作表" : "表格" }}
        </el-button>
        <el-button @click="emit('goPrev')">返回上一步</el-button>
      </el-empty>
    </div>
  </div>
</template>

<style scoped>
.mapping-rule-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-top: 12px;
}
</style>
