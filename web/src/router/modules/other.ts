const Layout = () => import("@/layout/index.vue");

export default {
  path: "/other",
  name: "Other",
  component: Layout,
  redirect: "/other/synonyms",
  meta: {
    icon: "ri:apps-line",
    title: "其他",
    rank: 5
  },
  children: [
    {
      path: "/other/synonyms",
      name: "Synonyms",
      component: () => import("@/views/other/synonyms/index.vue"),
      meta: {
        icon: "ri:translate-2",
        title: "同义词管理",
        permissions: ["page:other:synonyms"]
      }
    },
    {
      path: "/other/keywords",
      name: "Keywords",
      component: () => import("@/views/other/keywords/index.vue"),
      meta: {
        icon: "ri:hashtag",
        title: "关键字管理",
        permissions: ["page:other:keywords"]
      }
    },
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

