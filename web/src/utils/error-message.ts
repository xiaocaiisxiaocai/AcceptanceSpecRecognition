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
      if (Array.isArray(data.errors)) {
        const messages = data.errors.filter(
          (item): item is string => typeof item === "string" && item.length > 0
        );
        if (messages.length > 0) {
          return messages.join("；");
        }
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

/** 401/403 已由 HTTP 层统一提示或引导登录，页面不应再次弹出错误。 */
export function isGloballyHandledAuthError(error: unknown): boolean {
  return (
    isAxiosError(error) &&
    (error.response?.status === 401 || error.response?.status === 403)
  );
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
