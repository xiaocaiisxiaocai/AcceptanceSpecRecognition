<script setup lang="ts">
import { computed, ref } from "vue";
import type { FormInstance, FormRules } from "element-plus";
import { AiServicePurpose } from "@/api/ai-service";
import { MAX_RECALL_TOP_K } from "@/api/matching";
import { purposeOptions, serviceTypeOptions } from "../constants";
import type { AiServiceFormData } from "../form";
import { hasPurpose } from "../utils";
import {
  requiredSelectionRule,
  requiredTrimmedRule,
  validateForm
} from "@/utils/form-rules";

const visible = defineModel<boolean>({ required: true });
const formData = defineModel<AiServiceFormData>("formData", { required: true });

defineProps<{
  title: string;
  canSubmit: boolean;
}>();

const emit = defineEmits<{
  submit: [];
}>();

const form = computed(() => formData.value);
const formRef = ref<FormInstance>();
const formRules: FormRules<AiServiceFormData> = {
  name: [requiredTrimmedRule("请输入名称")],
  serviceType: [requiredSelectionRule("请选择类型")],
  purpose: [requiredSelectionRule("请选择用途")],
  embeddingModel: [requiredTrimmedRule("请输入 Embedding 模型")],
  llmModel: [requiredTrimmedRule("请输入 LLM 模型")]
};

const handleSubmit = async () => {
  if (!(await validateForm(formRef.value))) return;
  emit("submit");
};
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="min(640px, calc(100vw - 32px))"
  >
    <el-form
      ref="formRef"
      :model="form"
      :rules="formRules"
      label-width="120px"
      status-icon
    >
      <el-form-item label="名称" prop="name">
        <el-input v-model="form.name" maxlength="100" />
      </el-form-item>
      <el-form-item label="类型" prop="serviceType">
        <el-select
          v-model="form.serviceType"
          class="w-full"
          popper-class="config-select-popper"
        >
          <el-option
            v-for="opt in serviceTypeOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="用途" prop="purpose">
        <el-radio-group v-model="form.purpose">
          <el-radio
            v-for="opt in purposeOptions"
            :key="opt.value"
            :label="opt.value"
          >
            {{ opt.label }}
          </el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="优先级">
        <el-input-number
          v-model="form.priority"
          :min="0"
          :max="9999"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="Endpoint">
        <el-input
          v-model="form.endpoint"
          placeholder="例如 http://localhost:11434 或 https://api.moonshot.cn（不包含 /v1）"
        />
      </el-form-item>
      <el-form-item label="ApiKey">
        <el-input
          v-model="form.apiKey"
          type="password"
          show-password
          placeholder="可查看/修改（编辑时）"
        />
      </el-form-item>
      <el-form-item
        v-if="hasPurpose(form.purpose, AiServicePurpose.Embedding)"
        label="EmbeddingModel"
        prop="embeddingModel"
      >
        <el-input v-model="form.embeddingModel" />
      </el-form-item>
      <el-form-item
        v-if="hasPurpose(form.purpose, AiServicePurpose.Embedding)"
        label="匹配链路"
      >
        <div class="thinking-config">
          <el-tag type="success">证据裁决</el-tag>
          <div class="thinking-tip">
            固定执行 Embedding 召回、证据重排、冲突门禁和高歧义复核。
          </div>
        </div>
      </el-form-item>
      <el-form-item
        v-if="hasPurpose(form.purpose, AiServicePurpose.Embedding)"
        label="默认召回数"
      >
        <el-input-number
          v-model="form.defaultRecallTopK"
          :min="1"
          :max="MAX_RECALL_TOP_K"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item
        v-if="hasPurpose(form.purpose, AiServicePurpose.Llm)"
        label="LLMModel"
        prop="llmModel"
      >
        <el-input v-model="form.llmModel" />
      </el-form-item>
      <el-form-item
        v-if="hasPurpose(form.purpose, AiServicePurpose.Llm)"
        label="关闭思考模式"
      >
        <div class="thinking-config">
          <el-switch v-model="form.disableThinking" />
          <div class="thinking-tip">
            当前主要对 Ollama 生效，系统会优先请求关闭思考输出，并对
            `&lt;think&gt;` 内容做兜底清理
          </div>
        </div>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button v-if="canSubmit" type="primary" @click="handleSubmit">
        确定
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped src="../index.styles.css"></style>
