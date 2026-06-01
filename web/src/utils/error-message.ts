import type { AxiosError } from "axios";

/**
 * 从 Axios 错误或通用错误中提取用户可读的错误消息
 * 优先级：response.data.message > response.data.error.message > error.message > 默认消息
 */
export function getRequestErrorMessage(
  error: unknown,
  fallback = "请求失败，请稍后重试"
): string {
  if (!error) return fallback;

  // Axios 错误
  if (isAxiosError(error)) {
    const data = error.response?.data as Record<string, unknown> | undefined;
    if (data) {
      if (typeof data.message === "string" && data.message) {
        return data.message;
      }
      const nested = data.error as Record<string, unknown> | undefined;
      if (nested && typeof nested.message === "string" && nested.message) {
        return nested.message;
      }
    }
    if (error.message) return error.message;
  }

  // 标准 Error
  if (error instanceof Error && error.message) {
    return error.message;
  }

  // 字符串
  if (typeof error === "string" && error) {
    return error;
  }

  return fallback;
}

/**
 * 判断是否为 Axios 错误
 */
function isAxiosError(error: unknown): error is AxiosError {
  return (
    typeof error === "object" &&
    error !== null &&
    "isAxiosError" in error &&
    (error as AxiosError).isAxiosError === true
  );
}
