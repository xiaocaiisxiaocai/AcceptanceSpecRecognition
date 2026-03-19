import Cookies from "js-cookie";
import { useUserStoreHook } from "@/store/modules/user";
import { storageLocal, isString } from "@pureadmin/utils";
import { hasAllPermissions, hasPermission } from "./permission";

export interface DataInfo<T> {
  /** token */
  accessToken: string;
  /** `accessToken`的过期时间（时间戳） */
  expires: T;
  /** 用于调用刷新accessToken的接口时所需的token */
  refreshToken: string;
  /** 头像 */
  avatar?: string;
  /** 用户名 */
  username?: string;
  /** 昵称 */
  nickname?: string;
  /** 当前登录用户的角色 */
  roles?: Array<string>;
  /** 当前登录用户的 permission code 集合 */
  permissions?: Array<string>;
}

export const userKey = "user-info";
export const TokenKey = "authorized-token";
/**
 * 通过`multiple-tabs`是否在`cookie`中，判断用户是否已经登录系统，
 * 从而支持多标签页打开已经登录的系统后无需再登录。
 * 浏览器完全关闭后`multiple-tabs`将自动从`cookie`中销毁，
 * 再次打开浏览器需要重新登录系统
 * */
export const multipleTabsKey = "multiple-tabs";

function normalizeStringArray(values?: Array<string>) {
  return [...new Set((values ?? []).filter(Boolean).map(value => value.trim()))]
    .filter(Boolean)
    .sort((left, right) => left.localeCompare(right));
}

function isSameStringArray(left?: Array<string>, right?: Array<string>) {
  const normalizedLeft = normalizeStringArray(left);
  const normalizedRight = normalizeStringArray(right);

  if (normalizedLeft.length !== normalizedRight.length) {
    return false;
  }

  return normalizedLeft.every((value, index) => value === normalizedRight[index]);
}

/** 获取`token` */
export function getToken(): DataInfo<number> {
  const localToken = storageLocal().getItem<DataInfo<number>>(userKey);
  if (localToken?.accessToken && localToken?.refreshToken) {
    return localToken;
  }

  const cookieTokenRaw = Cookies.get(TokenKey);
  if (cookieTokenRaw) {
    try {
      const cookieToken = JSON.parse(cookieTokenRaw) as DataInfo<number>;
      if (cookieToken?.accessToken && cookieToken?.refreshToken) {
        return cookieToken;
      }
    } catch {
      // cookie 可能因超长被截断，忽略并回退到 localStorage
    }
  }

  return (localToken ?? {}) as DataInfo<number>;
}

/**
 * @description 设置`token`以及一些必要信息并采用无感刷新`token`方案
 * 无感刷新：后端返回`accessToken`（访问接口使用的`token`）、`refreshToken`（用于调用刷新`accessToken`的接口时所需的`token`，`refreshToken`的过期时间（比如30天）应大于`accessToken`的过期时间（比如2小时））、`expires`（`accessToken`的过期时间）
 * 将`accessToken`、`expires`、`refreshToken`这三条信息放在key值为authorized-token的cookie里（过期自动销毁）
 * 将`avatar`、`username`、`nickname`、`roles`、`permissions`、`refreshToken`、`expires`这七条信息放在key值为`user-info`的localStorage里（利用`multipleTabsKey`当浏览器完全关闭后自动销毁）
 */
export function setToken(data: DataInfo<Date>) {
  let expires = 0;
  const { accessToken, refreshToken } = data;
  const { isRemembered, loginDay } = useUserStoreHook();
  const previousUserInfo = storageLocal().getItem<DataInfo<number>>(userKey);
  expires = new Date(data.expires).getTime(); // 如果后端直接设置时间戳，将此处代码改为expires = data.expires，然后把上面的DataInfo<Date>改成DataInfo<number>即可
  const cookieString = JSON.stringify({ accessToken, expires, refreshToken });

  expires > 0
    ? Cookies.set(TokenKey, cookieString, {
        expires: (expires - Date.now()) / 86400000
      })
    : Cookies.set(TokenKey, cookieString);

  Cookies.set(
    multipleTabsKey,
    "true",
    isRemembered
      ? {
          expires: loginDay
        }
      : {}
  );

  function setUserKey({ avatar, username, nickname, roles, permissions }) {
    useUserStoreHook().SET_AVATAR(avatar);
    useUserStoreHook().SET_USERNAME(username);
    useUserStoreHook().SET_NICKNAME(nickname);
    useUserStoreHook().SET_ROLES(roles);
    useUserStoreHook().SET_PERMS(permissions);
    storageLocal().setItem(userKey, {
      accessToken,
      refreshToken,
      expires,
      avatar,
      username,
      nickname,
      roles,
      permissions
    });
  }

  const roles = data.roles ?? previousUserInfo?.roles ?? [];
  const permissions = data.permissions ?? previousUserInfo?.permissions ?? [];
  const username = data.username ?? previousUserInfo?.username ?? "";
  const nickname = data.nickname ?? previousUserInfo?.nickname ?? "";
  const avatar = data.avatar ?? previousUserInfo?.avatar ?? "";

  setUserKey({
    avatar,
    username,
    nickname,
    roles,
    permissions
  });

  return {
    authorizationChanged:
      !isSameStringArray(previousUserInfo?.roles, roles) ||
      !isSameStringArray(previousUserInfo?.permissions, permissions)
  };
}

/** 删除`token`以及key值为`user-info`的localStorage信息 */
export function removeToken() {
  Cookies.remove(TokenKey);
  Cookies.remove(multipleTabsKey);
  storageLocal().removeItem(userKey);
}

/** 格式化token（jwt格式） */
export const formatToken = (token: string): string => {
  return "Bearer " + token;
};

/** 是否拥有指定 permission code（页面、按钮、接口统一使用该字段）*/
export const hasPerms = (value: string | Array<string>): boolean => {
  if (!value) return false;
  const { permissions } = useUserStoreHook();
  if (!permissions) return false;
  const isAuths = isString(value)
    ? hasPermission(permissions, value)
    : hasAllPermissions(permissions, value);
  return isAuths ? true : false;
};
