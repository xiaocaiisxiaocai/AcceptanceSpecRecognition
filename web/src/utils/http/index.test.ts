import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => {
  const requestUse = vi.fn();
  const responseUse = vi.fn();
  return {
    axiosInstance: {
      interceptors: {
        request: { use: requestUse },
        response: { use: responseUse }
      },
      request: vi.fn()
    },
    requestUse,
    responseUse,
    getToken: vi.fn(),
    handRefreshToken: vi.fn(),
    logOut: vi.fn(),
    alert: vi.fn()
  };
});

vi.mock("axios", () => ({
  default: {
    create: vi.fn(() => mocks.axiosInstance),
    isCancel: vi.fn(() => false)
  }
}));

vi.mock("@/utils/auth", () => ({
  getToken: mocks.getToken,
  formatToken: (token: string) => `Bearer ${token}`
}));

vi.mock("@/store/modules/user", () => ({
  useUserStoreHook: () => ({
    handRefreshToken: mocks.handRefreshToken,
    logOut: mocks.logOut
  })
}));

vi.mock("@/router", () => ({
  router: {
    currentRoute: { value: { fullPath: "/protected" } }
  }
}));

vi.mock("@/utils/audit-context", () => ({
  createAuditTraceId: () => "trace-id",
  getAuditClientId: () => "client-id",
  getCurrentFrontendRoute: () => "/protected"
}));

vi.mock("element-plus", () => ({
  ElMessage: { error: vi.fn() },
  ElMessageBox: { alert: mocks.alert }
}));

const { authorizedFetch } = await import("./index");

type ResponseErrorHandler = (error: any) => Promise<unknown>;
type RequestHandler = (config: any) => Promise<unknown>;

const responseErrorHandler = () =>
  mocks.responseUse.mock.calls[0]?.[1] as ResponseErrorHandler;
const requestHandler = () =>
  mocks.requestUse.mock.calls[0]?.[0] as RequestHandler;

const createUnauthorizedError = (index: number) => ({
  isAxiosError: true,
  config: {
    method: "get",
    url: `/protected/${index}`,
    headers: { Authorization: "Bearer expired-token" }
  },
  response: {
    status: 401,
    data: { message: "access token expired" }
  }
});

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, resolve, reject };
};

describe("PureHttp 并发 401 刷新队列", () => {
  beforeEach(() => {
    mocks.getToken.mockReturnValue({
      accessToken: "expired-token",
      expires: Date.now() + 60_000
    });
    mocks.handRefreshToken.mockReset();
    mocks.logOut.mockReset();
    mocks.alert.mockReset().mockResolvedValue(undefined);
    mocks.axiosInstance.request.mockReset();
  });

  it("10 个并发 401 只刷新一次，成功后携带新 token 全部重放", async () => {
    const refresh = deferred<any>();
    mocks.handRefreshToken.mockReturnValue(refresh.promise);
    mocks.axiosInstance.request.mockImplementation(async config => ({
      replayedUrl: config.url,
      authorization: config.headers.Authorization
    }));

    const pending = Array.from({ length: 10 }, (_, index) =>
      responseErrorHandler()(createUnauthorizedError(index))
    );

    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(mocks.handRefreshToken).toHaveBeenCalledWith();
    refresh.resolve({ data: { accessToken: "fresh-token" } });

    const results = await Promise.all(pending);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(mocks.axiosInstance.request).toHaveBeenCalledTimes(10);
    expect(results).toHaveLength(10);
    for (const [
      index,
      call
    ] of mocks.axiosInstance.request.mock.calls.entries()) {
      expect(call[0]).toMatchObject({
        url: `/protected/${index}`,
        headers: { Authorization: "Bearer fresh-token" }
      });
    }
    expect(mocks.alert).not.toHaveBeenCalled();
    expect(mocks.logOut).not.toHaveBeenCalled();
  });

  it("请求前发现内存 token 过期时，瞬态刷新失败不会误判会话失效", async () => {
    const refresh = deferred<any>();
    const refreshError = new Error("refresh rejected before request");
    mocks.getToken.mockReturnValue({
      accessToken: "expired-token",
      expires: Date.now() - 1
    });
    mocks.handRefreshToken.mockReturnValue(refresh.promise);

    const pending = requestHandler()({
      method: "get",
      url: "/protected/preflight",
      headers: {}
    });
    refresh.reject(refreshError);

    await expect(pending).rejects.toBe(refreshError);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(mocks.alert).not.toHaveBeenCalled();
    expect(mocks.logOut).not.toHaveBeenCalled();
  });

  it("瞬态刷新失败时全部拒绝，但不会清理仍可能有效的会话", async () => {
    const refresh = deferred<any>();
    const refreshError = new Error("refresh rejected");
    mocks.handRefreshToken.mockReturnValue(refresh.promise);

    const pending = Array.from({ length: 10 }, (_, index) =>
      responseErrorHandler()(createUnauthorizedError(index))
    );
    refresh.reject(refreshError);

    const results = await Promise.allSettled(pending);
    expect(results.every(result => result.status === "rejected")).toBe(true);
    expect(
      results.every(
        result => result.status === "rejected" && result.reason === refreshError
      )
    ).toBe(true);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(mocks.axiosInstance.request).not.toHaveBeenCalled();
    expect(mocks.alert).not.toHaveBeenCalled();
    expect(mocks.logOut).not.toHaveBeenCalled();
  });

  it("刷新端点明确返回 401 时才触发一次登出引导", async () => {
    const refreshError = { response: { status: 401 } };
    mocks.handRefreshToken.mockRejectedValue(refreshError);

    const pending = responseErrorHandler()(createUnauthorizedError(1));

    await expect(pending).rejects.toBe(refreshError);
    expect(mocks.alert).toHaveBeenCalledTimes(1);
    await Promise.resolve();
    expect(mocks.logOut).toHaveBeenCalledTimes(1);
    expect(mocks.logOut).toHaveBeenCalledWith("/protected");
    await new Promise(resolve => setTimeout(resolve, 350));
  });
});

describe("authorizedFetch 401 重放", () => {
  beforeEach(() => {
    mocks.getToken.mockReturnValue({
      accessToken: "expired-token",
      expires: Date.now() + 60_000
    });
    mocks.handRefreshToken.mockReset();
    mocks.logOut.mockReset();
    mocks.alert.mockReset().mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("首次 401 共用刷新队列，保留 AbortSignal 并只重放一次", async () => {
    const controller = new AbortController();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response("stream", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    mocks.handRefreshToken.mockResolvedValue({
      data: { accessToken: "fresh-token" }
    });

    const response = await authorizedFetch("/protected/stream", {
      method: "POST",
      body: "payload",
      signal: controller.signal
    });

    expect(response.status).toBe(200);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
    expect(fetchMock.mock.calls[1]?.[1]?.signal).toBe(controller.signal);
    expect(
      new Headers(fetchMock.mock.calls[1]?.[1]?.headers).get("Authorization")
    ).toBe("Bearer fresh-token");
    expect(mocks.alert).not.toHaveBeenCalled();
  });

  it("多个原生 fetch 并发 401 时只刷新一次并全部重放", async () => {
    const requestCount = 5;
    const fetchMock = vi.fn().mockImplementation(async () => {
      const status = fetchMock.mock.calls.length <= requestCount ? 401 : 200;
      return new Response(null, { status });
    });
    vi.stubGlobal("fetch", fetchMock);
    const refresh = deferred<any>();
    mocks.handRefreshToken.mockReturnValue(refresh.promise);

    const pending = Array.from({ length: requestCount }, (_, index) =>
      authorizedFetch(`/protected/stream/${index}`)
    );
    await vi.waitFor(() => {
      expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
      expect(fetchMock).toHaveBeenCalledTimes(requestCount);
    });
    refresh.resolve({ data: { accessToken: "fresh-token" } });

    const responses = await Promise.all(pending);
    expect(responses.every(response => response.status === 200)).toBe(true);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(requestCount * 2);
  });

  it("重放后仍为 401 时停止重试并进入统一失效流程", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal("fetch", fetchMock);
    mocks.handRefreshToken.mockResolvedValue({
      data: { accessToken: "fresh-token" }
    });

    const response = await authorizedFetch("/protected/stream");

    expect(response.status).toBe(401);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(mocks.handRefreshToken).toHaveBeenCalledTimes(1);
    expect(mocks.alert).toHaveBeenCalledTimes(1);
    await Promise.resolve();
    expect(mocks.logOut).toHaveBeenCalledTimes(1);
    await new Promise(resolve => setTimeout(resolve, 350));
  });
});
