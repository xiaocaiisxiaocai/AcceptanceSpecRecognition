<script setup lang="ts">
import type { AiServiceConfig } from "@/api/ai-service";
import { getServiceTypeLabel } from "../config-selection";
import {
  getTestResultCardClass,
  type InlineTestResultCard
} from "../test-result";
import { formatPurpose, isRowLoading } from "../utils";

defineProps<{
  tableData: AiServiceConfig[];
  expandedTestRowKeys: string[];
  activeTestResult: InlineTestResultCard | null;
  hasActionButtons: boolean;
  canUpdate: boolean;
  canDelete: boolean;
  canTest: boolean;
  canProbeModels: boolean;
  disabledState: Record<string, boolean>;
  testingState: Record<string, boolean>;
  probingState: Record<string, boolean>;
}>();

const emit = defineEmits<{
  collapse: [];
  clearTestResult: [];
  edit: [row: AiServiceConfig];
  toggleDisabled: [row: AiServiceConfig];
  delete: [row: AiServiceConfig];
  test: [row: AiServiceConfig];
  probeModels: [row: AiServiceConfig];
}>();

const getRowKey = (row: AiServiceConfig) => String(row.id);
</script>

<template>
  <el-card class="service-table">
    <template #header>
      <div class="flex justify-between items-center">
        <span>全部配置</span>
        <el-button @click="emit('collapse')">收起</el-button>
      </div>
    </template>
    <el-table
      :data="tableData"
      stripe
      :row-key="getRowKey"
      :expand-row-keys="expandedTestRowKeys"
    >
      <el-table-column
        type="expand"
        width="1"
        class-name="test-result-expand-column"
      >
        <template #default="{ row }: { row: AiServiceConfig }">
          <div
            v-if="activeTestResult && activeTestResult.rowId === row.id"
            :id="`ai-test-result-${row.id}`"
            class="ai-test-result-shell"
          >
            <div
              class="ai-test-result-card"
              :class="
                getTestResultCardClass(
                  activeTestResult.category,
                  activeTestResult.success
                )
              "
            >
              <div class="ai-test-result-card__header">
                <div>
                  <div class="ai-test-result-card__title">
                    {{ activeTestResult.rowName }} ·
                    {{ activeTestResult.summary }}
                  </div>
                  <div class="ai-test-result-card__subtitle">
                    {{ activeTestResult.statusText }}
                  </div>
                </div>
                <el-button link type="info" @click="emit('clearTestResult')">
                  收起
                </el-button>
              </div>

              <div class="ai-test-result-card__tags">
                <el-tag
                  v-for="tag in activeTestResult.tags"
                  :key="`${row.id}-${tag.label}`"
                  size="small"
                  :type="tag.type"
                  effect="light"
                >
                  {{ tag.label }}
                </el-tag>
              </div>

              <div class="ai-test-result-card__message">
                {{ activeTestResult.message }}
              </div>

              <div class="ai-test-result-card__details">
                <div
                  v-for="detail in activeTestResult.details"
                  :key="`${row.id}-${detail.label}`"
                  class="ai-test-result-card__detail"
                >
                  <span class="ai-test-result-card__detail-label">
                    {{ detail.label }}
                  </span>
                  <span class="ai-test-result-card__detail-value">
                    {{ detail.value }}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </template>
      </el-table-column>
      <el-table-column prop="id" label="ID" width="80" />
      <el-table-column prop="name" label="名称" min-width="min(180px, calc(100vw - 32px))" />
      <el-table-column label="状态" width="min(100px, calc(100vw - 32px))">
        <template #default="{ row }: { row: AiServiceConfig }">
          <el-tag
            :type="row.isDisabled ? 'info' : 'success'"
            size="small"
            effect="light"
          >
            {{ row.isDisabled ? "已禁用" : "启用中" }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="serviceType" label="类型" width="min(160px, calc(100vw - 32px))">
        <template #default="{ row }: { row: AiServiceConfig }">
          {{ getServiceTypeLabel(row.serviceType) }}
        </template>
      </el-table-column>
      <el-table-column prop="purpose" label="用途" width="min(160px, calc(100vw - 32px))">
        <template #default="{ row }: { row: AiServiceConfig }">
          {{ formatPurpose(row.purpose) }}
        </template>
      </el-table-column>
      <el-table-column prop="endpoint" label="Endpoint" min-width="min(240px, calc(100vw - 32px))" />
      <el-table-column
        prop="embeddingModel"
        label="EmbeddingModel"
        min-width="min(160px, calc(100vw - 32px))"
      />
      <el-table-column prop="llmModel" label="LLMModel" min-width="min(160px, calc(100vw - 32px))" />
      <el-table-column label="关闭思考模式" width="min(140px, calc(100vw - 32px))">
        <template #default="{ row }: { row: AiServiceConfig }">
          {{ row.disableThinking ? "是" : "否" }}
        </template>
      </el-table-column>
      <el-table-column
        v-if="hasActionButtons"
        label="操作"
        width="min(360px, calc(100vw - 32px))"
        fixed="right"
      >
        <template #default="{ row }: { row: AiServiceConfig }">
          <el-button
            v-if="canUpdate"
            type="primary"
            link
            @click="emit('edit', row)"
          >
            编辑
          </el-button>
          <el-button
            v-if="canUpdate"
            :type="row.isDisabled ? 'success' : 'warning'"
            link
            :loading="isRowLoading(disabledState, row.id)"
            :disabled="isRowLoading(disabledState, row.id)"
            @click="emit('toggleDisabled', row)"
          >
            {{ row.isDisabled ? "启用" : "禁用" }}
          </el-button>
          <el-button
            v-if="canDelete"
            type="danger"
            link
            @click="emit('delete', row)"
          >
            删除
          </el-button>
          <el-button
            v-if="canTest"
            type="warning"
            link
            :loading="isRowLoading(testingState, row.id)"
            :disabled="row.isDisabled || isRowLoading(testingState, row.id)"
            @click="emit('test', row)"
          >
            完整测试
          </el-button>
          <el-button
            v-if="canProbeModels"
            type="success"
            link
            :loading="isRowLoading(probingState, row.id)"
            :disabled="row.isDisabled || isRowLoading(probingState, row.id)"
            @click="emit('probeModels', row)"
          >
            模型
          </el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-card>
</template>

<style scoped src="../index.styles.css"></style>
