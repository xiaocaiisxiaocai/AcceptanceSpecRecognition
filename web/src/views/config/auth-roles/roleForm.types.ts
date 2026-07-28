export type ScopeType = 0 | 1 | 2 | 3 | 4;

export interface RoleFormModel {
  id: number;
  code: string;
  name: string;
  description: string;
  isBuiltIn: boolean;
  isActive: boolean;
  permissionCodes: string[];
  scopeType: ScopeType;
  scopeOrgUnitIds: number[];
}

export interface RoleFormOption<T extends number = number> {
  label: string;
  value: T;
  disabled?: boolean;
}
