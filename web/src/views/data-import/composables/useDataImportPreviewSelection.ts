import { ref } from "vue";

export function useDataImportPreviewSelection() {
  const excludedRowIndexMap = ref<Record<number, number[]>>({});
  const importPreviewSelectionKeys = ref<string[]>([]);

  return {
    excludedRowIndexMap,
    importPreviewSelectionKeys
  };
}
