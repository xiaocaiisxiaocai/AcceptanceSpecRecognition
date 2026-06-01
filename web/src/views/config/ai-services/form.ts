import {
  AiServicePurpose,
  AiServiceType,
  type AiServiceConfigDetail,
  type CreateAiServiceRequest,
  type UpdateAiServiceRequest
} from "@/api/ai-service";
import { DEFAULT_RECALL_TOP_K, MAX_RECALL_TOP_K } from "@/api/matching";
import { normalizePurpose } from "./utils";

export interface AiServiceFormData {
  id: number;
  name: string;
  serviceType: AiServiceType;
  purpose: AiServicePurpose;
  priority: number;
  endpoint: string;
  apiKey: string;
  embeddingModel: string;
  llmModel: string;
  disableThinking: boolean;
  defaultRecallTopK: number;
}

export const getAiServiceDialogTitle = (isEdit: boolean) =>
  isEdit ? "编辑AI服务配置" : "新增AI服务配置";

export const getAiServiceSubmitPermission = (isEdit: boolean) => ({
  code: isEdit ? "btn:ai-service:update" : "btn:ai-service:create",
  message: isEdit ? "权限不足，无法保存AI服务配置" : "权限不足，无法新增AI服务配置"
});

export const getAiServiceSubmitSuccessMessage = (isEdit: boolean) =>
  isEdit ? "更新成功" : "创建成功";

export const createEmptyAiServiceFormData = (): AiServiceFormData => ({
  id: 0,
  name: "",
  serviceType: AiServiceType.Ollama,
  purpose: AiServicePurpose.Llm,
  priority: 0,
  endpoint: "",
  apiKey: "",
  embeddingModel: "",
  llmModel: "",
  disableThinking: false,
  defaultRecallTopK: DEFAULT_RECALL_TOP_K
});

export const createNewAiServiceFormData = (
  purpose: AiServicePurpose,
  priority: number
): AiServiceFormData => ({
  ...createEmptyAiServiceFormData(),
  purpose,
  priority,
  endpoint: "http://localhost:11434",
  embeddingModel:
    purpose === AiServicePurpose.Embedding ? "nomic-embed-text" : ""
});

export const createEditAiServiceFormData = (
  detail: AiServiceConfigDetail
): AiServiceFormData => ({
  id: detail.id,
  name: detail.name,
  serviceType: detail.serviceType,
  purpose: normalizePurpose(detail.purpose ?? AiServicePurpose.None),
  priority: detail.priority ?? 0,
  endpoint: detail.endpoint ?? "",
  apiKey: detail.apiKey ?? "",
  embeddingModel: detail.embeddingModel ?? "",
  llmModel: detail.llmModel ?? "",
  disableThinking: !!detail.disableThinking,
  defaultRecallTopK: detail.defaultRecallTopK ?? DEFAULT_RECALL_TOP_K
});

export const clearModelOutsidePurpose = (formData: AiServiceFormData) => {
  if (formData.purpose === AiServicePurpose.Llm) {
    formData.embeddingModel = "";
  } else if (formData.purpose === AiServicePurpose.Embedding) {
    formData.llmModel = "";
  }
};

export const validateAiServiceFormData = (formData: AiServiceFormData) => {
  if (!formData.name.trim()) return "请输入名称";
  if (!formData.purpose) return "请至少选择一个用途";
  if (
    formData.purpose !== AiServicePurpose.Llm &&
    formData.purpose !== AiServicePurpose.Embedding
  ) {
    return "用途只能选择一个（LLM 或 Embedding）";
  }
  if (formData.purpose === AiServicePurpose.Llm && !formData.llmModel.trim()) {
    return "请输入 LLM 模型";
  }
  if (
    formData.purpose === AiServicePurpose.Embedding &&
    !formData.embeddingModel.trim()
  ) {
    return "请输入 Embedding 模型";
  }
  return "";
};

const clampRecallTopK = (value: number) =>
  Math.min(MAX_RECALL_TOP_K, Math.max(1, value || DEFAULT_RECALL_TOP_K));

export const buildAiServiceBasePayload = (
  formData: AiServiceFormData
): CreateAiServiceRequest => {
  const payload: CreateAiServiceRequest = {
    name: formData.name.trim(),
    serviceType: formData.serviceType,
    purpose: formData.purpose,
    priority: formData.priority,
    endpoint: formData.endpoint?.trim() || null,
    embeddingModel: formData.embeddingModel?.trim() || null,
    llmModel: formData.llmModel?.trim() || null,
    disableThinking: !!formData.disableThinking,
    defaultRecallTopK: clampRecallTopK(formData.defaultRecallTopK)
  };

  if (formData.purpose === AiServicePurpose.Llm) {
    payload.embeddingModel = null;
  }
  if (formData.purpose === AiServicePurpose.Embedding) {
    payload.llmModel = null;
  }

  return payload;
};

export const buildCreateAiServicePayload = (
  formData: AiServiceFormData
): CreateAiServiceRequest => ({
  ...buildAiServiceBasePayload(formData),
  apiKey: formData.apiKey.trim() || ""
});

export const buildUpdateAiServicePayload = (
  formData: AiServiceFormData,
  originalApiKey: string
): UpdateAiServiceRequest => {
  const payload: UpdateAiServiceRequest = buildAiServiceBasePayload(formData);
  const apiKey = formData.apiKey.trim();
  if (apiKey !== originalApiKey) {
    payload.apiKey = apiKey; // 允许清空
  }
  return payload;
};
