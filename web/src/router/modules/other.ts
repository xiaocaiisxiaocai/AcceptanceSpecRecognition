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
        title: "同义词管理"
      }
    },
    {
      path: "/other/keywords",
      name: "Keywords",
      component: () => import("@/views/other/keywords/index.vue"),
      meta: {
        icon: "ri:hashtag",
        title: "关键字管理"
      }
    },
    {
      path: "/other/history",
      name: "OperationHistory",
      component: () => import("@/views/other/history/index.vue"),
      meta: {
        icon: "ri:history-line",
        title: "操作历史"
      }
    }
  ]
} satisfies RouteConfigsTable;

