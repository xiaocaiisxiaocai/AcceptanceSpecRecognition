const Layout = () => import("@/layout/index.vue");

export default {
  path: "/smart-fill",
  name: "SmartFill",
  component: Layout,
  redirect: "/smart-fill/fill",
  meta: {
    icon: "ri:magic-line",
    title: "智能填充",
    rank: 3
  },
  children: [
    {
      path: "/smart-fill/fill",
      name: "FillData",
      component: () => import("@/views/smart-fill/index.vue"),
      meta: {
        icon: "ri:edit-2-line",
        title: "填充数据",
        permissions: ["page:smart-fill:index"]
      }
    }
  ]
} satisfies RouteConfigsTable;
