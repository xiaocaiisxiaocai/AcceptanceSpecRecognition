const Layout = () => import("@/layout/index.vue");

export default {
  path: "/file-compare",
  name: "FileCompare",
  component: Layout,
  redirect: "/file-compare/compare",
  meta: {
    icon: "ri:compare-line",
    title: "文件对比",
    rank: 5
  },
  children: [
    {
      path: "/file-compare/compare",
      name: "FileComparePage",
      component: () => import("@/views/file-compare/index.vue"),
      meta: {
        icon: "ri:compare-line",
        title: "文件对比",
        permissions: ["page:file-compare:index"]
      }
    }
  ]
} satisfies RouteConfigsTable;
