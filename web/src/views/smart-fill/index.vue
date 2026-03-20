<script setup lang="ts">
import { ref, computed, onBeforeUnmount, watch } from "vue";
import { ElMessage } from "element-plus";
import { Loading } from "@element-plus/icons-vue";
import FileUpload from "@/views/data-import/components/FileUpload.vue";
import MatchConfig from "./components/MatchConfig.vue";
import BatchTableConfig from "./components/BatchTableConfig.vue";
import BatchPreviewTabs from "./components/BatchPreviewTabs.vue";
import ScoreDetailDialog from "./components/ScoreDetailDialog.vue";
import type { BatchTableConfigItem } from "./components/BatchTableConfig.vue";
import {
  batchPreviewMatch,
  batchExecuteFill,
  downloadFillResult,
  type MatchPreviewItem,
  type MatchConfig as MatchConfigType,
  type MatchResult,
  type BatchTablePreviewResult,
  defaultMatchConfig
} from "@/api/matching";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import { getFileTables } from "@/api/document";
import {
  getEffectiveColumnMappingRules,
  ColumnMappingTargetField,
  ColumnMappingMatchMode,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import { getToken, formatToken, hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({ name: "SmartFill" });

// 步骤
const currentStep = ref(0);
const steps = [
  { title: "上传文件", description: "选择目标文档" },
  { title: "选择表格", description: "选择要填充的表格并配置列索引" },
  { title: "配置匹配", description: "设置匹配参数" },
  { title: "预览确认", description: "确认匹配结果" }
];

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);
const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);
const canUploadSourceFile = computed(() => hasPerms("btn:document:upload"));
const canPreviewMatching = computed(() => hasPerms("btn:matching:preview-batch"));
const canLlmStream = computed(() => hasPerms("btn:matching:llm-stream"));
const canExecuteFill = computed(() => hasPerms("btn:matching-fill:execute-batch"));
const canDownloadFillResult = computed(() => hasPerms("btn:matching:download"));
const canExecuteAction = computed(
  () => canExecuteFill.value && canDownloadFillResult.value
);

// 所有表格信息
const allTables = ref<TableInfo[]>([]);
// 批量表格配置
const batchTableConfigs = ref<BatchTableConfigItem[]>([]);

// 匹配配置
const matchConfig = ref<MatchConfigType>({ ...defaultMatchConfig });
const matchConfigRef = ref<InstanceType<typeof MatchConfig> | null>(null);

// 批量预览结果
const batchPreviewResults = ref<BatchTablePreviewResult[]>([]);
const batchPreviewTabsRef = ref<InstanceType<typeof BatchPreviewTabs> | null>(
  null
);
const loading = ref(false);
const llmStreaming = ref(false);
const llmStreamController = ref<AbortController | null>(null);
let previewRequestVersion = 0;

const invalidatePendingPreview = () => {
  previewRequestVersion++;
  loading.value = false;
};

// 页面卸载时清理 SSE 连接，防止连接泄漏
onBeforeUnmount(() => {
  stopLlmStream();
});

watch(currentStep, (step) => {
  if (step !== 3) {
    invalidatePendingPreview();
    stopLlmStream();
  }
});

// 详情弹窗
const detailVisible = ref(false);
const detailItem = ref<MatchPreviewItem | null>(null);

// 执行状态
const executing = ref(false);
const taskId = ref<string | null>(null);

// 选中的表格数量
const selectedTableCount = computed(
  () => batchTableConfigs.value.filter((t) => t.selected).length
);

// 所有预览项（扁平化）
const allPreviewItems = computed(() =>
  batchPreviewResults.value.flatMap((t) => t.items)
);

// 计算属性
const canGoNext = computed(() => {
  switch (currentStep.value) {
    case 0:
      return uploadedFile.value !== null;
    case 1:
      return selectedTableCount.value > 0;
    case 2:
      return true;
    case 3:
      return allPreviewItems.value.length > 0;
    default:
      return false;
  }
});

const buildExcelTableConfig = (
  table: TableInfo,
  selected: boolean
): BatchTableConfigItem => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);

  return {
    tableIndex: table.index,
    projectColumnIndex: clampColumnIndex(0),
    specificationColumnIndex: clampColumnIndex(1),
    acceptanceColumnIndex: clampColumnIndex(2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: usedStartRow,
    headerRowCount: 1,
    dataStartRow: usedStartRow + 1,
    filterEmptySourceRows: true,
    selected,
    tableInfo: table
  };
};

// 文件上传完成
const handleFileUploaded = async (file: FileUploadResponse) => {
  invalidatePendingPreview();
  stopLlmStream();
  uploadedFile.value = file;
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  taskId.value = null;

  // Excel 改为手工配置，不再做自动识别；Word 仍按列映射规则自动匹配
  let tables: TableInfo[] = [];
  let rules: ColumnMappingRule[] = [];
  try {
    const tablesRes = await getFileTables(file.fileId);
    if (tablesRes.code === 0) tables = tablesRes.data;

    if (file.fileType !== 1) {
      const rulesRes = await getEffectiveColumnMappingRules();
      if (rulesRes.code === 0) rules = rulesRes.data;
    }
  } catch {
    ElMessage.warning("获取表格列表失败");
    return;
  }

  allTables.value = tables;

  if (file.fileType === 1) {
    batchTableConfigs.value = tables.map((t) =>
      buildExcelTableConfig(t, tables.length === 1)
    );
    return;
  }

  // Word 文档仍按列映射规则自动匹配
  batchTableConfigs.value = tables.map((t) => ({
    tableIndex: t.index,
    ...autoMatchColumns(t.headers, rules),
    headerRowStart: Math.max(1, t.usedRangeStartRow ?? 1),
    headerRowCount: 1,
    dataStartRow: Math.max(1, t.usedRangeStartRow ?? 1) + 1,
    filterEmptySourceRows: true,
    selected: tables.length === 1,
    tableInfo: t
  }));
};

/**
 * 根据列映射规则自动匹配表头，返回各列的索引。
 * 匹配逻辑：遍历每个字段的规则（按优先级排序），逐个表头检测是否命中。
 * 未命中则回退到硬编码默认值 0/1/2/3。
 */
const autoMatchColumns = (
  headers: string[],
  rules: ColumnMappingRule[]
) => {
  const fieldMap: Record<number, keyof ReturnType<typeof defaults>> = {
    [ColumnMappingTargetField.Project]: "projectColumnIndex",
    [ColumnMappingTargetField.Specification]: "specificationColumnIndex",
    [ColumnMappingTargetField.Acceptance]: "acceptanceColumnIndex",
    [ColumnMappingTargetField.Remark]: "remarkColumnIndex"
  };

  const defaults = () => ({
    projectColumnIndex: 0,
    specificationColumnIndex: 1,
    acceptanceColumnIndex: 2,
    remarkColumnIndex: 3 as number | undefined
  });

  const result = defaults();
  if (headers.length === 0 || rules.length === 0) return result;

  // 按 targetField 分组，组内按 priority 升序（越小越优先）
  const rulesByField = new Map<number, ColumnMappingRule[]>();
  for (const r of rules) {
    if (!rulesByField.has(r.targetField)) rulesByField.set(r.targetField, []);
    rulesByField.get(r.targetField)!.push(r);
  }
  for (const arr of rulesByField.values()) {
    arr.sort((a, b) => a.priority - b.priority);
  }

  const matched = new Set<number>(); // 已被占用的列索引

  for (const [targetField, fieldKey] of Object.entries(fieldMap)) {
    const fieldRules = rulesByField.get(Number(targetField));
    if (!fieldRules) continue;

    let found = false;
    for (const rule of fieldRules) {
      for (let i = 0; i < headers.length; i++) {
        if (matched.has(i)) continue;
        if (matchHeader(headers[i], rule)) {
          (result as any)[fieldKey] = i;
          matched.add(i);
          found = true;
          break;
        }
      }
      if (found) break;
    }
  }

  return result;
};

/** 判断表头是否命中映射规则 */
const matchHeader = (header: string, rule: ColumnMappingRule): boolean => {
  if (!header) return false;
  const h = header.toLowerCase();
  const p = rule.pattern.toLowerCase();
  switch (rule.matchMode) {
    case ColumnMappingMatchMode.Contains:
      return h.includes(p);
    case ColumnMappingMatchMode.Equals:
      return h === p;
    case ColumnMappingMatchMode.Regex:
      try {
        return new RegExp(rule.pattern, "i").test(header);
      } catch {
        return false;
      }
    default:
      return false;
  }
};

// 执行批量匹配预览
const doPreview = async () => {
  if (!ensurePermission("btn:matching:preview-batch", "权限不足，无法执行匹配预览")) {
    return;
  }
  if (!uploadedFile.value) return;

  const requestVersion = ++previewRequestVersion;
  const fileId = uploadedFile.value.fileId;
  stopLlmStream();

  const selectedConfigs = batchTableConfigs.value.filter((t) => t.selected);
  if (selectedConfigs.length === 0) {
    ElMessage.warning("请至少选择一个表格");
    return;
  }

  loading.value = true;
  try {
    const scope = matchConfigRef.value?.getScope() ?? {
      customerId: undefined,
      processId: undefined,
      machineModelId: undefined
    };

    const res = await batchPreviewMatch({
      fileId: uploadedFile.value.fileId,
      tables: selectedConfigs.map((t) => ({
        tableIndex: t.tableIndex,
        projectColumnIndex: t.projectColumnIndex,
        specificationColumnIndex: t.specificationColumnIndex,
        acceptanceColumnIndex: t.acceptanceColumnIndex,
        remarkColumnIndex: t.remarkColumnIndex,
        headerRowStart: t.headerRowStart,
        headerRowCount: t.headerRowCount,
        dataStartRow: t.dataStartRow,
        filterEmptySourceRows: t.filterEmptySourceRows
      })),
      customerId: scope.customerId,
      processId: scope.processId,
      machineModelId: scope.machineModelId,
      config: matchConfig.value
    });

    if (res.code === 0) {
      if (
        requestVersion !== previewRequestVersion ||
        currentStep.value !== 3 ||
        uploadedFile.value?.fileId !== fileId
      ) {
        return;
      }

      batchPreviewResults.value = res.data.tables;
      if (res.data.totalMatched === 0) {
        ElMessage.warning("未找到可匹配的数据");
      }
      startLlmStream();
    } else {
      if (requestVersion !== previewRequestVersion) return;
      ElMessage.error(res.message || "匹配预览失败");
    }
  } catch {
    if (requestVersion !== previewRequestVersion) return;
    ElMessage.error("匹配预览失败");
  } finally {
    if (requestVersion === previewRequestVersion) {
      loading.value = false;
    }
  }
};

const stopLlmStream = () => {
  const controller = llmStreamController.value;
  controller?.abort();
  if (llmStreamController.value === controller) {
    llmStreamController.value = null;
  }
  llmStreaming.value = false;
};

const getHighConfidenceThreshold = () =>
  Math.min(Math.max(matchConfig.value.highConfidenceThreshold ?? 0.95, 0.5), 1);

const startLlmStream = async () => {
  if (!canLlmStream.value) {
    return;
  }
  stopLlmStream();

  if (!allPreviewItems.value.length) return;
  if (!matchConfig.value.useLlmReview) return;

  const llmItems = batchPreviewResults.value.flatMap((tableResult) =>
    tableResult.items
      .filter(item => shouldStreamReview(item))
      .map((item) => ({
        tableIndex: tableResult.tableIndex,
        rowIndex: item.rowIndex,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        bestMatchSpecId: item.bestMatch?.specId,
        bestMatchScore: item.bestMatch?.score,
        scoreDetails: item.bestMatch?.scoreDetails
      }))
  );

  if (!llmItems.length) {
    llmStreaming.value = false;
    return;
  }

  const controller = new AbortController();
  llmStreamController.value = controller;
  llmStreaming.value = true;

  const payload = {
    items: llmItems,
    config: matchConfig.value
  };
  const token = getToken();

  try {
    const response = await fetch("/api/matching/llm-stream", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(token?.accessToken
          ? { Authorization: formatToken(token.accessToken) }
          : {})
      },
      body: JSON.stringify(payload),
      signal: controller.signal
    });

    if (!response.ok || !response.body) {
      ElMessage.warning("LLM流式输出不可用，已降级");
      if (llmStreamController.value === controller) {
        llmStreamController.value = null;
        llmStreaming.value = false;
      }
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";

    while (true) {
      if (
        controller.signal.aborted ||
        llmStreamController.value !== controller
      ) {
        break;
      }

      const { value, done } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split("\n\n");
      buffer = parts.pop() || "";

      for (const part of parts) {
        if (
          controller.signal.aborted ||
          llmStreamController.value !== controller
        ) {
          break;
        }
        handleSseEvent(part);
      }
    }
  } catch {
    if (!controller.signal.aborted) {
      ElMessage.warning("LLM流式输出中断，已降级");
    }
  } finally {
    if (llmStreamController.value === controller) {
      llmStreamController.value = null;
      llmStreaming.value = false;
    }
  }
};

const handleSseEvent = (raw: string) => {
  const lines = raw.split("\n").filter((line) => line.trim().length > 0);
  let event = "message";
  const dataLines: string[] = [];
  for (const line of lines) {
    if (line.startsWith("event:")) {
      event = line.replace("event:", "").trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.replace("data:", "").trim());
    }
  }

  if (dataLines.length === 0) return;

  try {
    const data = JSON.parse(dataLines.join("\n"));
    applySseUpdate(event, data);
  } catch {
    // ignore malformed chunk
  }
};

const applySseUpdate = (event: string, data: any) => {
  // 在所有表格的预览项中查找匹配的行
  let row: MatchPreviewItem | undefined;
  for (const tableResult of batchPreviewResults.value) {
    if (
      data.tableIndex !== undefined &&
      data.tableIndex !== null &&
      tableResult.tableIndex !== data.tableIndex
    ) {
      continue;
    }

    row = tableResult.items.find((item) => item.rowIndex === data.rowIndex);
    if (row) break;
  }
  if (!row) return;

  switch (event) {
    case "review.start":
      row.llmReviewDraft = "";
      row.llmReviewError = undefined;
      break;
    case "review.delta":
      row.llmReviewDraft = (row.llmReviewDraft || "") + (data.chunk || "");
      break;
    case "review.done":
      if (row.bestMatch) {
        row.bestMatch.llmScore = data.score;
        row.bestMatch.llmReason = data.reason;
        row.bestMatch.llmCommentary = data.commentary;
        row.bestMatch.isLlmReviewed = true;
      }
      row.llmReviewDraft = "";
      break;
    case "review.error":
      row.llmReviewError = data.message || "LLM复核失败";
      row.llmReviewDraft = "";
      break;
    default:
      break;
  }
};

const shouldStreamReview = (item: MatchPreviewItem) => {
  const score = item.bestMatch?.score ?? 0;
  return (
    !!matchConfig.value.useLlmReview &&
    !!item.bestMatch?.specId &&
    score > 0 &&
    score < getHighConfidenceThreshold()
  );
};

// 显示详情
const handleShowDetail = (item: MatchPreviewItem) => {
  detailItem.value = item;
  detailVisible.value = true;
};

// 选择变化
const handleSelect = (
  _tableIndex: number,
  _rowIndex: number,
  _spec: MatchResult | null
) => {
  // 可用于实时更新统计
};

// 执行填充
const handleExecute = async () => {
  if (
    !ensurePermission("btn:matching-fill:execute-batch", "权限不足，无法执行智能填充")
  ) {
    return;
  }
  if (!ensurePermission("btn:matching:download", "权限不足，无法下载填充结果")) {
    return;
  }
  if (!uploadedFile.value) return;
  if (llmStreaming.value) {
    ElMessage.warning("AI 仍在处理中，请等待完成后再执行填充");
    return;
  }

  const selectedConfigs = batchTableConfigs.value.filter((t) => t.selected);
  if (selectedConfigs.length === 0) return;

  // 获取各表格的选择结果
  const allSelections = batchPreviewTabsRef.value?.getAllSelections();
  if (!allSelections || allSelections.size === 0) {
    ElMessage.warning("请至少选择一项匹配结果");
    return;
  }

  // 构建批量填充请求
  const tables = selectedConfigs
    .map((config) => {
      const selections = allSelections.get(config.tableIndex) || [];
      if (selections.length === 0) return null;
      return {
        tableIndex: config.tableIndex,
        acceptanceColumnIndex: config.acceptanceColumnIndex,
        remarkColumnIndex: config.remarkColumnIndex,
        mappings: selections.map((s) => ({
          rowIndex: s.rowIndex,
          specId: s.specId,
          matchScore: s.matchScore,
          llmReviewScore: s.llmReviewScore
        }))
      };
    })
    .filter(Boolean) as Array<{
    tableIndex: number;
    acceptanceColumnIndex: number;
    remarkColumnIndex?: number;
    mappings: Array<{
      rowIndex: number;
      specId?: number;
      matchScore?: number;
      llmReviewScore?: number;
    }>;
  }>;

  if (tables.length === 0) {
    ElMessage.warning("请至少选择一项匹配结果");
    return;
  }

  executing.value = true;
  try {
    const res = await batchExecuteFill({
      fileId: uploadedFile.value.fileId,
      highConfidenceThreshold: getHighConfidenceThreshold(),
      tables
    });

    if (res.code === 0) {
      taskId.value = res.data.taskId;
      try {
        const blob = await downloadFillResult(res.data.taskId);
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        const originalName = uploadedFile.value.fileName || "filled.docx";
        const dotIndex = originalName.lastIndexOf(".");
        const baseName =
          dotIndex > 0 ? originalName.slice(0, dotIndex) : originalName;
        const ext = dotIndex > 0 ? originalName.slice(dotIndex) : ".docx";
        a.download = `${baseName}_filled_${Date.now()}${ext}`;
        a.click();
        window.URL.revokeObjectURL(url);

        if (isExcelFile.value) {
          ElMessage.success(
            `填充完成，共填充 ${res.data.filledCount} 条，已写回并下载 Excel`
          );
        } else {
          ElMessage.success(
            `填充完成，共填充 ${res.data.filledCount} 条，已下载结果文档`
          );
        }
      } catch {
        if (isExcelFile.value) {
          ElMessage.warning("填充完成，但 Excel 下载失败，请重试下载");
        } else {
          ElMessage.warning("填充完成，但结果文件下载失败，请重试");
        }
      }
    } else {
      ElMessage.error(res.message || "填充失败");
    }
  } catch {
    ElMessage.error("填充失败");
  } finally {
    executing.value = false;
  }
};

// 步骤切换
const goNext = () => {
  if (currentStep.value === 2) {
    if (!ensurePermission("btn:matching:preview-batch", "权限不足，无法执行匹配预览")) {
      return;
    }
  }
  if (!canGoNext.value || currentStep.value >= steps.length - 1) return;
  currentStep.value++;
  if (currentStep.value === 3) {
    doPreview();
  }
};

const goPrev = () => {
  if (currentStep.value > 0) currentStep.value--;
};

// 重新开始
const handleRestart = () => {
  invalidatePendingPreview();
  stopLlmStream();
  currentStep.value = 0;
  uploadedFile.value = null;
  allTables.value = [];
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  taskId.value = null;
  matchConfig.value = { ...defaultMatchConfig };
};
</script>

<template>
  <div class="page smart-fill">
    <div class="page-header">
      <div>
        <div class="page-title">智能填充</div>
        <div class="page-subtitle">匹配验收规格并批量回写文档</div>
      </div>
    </div>
    <!-- 步骤条 -->
    <el-card class="mb-4">
      <el-steps :active="currentStep" finish-status="success">
        <el-step
          v-for="(step, index) in steps"
          :key="index"
          :title="step.title"
          :description="step.description"
        />
      </el-steps>
    </el-card>

    <!-- 步骤内容 -->
    <el-card class="step-content">
      <!-- 步骤1: 上传文件 -->
      <div v-show="currentStep === 0" class="step-panel">
        <h3 class="step-title">上传目标文档</h3>
        <p class="step-desc">请选择需要填充验收标准的 Word/Excel 文档</p>
        <FileUpload
          v-if="canUploadSourceFile"
          v-model="uploadedFile"
          @uploaded="handleFileUploaded"
        />
        <el-alert
          v-else
          type="warning"
          :closable="false"
          show-icon
          title="当前账号没有文档上传权限"
        />
      </div>

      <!-- 步骤2: 选择表格 + 配置列索引 -->
      <div v-show="currentStep === 1" class="step-panel">
        <h3 class="step-title">选择表格并配置列索引</h3>
        <p class="step-desc">
          勾选需要填充的表格，并为每个表格指定各列索引（从0开始）
          <span v-if="isExcelFile">；Excel 请按实际内容手工调整行配置并刷新表头</span>
        </p>

        <BatchTableConfig
          v-if="batchTableConfigs.length > 0"
          v-model="batchTableConfigs"
          :file-id="uploadedFile?.fileId"
          :is-excel="isExcelFile"
          :tables="allTables"
        />

        <el-empty
          v-else-if="uploadedFile"
          description="未检测到表格，请确认文档格式"
        />
      </div>

      <!-- 步骤3: 配置匹配 -->
      <div v-show="currentStep === 2" class="step-panel">
        <h3 class="step-title">配置匹配参数</h3>
        <p class="step-desc">设置匹配范围和算法参数</p>
        <MatchConfig
          ref="matchConfigRef"
          v-model="matchConfig"
          :allow-llm="canLlmStream"
        />
      </div>

      <!-- 步骤4: 预览确认 -->
      <div v-show="currentStep === 3" class="step-panel">
        <h3 class="step-title">匹配预览</h3>
        <p class="step-desc">确认匹配结果，可手动调整选择</p>

        <!-- LLM 流式处理提示 -->
        <el-alert
          v-if="llmStreaming"
          title="AI 正在处理中..."
          description="LLM 正在逐行复核中，请等待完成后再执行填充"
          type="info"
          show-icon
          :closable="false"
          class="llm-streaming-alert"
        />

        <!-- 匹配进行中遮罩 -->
        <div v-if="loading" class="loading-overlay">
          <el-icon class="is-loading" :size="32"><Loading /></el-icon>
          <p class="loading-text">正在匹配中，请耐心等待...</p>
          <p class="loading-hint">
            正在对 {{ selectedTableCount }} 个表格执行 Embedding
            向量匹配，视数据量可能需要数十秒
          </p>
        </div>

        <BatchPreviewTabs
          ref="batchPreviewTabsRef"
          :results="batchPreviewResults"
          :loading="loading"
          :high-confidence-threshold="getHighConfidenceThreshold()"
          :llm-streaming="llmStreaming"
          @select="handleSelect"
          @show-detail="handleShowDetail"
        />

        <!-- 填充完成提示（紧凑内联） -->
        <el-alert
          v-if="taskId"
          :title="
            isExcelFile
              ? '填充完成 — 内容已回写到当前上传文档'
              : '填充完成 — 已生成并下载结果文档（源文档保持不变）'
          "
          type="success"
          show-icon
          closable
          class="fill-done-alert"
        />

        <!-- 操作按钮 -->
        <div v-if="allPreviewItems.length > 0" class="action-bar">
          <el-button v-if="canPreviewMatching" @click="doPreview" :loading="loading">
            重新匹配
          </el-button>
          <el-button
            v-if="canExecuteAction"
            type="primary"
            :loading="executing"
            :disabled="!!taskId || llmStreaming || loading"
            @click="handleExecute"
          >
            执行填充
          </el-button>
          <el-button v-if="taskId && canUploadSourceFile" @click="handleRestart">
            继续填充其他文档
          </el-button>
        </div>
      </div>

      <!-- 步骤按钮 -->
      <div class="step-actions">
        <el-button v-if="currentStep > 0 && !taskId" @click="goPrev">
          上一步
        </el-button>
        <el-button
          v-if="currentStep < steps.length - 1"
          type="primary"
          :disabled="!canGoNext || (currentStep === 2 && !canPreviewMatching)"
          @click="goNext"
        >
          下一步
        </el-button>
      </div>
    </el-card>

    <!-- 详情弹窗 -->
    <ScoreDetailDialog v-model:visible="detailVisible" :item="detailItem" />
  </div>
</template>

<style scoped>
.smart-fill {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.mb-4 {
  margin-bottom: 16px;
}

.step-content {
  min-height: 500px;
}

.step-panel {
  padding: 20px 0;
}

.step-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 8px;
}

.step-desc {
  font-size: 14px;
  color: #6b7280;
  margin-bottom: 24px;
}

.action-bar {
  margin-top: 20px;
  display: flex;
  gap: 12px;
}

.fill-done-alert {
  margin-top: 16px;
}

.llm-streaming-alert {
  margin-bottom: 12px;
}

.step-actions {
  margin-top: 32px;
  padding-top: 16px;
  border-top: 1px solid var(--el-border-color-lighter);
  display: flex;
  justify-content: center;
  gap: 16px;
}

.loading-overlay {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: var(--el-color-primary);
}

.loading-text {
  margin-top: 16px;
  font-size: 16px;
  font-weight: 500;
  color: var(--color-text);
}

.loading-hint {
  margin-top: 8px;
  font-size: 13px;
  color: #9ca3af;
}
</style>
