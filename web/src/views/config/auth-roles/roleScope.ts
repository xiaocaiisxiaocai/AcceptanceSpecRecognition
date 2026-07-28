import type { ScopeType } from "./roleForm.types";

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

export const validateScopeOrgUnitIds = (
  scopeType: ScopeType,
  orgUnitIds: number[]
) => {
  const validIds = [
    ...new Set(orgUnitIds.filter(item => Number.isInteger(item) && item > 0))
  ];
  if ((scopeType === 1 || scopeType === 2) && validIds.length !== 1) {
    return "请选择一个组织节点";
  }
  if (scopeType === 3 && validIds.length === 0) {
    return "请至少选择一个组织节点";
  }
  return null;
};
