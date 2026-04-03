import { getMenuPermission, getMenuTitle, getPagePermission, getPageTitle } from "../navigation-manifest";
const { VITE_HIDE_HOME } = import.meta.env;
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/",
  name: "Home",
  component: Layout,
  redirect: "/dashboard",
  meta: {
    icon: "ri:home-4-line",
    title: getMenuTitle("home"),
    rank: 0,
    permissions: getMenuPermission("home")
  },
  children: [
    {
      path: "/dashboard",
      name: "Dashboard",
      component: () => import("@/views/dashboard/index.vue"),
      meta: {
        title: getPageTitle("home-dashboard"),
        icon: "ri:dashboard-3-line",
        permissions: getPagePermission("home-dashboard"),
        showLink: VITE_HIDE_HOME === "true" ? false : true
      }
    },
    {
      path: "/welcome",
      name: "Welcome",
      redirect: "/dashboard",
      meta: {
        title: "欢迎页",
        showLink: false
      }
    }
  ]
} satisfies RouteConfigsTable;
