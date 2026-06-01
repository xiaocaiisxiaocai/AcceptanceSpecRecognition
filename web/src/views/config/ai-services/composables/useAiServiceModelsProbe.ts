import { reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  AiServicePurpose,
  getAiServiceModels,
  type AiServiceConfig,
  type AiServiceModelsResult
} from "@/api/ai-service";
import { getRequestErrorMessage } from "@/utils/error-message";
import { ensurePermission } from "@/utils/permission-guard";
import { isRowLoading, setRowLoading } from "../utils";

export const useAiServiceModelsProbe = () => {
  const probingState = reactive<Record<string, boolean>>({});
  const modelsDialogVisible = ref(false);
  const modelsLoading = ref(false);
  const modelsInfo = reactive({
    id: 0,
    name: "",
    purpose: AiServicePurpose.Llm,
    llmModels: [] as string[],
    embeddingModels: [] as string[],
    message: ""
  });

  const loadModels = async () => {
    if (
      !ensurePermission("btn:ai-service:models", "权限不足，无法探测AI服务模型")
    ) {
      return;
    }
    if (!modelsInfo.id) return;

    setRowLoading(probingState, modelsInfo.id, true);
    modelsLoading.value = true;
    try {
      const res = await getAiServiceModels(modelsInfo.id);
      if (res.code === 0) {
        const data = res.data as AiServiceModelsResult;
        modelsInfo.llmModels = data.llmModels || [];
        modelsInfo.embeddingModels = data.embeddingModels || [];
        modelsInfo.message = data.message || "";
      } else {
        modelsInfo.message = res.message || "模型探测失败";
      }
    } catch (error) {
      modelsInfo.message = getRequestErrorMessage(error, "模型探测失败");
    } finally {
      modelsLoading.value = false;
      setRowLoading(probingState, modelsInfo.id, false);
    }
  };

  const handleProbeModels = async (row: AiServiceConfig) => {
    if (
      !ensurePermission("btn:ai-service:models", "权限不足，无法探测AI服务模型")
    ) {
      return;
    }
    if (row.isDisabled) {
      ElMessage.warning("该配置已禁用，不能探测模型");
      return;
    }
    if (isRowLoading(probingState, row.id)) {
      return;
    }

    modelsInfo.id = row.id;
    modelsInfo.name = row.name;
    modelsInfo.purpose = row.purpose;
    modelsInfo.llmModels = [];
    modelsInfo.embeddingModels = [];
    modelsInfo.message = "正在探测远端模型，请稍候...";
    modelsDialogVisible.value = true;
    await loadModels();
  };

  const copyModelName = async (name: string) => {
    if (!name) return;
    try {
      await navigator.clipboard.writeText(name);
      ElMessage.success("已复制模型名称");
    } catch {
      try {
        const textarea = document.createElement("textarea");
        textarea.value = name;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        const ok = document.execCommand("copy");
        document.body.removeChild(textarea);
        if (ok) {
          ElMessage.success("已复制模型名称");
        } else {
          ElMessage.error("复制失败，请手动复制");
        }
      } catch {
        ElMessage.error("复制失败，请手动复制");
      }
    }
  };

  return {
    probingState,
    modelsDialogVisible,
    modelsLoading,
    modelsInfo,
    handleProbeModels,
    loadModels,
    copyModelName
  };
};
