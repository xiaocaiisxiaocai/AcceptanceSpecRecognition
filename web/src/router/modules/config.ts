const Layout = () => import("@/layout/index.vue");

export default {
  path: "/config",
  name: "Config",
  component: Layout,
  redirect: "/config/ai-services",
  meta: {
    icon: "ri:settings-3-line",
    title: "配置管理",
    rank: 4,
    permissions: ["menu:config"]
  },
  children: [
    {
      path: "/config/ai-services",
      name: "AiServicesConfig",
      component: () => import("@/views/config/ai-services/index.vue"),
      meta: {
        icon: "ri:robot-2-line",
        title: "AI服务配置",
        permissions: ["page:config:ai-services"]
      }
    },
    {
      path: "/config/matching-knowledge",
      name: "MatchingKnowledgeConfig",
      component: () => import("@/views/config/matching-knowledge/index.vue"),
      meta: {
        icon: "ri:book-open-line",
        title: "匹配知识配置",
        permissions: ["page:config:matching-knowledge"]
      }
    },
    {
      path: "/config/prompt-templates",
      name: "PromptTemplates",
      component: () => import("@/views/config/prompt-templates/index.vue"),
      meta: {
        icon: "ri:file-text-line",
        title: "Prompt模板",
        permissions: ["page:config:prompt-templates"]
      }
    },
    {
      path: "/config/column-mapping-rules",
      name: "ColumnMappingRules",
      component: () => import("@/views/config/column-mapping-rules/index.vue"),
      meta: {
        icon: "ri:table-line",
        title: "列映射规则",
        permissions: ["page:config:column-mapping-rules"]
      }
    }
  ]
} satisfies RouteConfigsTable;
