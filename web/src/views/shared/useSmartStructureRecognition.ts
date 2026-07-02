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
import {
  buildSmartConfigConfirmRequest,
  createSmartStructureSummary
} from "./smart-structure-recognition";

export function useSmartStructureRecognition() {
  const recognizing = ref(false);
  const confirmingTableIndex = ref<number | null>(null);
  const recognitionResult = ref<SmartConfigRecognizeResult | null>(null);
  const lastConfirmResult = ref<SmartConfigConfirmResult | null>(null);

  const recognizedTables = computed(
    () => recognitionResult.value?.tables ?? []
  );
  const summary = computed(() =>
    createSmartStructureSummary(recognizedTables.value)
  );

  const recognize = async (fileId: number, customerId?: number) => {
    recognizing.value = true;
    try {
      const res = await recognizeSmartConfig({ fileId, customerId });
      if (res.code !== 0) {
        throw new Error(res.message || "智能结构识别失败");
      }

      recognitionResult.value = res.data;
      return res.data;
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "智能结构识别失败"));
      return null;
    } finally {
      recognizing.value = false;
    }
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

    confirmingTableIndex.value =
      "tableIndex" in requestOrTable ? requestOrTable.tableIndex : null;
    try {
      const res = await confirmSmartConfig(request);
      if (res.code !== 0) {
        throw new Error(res.message || "确认结构失败");
      }

      lastConfirmResult.value = res.data;
      ElMessage.success("结构确认已保存");
      return res.data;
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "确认结构失败"));
      return null;
    } finally {
      confirmingTableIndex.value = null;
    }
  };

  const reset = () => {
    recognitionResult.value = null;
    lastConfirmResult.value = null;
    confirmingTableIndex.value = null;
  };

  return {
    recognizing,
    confirmingTableIndex,
    recognitionResult,
    recognizedTables,
    summary,
    lastConfirmResult,
    recognize,
    confirm,
    reset
  };
}
