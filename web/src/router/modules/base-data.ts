const Layout = () => import("@/layout/index.vue");

export default {
  path: "/base-data",
  name: "BaseData",
  component: Layout,
  redirect: "/base-data/customers",
  meta: {
    icon: "ri/database-2-line",
    title: "基础数据",
    rank: 1
  },
  children: [
    {
      path: "/base-data/customers",
      name: "Customers",
      component: () => import("@/views/base-data/customers/index.vue"),
      meta: {
        icon: "ri/user-line",
        title: "客户管理"
      }
    },
    {
      path: "/base-data/processes",
      name: "Processes",
      component: () => import("@/views/base-data/processes/index.vue"),
      meta: {
        icon: "ri/settings-3-line",
        title: "制程管理"
      }
    },
    {
      path: "/base-data/specs",
      name: "AcceptanceSpecs",
      component: () => import("@/views/base-data/specs/index.vue"),
      meta: {
        icon: "ri/file-list-3-line",
        title: "验收规格"
      }
    }
  ]
} satisfies RouteConfigsTable;
