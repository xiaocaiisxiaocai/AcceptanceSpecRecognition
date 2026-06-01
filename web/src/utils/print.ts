interface PrintFunction {
  extendOptions: Function;
  getStyle: Function;
  setDomHeight: Function;
  toPrint: Function;
}

interface PrintConf {
  styleStr: string;
  setDomHeightArr: string[];
  printBeforeFn: ((args: { doc: Document }) => void) | null;
  printDoneCallBack: (() => void) | null;
}

interface PrintMethods {
  extendOptions<T extends Record<string, unknown>>(
    obj: Record<string, unknown>,
    obj2: T
  ): T;
  init(): void;
  getStyle(): string;
  getHtml(): string;
  writeIframe(content: string): void;
  toPrint(frameWindow: Window): void;
  isDOM(obj: unknown): obj is HTMLElement;
  setDomHeight(arr: string[]): void;
}

interface PrintInstance extends PrintMethods {
  conf: PrintConf;
  dom: HTMLElement;
}

type PrintOptions = Partial<PrintConf>;
type VueDomLike = { $el: HTMLElement };

interface PrintFactory {
  new (
    dom: string | HTMLElement | VueDomLike,
    options?: PrintOptions
  ): PrintInstance;
  (
    dom: string | HTMLElement | VueDomLike,
    options?: PrintOptions
  ): PrintFunction | undefined;
  prototype: PrintMethods;
}

const Print = function (
  this: PrintInstance,
  dom: string | HTMLElement | VueDomLike,
  options: PrintOptions = {}
): PrintFunction | undefined {
  options = options || {};
  if (!(this instanceof (Print as PrintFactory))) {
    return new (Print as PrintFactory)(dom, options);
  }
  this.conf = {
    styleStr: "",
    // Elements that need to dynamically get and set the height
    setDomHeightArr: [],
    // Callback before printing
    printBeforeFn: null,
    // Callback after printing
    printDoneCallBack: null
  };
  for (const key in this.conf) {
    const confKey = key as keyof PrintConf;
    if (key && Object.prototype.hasOwnProperty.call(options, key)) {
      this.conf[confKey] = options[confKey] as never;
    }
  }
  if (typeof dom === "string") {
    const target = document.querySelector<HTMLElement>(dom);
    if (!target) return;
    this.dom = target;
  } else {
    this.dom = this.isDOM(dom) ? dom : dom.$el;
  }
  if (this.conf.setDomHeightArr && this.conf.setDomHeightArr.length) {
    this.setDomHeight(this.conf.setDomHeightArr);
  }
  this.init();
} as PrintFactory;

Print.prototype = {
  /**
   * init
   */
  init: function (this: PrintInstance): void {
    const content = this.getStyle() + this.getHtml();
    this.writeIframe(content);
  },
  /**
   * Configuration property extension
   * @param {Object} obj
   * @param {Object} obj2
   */
  extendOptions: function <T extends Record<string, unknown>>(
    obj: Record<string, unknown>,
    obj2: T
  ): T {
    for (const k in obj2) {
      obj[k] = obj2[k];
    }
    return obj as T;
  },
  /**
    Copy all styles of the original page
  */
  getStyle: function (this: PrintInstance): string {
    let str = "";
    const styles: NodeListOf<Element> = document.querySelectorAll("style,link");
    for (let i = 0; i < styles.length; i++) {
      str += styles[i].outerHTML;
    }
    str += `<style>.no-print{display:none;}${this.conf.styleStr}</style>`;
    return str;
  },
  // form assignment
  getHtml: function (this: PrintInstance): string {
    const inputs = document.querySelectorAll("input");
    const selects = document.querySelectorAll("select");
    const textareas = document.querySelectorAll("textarea");
    const canvass = document.querySelectorAll("canvas");

    for (let k = 0; k < inputs.length; k++) {
      if (inputs[k].type == "checkbox" || inputs[k].type == "radio") {
        if (inputs[k].checked == true) {
          inputs[k].setAttribute("checked", "checked");
        } else {
          inputs[k].removeAttribute("checked");
        }
      } else if (inputs[k].type == "text") {
        inputs[k].setAttribute("value", inputs[k].value);
      } else {
        inputs[k].setAttribute("value", inputs[k].value);
      }
    }

    for (let k2 = 0; k2 < textareas.length; k2++) {
      if (textareas[k2].type == "textarea") {
        textareas[k2].innerHTML = textareas[k2].value;
      }
    }

    for (let k3 = 0; k3 < selects.length; k3++) {
      if (selects[k3].type == "select-one") {
        const child = selects[k3].children;
        for (const i in child) {
          const option = child[i] as HTMLOptionElement | undefined;
          if (option?.tagName == "OPTION") {
            if (option.selected == true) {
              option.setAttribute("selected", "selected");
            } else {
              option.removeAttribute("selected");
            }
          }
        }
      }
    }

    for (let k4 = 0; k4 < canvass.length; k4++) {
      const imageURL = canvass[k4].toDataURL("image/png");
      const img = document.createElement("img");
      img.src = imageURL;
      img.setAttribute("style", "max-width: 100%;");
      img.className = "isNeedRemove";
      canvass[k4].parentNode?.insertBefore(img, canvass[k4].nextElementSibling);
    }

    return this.dom.outerHTML;
  },
  /**
    create iframe
  */
  writeIframe: function (this: PrintInstance, content: string) {
    let w: Window;
    let doc: Document;
    const iframe: HTMLIFrameElement = document.createElement("iframe");
    const f: HTMLIFrameElement = document.body.appendChild(iframe);
    iframe.id = "myIframe";
    iframe.setAttribute(
      "style",
      "position:absolute;width:0;height:0;top:-10px;left:-10px;"
    );

    const frameWindow = f.contentWindow;
    const frameDocument = f.contentDocument;
    if (!frameWindow || !frameDocument) return;

    // eslint-disable-next-line prefer-const
    w = frameWindow;
    doc = frameDocument;
    doc.open();
    doc.write(content);
    doc.close();

    const removes = document.querySelectorAll(".isNeedRemove");
    for (let k = 0; k < removes.length; k++) {
      removes[k].parentNode?.removeChild(removes[k]);
    }

    // eslint-disable-next-line @typescript-eslint/no-this-alias
    const _this = this;
    iframe.onload = function (): void {
      // Before popping, callback
      if (_this.conf.printBeforeFn) {
        _this.conf.printBeforeFn({ doc });
      }
      _this.toPrint(w);
      setTimeout(function () {
        document.body.removeChild(iframe);
        // After popup, callback
        if (_this.conf.printDoneCallBack) {
          _this.conf.printDoneCallBack();
        }
      }, 100);
    };
  },
  /**
    Print
  */
  toPrint: function (frameWindow: Window): void {
    try {
      setTimeout(function () {
        frameWindow.focus();
        try {
          if (!frameWindow.document.execCommand("print", false)) {
            frameWindow.print();
          }
        } catch {
          frameWindow.print();
        }
        frameWindow.close();
      }, 10);
    } catch (err) {
      console.error(err);
    }
  },
  isDOM:
    typeof HTMLElement === "object"
      ? function (obj: unknown): obj is HTMLElement {
          return obj instanceof HTMLElement;
        }
      : function (obj: unknown): obj is HTMLElement {
          return Boolean(
            obj &&
            typeof obj === "object" &&
            (obj as Node).nodeType === 1 &&
            typeof (obj as Node).nodeName === "string"
          );
        },
  /**
   * Set the height of the specified dom element by getting the existing height of the dom element and setting
   * @param {Array} arr
   */
  setDomHeight(arr: string[]) {
    if (arr && arr.length) {
      arr.forEach((name: string) => {
        const domArr = document.querySelectorAll(name);
        domArr.forEach(dom => {
          const element = dom as HTMLElement;
          element.style.height = element.offsetHeight + "px";
        });
      });
    }
  }
};

export default Print;
