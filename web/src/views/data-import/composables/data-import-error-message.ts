import { getRequestErrorMessage } from "@/utils/error-message";

export type DataImportExecutionPhase = "import" | "continue";

export const getDataImportExecutionErrorMessage = (
  error: unknown,
  phase: DataImportExecutionPhase
) =>
  getRequestErrorMessage(
    error,
    phase === "continue" ? "继续导入失败，请稍后重试" : "导入失败，请稍后重试"
  );
