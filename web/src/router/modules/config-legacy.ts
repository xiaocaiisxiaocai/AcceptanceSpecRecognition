const Layout = () => import("@/layout/index.vue");

export default [
  {
    path: "/config/auth-roles",
    name: "AuthRolesConfigLegacy",
    component: Layout,
    redirect: "/rbac/auth-roles",
    meta: {
      title: "角色管理",
      showLink: false,
      permissions: ["page:config:auth-roles"]
    }
  },
  {
    path: "/config/system-users",
    name: "SystemUsersConfigLegacy",
    component: Layout,
    redirect: "/rbac/system-users",
    meta: {
      title: "系统用户",
      showLink: false,
      permissions: ["page:config:system-users"]
    }
  },
  {
    path: "/config/org-units",
    name: "OrgUnitsConfigLegacy",
    component: Layout,
    redirect: "/rbac/org-units",
    meta: {
      title: "组织管理",
      showLink: false,
      permissions: ["page:config:org-units"]
    }
  }
] satisfies RouteConfigsTable[];
