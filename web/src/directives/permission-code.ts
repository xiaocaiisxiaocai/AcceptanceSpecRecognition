import { hasPerms } from "@/utils/auth";
import { useUserStoreHook } from "@/store/modules/user";
import {
  effectScope,
  watch,
  type Directive,
  type DirectiveBinding,
  type EffectScope
} from "vue";

type PermissionValue = string | Array<string>;
type PermissionDirectiveElement = HTMLElement & {
  __permissionCodeDisplay__?: string;
  __permissionCodeScope__?: EffectScope;
  __permissionCodeValue__?: PermissionValue;
};

function applyPermissionState(el: PermissionDirectiveElement) {
  const permissionValue = el.__permissionCodeValue__;
  if (!permissionValue) {
    return;
  }

  el.style.display = hasPerms(permissionValue)
    ? (el.__permissionCodeDisplay__ ?? "")
    : "none";
}

export function createPermissionDirective(name: string): Directive {
  return {
    mounted(el: HTMLElement, binding: DirectiveBinding<PermissionValue>) {
      const permissionEl = el as PermissionDirectiveElement;
      const { value } = binding;

      if (!value) {
        throw new Error(
          `[Directive: ${name}]: need permission codes! Like v-${name}="['btn:system-user:create']"`
        );
      }

      permissionEl.__permissionCodeDisplay__ = permissionEl.style.display;
      permissionEl.__permissionCodeValue__ = value;
      applyPermissionState(permissionEl);

      permissionEl.__permissionCodeScope__?.stop();
      const scope = effectScope();
      scope.run(() => {
        watch(
          () => useUserStoreHook().permissions.slice(),
          () => applyPermissionState(permissionEl)
        );
      });
      permissionEl.__permissionCodeScope__ = scope;
    },
    updated(el: HTMLElement, binding: DirectiveBinding<PermissionValue>) {
      const permissionEl = el as PermissionDirectiveElement;
      const { value } = binding;

      if (!value) {
        throw new Error(
          `[Directive: ${name}]: need permission codes! Like v-${name}="['btn:system-user:create']"`
        );
      }

      permissionEl.__permissionCodeValue__ = value;
      applyPermissionState(permissionEl);
    },
    beforeUnmount(el: HTMLElement) {
      const permissionEl = el as PermissionDirectiveElement;
      permissionEl.__permissionCodeScope__?.stop();
      delete permissionEl.__permissionCodeScope__;
      delete permissionEl.__permissionCodeValue__;
      delete permissionEl.__permissionCodeDisplay__;
    }
  };
}
