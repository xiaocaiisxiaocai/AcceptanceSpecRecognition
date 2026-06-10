import vue from "@vitejs/plugin-vue";
import { viteBuildInfo } from "./info";
import svgLoader from "vite-svg-loader";
import Icons from "unplugin-icons/vite";
import type { PluginOption } from "vite";
import vueJsx from "@vitejs/plugin-vue-jsx";
import tailwindcss from "@tailwindcss/vite";
import removeNoMatch from "vite-plugin-router-warn";

export async function getPluginsList(
  command: "build" | "serve",
  VITE_CDN: boolean,
  VITE_COMPRESSION: ViteCompression,
  VITE_ENABLE_CODE_INSPECTOR: boolean
): Promise<PluginOption[]> {
  const lifecycle = process.env.npm_lifecycle_event;
  const isBuild = command === "build";
  const enableCodeInspector = command === "serve" && VITE_ENABLE_CODE_INSPECTOR;
  const plugins: PluginOption[] = [
    tailwindcss(),
    vue(),
    // jsx、tsx语法支持
    vueJsx(),
    viteBuildInfo(),
    /**
     * 开发环境下移除非必要的vue-router动态路由警告No match found for location with path
     * 非必要具体看 https://github.com/vuejs/router/issues/521 和 https://github.com/vuejs/router/issues/359
     * vite-plugin-router-warn只在开发环境下启用，只处理vue-router文件并且只在服务启动或重启时运行一次，性能消耗可忽略不计
     */
    removeNoMatch(),
    // svg组件化支持
    svgLoader(),
    // 自动按需加载图标
    Icons({
      compiler: "vue3",
      scale: 1
    })
  ];

  if (enableCodeInspector) {
    const { codeInspectorPlugin } = await import("code-inspector-plugin");
    plugins.push(
      codeInspectorPlugin({
        bundler: "vite",
        hideConsole: true
      })
    );
  }

  if (isBuild && VITE_CDN) {
    const { cdn } = await import("./cdn");
    plugins.push(cdn);
  }

  if (isBuild) {
    const { configCompressPlugin } = await import("./compress");
    plugins.push(configCompressPlugin(VITE_COMPRESSION));

    const removeConsole = (await import("vite-plugin-remove-console")).default;
    plugins.push(
      removeConsole({ external: ["src/assets/iconfont/iconfont.js"] })
    );

    if (lifecycle === "report") {
      const { visualizer } = await import("rollup-plugin-visualizer");
      plugins.push(
        visualizer({ open: true, brotliSize: true, filename: "report.html" })
      );
    }
  }

  return plugins;
}
