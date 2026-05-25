import { getMenuPermission, getMenuTitle, getPagePermission, getPageTitle } from "../navigation-manifest";
const Layout = () => import("@/layout/index.vue");

export default {
  path: "/config",
  name: "Config",
  component: Layout,
  redirect: "/config/ai-services",
  meta: {
    icon: "ri:settings-3-line",
    title: getMenuTitle("config"),
    rank: 4,
    permissions: getMenuPermission("config")
  },
  children: [
    {
      path: "/config/ai-services",
      name: "AiServicesConfig",
      component: () => import("@/views/config/ai-services/index.vue"),
      meta: {
        icon: "ri:robot-2-line",
        title: getPageTitle("config-ai-services"),
        permissions: getPagePermission("config-ai-services")
      }
    },
    {
      path: "/config/embedding-cache-warmup",
      name: "EmbeddingCacheWarmup",
      component: () => import("@/views/config/embedding-cache-warmup/index.vue"),
      meta: {
        icon: "ri:database-2-line",
        title: getPageTitle("config-embedding-cache-warmup"),
        permissions: getPagePermission("config-embedding-cache-warmup")
      }
    },
    {
      path: "/config/database-backup",
      name: "DatabaseBackup",
      component: () => import("@/views/config/database-backup/index.vue"),
      meta: {
        icon: "ri:database-line",
        title: getPageTitle("config-database-backup"),
        permissions: getPagePermission("config-database-backup")
      }
    },
    {
      path: "/config/prompt-templates",
      name: "PromptTemplates",
      component: () => import("@/views/config/prompt-templates/index.vue"),
      meta: {
        icon: "ri:file-text-line",
        title: getPageTitle("config-prompt-templates"),
        permissions: getPagePermission("config-prompt-templates")
      }
    },
    {
      path: "/config/column-mapping-rules",
      name: "ColumnMappingRules",
      component: () => import("@/views/config/column-mapping-rules/index.vue"),
      meta: {
        icon: "ri:table-line",
        title: getPageTitle("config-column-mapping-rules"),
        permissions: getPagePermission("config-column-mapping-rules")
      }
    }
  ]
} satisfies RouteConfigsTable;
