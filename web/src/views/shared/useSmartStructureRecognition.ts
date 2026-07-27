import { computed, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  confirmSmartConfig,
  recognizeSmartConfig,
  type SmartConfigConfirmRequest,
  type SmartConfigConfirmResult,
  type SmartConfigRecognizeResult,
  type SmartConfigRecognizedTable
} from "@/api/smart-config";
import { getRequestErrorMessage } from "@/utils/error-message";
import { isRuntimeAiSelectionAvailable } from "@/utils/runtime-ai-selection";
import { waitForRuntimeAiSelection } from "@/utils/runtime-ai-selection-loader";
import {
  buildSmartConfigConfirmRequest,
  createSmartStructureSummary
} from "./smart-structure-recognition";

export function useSmartStructureRecognition() {
  const recognizing = ref(false);
  const recognitionAttempted = ref(false);
  const recognitionError = ref("");
  const activeRecognitionFileId = ref<number | null>(null);
  const activeRecognitionCustomerId = ref<number | null>(null);
  const confirmingTableIndex = ref<number | null>(null);
  const recognitionResult = ref<SmartConfigRecognizeResult | null>(null);
  const lastConfirmResult = ref<SmartConfigConfirmResult | null>(null);

  const recognizedTables = computed(
    () => recognitionResult.value?.tables ?? []
  );
  const summary = computed(() =>
    createSmartStructureSummary(recognizedTables.value)
  );
  let recognitionRequestVersion = 0;
  let contextVersion = 0;
  let selectionController: AbortController | undefined;

  const recognize = async (
    fileId: number,
    customerId?: number,
    options: {
      enableLlmAssistance?: boolean;
      llmServiceId?: number;
    } = {}
  ) => {
    const requestVersion = ++recognitionRequestVersion;
    selectionController?.abort();
    const currentSelectionController = new AbortController();
    selectionController = currentSelectionController;
    contextVersion += 1;
    activeRecognitionFileId.value = fileId;
    activeRecognitionCustomerId.value = customerId ?? null;
    confirmingTableIndex.value = null;
    recognizing.value = true;
    recognitionAttempted.value = false;
    recognitionError.value = "";
    recognitionResult.value = null;
    lastConfirmResult.value = null;
    const isCurrentRequest = () =>
      requestVersion === recognitionRequestVersion &&
      activeRecognitionFileId.value === fileId &&
      activeRecognitionCustomerId.value === (customerId ?? null);

    try {
      let enableLlmAssistance = options.enableLlmAssistance === true;
      let llmServiceId = options.llmServiceId;
      if (enableLlmAssistance) {
        try {
          const selection = await waitForRuntimeAiSelection("llm", {
            signal: currentSelectionController.signal
          });
          if (!isCurrentRequest()) return null;
          if (isRuntimeAiSelectionAvailable(selection)) {
            llmServiceId = selection.serviceId;
          } else {
            enableLlmAssistance = false;
            llmServiceId = undefined;
            ElMessage.warning(
              selection.status === "checking"
                ? "AI 服务仍在检测中，本次先使用规则识别"
                : "AI 服务当前不可用，本次先使用规则识别"
            );
          }
        } catch (error) {
          if (!isCurrentRequest()) return null;
          if (
            currentSelectionController.signal.aborted ||
            (error instanceof Error && error.name === "AbortError")
          ) {
            return null;
          }
          enableLlmAssistance = false;
          llmServiceId = undefined;
          ElMessage.warning("AI 服务状态检查失败，本次先使用规则识别");
        }
      }

      const res = await recognizeSmartConfig(
        {
          fileId,
          customerId,
          enableLlmAssistance,
          llmServiceId: enableLlmAssistance ? llmServiceId : undefined
        },
        { signal: currentSelectionController.signal }
      );
      if (res.code !== 0) {
        throw new Error(res.message || "智能结构识别失败");
      }
      if (!isCurrentRequest() || res.data.fileId !== fileId) {
        return null;
      }

      recognitionResult.value = res.data;
      return res.data;
    } catch (error) {
      if (!isCurrentRequest()) {
        return null;
      }
      recognitionError.value = getRequestErrorMessage(
        error,
        "智能结构识别失败"
      );
      ElMessage.error(recognitionError.value);
      return null;
    } finally {
      if (selectionController === currentSelectionController) {
        selectionController = undefined;
      }
      if (isCurrentRequest()) {
        recognizing.value = false;
        recognitionAttempted.value = true;
      }
    }
  };

  const replaceRecognizedTables = (
    tables: SmartConfigRecognizedTable[],
    expectedFileId = activeRecognitionFileId.value
  ) => {
    if (
      !recognitionResult.value ||
      expectedFileId == null ||
      recognitionResult.value.fileId !== expectedFileId ||
      activeRecognitionFileId.value !== expectedFileId
    ) {
      return false;
    }
    recognitionResult.value = {
      ...recognitionResult.value,
      tables
    };
    return true;
  };

  const confirm = async (
    requestOrTable: SmartConfigConfirmRequest | SmartConfigRecognizedTable,
    customerId?: number
  ) => {
    const request =
      "customerId" in requestOrTable
        ? requestOrTable
        : buildSmartConfigConfirmRequest(customerId ?? 0, requestOrTable);

    if (request.customerId <= 0) {
      ElMessage.warning("请先选择客户后再确认结构");
      return null;
    }

    if (activeRecognitionCustomerId.value !== request.customerId) {
      ElMessage.warning("客户已变更，请重新识别后再确认结构");
      return null;
    }

    const expectedFileId = request.fileId ?? activeRecognitionFileId.value;
    if (
      expectedFileId == null ||
      activeRecognitionFileId.value !== expectedFileId ||
      recognitionResult.value?.fileId !== expectedFileId
    ) {
      return null;
    }

    if (confirmingTableIndex.value != null) {
      ElMessage.warning("正在确认其他表格，请稍候");
      return null;
    }

    const requestContextVersion = contextVersion;
    const isCurrentRequest = () =>
      requestContextVersion === contextVersion &&
      activeRecognitionFileId.value === expectedFileId &&
      activeRecognitionCustomerId.value === request.customerId &&
      recognitionResult.value?.fileId === expectedFileId;

    confirmingTableIndex.value = requestOrTable.tableIndex;
    try {
      const res = await confirmSmartConfig(request);
      if (!isCurrentRequest()) {
        return null;
      }
      if (res.code !== 0) {
        throw new Error(res.message || "确认结构失败");
      }

      lastConfirmResult.value = res.data;
      const learnedText = res.data.learnedRuleCount
        ? `，已学习 ${res.data.learnedRuleCount} 条列映射`
        : "，列映射无需新增";
      ElMessage.success(
        `${res.data.templateSaved ? "结构模板已保存" : "结构确认完成"}${learnedText}`
      );
      return res.data;
    } catch (error) {
      if (!isCurrentRequest()) {
        return null;
      }
      ElMessage.error(getRequestErrorMessage(error, "确认结构失败"));
      return null;
    } finally {
      if (isCurrentRequest()) {
        confirmingTableIndex.value = null;
      }
    }
  };

  const cancelActiveRecognition = () => {
    recognitionRequestVersion += 1;
    contextVersion += 1;
    selectionController?.abort();
    selectionController = undefined;
    recognizing.value = false;
    confirmingTableIndex.value = null;
  };

  const reset = () => {
    cancelActiveRecognition();
    activeRecognitionFileId.value = null;
    activeRecognitionCustomerId.value = null;
    recognitionAttempted.value = false;
    recognitionResult.value = null;
    recognitionError.value = "";
    lastConfirmResult.value = null;
  };

  return {
    recognizing,
    recognitionAttempted,
    recognitionError,
    activeRecognitionFileId,
    activeRecognitionCustomerId,
    confirmingTableIndex,
    recognitionResult,
    recognizedTables,
    replaceRecognizedTables,
    summary,
    lastConfirmResult,
    recognize,
    confirm,
    cancelActiveRecognition,
    reset
  };
}
