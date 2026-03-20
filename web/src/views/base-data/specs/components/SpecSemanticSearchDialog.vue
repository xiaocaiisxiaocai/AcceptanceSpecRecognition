<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  semanticSearchSpecs,
  type SpecSemanticSearchItem,
  type SpecSemanticSearchRequest,
  type SpecSemanticSearchResponse
} from "@/api/spec";

const props = defineProps<{
  modelValue: boolean;
  groupLabel: string;
  customerId: number;
  machineModelId?: number;
  processId?: number;
  allowEdit: boolean;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: boolean): void;
  (e: "view", row: SpecSemanticSearchItem): void;
  (e: "edit", row: SpecSemanticSearchItem): void;
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});

const loading = ref(false);
const result = ref<SpecSemanticSearchResponse | null>(null);
const lastRequest = ref<SpecSemanticSearchRequest | null>(null);

const form = reactive({
  queryText: "",
  topK: 5,
  minScore: 0.5
});

const totalHits = computed(() =>
  result.value?.groups.reduce((sum, group) => sum + group.totalHits, 0) ?? 0
);

const hasAnyHit = computed(() =>
  result.value?.groups.some(group => group.totalHits > 0) ?? false
);

const actionColumnWidth = computed(() => (props.allowEdit ? 140 : 80));

const parseQueries = () => {
  return form.queryText
    .replaceAll("\r", "")
    .split("\n")
    .map(line => line.trim())
    .filter(line => line.length > 0);
};

const buildRequest = (): SpecSemanticSearchRequest | null => {
  const queries = parseQueries();
  if (queries.length === 0) {
    ElMessage.warning("请至少输入一条搜索内容");
    return null;
  }

  const request: SpecSemanticSearchRequest = {
    queries,
    customerId: props.customerId,
    topK: form.topK,
    minScore: Number(form.minScore.toFixed(2))
  };

  if (props.machineModelId != null) {
    request.machineModelId = props.machineModelId;
  } else {
    request.machineModelIdIsNull = true;
  }

  if (props.processId != null) {
    request.processId = props.processId;
  } else {
    request.processIdIsNull = true;
  }

  return request;
};

const performSearch = async (request: SpecSemanticSearchRequest) => {
  loading.value = true;
  try {
    lastRequest.value = request;
    const res = await semanticSearchSpecs(request);
    if (res.code === 0) {
      result.value = res.data;
    } else {
      result.value = null;
      ElMessage.error(res.message);
    }
  } catch {
    result.value = null;
    ElMessage.error("AI搜索失败");
  } finally {
    loading.value = false;
  }
};

const executeSearch = async () => {
  const request = buildRequest();
  if (!request) return;
  await performSearch(request);
};

const resetSearch = () => {
  form.queryText = "";
  form.topK = 5;
  form.minScore = 0.5;
  result.value = null;
  lastRequest.value = null;
};

const formatScore = (value: number) => `${(value * 100).toFixed(1)}%`;

const formatImportedAt = (value?: string) => {
  if (!value) return "-";
  return new Date(value).toLocaleString();
};

const scoreTagType = (value: number) => {
  if (value >= 0.9) return "success";
  if (value >= 0.75) return "warning";
  return "info";
};

const handleView = (row: SpecSemanticSearchItem) => {
  emit("view", row);
};

const handleEdit = (row: SpecSemanticSearchItem) => {
  if (!props.allowEdit) {
    ElMessage.error("权限不足，无法编辑规格");
    return;
  }
  emit("edit", row);
};

const reloadLastSearch = async () => {
  if (!lastRequest.value) return;
  await performSearch(lastRequest.value);
};

defineExpose({
  reloadLastSearch
});
</script>

<template>
  <el-dialog
    v-model="visible"
    title="AI搜索"
    width="1280"
    top="4vh"
    append-to-body
    :close-on-click-modal="false"
  >
    <div class="semantic-dialog">
      <aside class="control-panel">
        <div class="panel-header">
          <h3>批量输入</h3>
          <span>每行一条，按当前分组范围执行语义搜索</span>
        </div>
        <div class="group-label">
          <span class="group-label-title">当前分组</span>
          <strong>{{ groupLabel }}</strong>
        </div>
        <el-input
          v-model="form.queryText"
          type="textarea"
          resize="none"
          :rows="16"
          placeholder="示例：&#10;平台吸附精度 真空吸附平台平面度需控制在0.05mm以内&#10;升降模组行程 升降模组有效行程需达到300mm"
        />
        <div class="control-grid">
          <div class="control-item">
            <span>TopK</span>
            <el-input-number
              v-model="form.topK"
              :min="1"
              :max="20"
              controls-position="right"
            />
          </div>
          <div class="control-item">
            <span>最小分数</span>
            <el-input-number
              v-model="form.minScore"
              :min="0"
              :max="1"
              :step="0.05"
              :precision="2"
              controls-position="right"
            />
          </div>
        </div>
        <div class="control-actions">
          <el-button @click="resetSearch">清空</el-button>
          <el-button type="primary" :loading="loading" @click="executeSearch">
            执行搜索
          </el-button>
        </div>
      </aside>

      <section class="result-panel">
        <div class="summary-panel">
          <div class="summary-card">
            <span class="summary-label">输入条数</span>
            <strong class="summary-value">{{ result?.queryCount ?? 0 }}</strong>
          </div>
          <div class="summary-card">
            <span class="summary-label">候选规格</span>
            <strong class="summary-value">{{
              result?.candidateCount ?? 0
            }}</strong>
          </div>
          <div class="summary-card">
            <span class="summary-label">命中总数</span>
            <strong class="summary-value">{{ totalHits }}</strong>
          </div>
          <div class="summary-card">
            <span class="summary-label">Embedding模型</span>
            <strong class="summary-value ellipsis">{{
              result?.embeddingModel || "-"
            }}</strong>
          </div>
        </div>

        <el-scrollbar class="result-scroll">
          <div v-loading="loading" class="result-body">
            <el-empty
              v-if="!loading && !result"
              description="请输入要搜索的规格内容"
            />
            <el-empty
              v-else-if="!loading && !hasAnyHit"
              description="当前阈值下未找到命中结果"
            />
            <div v-else class="group-list">
              <el-card
                v-for="group in result?.groups"
                :key="group.queryIndex"
                class="group-card"
                shadow="never"
              >
                <div class="group-head">
                  <div class="group-main">
                    <div class="group-title">
                      <span class="group-index">#{{ group.queryIndex + 1 }}</span>
                      <strong>输入内容</strong>
                      <el-tag type="info" effect="light">
                        命中 {{ group.totalHits }} 条
                      </el-tag>
                    </div>
                    <p class="group-query">{{ group.queryText }}</p>
                  </div>
                </div>

                <el-empty
                  v-if="group.items.length === 0"
                  description="此条输入暂无命中结果"
                  :image-size="80"
                />

                <el-table
                  v-else
                  :data="group.items"
                  border
                  size="small"
                  max-height="280"
                >
                  <el-table-column label="相似度" width="110">
                    <template #default="{ row }">
                      <el-tag :type="scoreTagType(row.score)" effect="light">
                        {{ formatScore(row.score) }}
                      </el-tag>
                    </template>
                  </el-table-column>
                  <el-table-column prop="project" label="项目" min-width="150" />
                  <el-table-column
                    prop="specification"
                    label="规格内容"
                    min-width="260"
                    show-overflow-tooltip
                  />
                  <el-table-column
                    prop="acceptance"
                    label="验收标准"
                    min-width="200"
                    show-overflow-tooltip
                  />
                  <el-table-column
                    prop="remark"
                    label="备注"
                    min-width="160"
                    show-overflow-tooltip
                  />
                  <el-table-column label="导入时间" width="180">
                    <template #default="{ row }">
                      {{ formatImportedAt(row.importedAt) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="操作"
                    fixed="right"
                    :width="actionColumnWidth"
                  >
                    <template #default="{ row }">
                      <el-button type="primary" link @click="handleView(row)">
                        查看
                      </el-button>
                      <el-button
                        v-if="allowEdit"
                        type="primary"
                        link
                        @click="handleEdit(row)"
                      >
                        编辑
                      </el-button>
                    </template>
                  </el-table-column>
                </el-table>
              </el-card>
            </div>
          </div>
        </el-scrollbar>
      </section>
    </div>

    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.semantic-dialog {
  display: grid;
  grid-template-columns: 340px minmax(0, 1fr);
  gap: 16px;
  height: min(76vh, 820px);
  min-height: 560px;
}

.control-panel,
.result-panel {
  min-height: 0;
  border: 1px solid var(--el-border-color-light);
  border-radius: 16px;
  background: linear-gradient(180deg, #fff 0%, #f8fafc 100%);
}

.control-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 18px;
}

.panel-header {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.panel-header h3 {
  margin: 0;
  font-size: 18px;
  color: var(--el-text-color-primary);
}

.panel-header span,
.group-label-title {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.group-label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #f5f7fa;
}

.control-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.control-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.control-item span {
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.control-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.result-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 18px;
}

.summary-panel {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.summary-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 14px 16px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 12px;
  background: #fff;
}

.summary-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.summary-value {
  font-size: 18px;
  line-height: 1.4;
  color: var(--el-text-color-primary);
}

.result-scroll {
  flex: 1;
  min-height: 0;
}

.result-body {
  min-height: 180px;
}

.group-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.group-card {
  border-radius: 14px;
}

.group-head {
  display: flex;
  margin-bottom: 12px;
}

.group-main {
  flex: 1;
  min-width: 0;
}

.group-title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 8px;
}

.group-index {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.group-query {
  margin: 0;
  white-space: pre-wrap;
  line-height: 1.7;
  color: var(--el-text-color-primary);
}

.ellipsis {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1100px) {
  .semantic-dialog {
    grid-template-columns: 1fr;
    height: auto;
    min-height: 0;
  }

  .summary-panel {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .result-scroll {
    max-height: 56vh;
  }
}
</style>
