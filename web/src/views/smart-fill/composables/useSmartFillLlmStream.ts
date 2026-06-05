import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  applyMatchLlmStreamDisconnectToPreviewItem,
  applyMatchLlmStreamEventToPreviewItem,
  shouldStreamMatchReview
} from "../components/scoreDetail.formatters";
import {
  createMatchLlmStreamRequest,
  requestMatchLlmStream,
  type BatchTablePreviewResult,
  type MatchConfig,
  type MatchLlmStreamCompleteEventData,
  type MatchLlmStreamEvent,
  type MatchLlmStreamEventData,
  type MatchLlmStreamBaseEventData,
  type MatchPreviewItem
} from "@/api/matching";
import type { SmartFillScope } from "../smartFillExecution.helpers";

export { createMatchLlmStreamRequest, requestMatchLlmStream };

type UseSmartFillLlmStreamOptions = {
  canLlmStream: ComputedRef<boolean>;
  batchPreviewResults: Ref<BatchTablePreviewResult[]>;
  allPreviewItems: ComputedRef<MatchPreviewItem[]>;
  matchConfig: Ref<MatchConfig>;
  getScope: () => SmartFillScope;
  /** 由调用方提供的流式请求发起函数，接收 payload 和 controller，返回 Response */
  onStartStream?: (
    payload: ReturnType<typeof createMatchLlmStreamRequest>,
    controller: AbortController
  ) => Promise<Response>;
  /** 由调用方提供的 payload 构造函数，接收 scope 和 llmItems，返回 requestMatchLlmStream 的 payload */
  buildLlmStreamPayload?: (
    scope: SmartFillScope,
    llmItems: Parameters<typeof createMatchLlmStreamRequest>[0]["items"],
    config: MatchConfig
  ) => ReturnType<typeof createMatchLlmStreamRequest>;
};

export function useSmartFillLlmStream({
  canLlmStream,
  batchPreviewResults,
  allPreviewItems,
  matchConfig,
  getScope,
  onStartStream,
  buildLlmStreamPayload
}: UseSmartFillLlmStreamOptions) {
  const llmStreaming = ref(false);
  const llmStreamController = ref<AbortController | null>(null);
  const activeLlmStreamPendingRowKeys = ref<Set<string>>(new Set());

  const buildLlmStreamRowKey = (tableIndex: number, rowIndex: number) =>
    `${tableIndex}:${rowIndex}`;

  const finalizeInterruptedLlmStreamRows = (
    message = "LLM流式输出中断，已转为人工确认",
    pendingRowKeys = activeLlmStreamPendingRowKeys.value
  ) => {
    batchPreviewResults.value.forEach((tableResult) => {
      tableResult.items.forEach((item) => {
        if (!pendingRowKeys.has(buildLlmStreamRowKey(tableResult.tableIndex, item.rowIndex))) {
          return;
        }

        applyMatchLlmStreamDisconnectToPreviewItem(item, message);
      });
    });
    pendingRowKeys.clear();
  };

  const stopLlmStream = () => {
    const controller = llmStreamController.value;
    finalizeInterruptedLlmStreamRows("LLM复核已取消，已转为人工确认");
    controller?.abort();
    if (llmStreamController.value === controller) {
      llmStreamController.value = null;
    }
    activeLlmStreamPendingRowKeys.value.clear();
    llmStreaming.value = false;
  };

  const applySseUpdate = (
    event: MatchLlmStreamEvent,
    data: MatchLlmStreamEventData
  ) => {
    if (event === "stream.complete") {
      const complete = data as MatchLlmStreamCompleteEventData;
      const completedRowKeys = complete.completedRowKeys?.length
        ? complete.completedRowKeys
        : [...activeLlmStreamPendingRowKeys.value];

      batchPreviewResults.value.forEach((tableResult) => {
        tableResult.items.forEach((item) => {
          const rowKey = buildLlmStreamRowKey(tableResult.tableIndex, item.rowIndex);
          if (!completedRowKeys.includes(rowKey)) {
            return;
          }

          applyMatchLlmStreamEventToPreviewItem(item, event, data);
          activeLlmStreamPendingRowKeys.value.delete(rowKey);
        });
      });
      return;
    }

    const rowData = data as MatchLlmStreamBaseEventData;
    if (rowData.tableIndex === undefined || rowData.tableIndex === null) {
      return;
    }

    const tableResult = batchPreviewResults.value.find(
      tableResult => tableResult.tableIndex === rowData.tableIndex
    );
    const row = tableResult?.items.find((item) => item.rowIndex === rowData.rowIndex);
    if (!row) return;

    applyMatchLlmStreamEventToPreviewItem(row, event, data);
    if (event === "review.done" || event === "review.error") {
      activeLlmStreamPendingRowKeys.value.delete(
        buildLlmStreamRowKey(rowData.tableIndex, rowData.rowIndex)
      );
    }
  };

  const handleSseEvent = (raw: string) => {
    const lines = raw.split("\n").filter((line) => line.trim().length > 0);
    let event = "message";
    const dataLines: string[] = [];
    for (const line of lines) {
      if (line.startsWith("event:")) {
        event = line.replace("event:", "").trim();
      } else if (line.startsWith("data:")) {
        dataLines.push(line.replace("data:", "").trim());
      }
    }

    if (dataLines.length === 0) return;

    try {
      const data = JSON.parse(dataLines.join("\n")) as MatchLlmStreamEventData;
      applySseUpdate(event as MatchLlmStreamEvent, data);
    } catch {
      // 忽略不完整或畸形的 SSE 分片，后续分片仍可继续处理。
    }
  };

  const startLlmStream = async () => {
    if (!canLlmStream.value) {
      return;
    }
    stopLlmStream();

    if (!allPreviewItems.value.length) return;

    const scope = getScope();
    const buildPayload = buildLlmStreamPayload
      ?? ((s, items, config) => createMatchLlmStreamRequest({
          customerId: s.customerId,
          processId: s.processId,
          machineModelId: s.machineModelId,
          items,
          config
        }));
    const llmItems = batchPreviewResults.value.flatMap((tableResult) =>
      tableResult.items
        .filter(item => shouldStreamMatchReview(item.bestMatch))
        .map((item) => ({
          tableIndex: tableResult.tableIndex,
          rowIndex: item.rowIndex,
          sourceProject: item.sourceProject,
          sourceSpecification: item.sourceSpecification,
          bestMatchSpecId: item.bestMatch?.specId,
          bestMatchScore: item.bestMatch?.score,
          scoreDetails: item.bestMatch?.scoreDetails,
          decision: item.bestMatch?.decision,
          llmEquivalenceVerdict: item.bestMatch?.llmEquivalence?.verdict,
          isAmbiguous: item.bestMatch?.isAmbiguous ?? false,
          evidenceSummary: item.bestMatch?.evidenceSummary ?? [],
          conflictSummary: item.bestMatch?.conflictSummary ?? []
        }))
    );

    if (!llmItems.length) {
      llmStreaming.value = false;
      return;
    }

    const controller = new AbortController();
    llmStreamController.value = controller;
    llmStreaming.value = true;
    activeLlmStreamPendingRowKeys.value = new Set(
      llmItems.map(item => buildLlmStreamRowKey(item.tableIndex, item.rowIndex))
    );

    const payload = buildPayload(scope, llmItems, matchConfig.value);

    try {
      const doRequest = onStartStream ?? ((p, c) => requestMatchLlmStream(p, c.signal));
      const response = await doRequest(payload, controller);

      if (!response.ok || !response.body) {
        const message = "LLM流式输出不可用，已转为人工确认";
        finalizeInterruptedLlmStreamRows(message);
        stopLlmStream();
        ElMessage.warning(message);
        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder("utf-8");
      let buffer = "";

      while (true) {
        if (controller.signal.aborted || llmStreamController.value !== controller) {
          break;
        }

        const { value, done } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const parts = buffer.split("\n\n");
        buffer = parts.pop() || "";

        for (const part of parts) {
          if (
            controller.signal.aborted ||
            llmStreamController.value !== controller
          ) {
            break;
          }
          handleSseEvent(part);
        }
      }
    } catch {
      if (!controller.signal.aborted) {
        ElMessage.warning("LLM流式输出中断，已降级");
      }
    } finally {
      if (llmStreamController.value === controller) {
        if (!controller.signal.aborted) {
          finalizeInterruptedLlmStreamRows();
        }
        llmStreamController.value = null;
        llmStreaming.value = false;
      }
    }
  };

  const handleWindowOffline = () => {
    if (!llmStreaming.value) {
      return;
    }

    const message = "浏览器网络已断开，LLM 复核已转为人工确认";
    finalizeInterruptedLlmStreamRows(message);
    stopLlmStream();
    ElMessage.warning(message);
  };

  return {
    llmStreaming,
    startLlmStream,
    stopLlmStream,
    handleWindowOffline
  };
}
