import { h, defineComponent, withDirectives, resolveDirective } from "vue";
import { useMediaQuery } from "@vueuse/core";

/** 封装@vueuse/motion动画库中的自定义指令v-motion */
export default defineComponent({
  name: "Motion",
  props: {
    delay: {
      type: Number,
      default: 50
    }
  },
  setup() {
    return {
      prefersReducedMotion: useMediaQuery("(prefers-reduced-motion: reduce)")
    };
  },
  render() {
    const { delay } = this;
    const content = h(
      "div",
      {},
      {
        default: () => [this.$slots.default?.()]
      }
    );
    if (this.prefersReducedMotion) return content;

    const motion = resolveDirective("motion");
    return withDirectives(content, [
      [
        motion,
        {
          initial: { opacity: 0, y: 100 },
          enter: {
            opacity: 1,
            y: 0,
            transition: {
              delay
            }
          }
        }
      ]
    ]);
  }
});
