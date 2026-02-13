const Layout = () => import("@/layout/index.vue");

export default {
  path: "/data-import",
  name: "DataImport",
  component: Layout,
  redirect: "/data-import/import",
  meta: {
    icon: "ri:upload-cloud-2-line",
    title: "数据导入",
    rank: 2
  },
  children: [
    {
      path: "/data-import/import",
      name: "ImportData",
      component: () => import("@/views/data-import/index.vue"),
      meta: {
        icon: "ri:file-upload-line",
        title: "导入数据"
      }
    }
  ]
} satisfies RouteConfigsTable;
