export interface BuiltInRoleIdentity {
  code: string;
  isBuiltIn: boolean;
}

export const isProtectedBuiltInRole = (role: BuiltInRoleIdentity) =>
  role.isBuiltIn && role.code.trim().toLowerCase() !== "common";
