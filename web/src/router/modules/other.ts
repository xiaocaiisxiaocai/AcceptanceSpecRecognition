import { getMenuPermission, getMenuTitle, getPagePermission, getPageTitle } from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/other",
  name: "Other",
  component: Layout,
  redirect: "/other/audit-logs",
  meta: {
    icon: "ri:apps-line",
    title: getMenuTitle("other"),
    rank: 5,
    permissions: getMenuPermission("other")
  },
  children: [
    {
      path: "/other/audit-logs",
      name: "AuditLogs",
      component: () => import("@/views/other/audit-logs/index.vue"),
      meta: {
        icon: "ri:file-list-3-line",
        title: getPageTitle("other-audit-logs"),
        permissions: getPagePermission("other-audit-logs")
      }
    },
    {
      path: "/other/execution-history",
      name: "ExecutionHistory",
      component: () => import("@/views/other/execution-history/index.vue"),
      meta: {
        icon: "ri:history-line",
        title: getPageTitle("other-execution-history"),
        permissions: getPagePermission("other-execution-history")
      }
    }
  ]
} satisfies RouteConfigsTable;
