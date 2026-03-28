const Layout = () => import("@/layout/index.vue");

export default {
  path: "/other",
  name: "Other",
  component: Layout,
  redirect: "/other/audit-logs",
  meta: {
    icon: "ri:apps-line",
    title: "其他",
    rank: 5,
    permissions: ["menu:other"]
  },
  children: [
    {
      path: "/other/audit-logs",
      name: "AuditLogs",
      component: () => import("@/views/other/audit-logs/index.vue"),
      meta: {
        icon: "ri:file-list-3-line",
        title: "审计日志",
        permissions: ["page:other:audit-logs"]
      }
    }
  ]
} satisfies RouteConfigsTable;
