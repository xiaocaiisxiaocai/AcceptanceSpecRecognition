import type { AxiosProgressEvent } from "axios";

export interface UploadTransportOptions {
  signal?: AbortSignal;
  onUploadProgress?: (event: AxiosProgressEvent) => void;
}

export const isUploadRequestCancelled = (error: unknown) => {
  if (!error || typeof error !== "object") return false;

  const requestError = error as {
    name?: string;
    code?: string;
    isCancelRequest?: boolean;
  };
  return (
    requestError.name === "AbortError" ||
    requestError.name === "CanceledError" ||
    requestError.code === "ERR_CANCELED" ||
    requestError.isCancelRequest === true
  );
};

export const throwIfUploadCancelled = (signal: AbortSignal) => {
  if (!signal.aborted) return;
  throw new DOMException("上传已取消", "AbortError");
};

export const formatUploadBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};
