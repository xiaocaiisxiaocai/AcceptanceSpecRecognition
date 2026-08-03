import type { RoleFormModel, ScopeType } from "./roleForm.types";

type DynamicPrimaryOrgSubtreeRole = Pick<RoleFormModel, "code" | "isBuiltIn">;

export const supportsDynamicPrimaryOrgSubtree = (
  role: DynamicPrimaryOrgSubtreeRole
) => role.isBuiltIn && role.code.trim().toLowerCase() === "common";

export const normalizeScopeOrgUnitIds = (
  scopeType: ScopeType,
  orgUnitIds: number[]
) => {
  const normalized = [
    ...new Set(orgUnitIds.filter(item => Number.isInteger(item) && item > 0))
  ];

  if (scopeType === 1 || scopeType === 2) return normalized.slice(0, 1);
  if (scopeType === 3) return normalized;
  return [];
};

export const resolveScopeOrgUnitIds = (
  scopeType: ScopeType,
  orgUnitIds: number[],
  fallbackOrgUnitId?: number,
  allowDynamicPrimaryOrgSubtree = false
) => {
  const normalized = normalizeScopeOrgUnitIds(scopeType, orgUnitIds);
  if (
    scopeType === 2 &&
    allowDynamicPrimaryOrgSubtree &&
    normalized.length === 0
  ) {
    return [];
  }

  if (
    (scopeType === 1 || scopeType === 2) &&
    normalized.length === 0 &&
    Number.isInteger(fallbackOrgUnitId) &&
    (fallbackOrgUnitId ?? 0) > 0
  ) {
    return [fallbackOrgUnitId!];
  }

  return normalized;
};

export const resolveRoleScopeOrgUnitIds = (
  role: DynamicPrimaryOrgSubtreeRole,
  scopeType: ScopeType,
  orgUnitIds: number[],
  fallbackOrgUnitId?: number
) =>
  resolveScopeOrgUnitIds(
    scopeType,
    orgUnitIds,
    fallbackOrgUnitId,
    supportsDynamicPrimaryOrgSubtree(role)
  );

export const buildSpecDataScopes = (
  scopeType: ScopeType,
  orgUnitIds: number[]
) => [
  {
    resource: "spec",
    scopeType,
    orgUnitIds: normalizeScopeOrgUnitIds(scopeType, orgUnitIds)
  }
];

export const validateScopeOrgUnitIds = (
  scopeType: ScopeType,
  orgUnitIds: number[],
  allowDynamicPrimaryOrgSubtree = false
) => {
  const validIds = [
    ...new Set(orgUnitIds.filter(item => Number.isInteger(item) && item > 0))
  ];
  if (scopeType === 1 && validIds.length !== 1) {
    return "请选择一个组织节点";
  }
  if (
    scopeType === 2 &&
    validIds.length !== 1 &&
    !(allowDynamicPrimaryOrgSubtree && validIds.length === 0)
  ) {
    return "请选择一个组织节点";
  }
  if (scopeType === 3 && validIds.length === 0) {
    return "请至少选择一个组织节点";
  }
  return null;
};
