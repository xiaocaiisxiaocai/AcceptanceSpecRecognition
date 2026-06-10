import { defineStore } from "pinia";
import {
  type userType,
  store,
  router,
  resetRouter,
  routerArrays,
  storageLocal
} from "../utils";
import type { LoginRequestPayload, RefreshTokenPayload } from "../types";
import {
  type UserResult,
  type RefreshTokenResult,
  getLogin,
  refreshTokenApi
} from "@/api/user";
import { useMultiTagsStoreHook } from "./multiTags";
import { usePermissionStoreHook } from "./permission";
import { type DataInfo, setToken, removeToken, userKey } from "@/utils/auth";
import { hasAnyPermission } from "@/utils/permission";

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
            if (data?.success) setToken(data.data);
            resolve(data);
          })
          .catch(error => {
            reject(error);
          });
      });
    },
    /** 前端登出（不调用接口） */
    logOut(redirectPath?: string) {
      this.avatar = "";
      this.username = "";
      this.nickname = "";
      this.roleCode = "";
      this.permissions = [];
      removeToken();
      usePermissionStoreHook().clearAllCachePage();
      useMultiTagsStoreHook().handleTags("equal", [...routerArrays]);
      resetRouter();
      const loginTarget =
        typeof redirectPath === "string" &&
        redirectPath.startsWith("/") &&
        redirectPath !== "/login"
          ? { path: "/login", query: { redirect: redirectPath } }
          : { path: "/login" };
      router.push(loginTarget);
    },
    /** 刷新`token` */
    async handRefreshToken(data: RefreshTokenPayload) {
      return new Promise<RefreshTokenResult>((resolve, reject) => {
        refreshTokenApi(data)
          .then(data => {
            if (data) {
              const { authorizationChanged } = setToken(data.data);
              if (authorizationChanged) {
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
              resolve(data);
            }
          })
          .catch(error => {
            reject(error);
          });
      });
    }
  }
});

export function useUserStoreHook() {
  return useUserStore(store);
}
