import { ref } from "vue";
import type { BatchExecuteFillRequest } from "@/api/matching";
import type { SmartFillBackfillCandidate } from "../smartFillBackfill.types";

export type { SmartFillBackfillCandidate } from "../smartFillBackfill.types";

export function useSmartFillBackfillState() {
  const backfillDialogVisible = ref(false);
  const backfillCandidates = ref<SmartFillBackfillCandidate[]>([]);
  const pendingExecuteRequest = ref<BatchExecuteFillRequest | null>(null);
  const backfillingSpecs = ref(false);

  const resetPendingBackfillState = () => {
    pendingExecuteRequest.value = null;
    backfillCandidates.value = [];
    backfillDialogVisible.value = false;
    backfillingSpecs.value = false;
  };

  const closeBackfillDialog = () => {
    backfillDialogVisible.value = false;
  };

  const openBackfillDialog = (
    request: BatchExecuteFillRequest,
    candidates: SmartFillBackfillCandidate[]
  ) => {
    pendingExecuteRequest.value = request;
    backfillCandidates.value = candidates;
    backfillDialogVisible.value = true;
  };

  const setBackfillingSpecs = (value: boolean) => {
    backfillingSpecs.value = value;
  };

  const clearPendingExecuteRequest = () => {
    pendingExecuteRequest.value = null;
  };

  return {
    backfillDialogVisible,
    backfillCandidates,
    pendingExecuteRequest,
    backfillingSpecs,
    resetPendingBackfillState,
    closeBackfillDialog,
    openBackfillDialog,
    setBackfillingSpecs,
    clearPendingExecuteRequest
  };
}
