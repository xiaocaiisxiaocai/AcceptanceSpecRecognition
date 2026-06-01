<script setup lang="ts">
import { AiServicePurpose } from "@/api/ai-service";
import { hasPurpose } from "../utils";

const visible = defineModel<boolean>({ required: true });

defineProps<{
  loading: boolean;
  canProbeModels: boolean;
  modelsInfo: {
    id: number;
    name: string;
    purpose: AiServicePurpose;
    llmModels: string[];
    embeddingModels: string[];
    message: string;
  };
}>();

const emit = defineEmits<{
  reload: [];
  copyModelName: [name: string];
}>();
</script>

<template>
  <el-dialog v-model="visible" title="远端模型探测" width="520px">
    <div v-loading="loading">
      <div class="model-title">
        {{ modelsInfo.name || "AI服务" }}
      </div>
      <div v-if="modelsInfo.message" class="model-message">
        {{ modelsInfo.message }}
      </div>
      <div
        v-if="!loading && hasPurpose(modelsInfo.purpose, AiServicePurpose.Llm)"
        class="model-section"
      >
        <div class="model-label">LLM 模型</div>
        <div v-if="modelsInfo.llmModels.length" class="model-tags">
          <el-tag
            v-for="m in modelsInfo.llmModels"
            :key="m"
            size="small"
            class="model-tag"
            :title="`点击复制 ${m}`"
            @click="emit('copyModelName', m)"
          >
            {{ m }}
          </el-tag>
        </div>
        <div v-else class="model-empty">未返回 LLM 模型</div>
      </div>
      <div
        v-if="
          !loading && hasPurpose(modelsInfo.purpose, AiServicePurpose.Embedding)
        "
        class="model-section"
      >
        <div class="model-label">Embedding 模型</div>
        <div v-if="modelsInfo.embeddingModels.length" class="model-tags">
          <el-tag
            v-for="m in modelsInfo.embeddingModels"
            :key="m"
            size="small"
            type="info"
            class="model-tag"
            :title="`点击复制 ${m}`"
            @click="emit('copyModelName', m)"
          >
            {{ m }}
          </el-tag>
        </div>
        <div v-else class="model-empty">未返回 Embedding 模型</div>
      </div>
    </div>
    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
      <el-button
        v-if="canProbeModels"
        type="primary"
        :loading="loading"
        @click="emit('reload')"
      >
        重新探测
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped src="../index.styles.css"></style>
