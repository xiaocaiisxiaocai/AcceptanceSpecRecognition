const { VITE_HIDE_HOME } = import.meta.env;
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/",
  name: "Home",
  component: Layout,
  redirect: "/dashboard",
  meta: {
    icon: "ri:home-4-line",
    title: "首页",
    rank: 0
  },
  children: [
    {
      path: "/dashboard",
      name: "Dashboard",
      component: () => import("@/views/dashboard/index.vue"),
      meta: {
        title: "仪表盘",
        icon: "ri:dashboard-3-line",
        permissions: ["page:home:dashboard"],
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
