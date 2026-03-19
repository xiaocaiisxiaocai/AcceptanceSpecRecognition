const Layout = () => import("@/layout/index.vue");

export default {
  path: "/rbac",
  name: "RbacCenter",
  component: Layout,
  redirect: "/rbac/auth-roles",
  meta: {
    icon: "ri:shield-keyhole-line",
    title: "权限中心",
    rank: 4.5
  },
  children: [
    {
      path: "/rbac/auth-roles",
      name: "AuthRolesConfig",
      component: () => import("@/views/config/auth-roles/index.vue"),
      meta: {
        icon: "ri:shield-user-line",
        title: "角色管理",
        permissions: ["page:config:auth-roles"]
      }
    },
    {
      path: "/rbac/system-users",
      name: "SystemUsersConfig",
      component: () => import("@/views/config/system-users/index.vue"),
      meta: {
        icon: "ri:admin-line",
        title: "系统用户",
        permissions: ["page:config:system-users"]
      }
    },
    {
      path: "/rbac/org-units",
      name: "OrgUnitsConfig",
      component: () => import("@/views/config/org-units/index.vue"),
      meta: {
        icon: "ri:git-merge-line",
        title: "组织管理",
        permissions: ["page:config:org-units"]
      }
    },
    {
      path: "/rbac/permissions",
      name: "AuthPermissionsView",
      component: () => import("@/views/rbac/permissions/index.vue"),
      meta: {
        icon: "ri:key-2-line",
        title: "权限字典",
        permissions: ["page:rbac:permissions"]
      }
    }
  ]
} satisfies RouteConfigsTable;
