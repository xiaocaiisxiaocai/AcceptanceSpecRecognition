import {
  getMenuPermission,
  getMenuTitle,
  getPagePermission,
  getPageTitle
} from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/rbac",
  name: "RbacCenter",
  component: Layout,
  redirect: "/rbac/auth-roles",
  meta: {
    icon: "ri:shield-keyhole-line",
    title: getMenuTitle("rbac"),
    rank: 4.5,
    permissions: getMenuPermission("rbac")
  },
  children: [
    {
      path: "/rbac/auth-roles",
      name: "AuthRolesConfig",
      component: () => import("@/views/config/auth-roles/index.vue"),
      meta: {
        icon: "ri:shield-user-line",
        title: getPageTitle("config-auth-roles"),
        permissions: getPagePermission("config-auth-roles")
      }
    },
    {
      path: "/rbac/system-users",
      name: "SystemUsersConfig",
      component: () => import("@/views/config/system-users/index.vue"),
      meta: {
        icon: "ri:admin-line",
        title: getPageTitle("config-system-users"),
        permissions: getPagePermission("config-system-users")
      }
    },
    {
      path: "/rbac/org-units",
      name: "OrgUnitsConfig",
      component: () => import("@/views/config/org-units/index.vue"),
      meta: {
        icon: "ri:git-merge-line",
        title: getPageTitle("config-org-units"),
        permissions: getPagePermission("config-org-units")
      }
    },
    {
      path: "/rbac/permissions",
      name: "AuthPermissionsView",
      component: () => import("@/views/rbac/permissions/index.vue"),
      meta: {
        icon: "ri:key-2-line",
        title: getPageTitle("rbac-permissions"),
        permissions: getPagePermission("rbac-permissions")
      }
    }
  ]
} satisfies RouteConfigsTable;
