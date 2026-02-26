<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from "vue";
import { ElMessage } from "element-plus";
import FileUpload from "@/views/data-import/components/FileUpload.vue";
import TableSelector from "@/views/data-import/components/TableSelector.vue";
import MatchConfig from "./components/MatchConfig.vue";
import MatchPreviewTable from "./components/MatchPreviewTable.vue";
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

// 页面卸载时清理 SSE 连接，防止连接泄漏
onBeforeUnmount(() => {
  stopLlmStream();
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

// 文件上传完成
const handleFileUploaded = async (file: FileUploadResponse) => {
  uploadedFile.value = file;
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  taskId.value = null;

  // 自动获取文件中的表格列表
  try {
    const res = await getFileTables(file.fileId);
    if (res.code === 0) {
      allTables.value = res.data;
      // 初始化批量配置（默认全选，列索引用常见默认值）
      batchTableConfigs.value = res.data.map((t) => ({
        tableIndex: t.index,
        projectColumnIndex: 0,
        specificationColumnIndex: 1,
        acceptanceColumnIndex: 2,
        remarkColumnIndex: 3,
        selected: res.data.length === 1, // 单表格时默认选中
        tableInfo: t
      }));
    }
  } catch {
    ElMessage.warning("获取表格列表失败");
  }
};

// 执行批量匹配预览
const doPreview = async () => {
  if (!uploadedFile.value) return;

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
        remarkColumnIndex: t.remarkColumnIndex
      })),
      customerId: scope.customerId,
      processId: scope.processId,
      machineModelId: scope.machineModelId,
      config: matchConfig.value
    });

    if (res.code === 0) {
      batchPreviewResults.value = res.data.tables;
      if (res.data.totalMatched === 0) {
        ElMessage.warning("未找到可匹配的数据");
      }
      startLlmStream();
    } else {
      ElMessage.error(res.message || "匹配预览失败");
    }
  } catch {
    ElMessage.error("匹配预览失败");
  } finally {
    loading.value = false;
  }
};

const stopLlmStream = () => {
  llmStreamController.value?.abort();
  llmStreamController.value = null;
  llmStreaming.value = false;
};

const startLlmStream = async () => {
  stopLlmStream();

  if (!allPreviewItems.value.length) return;
  if (!matchConfig.value.useLlmReview && !matchConfig.value.useLlmSuggestion)
    return;

  const controller = new AbortController();
  llmStreamController.value = controller;
  llmStreaming.value = true;

  const payload = {
    items: allPreviewItems.value.map((item) => ({
      rowIndex: item.rowIndex,
      sourceProject: item.sourceProject,
      sourceSpecification: item.sourceSpecification,
      bestMatchSpecId: item.bestMatch?.specId,
      bestMatchScore: item.bestMatch?.score,
      scoreDetails: item.bestMatch?.scoreDetails
    })),
    config: matchConfig.value
  };

  try {
    const response = await fetch("/api/matching/llm-stream", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
      signal: controller.signal
    });

    if (!response.ok || !response.body) {
      ElMessage.warning("LLM流式输出不可用，已降级");
      llmStreaming.value = false;
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split("\n\n");
      buffer = parts.pop() || "";

      for (const part of parts) {
        handleSseEvent(part);
      }
    }
  } catch {
    if (!controller.signal.aborted) {
      ElMessage.warning("LLM流式输出中断，已降级");
    }
  } finally {
    llmStreaming.value = false;
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
    case "suggestion.start":
      row.llmSuggestionDraft = "";
      row.llmSuggestionError = undefined;
      break;
    case "suggestion.delta":
      row.llmSuggestionDraft =
        (row.llmSuggestionDraft || "") + (data.chunk || "");
      break;
    case "suggestion.done":
      row.llmSuggestion = {
        acceptance: data.acceptance,
        remark: data.remark,
        reason: data.reason
      };
      row.llmSuggestionDraft = "";
      break;
    case "suggestion.error":
      row.llmSuggestionError = data.message;
      row.llmSuggestionDraft = "";
      break;
    default:
      break;
  }
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
  if (!uploadedFile.value) return;

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
          useLlmSuggestion: s.useLlmSuggestion,
          acceptance: s.acceptance,
          remark: s.remark
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
      useLlmSuggestion?: boolean;
      acceptance?: string;
      remark?: string;
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
      tables
    });

    if (res.code === 0) {
      taskId.value = res.data.taskId;
      ElMessage.success(`填充完成，共填充 ${res.data.filledCount} 条`);
    } else {
      ElMessage.error(res.message || "填充失败");
    }
  } catch {
    ElMessage.error("填充失败");
  } finally {
    executing.value = false;
  }
};

// 下载结果
const handleDownload = async () => {
  if (!taskId.value) return;

  try {
    const blob = await downloadFillResult(taskId.value);
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `filled_${uploadedFile.value?.fileName || "result.docx"}`;
    a.click();
    window.URL.revokeObjectURL(url);
  } catch {
    ElMessage.error("下载失败");
  }
};

// 步骤切换
const goNext = () => {
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
        <p class="step-desc">请选择需要填充验收标准的Word文档</p>
        <FileUpload v-model="uploadedFile" @uploaded="handleFileUploaded" />
      </div>

      <!-- 步骤2: 选择表格 + 配置列索引 -->
      <div v-show="currentStep === 1" class="step-panel">
        <h3 class="step-title">选择表格并配置列索引</h3>
        <p class="step-desc">
          勾选需要填充的表格，并为每个表格指定各列的索引（从0开始）
        </p>

        <BatchTableConfig
          v-if="batchTableConfigs.length > 0"
          v-model="batchTableConfigs"
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
        <MatchConfig ref="matchConfigRef" v-model="matchConfig" />
      </div>

      <!-- 步骤4: 预览确认 -->
      <div v-show="currentStep === 3" class="step-panel">
        <h3 class="step-title">匹配预览</h3>
        <p class="step-desc">确认匹配结果，可手动调整选择</p>

        <BatchPreviewTabs
          ref="batchPreviewTabsRef"
          :results="batchPreviewResults"
          :loading="loading"
          @select="handleSelect"
          @show-detail="handleShowDetail"
        />

        <!-- 操作按钮 -->
        <div v-if="allPreviewItems.length > 0" class="action-bar">
          <el-button @click="doPreview" :loading="loading">
            重新匹配
          </el-button>
          <el-button
            type="primary"
            :loading="executing"
            :disabled="!allPreviewItems.length"
            @click="handleExecute"
          >
            执行填充
          </el-button>
          <el-button v-if="taskId" type="success" @click="handleDownload">
            下载结果
          </el-button>
        </div>

        <!-- 完成提示 -->
        <div v-if="taskId" class="success-tip">
          <el-alert
            title="填充完成"
            type="success"
            description="验收标准已填充到文档中，点击下载结果获取文件"
            show-icon
            :closable="false"
          />
          <el-button type="primary" class="mt-4" @click="handleRestart">
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
          :disabled="!canGoNext"
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

.mt-4 {
  margin-top: 16px;
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

.success-tip {
  margin-top: 24px;
  text-align: center;
}

.step-actions {
  margin-top: 32px;
  padding-top: 16px;
  border-top: 1px solid var(--el-border-color-lighter);
  display: flex;
  justify-content: center;
  gap: 16px;
}
</style>
