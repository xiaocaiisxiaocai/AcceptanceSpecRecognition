import {
  getMenuPermission,
  getMenuTitle,
  getPagePermission,
  getPageTitle
} from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/smart-fill",
  name: "SmartFill",
  component: Layout,
  redirect: "/smart-fill/fill",
  meta: {
    icon: "ri:magic-line",
    title: getMenuTitle("smart-fill"),
    rank: 3,
    permissions: getMenuPermission("smart-fill")
  },
  children: [
    {
      path: "/smart-fill/fill",
      name: "FillData",
      component: () => import("@/views/smart-fill/index.vue"),
      meta: {
        icon: "ri:edit-2-line",
        title: getPageTitle("smart-fill-index"),
        permissions: getPagePermission("smart-fill-index"),
        transition: { name: "fade" },
        keepAlive: true
      }
    }
  ]
} satisfies RouteConfigsTable;
