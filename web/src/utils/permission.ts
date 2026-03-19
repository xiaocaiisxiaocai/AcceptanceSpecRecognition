function normalizePermissionCode(value: string) {
  return value.trim().toLowerCase();
}

export function matchPermission(granted: string, required: string): boolean {
  const normalizedGranted = normalizePermissionCode(granted);
  const normalizedRequired = normalizePermissionCode(required);
  const grantedSeg = normalizedGranted.split(":");
  const requiredSeg = normalizedRequired.split(":");

  if (grantedSeg.length !== requiredSeg.length) {
    return false;
  }

  return grantedSeg.every(
    (segment, index) => segment === "*" || segment === requiredSeg[index]
  );
}

export function hasPermission(
  grantedPermissions: Array<string>,
  requiredPermission: string
): boolean {
  const normalizedRequired = requiredPermission?.trim();
  if (!normalizedRequired) {
    return false;
  }

  const normalizedGranted = (grantedPermissions ?? [])
    .filter(Boolean)
    .map(item => item.trim())
    .filter(Boolean);

  if (!normalizedGranted.length) {
    return false;
  }

  return normalizedGranted.some(
    grantedCode =>
      normalizePermissionCode(grantedCode) ===
        normalizePermissionCode(normalizedRequired) ||
      grantedCode === "*:*:*" ||
      matchPermission(grantedCode, normalizedRequired)
  );
}

export function hasAnyPermission(
  grantedPermissions: Array<string>,
  required: string | Array<string> | undefined
): boolean {
  if (!required) {
    return true;
  }

  const requiredList = (Array.isArray(required) ? required : [required]).filter(
    item => !!item && !!item.trim()
  );

  if (!requiredList.length) {
    return true;
  }

  return requiredList.some(requiredPermission =>
    hasPermission(grantedPermissions, requiredPermission)
  );
}

export function hasAllPermissions(
  grantedPermissions: Array<string>,
  required: string | Array<string>
): boolean {
  if (!required) {
    return false;
  }

  const requiredList = (Array.isArray(required) ? required : [required]).filter(
    item => !!item && !!item.trim()
  );

  if (!requiredList.length) {
    return false;
  }

  return requiredList.every(requiredPermission =>
    hasPermission(grantedPermissions, requiredPermission)
  );
}
