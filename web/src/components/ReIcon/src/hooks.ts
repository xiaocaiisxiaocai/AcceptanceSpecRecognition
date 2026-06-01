import type { iconType } from "./types";
import {
  h,
  defineComponent,
  isVNode,
  type Component,
  type VNode,
  type ComponentPublicInstance
} from "vue";
import { FontIcon, IconifyIconOnline, IconifyIconOffline } from "../index";

/**
 * 支持 `iconfont`、自定义 `svg` 以及 `iconify` 中所有的图标
 * @see 点击查看文档图标篇 {@link https://pure-admin.cn/pages/icon/}
 * @param icon 必传 图标
 * @param attrs 可选 iconType 属性
 * @returns Component
 */
type RenderableIcon = string | Component | VNode | null | undefined;
type RenderableComponent = Component | ComponentPublicInstance;

export function useRenderIcon(
  icon: RenderableIcon,
  attrs?: iconType
): Component {
  // iconfont
  const ifReg = /^IF-/;
  // typeof icon === "function" 属于SVG
  if (typeof icon === "string" && ifReg.test(icon)) {
    // iconfont
    const name = icon.split(ifReg)[1];
    const iconName = name.slice(
      0,
      name.indexOf(" ") == -1 ? name.length : name.indexOf(" ")
    );
    const iconType = name.slice(name.indexOf(" ") + 1, name.length);
    return defineComponent({
      name: "FontIcon",
      render() {
        return h(FontIcon, {
          icon: iconName,
          iconType,
          ...attrs
        });
      }
    });
  } else if (
    typeof icon === "function" ||
    (typeof icon === "object" &&
      icon !== null &&
      "render" in icon &&
      !isVNode(icon))
  ) {
    // svg
    const iconComponent = icon as RenderableComponent;
    return attrs ? h(iconComponent, { ...attrs }) : iconComponent;
  } else if (icon && typeof icon === "object") {
    return defineComponent({
      name: "OfflineIcon",
      render() {
        return h(IconifyIconOffline, {
          icon: icon,
          ...attrs
        });
      }
    });
  } else {
    // 通过是否存在 : 符号来判断是在线还是本地图标，存在即是在线图标，反之
    return defineComponent({
      name: "Icon",
      render() {
        if (typeof icon !== "string") return null;
        const IconifyIcon = icon.includes(":")
          ? IconifyIconOnline
          : IconifyIconOffline;
        return h(IconifyIcon, {
          icon,
          ...attrs
        });
      }
    });
  }
}
