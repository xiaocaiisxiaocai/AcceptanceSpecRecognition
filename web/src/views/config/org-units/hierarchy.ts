export type OrgUnitType = 0 | 1 | 2 | 3;

export const orgUnitTypeLabels: Record<OrgUnitType, string> = {
  0: "公司",
  1: "事业部",
  2: "部门",
  3: "课别"
};

export const getAllowedChildTypes = (
  parentType: OrgUnitType
): OrgUnitType[] => {
  if (!Number.isInteger(parentType) || parentType < 0 || parentType >= 3) {
    return [];
  }

  return ([1, 2, 3] as OrgUnitType[]).filter(type => type > parentType);
};
