// @ts-nocheck
import { h, defineComponent, type Component, type PropType } from "vue";
import type { IconifyIcon as IconifyIconData } from "@iconify/vue";
import { Icon as IconifyIcon, addIcon } from "@iconify/vue/dist/offline";

// Iconify Icon在Vue里本地使用（用于内网环境）
export default defineComponent({
  name: "IconifyIconOffline",
  components: { IconifyIcon },
  props: {
    icon: {
      type: [String, Object, Function] as PropType<
        string | IconifyIconData | Component | null
      >,
      default: null
    }
  },
  render() {
    if (this.icon && typeof this.icon === "object") {
      addIcon(this.icon, this.icon);
    }
    const attrs = this.$attrs;
    if (typeof this.icon === "string") {
      return h(
        IconifyIcon,
        {
          icon: this.icon,
          "aria-hidden": false,
          style: attrs?.style
            ? Object.assign(attrs.style, { outline: "none" })
            : { outline: "none" },
          ...attrs
        },
        {
          default: () => []
        }
      );
    } else if (this.icon) {
      return h(
        this.icon,
        {
          "aria-hidden": false,
          style: attrs?.style
            ? Object.assign(attrs.style, { outline: "none" })
            : { outline: "none" },
          ...attrs
        },
        {
          default: () => []
        }
      );
    }
    return null;
  }
});
