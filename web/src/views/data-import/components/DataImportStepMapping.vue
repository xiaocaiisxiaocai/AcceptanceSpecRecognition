<script setup lang="ts">
defineProps<{
  isExcelFile: boolean;
  uploadedFile: any;
  tableConfigs: any[];
  loadingMappingRules: boolean;
  mappingRulesLength: number;
  canPasteClipboard: boolean;
  mappingClipboardSourceIndex: number | null;
  activeTableIndex: number | null;
  getExcelPreviewOptions: (cfg: any) => {
    headerRowIndex: number;
    headerRowCount: number;
    dataStartRowIndex: number;
  };
}>();

const emit = defineEmits<{
  (e: "reloadRules"): void;
  (e: "reapplyRules"): void;
  (e: "copyMapping"): void;
  (e: "pasteMapping"): void;
  (e: "update:activeTableIndex", value: number | null): void;
  (e: "tabRemove", value: string | number): void;
  (e: "previewLoaded", payload: { tableIndex: number; data: any }): void;
  (e: "updateExcelMapping", payload: { tableIndex: number; value: any }): void;
  (e: "restoreTables"): void;
  (e: "goPrev"): void;
}>();
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">{{ isExcelFile ? "配置列序号" : "配置列映射" }}</h3>
    <div class="flex items-center justify-between mb-2">
      <p class="step-desc m-0">
        <span v-if="!isExcelFile">
          系统会根据“列映射规则”自动预填映射；若未命中你仍可手动调整
        </span>
        <span v-else>按列序号指定字段（列号 1-based：第 1 列为 A）。</span>
      </p>
      <div v-if="!isExcelFile" class="flex gap-2">
        <el-button size="small" :loading="loadingMappingRules" @click="emit('reloadRules')">
          重新加载规则
        </el-button>
        <el-button
          size="small"
          type="primary"
          :disabled="!mappingRulesLength"
          @click="emit('reapplyRules')"
        >
          重新应用规则
        </el-button>
      </div>
    </div>
    <div v-if="uploadedFile && tableConfigs.length > 0" class="mapping-quick-actions">
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
      <span v-if="mappingClipboardSourceIndex !== null" class="mapping-clipboard-tip">
        已复制{{ isExcelFile ? "工作表" : "表格" }} {{ mappingClipboardSourceIndex + 1 }} 的字段配置
      </span>
    </div>

    <slot />

    <div v-if="!tableConfigs.length && uploadedFile" class="mapping-empty-state">
      <el-empty :description="`至少需要保留一个${isExcelFile ? '工作表' : '表格'}才能继续配置映射`">
        <el-button type="primary" @click="emit('restoreTables')">
          重新载入{{ isExcelFile ? "工作表" : "表格" }}
        </el-button>
        <el-button @click="emit('goPrev')">返回上一步</el-button>
      </el-empty>
    </div>
  </div>
</template>
