import {
  getMenuPermission,
  getMenuTitle,
  getPagePermission,
  getPageTitle
} from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/batch-reply",
  name: "BatchReply",
  component: Layout,
  redirect: "/batch-reply/index",
  meta: {
    icon: "ri:file-copy-2-line",
    title: getMenuTitle("batch-reply"),
    rank: 4,
    permissions: getMenuPermission("batch-reply")
  },
  children: [
    {
      path: "/batch-reply/index",
      name: "BatchReplyPage",
      component: () => import("@/views/batch-reply/index.vue"),
      meta: {
        icon: "ri:file-copy-2-line",
        title: getPageTitle("batch-reply-index"),
        permissions: getPagePermission("batch-reply-index"),
        keepAlive: true
      }
    }
  ]
} satisfies RouteConfigsTable;
