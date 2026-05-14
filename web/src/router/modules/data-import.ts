import { getMenuPermission, getMenuTitle, getPagePermission, getPageTitle } from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/data-import",
  name: "DataImport",
  component: Layout,
  redirect: "/data-import/import",
  meta: {
    icon: "ri:upload-cloud-2-line",
    title: getMenuTitle("data-import"),
    rank: 2,
    permissions: getMenuPermission("data-import")
  },
  children: [
    {
      path: "/data-import/import",
      name: "ImportData",
      component: () => import("@/views/data-import/index.vue"),
      meta: {
        icon: "ri:file-upload-line",
        title: getPageTitle("data-import-index"),
        permissions: getPagePermission("data-import-index"),
        keepAlive: true
      }
    }
  ]
} satisfies RouteConfigsTable;
