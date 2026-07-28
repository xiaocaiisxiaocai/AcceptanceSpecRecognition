<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  ArrowRight,
  CircleCheckFilled,
  InfoFilled,
  WarningFilled
} from "@element-plus/icons-vue";
import type { AiServiceSelection } from "@/api/ai-service";
import type { Customer } from "@/api/customer";
import type { Process } from "@/api/process";
import { getDistinctAiServiceModel } from "@/views/shared/ai-service-display";
import {
  getRuntimeAiSelectionStatusText,
  isRuntimeAiSelectionAvailable
} from "@/utils/runtime-ai-selection";
import type {
  CombinedImportResult,
  ImportDuplicateAiConfig,
  ImportPreviewGroup,
  ImportPreviewRow,
  SkippedRowsGroup,
  TableImportConfig
} from "../dataImport.types";
import {
  mergeSkippedPreviewCellValues,
  shouldBackfillProjectFromSpecification
} from "../dataImport.helpers";

const props = withDefaults(
  defineProps<{
    importResult: CombinedImportResult | null;
    isExcelFile: boolean;
    canUploadSourceFile: boolean;
    canImportAny: boolean;
    canImportCurrentFile: boolean;
    currentImportPermissionMessage: string;
    hasPendingDifferenceConfirmation: boolean;
    pendingDifferencesCount: number;
    hasCommittedImportProgress: boolean;
    committedSuccessCount: number;
    committedSkippedCount: number;
    committedFailedCount: number;
    uploadedFileName?: string;
    tableConfigs: TableImportConfig[];
    selectedSheetCount?: number;
    pendingSelectedSheetCount?: number;
    customers: Customer[];
    processes: Process[];
    selectedCustomerId?: number;
    selectedProcessId?: number;
    selectedMachineModelName: string;
    previewDataCount: number;
    importDuplicateAiConfig: ImportDuplicateAiConfig;
    loadingAiServices: boolean;
    embeddingSelection: AiServiceSelection;
    llmSelection: AiServiceSelection;
    removedPreviewRowCount: number;
    selectedImportPreviewRowsCount: number;
    importPreviewGroups: ImportPreviewGroup[];
    previewLoadState: {
      loadedRows: number;
      totalRows: number;
      hasPartialPreview: boolean;
    };
    importing: boolean;
    importProgressText: string;
    importProgressDescription: string;
    importPrimaryButtonText: string;
    skippedRowsGroups: SkippedRowsGroup[];
    showImportAction?: boolean;
    allowEmptyPreviewAction?: boolean;
  }>(),
  {
    showImportAction: true,
    allowEmptyPreviewAction: false
  }
);

const emit = defineEmits<{
  restart: [];
  openDifferenceConfirmDialog: [];
  removeSelectedPreviewRows: [];
  restoreRemovedPreviewRows: [];
  importPreviewSelectionChange: [tableIndex: number, rows: ImportPreviewRow[]];
  removeSinglePreviewRow: [row: ImportPreviewRow];
  loadFullPreview: [];
  import: [];
}>();

// 父组件以引用方式传入配置对象，子组件通过该代理直接编辑其字段；
// mutation 经同一引用回传父组件，行为与直接改 prop 一致，同时规避 vue/no-mutating-props。
const duplicateAiConfig = computed(() => props.importDuplicateAiConfig);
const activeCollapseNames = ref<string[]>([]);
const showDuplicateAdvanced = ref(false);
type SkippedSheetRow = SkippedRowsGroup["rows"][number] & {
  projectValue: string;
  specificationValue: string;
  acceptanceValue: string;
  remarkValue: string;
  isRegionSeparator: boolean;
  separatorLabel: string;
};
const getSkippedBusinessValue = (
  group: SkippedRowsGroup,
  row: SkippedRowsGroup["rows"][number],
  label: string
) => {
  const column = group.columns.find(item => item.label === label);
  return column
    ? mergeSkippedPreviewCellValues(row.rowValues, column.indexes)
    : "";
};
const skippedSheetGroups = computed(() => {
  const groupsBySheet = new Map<number, SkippedRowsGroup[]>();
  props.skippedRowsGroups.forEach(group => {
    const groups = groupsBySheet.get(group.tableIndex) ?? [];
    groups.push(group);
    groupsBySheet.set(group.tableIndex, groups);
  });

  return Array.from(groupsBySheet.entries())
    .sort(
      ([firstTableIndex], [secondTableIndex]) =>
        firstTableIndex - secondTableIndex
    )
    .map(([tableIndex, regionGroups]) => {
      const orderedRegionGroups = [...regionGroups].sort((first, second) => {
        const firstRow = Math.min(...first.rows.map(row => row.rowIndex));
        const secondRow = Math.min(...second.rows.map(row => row.rowIndex));
        return firstRow - secondRow;
      });
      const rows: SkippedSheetRow[] = orderedRegionGroups.flatMap(
        (group, regionPosition) => {
          const dataRows = [...group.rows]
            .sort((first, second) => first.rowIndex - second.rowIndex)
            .map(row => ({
              ...row,
              projectValue: getSkippedBusinessValue(group, row, "项目"),
              specificationValue: getSkippedBusinessValue(group, row, "规格"),
              acceptanceValue: getSkippedBusinessValue(group, row, "验收"),
              remarkValue: getSkippedBusinessValue(group, row, "备注"),
              isRegionSeparator: false,
              separatorLabel: ""
            }));
          const firstRow = dataRows[0];
          if (regionPosition === 0 || !firstRow) return dataRows;

          const regionNumber = (group.regionIndex ?? regionPosition) + 1;
          const displayRowNumber = props.isExcelFile
            ? firstRow.rowIndex
            : firstRow.rowIndex + 1;
          const separatorLabel = `区域 ${regionNumber} · 从第 ${displayRowNumber} 行开始`;
          return [
            {
              ...firstRow,
              message: separatorLabel,
              projectValue: "",
              specificationValue: "",
              acceptanceValue: "",
              remarkValue: "",
              isRegionSeparator: true,
              separatorLabel
            },
            ...dataRows
          ];
        }
      );
      const dataCount = regionGroups.reduce(
        (total, group) => total + group.rows.length,
        0
      );
      return { tableIndex, rows, dataCount };
    });
});
const activeSkippedSheetKey = ref("");
const getSkippedSheetKey = (tableIndex: number) => String(tableIndex);
const getSkippedRowClassName = ({ row }: { row: SkippedSheetRow }) =>
  row.isRegionSeparator ? "skipped-region-separator" : "";
const getSkippedSpanMethod = ({
  row,
  columnIndex
}: {
  row: SkippedSheetRow;
  columnIndex: number;
}) => {
  if (!row.isRegionSeparator) return undefined;
  return columnIndex === 0
    ? ([1, 6] as [number, number])
    : ([0, 0] as [number, number]);
};
watch(
  () => skippedSheetGroups.value.map(group => group.tableIndex).join("|"),
  () => {
    const keys = skippedSheetGroups.value.map(group =>
      getSkippedSheetKey(group.tableIndex)
    );
    if (!keys.includes(activeSkippedSheetKey.value)) {
      activeSkippedSheetKey.value = keys[0] ?? "";
    }
  },
  { immediate: true }
);
const hasAvailableEmbeddingService = computed(() =>
  isRuntimeAiSelectionAvailable(props.embeddingSelection)
);
const hasAvailableLlmService = computed(() =>
  isRuntimeAiSelectionAvailable(props.llmSelection)
);
const embeddingServiceModel = computed(() =>
  getDistinctAiServiceModel(
    props.embeddingSelection.name,
    props.embeddingSelection.model
  )
);
const llmServiceModel = computed(() =>
  getDistinctAiServiceModel(props.llmSelection.name, props.llmSelection.model)
);
const embeddingStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(props.embeddingSelection, "Embedding")
);
const llmStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(props.llmSelection, "LLM")
);
const duplicateCheckSummary = computed(() => {
  if (!duplicateAiConfig.value.enableSemanticDuplicateCheck) {
    return "仅使用规则检查";
  }

  const parts = [
    `每条最多 ${duplicateAiConfig.value.semanticTopK} 个候选`,
    `最低相似度 ${(duplicateAiConfig.value.semanticMinScore * 100).toFixed(0)}%`
  ];
  if (duplicateAiConfig.value.enableLlmDuplicateReview) {
    parts.push("LLM 复核已开启");
  }
  return parts.join(" · ");
});
const handleCollapseChange = (
  activeNames: string | number | Array<string | number>
) => {
  const names = Array.isArray(activeNames) ? activeNames : [activeNames];
  if (
    names.includes("preview-list") &&
    props.previewLoadState.hasPartialPreview
  ) {
    emit("loadFullPreview");
  }
};
const hasSpecificationOnlyBackfillTables = computed(() =>
  props.tableConfigs.some(shouldBackfillProjectFromSpecification)
);
const formatResultRowNumber = (rowIndex: number) =>
  props.isExcelFile ? rowIndex : rowIndex + 1;
const effectiveSelectedSheetCount = computed(
  () => props.selectedSheetCount ?? props.tableConfigs.length
);
const effectivePendingSelectedSheetCount = computed(
  () => props.pendingSelectedSheetCount ?? 0
);
const importResultPresentation = computed(() => {
  const result = props.importResult;
  if (!result) {
    return {
      tone: "info",
      icon: InfoFilled,
      title: "导入结果",
      description: "尚无导入结果",
      total: 0
    };
  }

  const total = result.successCount + result.skippedCount + result.failedCount;
  if (result.failedCount > 0) {
    return {
      tone: "warning",
      icon: WarningFilled,
      title: "导入完成，部分数据处理失败",
      description: `本次处理 ${total} 条数据，请优先查看失败与跳过明细。`,
      total
    };
  }
  if (result.successCount > 0) {
    return {
      tone: "success",
      icon: CircleCheckFilled,
      title: "导入完成",
      description: `已新增 ${result.successCount} 条数据，其余未写入记录可在下方查看。`,
      total
    };
  }
  if (result.skippedCount > 0) {
    return {
      tone: "info",
      icon: InfoFilled,
      title: "本次没有新增数据",
      description: `${result.skippedCount} 条数据已存在或不符合导入条件，均未写入新记录。`,
      total
    };
  }
  return {
    tone: "info",
    icon: InfoFilled,
    title: "没有可导入的数据",
    description: "本次未处理任何数据，可以返回后重新选择文件或调整范围。",
    total
  };
});
</script>

<template>
  <!-- 导入结果 -->
  <div v-if="importResult" class="import-result">
    <section
      class="result-overview"
      :class="`result-overview--${importResultPresentation.tone}`"
      role="status"
      aria-live="polite"
    >
      <div class="result-overview__main">
        <div class="result-overview__icon" aria-hidden="true">
          <el-icon><component :is="importResultPresentation.icon" /></el-icon>
        </div>
        <div class="result-overview__copy">
          <span class="result-overview__eyebrow">
            本次共处理 {{ importResultPresentation.total }} 条
          </span>
          <h2>{{ importResultPresentation.title }}</h2>
        </div>
      </div>

      <div class="result-metrics" aria-label="导入结果统计">
        <div class="result-metric result-metric--success">
          <span>新增成功</span>
          <strong>{{ importResult.successCount }}</strong>
          <small>条</small>
        </div>
        <div class="result-metric result-metric--skipped">
          <span>未写入</span>
          <strong>{{ importResult.skippedCount }}</strong>
          <small>条</small>
        </div>
        <div class="result-metric result-metric--failed">
          <span>处理失败</span>
          <strong>{{ importResult.failedCount }}</strong>
          <small>条</small>
        </div>
      </div>

      <div class="result-overview__action">
        <el-button
          v-if="canUploadSourceFile && canImportAny"
          type="primary"
          size="large"
          @click="emit('restart')"
        >
          继续导入
        </el-button>
      </div>
    </section>

    <section
      v-if="importResult.errors.length > 0"
      class="result-detail result-detail--danger"
    >
      <header class="result-detail__header">
        <div>
          <span class="result-detail__kicker">需要处理</span>
          <h3>失败明细</h3>
          <p>以下数据未完成处理，请根据错误信息修正后重新导入。</p>
        </div>
        <strong>{{ importResult.failedCount }} 条</strong>
      </header>
      <el-table
        :data="importResult.errors"
        max-height="320"
        size="small"
        stripe
        class="result-detail__table"
      >
        <el-table-column prop="tableIndex" label="表格" width="80">
          <template #default="{ row }">
            {{ row.tableIndex + 1 }}
          </template>
        </el-table-column>
        <el-table-column prop="rowIndex" label="行号" width="80">
          <template #default="{ row }">
            {{ formatResultRowNumber(row.rowIndex) }}
          </template>
        </el-table-column>
        <el-table-column prop="message" label="错误信息" />
      </el-table>
    </section>

    <section
      v-if="importResult.skippedCount > 0"
      class="result-detail result-detail--skipped"
    >
      <el-alert
        v-if="!importResult.skippedRows.length"
        type="info"
        :closable="false"
        show-icon
        title="已跳过部分数据，但未返回可展示的行明细"
        description="请查看导入日志或重新导入；系统已默认请求保留未导入明细。"
      />
      <el-tabs
        v-else
        v-model="activeSkippedSheetKey"
        class="skipped-tabs"
        :class="{ 'skipped-tabs--single': skippedSheetGroups.length === 1 }"
      >
        <el-tab-pane
          v-for="sheet in skippedSheetGroups"
          :key="getSkippedSheetKey(sheet.tableIndex)"
          :name="getSkippedSheetKey(sheet.tableIndex)"
          lazy
        >
          <template #label>
            <span class="skipped-tab-label">
              表格 {{ sheet.tableIndex + 1 }}
              <strong>{{ sheet.dataCount }}</strong>
            </span>
          </template>
          <el-table
            :data="sheet.rows"
            height="100%"
            size="small"
            stripe
            class="skipped-rows-table"
            :row-class-name="getSkippedRowClassName"
            :span-method="getSkippedSpanMethod"
          >
            <el-table-column label="行号" width="88">
              <template #default="{ row }">
                <span
                  v-if="row.isRegionSeparator"
                  class="skipped-region-separator__label"
                >
                  {{ row.separatorLabel }}
                </span>
                <template v-else>
                  {{ formatResultRowNumber(row.rowIndex) }}
                </template>
              </template>
            </el-table-column>
            <el-table-column
              prop="message"
              label="跳过原因"
              min-width="220"
              show-overflow-tooltip
            />
            <el-table-column
              prop="projectValue"
              label="项目"
              min-width="150"
              show-overflow-tooltip
            />
            <el-table-column
              prop="specificationValue"
              label="规格"
              min-width="280"
              show-overflow-tooltip
            />
            <el-table-column
              prop="acceptanceValue"
              label="验收"
              min-width="150"
              show-overflow-tooltip
            />
            <el-table-column
              prop="remarkValue"
              label="备注"
              min-width="180"
              show-overflow-tooltip
            />
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </section>
  </div>

  <!-- 导入确认 -->
  <div v-else class="import-confirm">
    <div v-if="hasPendingDifferenceConfirmation" class="difference-entry">
      <el-alert
        type="warning"
        :closable="false"
        show-icon
        :title="`检测到 ${pendingDifferencesCount} 条重复、差异或 AI 疑似重复数据，请在弹窗中逐条确认是否覆盖已有记录。`"
        description="左侧为数据库已有数据，右侧为本次待导入数据。未命中的数据已按当前流程处理。"
      />
      <div class="difference-entry__actions">
        <el-button type="warning" @click="emit('openDifferenceConfirmDialog')">
          打开重复确认弹窗
        </el-button>
      </div>
    </div>

    <el-alert
      v-if="!canImportCurrentFile"
      type="warning"
      :closable="false"
      show-icon
      :title="currentImportPermissionMessage"
      class="mb-4"
    />
    <el-alert
      v-if="hasSpecificationOnlyBackfillTables"
      type="warning"
      :closable="false"
      show-icon
      title="项目将使用规格内容自动补齐"
      description="仅规格导入会将项目和规格写成同一内容，可能增加跨项目匹配风险；请确认没有独立项目列，再开始导入。"
      class="mb-4"
    />
    <el-alert
      v-if="effectivePendingSelectedSheetCount > 0"
      type="warning"
      :closable="false"
      show-icon
      :title="`已勾选 ${effectivePendingSelectedSheetCount} 张待配置 Sheet`"
      description="请切换到对应 Sheet 补齐必填列；全部可确认后，即可使用文件级主操作统一学习并导入。"
      class="mb-4"
    />
    <div class="import-summary-bar">
      <div class="import-summary-bar__meta">
        <span class="import-summary-bar__file" :title="uploadedFileName">
          {{ uploadedFileName || "-" }}
        </span>
        <span>
          客户：{{
            customers.find(c => c.id === selectedCustomerId)?.name || "-"
          }}
        </span>
        <span>
          制程：{{
            processes.find(p => p.id === selectedProcessId)?.name || "-"
          }}
        </span>
        <span>机型：{{ selectedMachineModelName || "-" }}</span>
        <span>已勾选 {{ effectiveSelectedSheetCount }} 张 Sheet</span>
        <span v-if="effectivePendingSelectedSheetCount > 0">
          可导入 {{ tableConfigs.length }} 张，待配置
          {{ effectivePendingSelectedSheetCount }} 张
        </span>
        <span class="import-summary-bar__count"
          >已配置合计预计 {{ previewDataCount }} 条</span
        >
      </div>
      <div class="import-summary-bar__actions">
        <el-button
          v-if="showImportAction && canImportCurrentFile"
          type="primary"
          :loading="importing"
          :disabled="
            effectivePendingSelectedSheetCount > 0 ||
            (!allowEmptyPreviewAction &&
              !hasPendingDifferenceConfirmation &&
              previewDataCount === 0)
          "
          @click="emit('import')"
        >
          {{ importPrimaryButtonText }}
        </el-button>
      </div>
      <div v-if="importing" class="import-summary-bar__progress">
        <strong>{{ importProgressText }}</strong>
        <span>{{ importProgressDescription }}</span>
      </div>
    </div>

    <el-collapse
      v-model="activeCollapseNames"
      class="confirm-panel-collapse"
      @change="handleCollapseChange"
    >
      <el-collapse-item name="duplicate-ai">
        <template #title>
          <div class="collapse-title">
            <span class="collapse-title__main">导入设置</span>
            <span
              class="collapse-status"
              :class="{
                active: duplicateAiConfig.enableSemanticDuplicateCheck
              }"
            >
              疑似重复检查{{
                loadingAiServices || embeddingSelection.status === "checking"
                  ? "检测中"
                  : duplicateAiConfig.enableSemanticDuplicateCheck
                    ? "已开启"
                    : "未开启"
              }}
            </span>
          </div>
        </template>
        <div class="duplicate-ai-panel">
          <div class="duplicate-ai-panel__header">
            <div class="duplicate-ai-panel__heading">
              <div class="duplicate-ai-panel__mark" aria-hidden="true">AI</div>
              <div>
                <div class="duplicate-ai-panel__title">AI 疑似重复检查</div>
                <div class="duplicate-ai-panel__desc">
                  先按规则检查；未命中时比较“项目 +
                  规格”的语义相似度。命中结果只会进入人工确认，不会自动覆盖。
                </div>
              </div>
            </div>
            <div class="duplicate-ai-panel__control">
              <span>{{
                loadingAiServices || embeddingSelection.status === "checking"
                  ? "检测中"
                  : duplicateAiConfig.enableSemanticDuplicateCheck
                    ? "已开启"
                    : "已关闭"
              }}</span>
              <el-switch
                v-model="duplicateAiConfig.enableSemanticDuplicateCheck"
                aria-label="启用 AI 疑似重复识别"
                :disabled="loadingAiServices || !hasAvailableEmbeddingService"
              />
            </div>
          </div>
          <div
            v-if="!duplicateAiConfig.enableSemanticDuplicateCheck"
            class="duplicate-ai-panel__inactive"
          >
            AI
            疑似重复检查当前关闭，本次导入仅使用完全相同、同项目同规格等规则判断。
            <span class="duplicate-ai-panel__runtime-status">
              {{ embeddingStatusText }}
            </span>
          </div>
          <el-form v-else label-position="top" class="duplicate-ai-form">
            <el-row :gutter="16">
              <el-col :xs="24" :md="12">
                <el-form-item label="Embedding 服务">
                  <div
                    v-if="hasAvailableEmbeddingService"
                    class="duplicate-ai-service"
                    role="status"
                    aria-live="polite"
                  >
                    <span>自动使用</span>
                    <strong>{{ embeddingSelection.name }}</strong>
                    <small v-if="embeddingServiceModel">
                      {{ embeddingServiceModel }}
                    </small>
                  </div>
                  <el-alert
                    v-else
                    type="info"
                    :closable="false"
                    :title="embeddingStatusText"
                  />
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <div class="duplicate-ai-panel__llm">
                  <div class="llm-toggle">
                    <div>
                      <strong>LLM 二次复核</strong>
                      <span>对语义候选进一步判断是否为同一条规格</span>
                    </div>
                    <div class="llm-toggle__control">
                      <span>{{
                        loadingAiServices || llmSelection.status === "checking"
                          ? "检测中"
                          : duplicateAiConfig.enableLlmDuplicateReview
                            ? "已开启"
                            : "未开启"
                      }}</span>
                      <el-switch
                        v-model="duplicateAiConfig.enableLlmDuplicateReview"
                        aria-label="启用 LLM 二次复核"
                        :disabled="loadingAiServices || !hasAvailableLlmService"
                      />
                    </div>
                  </div>
                </div>
              </el-col>
              <el-col
                v-if="duplicateAiConfig.enableLlmDuplicateReview"
                :xs="24"
                :md="12"
              >
                <el-form-item label="LLM 服务">
                  <div
                    v-if="hasAvailableLlmService"
                    class="duplicate-ai-service"
                    role="status"
                    aria-live="polite"
                  >
                    <span>自动使用</span>
                    <strong>{{ llmSelection.name }}</strong>
                    <small v-if="llmServiceModel">{{ llmServiceModel }}</small>
                  </div>
                  <el-alert
                    v-else
                    type="info"
                    :closable="false"
                    :title="llmStatusText"
                  />
                </el-form-item>
              </el-col>
            </el-row>

            <div class="duplicate-ai-summary">
              <div>
                <span>当前策略</span>
                <strong>{{ duplicateCheckSummary }}</strong>
              </div>
              <div class="duplicate-ai-summary__services">
                <span>
                  Embedding：{{ embeddingSelection.model || "运行时自动选择" }}
                </span>
                <span v-if="duplicateAiConfig.enableLlmDuplicateReview">
                  LLM：{{ llmSelection.model || llmStatusText }}
                </span>
              </div>
            </div>

            <button
              type="button"
              class="duplicate-ai-advanced-toggle"
              :aria-expanded="showDuplicateAdvanced"
              aria-controls="duplicate-ai-advanced-options"
              @click="showDuplicateAdvanced = !showDuplicateAdvanced"
            >
              <span>
                <strong>高级参数</strong>
                <small>候选数量、相似度门槛与置信标签</small>
              </span>
              <el-icon :class="{ rotated: showDuplicateAdvanced }">
                <ArrowRight />
              </el-icon>
            </button>

            <el-collapse-transition>
              <div
                v-show="showDuplicateAdvanced"
                id="duplicate-ai-advanced-options"
                class="duplicate-ai-advanced"
              >
                <el-row :gutter="16">
                  <el-col :xs="24" :md="12">
                    <el-form-item label="每条最多候选数">
                      <el-input-number
                        v-model="duplicateAiConfig.semanticTopK"
                        :min="1"
                        :max="10"
                        style="width: 100%"
                      />
                      <div class="duplicate-ai-field-tip">
                        规则未命中后，每条数据最多保留多少个语义候选。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col :xs="24" :md="12">
                    <el-form-item label="最低相似度">
                      <el-slider
                        v-model="duplicateAiConfig.semanticMinScore"
                        :min="0"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        低于该分数的候选不会进入疑似重复清单。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col :xs="24" :md="12">
                    <el-form-item label="高置信标签阈值">
                      <el-slider
                        v-model="duplicateAiConfig.highConfidenceThreshold"
                        :min="0.5"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        仅控制确认弹窗中的“高置信”标签，不会自动覆盖数据。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col
                    v-if="duplicateAiConfig.enableLlmDuplicateReview"
                    :xs="24"
                    :md="12"
                  >
                    <el-form-item label="LLM 通过阈值">
                      <el-slider
                        v-model="duplicateAiConfig.llmPassScore"
                        :min="0"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        LLM 评分达到该值后，候选才会进入人工确认。
                      </div>
                    </el-form-item>
                  </el-col>
                </el-row>
              </div>
            </el-collapse-transition>
          </el-form>
        </div>
      </el-collapse-item>

      <el-collapse-item name="preview-list">
        <template #title>
          <div class="collapse-title">
            <span class="collapse-title__main">待导入清单</span>
            <span class="collapse-subtitle">
              已配置 Sheet 合计 {{ previewDataCount }} 条
              <template v-if="removedPreviewRowCount > 0">
                · 已移出 {{ removedPreviewRowCount }} 条
              </template>
            </span>
          </div>
        </template>
        <div class="import-preview-panel">
          <div class="import-preview-toolbar">
            <div class="import-preview-summary">
              <div class="preview-metric primary">
                <strong>{{ previewDataCount }}</strong>
                <span>待导入</span>
              </div>
              <div class="preview-metric">
                <strong>{{ previewLoadState.loadedRows }}</strong>
                <span>当前显示</span>
              </div>
              <div
                v-if="removedPreviewRowCount > 0"
                class="preview-metric warning"
              >
                <strong>{{ removedPreviewRowCount }}</strong>
                <span>已移出</span>
              </div>
            </div>
            <div class="import-preview-actions">
              <el-button
                size="small"
                type="danger"
                plain
                :disabled="
                  hasPendingDifferenceConfirmation ||
                  selectedImportPreviewRowsCount === 0
                "
                @click="emit('removeSelectedPreviewRows')"
              >
                移出所选（{{ selectedImportPreviewRowsCount }}）
              </el-button>
              <el-button
                size="small"
                :disabled="
                  hasPendingDifferenceConfirmation ||
                  removedPreviewRowCount === 0
                "
                @click="emit('restoreRemovedPreviewRows')"
              >
                恢复移出项
              </el-button>
            </div>
          </div>

          <div class="import-preview-note">
            <el-icon><InfoFilled /></el-icon>
            <span>
              移出仅影响本次导入，不会修改原文件。
              <template v-if="previewLoadState.hasPartialPreview">
                当前为前
                {{ previewLoadState.loadedRows }}
                条预览，导入前会自动补齐完整数据。
              </template>
            </span>
          </div>

          <div v-if="previewDataCount > 0" class="import-preview-groups">
            <div
              v-for="group in importPreviewGroups"
              :key="`import-preview-${group.tableIndex}`"
              class="import-preview-group"
            >
              <div class="import-preview-group__header">
                <span>{{ group.label }}</span>
                <span class="group-count">保留 {{ group.rows.length }} 条</span>
              </div>
              <el-table
                :data="group.rows"
                border
                size="small"
                row-key="key"
                reserve-selection
                @selection-change="
                  rows =>
                    emit('importPreviewSelectionChange', group.tableIndex, rows)
                "
              >
                <el-table-column type="selection" width="48" />
                <el-table-column
                  prop="displayRowNumber"
                  label="行号"
                  width="80"
                />
                <el-table-column
                  prop="project"
                  label="项目"
                  min-width="140"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.project || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="specification"
                  label="规格"
                  min-width="260"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.specification || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="acceptance"
                  label="验收"
                  min-width="160"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.acceptance || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="remark"
                  label="备注"
                  min-width="160"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.remark || "-" }}
                  </template>
                </el-table-column>
                <el-table-column label="操作" width="100" fixed="right">
                  <template #default="{ row }">
                    <el-button
                      type="danger"
                      link
                      :disabled="hasPendingDifferenceConfirmation"
                      @click="emit('removeSinglePreviewRow', row)"
                    >
                      移出
                    </el-button>
                  </template>
                </el-table-column>
              </el-table>
            </div>
          </div>
          <el-empty
            v-else
            description="当前没有待导入数据，可恢复已删除数据或返回上一步调整配置。"
          />
        </div>
      </el-collapse-item>
    </el-collapse>
  </div>
</template>

<style scoped>
.import-result {
  display: flex;
  flex-direction: column;
  width: min(100%, 1600px);
  height: 100%;
  min-height: 0;
  padding: 16px 12px;
  margin: 0 auto;
  overflow: hidden;
}

.result-overview {
  --result-accent: var(--app-primary);
  --result-soft: var(--app-info-bg);

  position: relative;
  display: grid;
  grid-template-columns: minmax(320px, 1.2fr) minmax(360px, 0.9fr) auto;
  gap: 28px;
  align-items: center;
  min-height: 116px;
  padding: 16px 24px;
  overflow: hidden;
  background:
    linear-gradient(115deg, var(--result-soft) 0%, transparent 46%),
    var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 14px;
  box-shadow: 0 12px 32px rgb(15 46 82 / 6%);
}

.result-overview::before {
  position: absolute;
  inset: 0 auto 0 0;
  width: 5px;
  content: "";
  background: var(--result-accent);
}

.result-overview--success {
  --result-accent: var(--el-color-success);
  --result-soft: var(--el-color-success-light-9);
}

.result-overview--warning {
  --result-accent: var(--el-color-warning);
  --result-soft: var(--el-color-warning-light-9);
}

.result-overview__main,
.result-overview__action,
.result-metrics {
  display: flex;
  align-items: center;
}

.result-overview__main {
  gap: 16px;
  min-width: 0;
}

.result-overview__icon {
  display: grid;
  flex: 0 0 54px;
  place-items: center;
  width: 54px;
  height: 54px;
  color: var(--result-accent);
  background: color-mix(in srgb, var(--result-accent) 10%, white);
  border: 1px solid color-mix(in srgb, var(--result-accent) 28%, transparent);
  border-radius: 14px;
}

.result-overview__icon :deep(.el-icon) {
  font-size: 28px;
}

.result-overview__copy {
  min-width: 0;
}

.result-overview__eyebrow,
.result-detail__kicker {
  font-size: 11px;
  font-weight: 700;
  color: var(--result-accent);
  letter-spacing: 0.08em;
}

.result-overview h2 {
  margin: 5px 0 4px;
  font-size: clamp(20px, 2vw, 27px);
  font-weight: 720;
  line-height: 1.2;
  color: var(--app-text-primary);
  letter-spacing: -0.02em;
}

.result-detail__header p {
  margin: 0;
  font-size: 13px;
  line-height: 1.65;
  color: var(--app-text-secondary);
}

.result-metrics {
  justify-content: center;
  min-width: 0;
  padding: 8px 0;
  background: color-mix(in srgb, var(--app-bg-card) 82%, transparent);
  border: 1px solid var(--app-border);
  border-radius: 10px;
}

.result-metric {
  display: grid;
  grid-template-columns: auto auto;
  gap: 1px 4px;
  align-items: baseline;
  min-width: 104px;
  padding: 2px 18px;
  border-right: 1px solid var(--app-border);
}

.result-metric:last-child {
  border-right: 0;
}

.result-metric span {
  grid-column: 1 / -1;
  font-size: 11px;
  color: var(--app-text-secondary);
}

.result-metric strong {
  font-size: 25px;
  font-weight: 720;
  font-variant-numeric: tabular-nums;
  line-height: 1.2;
  color: var(--app-text-primary);
}

.result-metric small {
  font-size: 11px;
  color: var(--app-text-secondary);
}

.result-metric--success strong {
  color: var(--el-color-success-dark-2);
}

.result-metric--skipped strong {
  color: var(--el-color-warning-dark-2);
}

.result-metric--failed strong {
  color: var(--el-color-danger);
}

.result-overview__action {
  flex-direction: column;
  align-items: stretch;
  min-width: 122px;
  text-align: center;
}

.result-detail {
  margin-top: 18px;
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 12px;
}

.result-detail--skipped {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.result-detail__header {
  display: flex;
  gap: 24px;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px;
  background: var(--el-fill-color-extra-light);
  border-bottom: 1px solid var(--app-border);
}

.result-detail__header h3 {
  margin: 2px 0;
  font-size: 17px;
  font-weight: 680;
  color: var(--app-text-primary);
}

.result-detail__header > strong {
  flex: 0 0 auto;
  padding: 5px 10px;
  font-size: 12px;
  color: var(--app-text-primary);
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 999px;
}

.result-detail--danger .result-detail__kicker {
  color: var(--el-color-danger);
}

.result-detail--skipped .result-detail__kicker {
  color: var(--el-color-warning-dark-2);
}

.result-detail > :deep(.el-alert),
.result-detail__table {
  margin: 14px;
}

.result-detail :deep(.el-table) {
  --el-table-border-color: var(--app-border);
  --el-table-header-bg-color: var(--el-fill-color-extra-light);

  width: calc(100% - 28px);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.result-detail :deep(.el-table__inner-wrapper::before) {
  display: none;
}

.skipped-tabs {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding: 0 14px 14px;
}

.skipped-tabs :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.skipped-tabs :deep(.el-tab-pane) {
  height: 100%;
}

.skipped-tabs :deep(.el-tabs__header) {
  margin: 0 0 12px;
}

.skipped-tabs--single {
  padding-top: 14px;
}

.skipped-tabs--single :deep(.el-tabs__header) {
  display: none;
}

.skipped-tabs :deep(.el-tabs__nav-wrap::after) {
  height: 1px;
  background: var(--app-border);
}

.skipped-tabs :deep(.el-tabs__item) {
  height: 48px;
  padding: 0 18px;
  font-weight: 600;
  color: var(--app-text-secondary);
}

.skipped-tabs :deep(.el-tabs__item.is-active) {
  color: var(--app-primary);
}

.skipped-tabs :deep(.el-tabs__active-bar) {
  height: 3px;
  border-radius: 3px 3px 0 0;
}

.skipped-tab-label {
  display: flex;
  gap: 7px;
  align-items: center;
  font-size: 13px;
}

.skipped-tab-label strong {
  min-width: 24px;
  padding: 2px 7px;
  font-size: 10px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  line-height: 16px;
  color: inherit;
  text-align: center;
  background: var(--el-fill-color);
  border-radius: 999px;
}

.skipped-rows-table {
  width: 100% !important;
  height: 100%;
  margin: 0;
}

.skipped-rows-table :deep(.el-table__header .cell) {
  overflow: hidden;
  text-overflow: ellipsis;
  word-break: keep-all;
  white-space: nowrap;
}

.skipped-rows-table :deep(.skipped-region-separator td) {
  padding: 0 !important;
  background: var(--el-color-primary-light-9) !important;
  border-top: 1px solid var(--el-color-primary-light-7);
  border-bottom: 1px solid var(--el-color-primary-light-7);
}

.skipped-region-separator__label {
  display: flex;
  gap: 8px;
  align-items: center;
  height: 32px;
  padding: 0 14px;
  font-size: 12px;
  font-weight: 700;
  color: var(--app-primary);
}

.skipped-region-separator__label::before {
  width: 3px;
  height: 14px;
  content: "";
  background: var(--app-primary);
  border-radius: 999px;
}

.import-summary-bar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px 16px;
  align-items: center;
  padding: 12px;
  margin-bottom: 12px;
  background: var(--app-info-bg);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-summary-bar__meta,
.import-summary-bar__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 14px;
  align-items: center;
  min-width: 0;
  font-size: 13px;
  color: var(--app-text-secondary);
}

.import-summary-bar__actions {
  justify-content: flex-end;
  color: var(--app-text-primary);
}

.import-summary-bar__file {
  min-width: 0;
  max-width: min(340px, 42vw);
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 600;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.import-summary-bar__count {
  font-weight: 600;
  color: var(--app-primary);
}

.import-summary-bar__progress {
  display: flex;
  grid-column: 1 / -1;
  gap: 8px;
  align-items: baseline;
  padding-top: 8px;
  font-size: 12px;
  color: var(--app-text-secondary);
  border-top: 1px dashed var(--app-border);
}

.import-summary-bar__progress strong {
  color: var(--app-primary);
}

.confirm-panel-collapse {
  margin-top: 10px;
  border-top: 1px solid var(--app-border);
  border-bottom: 1px solid var(--app-border);
}

.confirm-panel-collapse :deep(.el-collapse-item__header) {
  height: 48px;
  padding: 0 10px;
  font-weight: 600;
}

.confirm-panel-collapse :deep(.el-collapse-item__content) {
  padding: 0 10px 16px;
}

.collapse-title {
  display: flex;
  gap: 10px;
  align-items: center;
  min-width: 0;
}

.collapse-title__main {
  font-size: 14px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.collapse-subtitle {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  font-weight: 400;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.collapse-status {
  padding: 2px 8px;
  font-size: 11px;
  font-weight: 500;
  line-height: 18px;
  color: var(--app-text-secondary);
  background: var(--el-fill-color);
  border: 1px solid var(--app-border);
  border-radius: 999px;
}

.collapse-status.active {
  color: var(--el-color-success-dark-2);
  background: var(--el-color-success-light-9);
  border-color: var(--el-color-success-light-5);
}

.duplicate-ai-panel {
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 10px;
}

.duplicate-ai-panel__header,
.duplicate-ai-panel__heading,
.duplicate-ai-panel__control,
.llm-toggle,
.llm-toggle__control {
  display: flex;
  align-items: center;
}

.duplicate-ai-panel__header {
  gap: 18px;
  justify-content: space-between;
  padding: 14px 16px;
  background: linear-gradient(
    110deg,
    var(--app-info-bg) 0%,
    var(--app-bg-card) 72%
  );
  border-bottom: 1px solid var(--app-border);
}

.duplicate-ai-panel__heading {
  gap: 12px;
  min-width: 0;
}

.duplicate-ai-panel__mark {
  display: grid;
  flex: 0 0 36px;
  place-items: center;
  width: 36px;
  height: 36px;
  font-size: 12px;
  font-weight: 750;
  color: var(--app-primary);
  letter-spacing: 0.06em;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 9px;
}

.duplicate-ai-panel__title {
  font-size: 14px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.duplicate-ai-panel__desc {
  margin-top: 3px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.duplicate-ai-panel__control,
.llm-toggle__control {
  flex: 0 0 auto;
  gap: 8px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-panel__inactive {
  padding: 13px 16px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-secondary);
  background: var(--el-fill-color-extra-light);
}

.duplicate-ai-panel__runtime-status {
  display: block;
  margin-top: 4px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.duplicate-ai-service {
  display: flex;
  gap: 8px;
  align-items: center;
  width: 100%;
  min-height: 40px;
  padding: 8px 11px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.duplicate-ai-service span,
.duplicate-ai-service small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-service strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.duplicate-ai-form {
  padding: 16px 16px 2px;
}

.duplicate-ai-form :deep(.el-form-item) {
  margin-bottom: 16px;
}

.duplicate-ai-form :deep(.el-form-item__label) {
  height: auto;
  padding: 0 0 7px;
  font-size: 12px;
  font-weight: 600;
  line-height: 1.4;
  color: var(--app-text-secondary);
}

.duplicate-ai-form :deep(.el-slider__runway.show-input) {
  margin-right: 16px;
}

.duplicate-ai-panel__llm {
  padding: 11px 12px;
  margin-bottom: 16px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.llm-toggle {
  gap: 16px;
  justify-content: space-between;
}

.llm-toggle > div:first-child {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.llm-toggle strong {
  font-size: 13px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.llm-toggle > div:first-child span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-summary {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
  padding: 11px 13px;
  margin-bottom: 10px;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 8px;
}

.duplicate-ai-summary > div:first-child,
.duplicate-ai-summary__services {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.duplicate-ai-summary span,
.duplicate-ai-summary strong {
  font-size: 12px;
  line-height: 1.5;
}

.duplicate-ai-summary span {
  color: var(--app-text-secondary);
}

.duplicate-ai-summary strong {
  font-weight: 650;
  color: var(--app-text-primary);
}

.duplicate-ai-summary__services {
  min-width: 0;
  text-align: right;
}

.duplicate-ai-summary__services span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.duplicate-ai-advanced-toggle {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 10px 12px;
  margin: 0 0 12px;
  font: inherit;
  color: var(--app-text-primary);
  text-align: left;
  cursor: pointer;
  background: transparent;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  transition:
    color 180ms ease,
    background-color 180ms ease,
    border-color 180ms ease;
}

.duplicate-ai-advanced-toggle:hover {
  color: var(--app-primary);
  background: var(--el-fill-color-extra-light);
  border-color: var(--el-color-primary-light-5);
}

.duplicate-ai-advanced-toggle:focus-visible {
  outline: 2px solid var(--app-primary);
  outline-offset: 2px;
}

.duplicate-ai-advanced-toggle > span {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.duplicate-ai-advanced-toggle strong {
  font-size: 13px;
  font-weight: 650;
}

.duplicate-ai-advanced-toggle small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-advanced-toggle .el-icon {
  flex: 0 0 auto;
  transition: transform 180ms ease;
}

.duplicate-ai-advanced-toggle .el-icon.rotated {
  transform: rotate(90deg);
}

.duplicate-ai-advanced {
  padding: 14px 14px 0;
  margin-bottom: 14px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.duplicate-ai-field-tip {
  width: 100%;
  margin-top: 5px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.import-preview-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.import-preview-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  align-items: center;
  justify-content: space-between;
  padding: 11px 14px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-preview-summary,
.import-preview-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
}

.import-preview-summary {
  gap: 0;
}

.preview-metric {
  display: flex;
  gap: 6px;
  align-items: baseline;
  padding: 0 14px;
  color: var(--app-text-secondary);
  border-left: 1px solid var(--app-border);
}

.preview-metric:first-child {
  padding-left: 0;
  border-left: 0;
}

.preview-metric strong {
  font-size: 18px;
  font-weight: 700;
  color: var(--app-text-primary);
  letter-spacing: -0.03em;
}

.preview-metric span {
  font-size: 12px;
}

.preview-metric.primary strong {
  color: var(--app-primary);
}

.preview-metric.warning strong {
  color: var(--app-warning);
}

.import-preview-actions {
  gap: 8px;
}

.import-preview-note {
  display: flex;
  gap: 7px;
  align-items: flex-start;
  padding: 0 2px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-secondary);
}

.import-preview-note .el-icon {
  flex: 0 0 auto;
  margin-top: 3px;
  color: var(--el-color-info);
}

.import-preview-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.import-preview-group {
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-preview-group__header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 9px 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-primary);
  background: var(--el-fill-color-extra-light);
  border-bottom: 1px solid var(--app-border);
}

.group-count {
  font-size: 12px;
  font-weight: 500;
  color: var(--app-text-secondary);
}

.import-preview-group :deep(.el-table) {
  --el-table-border-color: var(--app-border);

  border: 0;
}

.import-preview-group :deep(.el-table__inner-wrapper::before) {
  display: none;
}

@media (width <= 900px) {
  .result-overview {
    grid-template-columns: 1fr;
    gap: 18px;
  }

  .result-metrics {
    justify-content: flex-start;
    width: 100%;
  }

  .result-metric {
    flex: 1;
  }

  .result-overview__action {
    flex-direction: row;
    align-items: center;
    text-align: left;
  }

  .import-summary-bar {
    grid-template-columns: 1fr;
  }

  .import-summary-bar__actions {
    justify-content: space-between;
  }

  .import-summary-bar__actions :deep(.el-button) {
    flex: 1;
    min-height: 40px;
  }

  .duplicate-ai-panel__header,
  .llm-toggle {
    align-items: flex-start;
  }

  .import-preview-toolbar {
    align-items: stretch;
  }

  .import-preview-summary,
  .import-preview-actions {
    width: 100%;
  }

  .import-preview-actions :deep(.el-button) {
    flex: 1;
    min-height: 40px;
    margin-left: 0;
  }
}

@media (width <= 560px) {
  .import-result {
    padding: 14px 10px 28px;
  }

  .result-overview {
    padding: 20px 16px;
  }

  .result-overview__main {
    align-items: flex-start;
  }

  .result-overview__icon {
    flex-basis: 44px;
    width: 44px;
    height: 44px;
  }

  .result-metrics {
    align-items: stretch;
  }

  .result-metric {
    min-width: 0;
    padding: 2px 10px;
  }

  .result-metric strong {
    font-size: 21px;
  }

  .result-overview__action,
  .result-detail__header {
    align-items: stretch;
  }

  .result-overview__action {
    flex-direction: column;
  }

  .result-detail__header {
    flex-direction: column;
    gap: 10px;
  }

  .result-detail__header > strong {
    align-self: flex-start;
  }

  .skipped-tabs {
    padding-inline: 10px;
  }

  .skipped-tabs :deep(.el-tabs__item) {
    padding: 0 12px;
  }

  .collapse-subtitle {
    display: none;
  }

  .duplicate-ai-panel__header {
    flex-direction: column;
  }

  .duplicate-ai-panel__control {
    justify-content: space-between;
    width: 100%;
    padding-left: 48px;
  }

  .duplicate-ai-summary {
    flex-direction: column;
  }

  .duplicate-ai-summary__services {
    width: 100%;
    text-align: left;
  }

  .preview-metric {
    padding: 0 10px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .duplicate-ai-advanced-toggle,
  .duplicate-ai-advanced-toggle .el-icon {
    transition: none;
  }
}
</style>
