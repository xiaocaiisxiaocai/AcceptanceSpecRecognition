<script setup lang="ts">
import { computed } from "vue";
import type { SpecDuplicateDetectionResult, SpecDuplicateGroup } from "@/api/spec";

const props = defineProps<{
  modelValue: boolean;
  loading: boolean;
  result: SpecDuplicateDetectionResult | null;
  groupLabel: string;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: boolean): void;
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});

const hasAnyGroup = computed(() => {
  const result = props.result;
  if (!result) return false;
  return result.exactGroupCount > 0 || result.similarGroupCount > 0;
});

const formatImportedAt = (value?: string) => {
  if (!value) return "-";
  return new Date(value).toLocaleString();
};

const formatScore = (value: number) => `${(value * 100).toFixed(1)}%`;

const scoreTagType = (group: SpecDuplicateGroup) => {
  if (group.groupType === "exact") return "danger";
  if (group.similarityScore >= 0.95) return "danger";
  if (group.similarityScore >= 0.9) return "warning";
  return "info";
};
</script>

<template>
  <el-dialog
    v-model="visible"
    title="重复规格排查"
    width="1100"
    top="5vh"
    destroy-on-close
  >
    <div class="duplicate-dialog">
      <div class="summary-panel">
        <div class="summary-card">
          <span class="summary-label">当前分组</span>
          <strong class="summary-value">{{ groupLabel }}</strong>
        </div>
        <div class="summary-card">
          <span class="summary-label">扫描规格</span>
          <strong class="summary-value">{{ result?.scannedCount ?? 0 }}</strong>
        </div>
        <div class="summary-card danger">
          <span class="summary-label">完全重复</span>
          <strong class="summary-value">{{ result?.exactGroupCount ?? 0 }}</strong>
        </div>
        <div class="summary-card warning">
          <span class="summary-label">近重复</span>
          <strong class="summary-value">{{ result?.similarGroupCount ?? 0 }}</strong>
        </div>
      </div>

      <el-scrollbar class="content-scroll">
        <div v-loading="loading" class="content-body">
          <el-empty
            v-if="!loading && !hasAnyGroup"
            description="当前分组未发现重复或近重复规格"
          />

          <template v-else>
            <section class="group-section">
              <div class="section-header">
                <h3>完全重复</h3>
                <span>忽略空白和标点后完全一致</span>
              </div>
              <el-empty
                v-if="!result?.exactGroups.length"
                description="未发现完全重复规格"
              />
              <div v-else class="group-list">
                <el-card
                  v-for="(group, index) in result?.exactGroups"
                  :key="`exact-${index}`"
                  class="group-card"
                  shadow="never"
                >
                  <div class="group-head">
                    <div class="group-main">
                      <div class="group-title">
                        <span class="group-index">#{{ index + 1 }}</span>
                        <strong>{{ group.project || "未命名项目" }}</strong>
                        <el-tag :type="scoreTagType(group)" effect="light">
                          {{ group.itemCount }} 条
                        </el-tag>
                      </div>
                      <p class="group-preview">{{ group.specificationPreview }}</p>
                      <p class="group-reason">{{ group.reason }}</p>
                    </div>
                    <div class="group-score">
                      <span>相似度</span>
                      <strong>{{ formatScore(group.similarityScore) }}</strong>
                    </div>
                  </div>

                  <el-table :data="group.items" border size="small" max-height="280">
                    <el-table-column prop="id" label="ID" width="80" />
                    <el-table-column prop="project" label="项目" min-width="140" />
                    <el-table-column prop="specification" label="规格内容" min-width="280" show-overflow-tooltip />
                    <el-table-column prop="acceptance" label="验收标准" min-width="180" show-overflow-tooltip />
                    <el-table-column prop="remark" label="备注" min-width="160" show-overflow-tooltip />
                    <el-table-column label="导入时间" width="180">
                      <template #default="{ row }">
                        {{ formatImportedAt(row.importedAt) }}
                      </template>
                    </el-table-column>
                  </el-table>
                </el-card>
              </div>
            </section>

            <section class="group-section">
              <div class="section-header">
                <h3>近重复</h3>
                <span>项目一致或接近，规格文本高度相似</span>
              </div>
              <el-empty
                v-if="!result?.similarGroups.length"
                description="未发现近重复规格"
              />
              <div v-else class="group-list">
                <el-card
                  v-for="(group, index) in result?.similarGroups"
                  :key="`similar-${index}`"
                  class="group-card"
                  shadow="never"
                >
                  <div class="group-head">
                    <div class="group-main">
                      <div class="group-title">
                        <span class="group-index">#{{ index + 1 }}</span>
                        <strong>{{ group.project || "未命名项目" }}</strong>
                        <el-tag :type="scoreTagType(group)" effect="light">
                          {{ group.itemCount }} 条
                        </el-tag>
                      </div>
                      <p class="group-preview">{{ group.specificationPreview }}</p>
                      <p class="group-reason">{{ group.reason }}</p>
                    </div>
                    <div class="group-score">
                      <span>相似度</span>
                      <strong>{{ formatScore(group.similarityScore) }}</strong>
                    </div>
                  </div>

                  <el-table :data="group.items" border size="small" max-height="280">
                    <el-table-column prop="id" label="ID" width="80" />
                    <el-table-column prop="project" label="项目" min-width="140" />
                    <el-table-column prop="specification" label="规格内容" min-width="280" show-overflow-tooltip />
                    <el-table-column prop="acceptance" label="验收标准" min-width="180" show-overflow-tooltip />
                    <el-table-column prop="remark" label="备注" min-width="160" show-overflow-tooltip />
                    <el-table-column label="导入时间" width="180">
                      <template #default="{ row }">
                        {{ formatImportedAt(row.importedAt) }}
                      </template>
                    </el-table-column>
                  </el-table>
                </el-card>
              </div>
            </section>
          </template>
        </div>
      </el-scrollbar>
    </div>

    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.duplicate-dialog {
  display: flex;
  flex-direction: column;
  gap: 16px;
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
  background: linear-gradient(180deg, #fff 0%, #f7f9fc 100%);
}

.summary-card.danger {
  background: linear-gradient(180deg, #fff7f7 0%, #fff0f0 100%);
}

.summary-card.warning {
  background: linear-gradient(180deg, #fffaf2 0%, #fff4e2 100%);
}

.summary-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.summary-value {
  font-size: 18px;
  color: var(--el-text-color-primary);
  line-height: 1.4;
}

.content-scroll {
  height: min(68vh, 720px);
  padding-right: 4px;
}

.content-body {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-height: 160px;
}

.group-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.section-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}

.section-header h3 {
  margin: 0;
  font-size: 16px;
  color: var(--el-text-color-primary);
}

.section-header span {
  font-size: 12px;
  color: var(--el-text-color-secondary);
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
  justify-content: space-between;
  gap: 16px;
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
  margin-bottom: 6px;
}

.group-index {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.group-preview,
.group-reason {
  margin: 0;
  line-height: 1.6;
}

.group-preview {
  color: var(--el-text-color-primary);
}

.group-reason {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.group-score {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: center;
  min-width: 96px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.group-score strong {
  font-size: 18px;
  color: var(--el-text-color-primary);
}

@media (max-width: 960px) {
  .summary-panel {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .group-head {
    flex-direction: column;
  }

  .group-score {
    align-items: flex-start;
  }
}
</style>
