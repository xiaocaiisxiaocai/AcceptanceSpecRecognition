import { ElMessage } from "element-plus";
import { hasPerms } from "@/utils/auth";

export type PermissionValue = string | Array<string>;

export function hasAnyPerms(permissions: PermissionValue[]): boolean {
  return permissions.some(permission => hasPerms(permission));
}

export function ensurePermission(
  permission: PermissionValue,
  deniedMessage: string
): boolean {
  if (hasPerms(permission)) {
    return true;
  }

  ElMessage.error(deniedMessage);
  return false;
}

export function ensureAnyPermission(
  permissions: PermissionValue[],
  deniedMessage: string
): boolean {
  if (hasAnyPerms(permissions)) {
    return true;
  }

  ElMessage.error(deniedMessage);
  return false;
}
