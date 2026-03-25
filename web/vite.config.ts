import { getPluginsList } from "./build/plugins";
import { include, exclude } from "./build/optimize";
import { type UserConfigExport, type ConfigEnv, loadEnv } from "vite";
import {
  root,
  alias,
  wrapperEnv,
  pathResolve,
  __APP_INFO__
} from "./build/utils";

export default async ({ mode }: ConfigEnv): Promise<UserConfigExport> => {
  const {
    VITE_CDN,
    VITE_PORT,
    VITE_COMPRESSION,
    VITE_PUBLIC_PATH,
    VITE_ENABLE_CODE_INSPECTOR,
    VITE_DEV_WARMUP
  } = wrapperEnv(loadEnv(mode, root));
  const enableDevWarmup = mode === "development" && VITE_DEV_WARMUP;

  return {
    base: VITE_PUBLIC_PATH,
    root,
    resolve: {
      alias
    },
    // 服务端渲染
    server: {
      // 端口号
      port: VITE_PORT,
      host: "0.0.0.0",
      // 本地跨域代理 https://cn.vitejs.dev/config/server-options.html#server-proxy
      proxy: {
        "/api": {
          target: "http://localhost:5290",
          changeOrigin: true,
          rewrite: path => path,
          // SSE 长连接需要禁用代理超时，否则 LLM 流式输出会被提前断开
          timeout: 0
        },
        "/login": {
          target: "http://localhost:5290",
          changeOrigin: true
        },
        "/refresh-token": {
          target: "http://localhost:5290",
          changeOrigin: true
        },
        "/get-async-routes": {
          target: "http://localhost:5290",
          changeOrigin: true
        }
      },
      ...(enableDevWarmup
        ? {
            // 仅预热登录和首页最小链路，避免全量扫描 views/components 拖慢开发服务启动
            warmup: {
              clientFiles: [
                "./index.html",
                "./src/main.ts",
                "./src/App.vue",
                "./src/views/login/index.vue",
                "./src/views/dashboard/index.vue"
              ]
            }
          }
        : {})
    },
    plugins: await getPluginsList(
      VITE_CDN,
      VITE_COMPRESSION,
      VITE_ENABLE_CODE_INSPECTOR
    ),
    // https://cn.vitejs.dev/config/dep-optimization-options.html#dep-optimization-options
    optimizeDeps: {
      include,
      exclude
    },
    build: {
      // https://cn.vitejs.dev/guide/build.html#browser-compatibility
      target: "es2015",
      sourcemap: false,
      // 消除打包大小超过500kb警告
      chunkSizeWarningLimit: 4000,
      rollupOptions: {
        input: {
          index: pathResolve("./index.html", import.meta.url)
        },
        // 静态资源分类打包
        output: {
          chunkFileNames: "static/js/[name]-[hash].js",
          entryFileNames: "static/js/[name]-[hash].js",
          assetFileNames: "static/[ext]/[name]-[hash].[ext]"
        }
      }
    },
    define: {
      __INTLIFY_PROD_DEVTOOLS__: false,
      __APP_INFO__: JSON.stringify(__APP_INFO__)
    }
  };
};
