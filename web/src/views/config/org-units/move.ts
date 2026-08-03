import type { OrgUnit } from "@/api/org-unit";

export const getOrgUnitMoveTargetDisabledReason = (
  source: OrgUnit,
  target: OrgUnit,
  currentParentId: number | null
): string | null => {
  if (target.id === currentParentId) return "当前上级";
  if (target.id === source.id) return "不能选择自身";
  if (target.path.startsWith(source.path)) return "不能选择自身下级";
  if (!target.isActive) return "组织已停用";
  if (target.unitType === 3) return "课别不能作为上级组织";
  if (source.unitType <= target.unitType)
    return "移动节点必须是目标上级的下级类型";
  return null;
};

export const countOrgUnitSubtree = (source: OrgUnit): number =>
  1 +
  (source.children ?? []).reduce(
    (total, child) => total + countOrgUnitSubtree(child),
    0
  );
