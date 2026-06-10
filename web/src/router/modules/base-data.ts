import {
  getMenuPermission,
  getMenuTitle,
  getPagePermission,
  getPageTitle
} from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/base-data",
  name: "BaseData",
  component: Layout,
  redirect: "/base-data/customers",
  meta: {
    icon: "ri:database-2-line",
    title: getMenuTitle("base-data"),
    rank: 1,
    permissions: getMenuPermission("base-data")
  },
  children: [
    {
      path: "/base-data/customers",
      name: "Customers",
      component: () => import("@/views/base-data/customers/index.vue"),
      meta: {
        icon: "ri:user-line",
        title: getPageTitle("base-data-customers"),
        permissions: getPagePermission("base-data-customers")
      }
    },
    {
      path: "/base-data/processes",
      name: "Processes",
      component: () => import("@/views/base-data/processes/index.vue"),
      meta: {
        icon: "ri:git-merge-line",
        title: getPageTitle("base-data-processes"),
        permissions: getPagePermission("base-data-processes")
      }
    },
    {
      path: "/base-data/machine-models",
      name: "MachineModels",
      component: () => import("@/views/base-data/machine-models/index.vue"),
      meta: {
        icon: "ri:cpu-line",
        title: getPageTitle("base-data-machine-models"),
        permissions: getPagePermission("base-data-machine-models")
      }
    },
    {
      path: "/base-data/specs",
      name: "AcceptanceSpecs",
      component: () => import("@/views/base-data/specs/index.vue"),
      meta: {
        icon: "ri:file-list-3-line",
        title: getPageTitle("base-data-specs"),
        permissions: getPagePermission("base-data-specs")
      }
    }
  ]
} satisfies RouteConfigsTable;
