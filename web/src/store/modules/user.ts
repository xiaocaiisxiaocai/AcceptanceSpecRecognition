import { defineStore } from "pinia";
import {
  type userType,
  store,
  router,
  resetRouter,
  routerArrays,
  storageLocal
} from "../utils";
import type { LoginRequestPayload } from "../types";
import {
  type UserResult,
  type RefreshTokenResult,
  getLogin,
  refreshTokenApi,
  logoutApi
} from "@/api/user";
import { useMultiTagsStoreHook } from "./multiTags";
import { usePermissionStoreHook } from "./permission";
import { type DataInfo, setToken, removeToken, userKey } from "@/utils/auth";
import { hasAnyPermission } from "@/utils/permission";
import {
  onAuthSessionEvent,
  publishAuthSessionEvent
} from "@/utils/auth-session-events";
import { isRefreshSessionInvalidError } from "@/utils/auth-refresh-error";

let sessionEventsInitialized = false;
let refreshPromise: Promise<RefreshTokenResult> | undefined;

async function withCrossTabRefreshLock<T>(operation: () => Promise<T>) {
  if (typeof navigator !== "undefined" && navigator.locks) {
    return navigator.locks.request("acceptance-spec-refresh-token", operation);
  }
  return operation();
}

function findFirstMenuPath(routes: Array<any>): string | null {
  for (const route of routes ?? []) {
    if (route?.children?.length) {
      const childPath = findFirstMenuPath(route.children);
      if (childPath) return childPath;
    }

    if (typeof route?.path === "string" && route.path.length > 0) {
      return route.path;
    }
  }

  return null;
}

export const useUserStore = defineStore("pure-user", {
  state: (): userType => ({
    // 头像
    avatar: storageLocal().getItem<DataInfo<number>>(userKey)?.avatar ?? "",
    // 用户名
    username: storageLocal().getItem<DataInfo<number>>(userKey)?.username ?? "",
    // 昵称
    nickname: storageLocal().getItem<DataInfo<number>>(userKey)?.nickname ?? "",
    // 角色编码
    roleCode: storageLocal().getItem<DataInfo<number>>(userKey)?.roleCode ?? "",
    // 页面/按钮/API 的 permission code 集合
    permissions:
      storageLocal().getItem<DataInfo<number>>(userKey)?.permissions ?? [],
    // 是否勾选了登录页的免登录
    isRemembered: false,
    // 登录页的免登录存储几天，默认7天
    loginDay: 7
  }),
  actions: {
    /** 存储头像 */
    SET_AVATAR(avatar: string) {
      this.avatar = avatar;
    },
    /** 存储用户名 */
    SET_USERNAME(username: string) {
      this.username = username;
    },
    /** 存储昵称 */
    SET_NICKNAME(nickname: string) {
      this.nickname = nickname;
    },
    /** 存储角色编码 */
    SET_ROLE_CODE(roleCode: string) {
      this.roleCode = roleCode;
    },
    /** 存储按钮级别权限 */
    SET_PERMS(permissions: Array<string>) {
      this.permissions = permissions;
    },
    /** 存储是否勾选了登录页的免登录 */
    SET_ISREMEMBERED(bool: boolean) {
      this.isRemembered = bool;
    },
    /** 设置登录页的免登录存储几天 */
    SET_LOGINDAY(value: number) {
      this.loginDay = Number(value);
    },
    /** 登入 */
    async loginByUsername(data: LoginRequestPayload) {
      return new Promise<UserResult>((resolve, reject) => {
        getLogin(data)
          .then(data => {
            if (data?.success) {
              setToken(data.data);
              publishAuthSessionEvent("session-established");
            }
            resolve(data);
          })
          .catch(error => {
            reject(error);
          });
      });
    },
    /** 前端登出（不调用接口） */
    logOut(redirectPath?: string, broadcast = true) {
      this.avatar = "";
      this.username = "";
      this.nickname = "";
      this.roleCode = "";
      this.permissions = [];
      removeToken();
      usePermissionStoreHook().clearAllCachePage();
      useMultiTagsStoreHook().handleTags("equal", [...routerArrays]);
      resetRouter();
      if (broadcast) publishAuthSessionEvent("session-ended");
      const loginTarget =
        typeof redirectPath === "string" &&
        redirectPath.startsWith("/") &&
        redirectPath !== "/login"
          ? { path: "/login", query: { redirect: redirectPath } }
          : { path: "/login" };
      router.push(loginTarget);
    },
    /** 撤销服务端会话后清理当前页面。 */
    async logout(redirectPath?: string) {
      let revoked = true;
      try {
        await logoutApi();
      } catch {
        revoked = false;
      } finally {
        this.logOut(redirectPath);
      }
      return revoked;
    },
    /** 刷新`token` */
    async handRefreshToken() {
      if (refreshPromise) return refreshPromise;

      refreshPromise = withCrossTabRefreshLock(async () => {
        const data = await refreshTokenApi();
        if (data) {
          const { authorizationChanged } = setToken(data.data);
          if (authorizationChanged) {
            publishAuthSessionEvent("authorization-changed");
            usePermissionStoreHook().handleWholeMenus([]);

            const requiredPermissions =
              (router.currentRoute.value.meta?.permissions as
                | Array<string>
                | undefined) ??
              (router.currentRoute.value.meta?.permission as
                | string
                | undefined);

            if (
              requiredPermissions &&
              !hasAnyPermission(this.permissions, requiredPermissions)
            ) {
              const fallbackPath =
                findFirstMenuPath(usePermissionStoreHook().wholeMenus) ??
                "/dashboard";
              if (fallbackPath !== router.currentRoute.value.path) {
                router.replace(fallbackPath);
              }
            }
          }
        }
        return data;
      })
        .catch(error => {
          if (isRefreshSessionInvalidError(error)) {
            publishAuthSessionEvent("session-invalidated");
          }
          throw error;
        })
        .finally(() => {
          refreshPromise = undefined;
        });

      return refreshPromise;
    },
    /** 页面启动时通过 HttpOnly RefreshToken Cookie 恢复内存会话。 */
    async restoreSession() {
      try {
        const result = await this.handRefreshToken();
        return Boolean(result?.success);
      } catch (error) {
        if (isRefreshSessionInvalidError(error)) {
          removeToken();
        }
        return false;
      }
    },
    /** 安装一次不含令牌的跨标签会话事件监听。 */
    initializeSessionEvents() {
      if (sessionEventsInitialized) return;
      sessionEventsInitialized = true;
      onAuthSessionEvent(event => {
        if (
          event.type === "session-ended" ||
          event.type === "session-invalidated"
        ) {
          this.logOut(undefined, false);
          return;
        }

        // A token is never transmitted between tabs. The receiving tab obtains
        // its own access token through the protected cookie refresh endpoint.
        void this.restoreSession();
      });
    }
  }
});

export function useUserStoreHook() {
  return useUserStore(store);
}
