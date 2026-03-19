const Layout = () => import("@/layout/index.vue");

export default {
  path: "/base-data",
  name: "BaseData",
  component: Layout,
  redirect: "/base-data/customers",
  meta: {
    icon: "ri:database-2-line",
    title: "基础数据",
    rank: 1
  },
  children: [
    {
      path: "/base-data/customers",
      name: "Customers",
      component: () => import("@/views/base-data/customers/index.vue"),
      meta: {
        icon: "ri:user-line",
        title: "客户管理",
        permissions: ["page:base-data:customers"]
      }
    },
    {
      path: "/base-data/processes",
      name: "Processes",
      component: () => import("@/views/base-data/processes/index.vue"),
      meta: {
        icon: "ri:git-merge-line",
        title: "制程管理",
        permissions: ["page:base-data:processes"]
      }
    },
    {
      path: "/base-data/machine-models",
      name: "MachineModels",
      component: () => import("@/views/base-data/machine-models/index.vue"),
      meta: {
        icon: "ri:cpu-line",
        title: "机型管理",
        permissions: ["page:base-data:machine-models"]
      }
    },
    {
      path: "/base-data/specs",
      name: "AcceptanceSpecs",
      component: () => import("@/views/base-data/specs/index.vue"),
      meta: {
        icon: "ri:file-list-3-line",
        title: "验收规格",
        permissions: ["page:base-data:specs"]
      }
    }
  ]
} satisfies RouteConfigsTable;
