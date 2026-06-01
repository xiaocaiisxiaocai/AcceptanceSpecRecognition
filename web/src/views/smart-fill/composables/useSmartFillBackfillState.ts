import { computed, ref } from "vue";
import type { BatchExecuteFillRequest } from "@/api/matching";

export type SmartFillBackfillCandidate = {
  tableIndex: number;
  rowIndex: number;
  specId?: number;
  sourceProject: string;
  sourceSpecification: string;
  originalAcceptance?: string;
  originalRemark?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  actionType: "update" | "create";
  selected: boolean;
};

export function useSmartFillBackfillState() {
  const backfillDialogVisible = ref(false);
  const backfillCandidates = ref<SmartFillBackfillCandidate[]>([]);
  const pendingExecuteRequest = ref<BatchExecuteFillRequest | null>(null);
  const backfillingSpecs = ref(false);

  const selectedBackfillCandidates = computed(() =>
    backfillCandidates.value.filter(item => item.selected)
  );

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
    selectedBackfillCandidates,
    resetPendingBackfillState,
    closeBackfillDialog,
    openBackfillDialog,
    setBackfillingSpecs,
    clearPendingExecuteRequest
  };
}
