<script setup lang="ts">
import { AiServicePurpose, type AiServiceConfig } from "@/api/ai-service";
import { getServiceTypeLabel } from "../config-selection";
import { formatValue, isRowLoading } from "../utils";

const props = defineProps<{
  title: string;
  purpose: AiServicePurpose;
  emptyDescription: string;
  config: AiServiceConfig | null;
  loading: boolean;
  canCreate: boolean;
  canUpdate: boolean;
  canDelete: boolean;
  canProbeModels: boolean;
  hasSummaryActionButtons: boolean;
  disabledState: Record<string, boolean>;
  probingState: Record<string, boolean>;
}>();

const emit = defineEmits<{
  add: [purpose: AiServicePurpose];
  edit: [row: AiServiceConfig];
  toggleDisabled: [row: AiServiceConfig];
  delete: [row: AiServiceConfig];
  probeModels: [row: AiServiceConfig];
}>();
</script>

<template>
  <el-card v-loading="loading" class="service-card">
    <template #header>
      <div class="card-header">
        <span>{{ title }}</span>
        <div class="card-actions">
          <el-button v-if="canCreate" type="primary" @click="emit('add', purpose)">
            新增
          </el-button>
          <template v-if="config && hasSummaryActionButtons">
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="emit('edit', config)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canUpdate"
              type="warning"
              link
              :loading="isRowLoading(disabledState, config.id)"
              :disabled="isRowLoading(disabledState, config.id)"
              @click="emit('toggleDisabled', config)"
            >
              禁用
            </el-button>
            <el-button
              v-if="canDelete"
              type="danger"
              link
              @click="emit('delete', config)"
            >
              删除
            </el-button>
            <el-button
              v-if="canProbeModels"
              type="success"
              link
              :loading="isRowLoading(probingState, config.id)"
              :disabled="isRowLoading(probingState, config.id)"
              @click="emit('probeModels', config)"
            >
              模型
            </el-button>
          </template>
        </div>
      </div>
    </template>

    <el-empty v-if="!config" :description="emptyDescription" />
    <div v-else class="config-grid">
      <div class="config-row">
        <div class="config-label">名称</div>
        <div class="config-value">{{ formatValue(config.name) }}</div>
      </div>
      <div class="config-row">
        <div class="config-label">类型</div>
        <div class="config-value">
          {{ getServiceTypeLabel(config.serviceType) }}
        </div>
      </div>
      <div class="config-row">
        <div class="config-label">优先级</div>
        <div class="config-value">{{ config.priority }}</div>
      </div>
      <div class="config-row">
        <div class="config-label">Endpoint</div>
        <div class="config-value">{{ formatValue(config.endpoint) }}</div>
      </div>

      <template v-if="purpose === AiServicePurpose.Llm">
        <div class="config-row">
          <div class="config-label">LLM 模型</div>
          <div class="config-value">{{ formatValue(config.llmModel) }}</div>
        </div>
        <div class="config-row">
          <div class="config-label">关闭思考模式</div>
          <div class="config-value">
            {{ config.disableThinking ? "已开启" : "未开启" }}
          </div>
        </div>
      </template>

      <template v-else>
        <div class="config-row">
          <div class="config-label">Embedding 模型</div>
          <div class="config-value">
            {{ formatValue(config.embeddingModel) }}
          </div>
        </div>
        <div class="config-row">
          <div class="config-label">匹配链路</div>
          <div class="config-value">证据裁决</div>
        </div>
        <div class="config-row">
          <div class="config-label">默认召回候选数</div>
          <div class="config-value">{{ config.defaultRecallTopK }}</div>
        </div>
      </template>
    </div>
  </el-card>
</template>

<style scoped src="../index.styles.css"></style>
