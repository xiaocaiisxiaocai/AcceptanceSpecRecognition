import { useUserStoreHook } from "@/store/modules/user";
import { storageLocal, isString } from "@pureadmin/utils";
import { hasAllPermissions, hasPermission } from "./permission";

export interface DataInfo<T> {
  /** Short-lived access token. This value is kept in memory only. */
  accessToken: string;
  /** Access token expiry. */
  expires: T;
  avatar?: string;
  username?: string;
  nickname?: string;
  roleCode?: string;
  permissions?: Array<string>;
}

type PersistedUserInfo = Omit<DataInfo<number>, "accessToken" | "expires">;

export const userKey = "user-info";
/** Legacy keys are exported only so cleanup/migration code can remove them. */
export const TokenKey = "authorized-token";
export const multipleTabsKey = "multiple-tabs";
export const CsrfCookieName = "acceptance-csrf";

let accessToken = "";
let accessTokenExpires = 0;

function normalizeStringArray(values?: Array<string>) {
  return [...new Set((values ?? []).filter(Boolean).map(value => value.trim()))]
    .filter(Boolean)
    .sort((left, right) => left.localeCompare(right));
}

function isSameStringArray(left?: Array<string>, right?: Array<string>) {
  const normalizedLeft = normalizeStringArray(left);
  const normalizedRight = normalizeStringArray(right);

  return (
    normalizedLeft.length === normalizedRight.length &&
    normalizedLeft.every((value, index) => value === normalizedRight[index])
  );
}

function normalizeStringValue(value?: string) {
  return (value ?? "").trim();
}

function removeCookie(name: string) {
  if (typeof document === "undefined") return;
  document.cookie = `${encodeURIComponent(name)}=; Max-Age=0; Path=/; SameSite=Lax`;
}

/** A readable CSRF cookie is the browser-side marker for a refresh session. */
export function hasBrowserRefreshSession(
  cookieSource = typeof document === "undefined" ? "" : document.cookie
) {
  const cookiePrefix = `${encodeURIComponent(CsrfCookieName)}=`;
  return cookieSource
    .split(";")
    .some(cookie => cookie.trimStart().startsWith(cookiePrefix));
}

/** Remove credentials persisted by pre-migration clients. */
export function removeLegacyPersistedTokens() {
  removeCookie(TokenKey);
  removeCookie(multipleTabsKey);
  const persisted = storageLocal().getItem<Record<string, unknown>>(userKey);
  if (!persisted) return;

  const safe = { ...persisted };
  delete safe.accessToken;
  delete safe.refreshToken;
  delete safe.expires;
  storageLocal().setItem(userKey, safe);
}

/** Return the current page's in-memory access token. */
export function getToken(): DataInfo<number> {
  const user =
    storageLocal().getItem<PersistedUserInfo>(userKey) ??
    ({} as PersistedUserInfo);
  return {
    ...user,
    accessToken,
    expires: accessTokenExpires
  };
}

/** Store the access token in memory and only non-secret profile data locally. */
export function setToken(data: DataInfo<Date>) {
  const expires = new Date(data.expires).getTime();
  const previousUserInfo =
    storageLocal().getItem<PersistedUserInfo>(userKey) ?? undefined;

  accessToken = data.accessToken;
  accessTokenExpires = Number.isFinite(expires) ? expires : 0;
  removeLegacyPersistedTokens();

  const roleCode = data.roleCode ?? previousUserInfo?.roleCode ?? "";
  const permissions = data.permissions ?? previousUserInfo?.permissions ?? [];
  const username = data.username ?? previousUserInfo?.username ?? "";
  const nickname = data.nickname ?? previousUserInfo?.nickname ?? "";
  const avatar = data.avatar ?? previousUserInfo?.avatar ?? "";

  useUserStoreHook().SET_AVATAR(avatar);
  useUserStoreHook().SET_USERNAME(username);
  useUserStoreHook().SET_NICKNAME(nickname);
  useUserStoreHook().SET_ROLE_CODE(roleCode);
  useUserStoreHook().SET_PERMS(permissions);
  storageLocal().setItem<PersistedUserInfo>(userKey, {
    avatar,
    username,
    nickname,
    roleCode,
    permissions
  });

  return {
    authorizationChanged:
      normalizeStringValue(previousUserInfo?.roleCode) !==
        normalizeStringValue(roleCode) ||
      !isSameStringArray(previousUserInfo?.permissions, permissions)
  };
}

/** Clear the in-memory token and non-secret cached profile. */
export function removeToken() {
  accessToken = "";
  accessTokenExpires = 0;
  removeLegacyPersistedTokens();
  storageLocal().removeItem(userKey);
}

export const formatToken = (token: string): string => `Bearer ${token}`;

export const hasPerms = (value: string | Array<string>): boolean => {
  if (!value) return false;
  const { permissions } = useUserStoreHook();
  if (!permissions) return false;
  return isString(value)
    ? hasPermission(permissions, value)
    : hasAllPermissions(permissions, value);
};
