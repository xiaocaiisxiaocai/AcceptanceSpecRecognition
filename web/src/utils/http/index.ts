import Axios, {
  type AxiosInstance,
  type AxiosRequestConfig,
  type CustomParamsSerializer
} from "axios";
import type {
  PureHttpError,
  RequestMethods,
  PureHttpResponse,
  PureHttpRequestConfig
} from "./types.d";
import { stringify } from "qs";
import { getToken, formatToken } from "@/utils/auth";
import { useUserStoreHook } from "@/store/modules/user";
import { router } from "@/router";
import {
  createAuditTraceId,
  getAuditClientId,
  getCurrentFrontendRoute
} from "@/utils/audit-context";
import { ElMessage, ElMessageBox } from "element-plus";

// 相关配置请参考：www.axios-js.com/zh-cn/docs/#axios-request-config-1
const defaultConfig: AxiosRequestConfig = {
  // 请求超时时间
  timeout: 10000,
  headers: {
    Accept: "application/json, text/plain, */*",
    "Content-Type": "application/json",
    "X-Requested-With": "XMLHttpRequest"
  },
  // Refresh/logout authentication is carried by a constrained HttpOnly cookie.
  // Axios copies the non-secret XSRF cookie to this header for same-origin and
  // explicitly configured cross-origin deployments.
  withCredentials: true,
  withXSRFToken: true,
  xsrfCookieName: "acceptance-csrf",
  xsrfHeaderName: "X-CSRF-Token",
  // 数组格式参数序列化（https://github.com/axios/axios/issues/5142）
  paramsSerializer: {
    serialize: stringify as unknown as CustomParamsSerializer
  }
};

const ensureAuditHeaders = (config: PureHttpRequestConfig) => {
  config.headers = config.headers ?? {};
  config.headers["X-Client-Trace-Id"] ??= createAuditTraceId();
  config.headers["X-Client-Id"] ??= getAuditClientId();
  config.headers["X-Frontend-Route"] ??= getCurrentFrontendRoute();
};

const normalizeFetchHeaders = (
  headers?: HeadersInit
): Record<string, string> => {
  if (!headers) {
    return {};
  }

  if (headers instanceof Headers) {
    const normalized: Record<string, string> = {};
    headers.forEach((value, key) => {
      normalized[key] = value;
    });
    return normalized;
  }

  if (Array.isArray(headers)) {
    return Object.fromEntries(headers);
  }

  return { ...headers };
};

const getBackendMessage = (responseData: unknown): string | undefined => {
  if (
    typeof responseData === "object" &&
    responseData !== null &&
    "message" in responseData
  ) {
    const message = responseData.message;
    return typeof message === "string" && message ? message : undefined;
  }

  return undefined;
};

class PureHttp {
  constructor() {
    this.httpInterceptorsRequest();
    this.httpInterceptorsResponse();
  }

  /** `token`过期后，暂存待执行的请求 */
  private static requests: Array<{
    resolve: (config: PureHttpRequestConfig) => void;
    reject: (error: unknown) => void;
  }> = [];

  /** 防止重复刷新`token` */
  private static isRefreshing = false;
  /** 防止401并发时重复提示与跳转 */
  private static isAuthRedirecting = false;

  /** 初始化配置对象 */
  private static initConfig: PureHttpRequestConfig = {};

  /** 保存当前`Axios`实例对象 */
  private static axiosInstance: AxiosInstance = Axios.create(defaultConfig);

  /** 重连原始请求 */
  private static retryOriginalRequest(config: PureHttpRequestConfig) {
    return new Promise<PureHttpRequestConfig>((resolve, reject) => {
      PureHttp.requests.push({
        resolve: nextConfig => {
          config.headers = config.headers ?? {};
          config.headers["Authorization"] = nextConfig.headers?.Authorization;
          resolve(config);
        },
        reject
      });
    });
  }

  private static resolvePendingRequests(token: string) {
    PureHttp.requests.forEach(({ resolve }) => {
      resolve({
        headers: {
          Authorization: formatToken(token)
        }
      } as PureHttpRequestConfig);
    });
    PureHttp.requests = [];
  }

  private static rejectPendingRequests(error: unknown) {
    PureHttp.requests.forEach(({ reject }) => reject(error));
    PureHttp.requests = [];
  }

  private static startTokenRefresh() {
    if (PureHttp.isRefreshing) {
      return;
    }

    PureHttp.isRefreshing = true;
    void useUserStoreHook()
      .handRefreshToken()
      .then(res => {
        PureHttp.resolvePendingRequests(res.data.accessToken);
      })
      .catch(error => {
        PureHttp.rejectPendingRequests(error);
      })
      .finally(() => {
        PureHttp.isRefreshing = false;
      });
  }

  private static waitForTokenRefresh(config: PureHttpRequestConfig) {
    PureHttp.startTokenRefresh();
    return PureHttp.retryOriginalRequest(config).catch(error => {
      PureHttp.handleAuthFailure(401, String(config.url ?? ""));
      throw error;
    });
  }

  public static async ensureAuthorization(
    config: PureHttpRequestConfig
  ): Promise<PureHttpRequestConfig> {
    const requestUrl = String(config.url ?? "");
    const whiteList = ["/refresh-token", "/login", "/logout"];
    if (whiteList.some(url => requestUrl.endsWith(url))) {
      return config;
    }

    config.headers = config.headers ?? {};
    const data = getToken();
    if (!data?.accessToken) {
      return config;
    }

    const now = new Date().getTime();
    const expired = parseInt(data.expires) - now <= 0;
    if (!expired) {
      config.headers["Authorization"] = formatToken(data.accessToken);
      return config;
    }

    return PureHttp.waitForTokenRefresh(config);
  }

  public static handleAuthFailure(
    status?: number,
    requestUrl = "",
    responseData?: unknown
  ) {
    const skipAuthHandler = ["/login", "/refresh-token", "/logout"].some(url =>
      requestUrl.endsWith(url)
    );
    if (!skipAuthHandler && status === 401) {
      if (!PureHttp.isAuthRedirecting) {
        PureHttp.isAuthRedirecting = true;
        const backendMessage =
          getBackendMessage(responseData) ?? "登录状态已失效，请重新登录";
        const currentPath = router.currentRoute.value.fullPath || "/";

        void ElMessageBox.alert(backendMessage, "登录状态已失效", {
          confirmButtonText: "重新登录",
          type: "warning",
          closeOnClickModal: false,
          closeOnPressEscape: false,
          showClose: false
        }).finally(() => {
          useUserStoreHook().logOut(currentPath);
          setTimeout(() => {
            PureHttp.isAuthRedirecting = false;
          }, 300);
        });
      }
    } else if (!skipAuthHandler && status === 403) {
      const backendMessage =
        getBackendMessage(responseData) ?? "权限不足，无法执行当前操作";
      ElMessage.error(backendMessage);
    }
  }

  private static async applyBeforeRequestCallback(
    config: PureHttpRequestConfig,
    callback: (config: PureHttpRequestConfig) => void | Promise<void>
  ): Promise<PureHttpRequestConfig> {
    await callback(config);
    ensureAuditHeaders(config);
    return PureHttp.ensureAuthorization(config);
  }

  /** 请求拦截 */
  private httpInterceptorsRequest(): void {
    PureHttp.axiosInstance.interceptors.request.use(
      async (config: PureHttpRequestConfig): Promise<any> => {
        ensureAuditHeaders(config);
        const beforeCallback =
          typeof config.beforeRequestCallback === "function"
            ? config.beforeRequestCallback
            : PureHttp.initConfig.beforeRequestCallback;

        if (typeof beforeCallback === "function") {
          return PureHttp.applyBeforeRequestCallback(config, beforeCallback);
        }

        return PureHttp.ensureAuthorization(config);
      },
      error => {
        return Promise.reject(error);
      }
    );
  }

  /** 响应拦截 */
  private httpInterceptorsResponse(): void {
    const instance = PureHttp.axiosInstance;
    instance.interceptors.response.use(
      (response: PureHttpResponse) => {
        const $config = response.config;
        // 优先判断post/get等方法是否传入回调，否则执行初始化设置等回调
        if (typeof $config.beforeResponseCallback === "function") {
          $config.beforeResponseCallback(response);
          return response.data;
        }
        if (PureHttp.initConfig.beforeResponseCallback) {
          PureHttp.initConfig.beforeResponseCallback(response);
          return response.data;
        }
        return response.data;
      },
      async (error: PureHttpError) => {
        const $error = error;
        $error.isCancelRequest = Axios.isCancel($error);

        const status = $error?.response?.status;
        const requestUrl = String($error?.config?.url ?? "");
        const requestConfig = $error?.config as
          | PureHttpRequestConfig
          | undefined;
        const token = getToken();
        const canRefresh =
          status === 401 &&
          requestConfig != null &&
          !requestConfig.authRetryAttempted &&
          Boolean(token?.accessToken) &&
          !requestUrl.endsWith("/login") &&
          !requestUrl.endsWith("/refresh-token") &&
          !requestUrl.endsWith("/logout");

        if (canRefresh) {
          requestConfig.authRetryAttempted = true;
          try {
            const retryConfig =
              await PureHttp.waitForTokenRefresh(requestConfig);
            return await PureHttp.axiosInstance.request(retryConfig);
          } catch (refreshError) {
            PureHttp.handleAuthFailure(
              status,
              requestUrl,
              $error?.response?.data
            );
            return Promise.reject(refreshError);
          }
        }

        PureHttp.handleAuthFailure(status, requestUrl, $error?.response?.data);

        // 所有的响应异常 区分来源为取消请求/非取消请求
        return Promise.reject($error);
      }
    );
  }

  /** 通用请求工具函数 */
  public request<T>(
    method: RequestMethods,
    url: string,
    param?: AxiosRequestConfig,
    axiosConfig?: PureHttpRequestConfig
  ): Promise<T> {
    const config = {
      method,
      url,
      ...param,
      ...axiosConfig
    } as PureHttpRequestConfig;

    // 单独处理自定义请求/响应回调
    return new Promise((resolve, reject) => {
      PureHttp.axiosInstance
        .request(config)
        .then((response: unknown) => {
          resolve(response as T);
        })
        .catch(error => {
          reject(error);
        });
    });
  }

  /** 单独抽离的`post`工具函数 */
  public post<T, P>(
    url: string,
    params?: AxiosRequestConfig<P>,
    config?: PureHttpRequestConfig
  ): Promise<T> {
    return this.request<T>("post", url, params, config);
  }

  /** 单独抽离的`get`工具函数 */
  public get<T, P>(
    url: string,
    params?: AxiosRequestConfig<P>,
    config?: PureHttpRequestConfig
  ): Promise<T> {
    return this.request<T>("get", url, params, config);
  }
}

export const http = new PureHttp();

export async function createAuthorizedFetchInit(
  url: string,
  init: RequestInit = {}
): Promise<RequestInit> {
  const config = {
    url,
    headers: normalizeFetchHeaders(init.headers)
  } as PureHttpRequestConfig;
  ensureAuditHeaders(config);
  const authorizedConfig = await PureHttp.ensureAuthorization(config);

  return {
    ...init,
    credentials: init.credentials ?? "include",
    headers: authorizedConfig.headers as HeadersInit
  };
}

export async function ensureFetchResponseAuthHandled(
  response: Response,
  url: string
): Promise<void> {
  if (response.status !== 401 && response.status !== 403) {
    return;
  }

  let responseData: unknown;
  try {
    responseData = await response.clone().json();
  } catch {
    responseData = undefined;
  }

  PureHttp.handleAuthFailure(response.status, url, responseData);
}

export async function authorizedFetch(
  url: string,
  init: RequestInit = {},
  options: { handleAuthFailure?: boolean } = {}
): Promise<Response> {
  const response = await fetch(url, await createAuthorizedFetchInit(url, init));
  if (options.handleAuthFailure !== false) {
    await ensureFetchResponseAuthHandled(response, url);
  }
  return response;
}
