import {
  getMenuPermission,
  getMenuTitle,
  getPagePermission,
  getPageTitle
} from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/file-compare",
  name: "FileCompare",
  component: Layout,
  redirect: "/file-compare/compare",
  meta: {
    icon: "ri:arrow-left-right-line",
    title: getMenuTitle("file-compare"),
    rank: 5,
    permissions: getMenuPermission("file-compare")
  },
  children: [
    {
      path: "/file-compare/compare",
      name: "FileComparePage",
      component: () => import("@/views/file-compare/index.vue"),
      meta: {
        icon: "ri:arrow-left-right-line",
        title: getPageTitle("file-compare-index"),
        permissions: getPagePermission("file-compare-index"),
        keepAlive: true
      }
    }
  ]
} satisfies RouteConfigsTable;
