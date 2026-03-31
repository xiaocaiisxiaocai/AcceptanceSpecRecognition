import { getPagePermission, getPageTitle } from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default [
  {
    path: "/config/auth-roles",
    name: "AuthRolesConfigLegacy",
    component: Layout,
    redirect: "/rbac/auth-roles",
    meta: {
      title: getPageTitle("config-auth-roles"),
      showLink: false,
      permissions: getPagePermission("config-auth-roles")
    }
  },
  {
    path: "/config/system-users",
    name: "SystemUsersConfigLegacy",
    component: Layout,
    redirect: "/rbac/system-users",
    meta: {
      title: getPageTitle("config-system-users"),
      showLink: false,
      permissions: getPagePermission("config-system-users")
    }
  },
  {
    path: "/config/org-units",
    name: "OrgUnitsConfigLegacy",
    component: Layout,
    redirect: "/rbac/org-units",
    meta: {
      title: getPageTitle("config-org-units"),
      showLink: false,
      permissions: getPagePermission("config-org-units")
    }
  }
] satisfies RouteConfigsTable[];
