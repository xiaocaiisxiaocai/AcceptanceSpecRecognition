const Layout = () => import("@/layout/index.vue");

export default {
  path: "/smart-fill",
  name: "SmartFill",
  component: Layout,
  redirect: "/smart-fill/fill",
  meta: {
    icon: "ri/magic-line",
    title: "智能填充",
    rank: 3
  },
  children: [
    {
      path: "/smart-fill/fill",
      name: "FillData",
      component: () => import("@/views/smart-fill/index.vue"),
      meta: {
        icon: "ri/file-edit-line",
        title: "填充数据"
      }
    }
  ]
} satisfies RouteConfigsTable;
