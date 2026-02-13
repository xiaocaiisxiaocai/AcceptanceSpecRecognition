const Layout = () => import("@/layout/index.vue");

export default {
  path: "/config",
  name: "Config",
  component: Layout,
  redirect: "/config/ai-services",
  meta: {
    icon: "ri:settings-3-line",
    title: "配置管理",
    rank: 4
  },
  children: [
    {
      path: "/config/ai-services",
      name: "AiServicesConfig",
      component: () => import("@/views/config/ai-services/index.vue"),
      meta: {
        icon: "ri:robot-2-line",
        title: "AI服务配置"
      }
    },
    {
      path: "/config/text-processing",
      name: "TextProcessingConfig",
      component: () => import("@/views/config/text-processing/index.vue"),
      meta: {
        icon: "ri:text",
        title: "文本处理配置"
      }
    },
    {
      path: "/config/prompt-templates",
      name: "PromptTemplates",
      component: () => import("@/views/config/prompt-templates/index.vue"),
      meta: {
        icon: "ri:file-text-line",
        title: "Prompt模板"
      }
    },
    {
      path: "/config/column-mapping-rules",
      name: "ColumnMappingRules",
      component: () => import("@/views/config/column-mapping-rules/index.vue"),
      meta: {
        icon: "ri:table-line",
        title: "列映射规则"
      }
    }
  ]
} satisfies RouteConfigsTable;

