const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

const edgePath =
  process.env.EDGE_PATH ||
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const baseUrl = process.env.UI_BASE_URL || "http://localhost:8849";
const port = Number(
  process.env.EDGE_CDP_PORT || 9300 + Math.floor(Math.random() * 500)
);
const outputDir = path.resolve("output/playwright/ui-final");
const userDataDir = path.resolve("output/playwright/.edge-ui-final-profile");

const pages = [
  ["dashboard", "/dashboard"],
  ["smart-fill", "/smart-fill/fill"],
  ["data-import", "/data-import/import"],
  ["batch-reply", "/batch-reply/index"],
  ["file-compare", "/file-compare/compare"],
  ["customers", "/base-data/customers"],
  ["specs", "/base-data/specs"],
  ["audit-logs", "/other/audit-logs"]
];

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    throw new Error(`${url} -> ${response.status}`);
  }
  return response.json();
}

async function loginFromNode() {
  const response = await fetchJson("http://localhost:5291/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "admin", password: "admin" })
  });
  if (!response.success || !response.data) {
    throw new Error("Login failed");
  }
  return response.data;
}

class Cdp {
  constructor(wsUrl) {
    this.nextId = 1;
    this.pending = new Map();
    this.ws = new WebSocket(wsUrl);
    this.ws.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      if (!message.id) return;
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result);
    });
  }

  async open() {
    if (this.ws.readyState === WebSocket.OPEN) return;
    await new Promise((resolve, reject) => {
      this.ws.addEventListener("open", resolve, { once: true });
      this.ws.addEventListener("error", reject, { once: true });
    });
  }

  send(method, params = {}, sessionId) {
    const id = this.nextId++;
    const payload = { id, method, params };
    if (sessionId) payload.sessionId = sessionId;
    this.ws.send(JSON.stringify(payload));
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
    });
  }

  close() {
    this.ws.close();
  }
}

function layout(theme) {
  return {
    layout: "vertical",
    theme,
    darkMode: theme === "dark",
    sidebarStatus: true,
    epThemeColor: "#7C3AED",
    themeColor: "light",
    overallStyle: theme,
    grey: false,
    weak: false,
    hideTabs: false,
    hideFooter: true,
    showLogo: true,
    multiTagsCache: true
  };
}

async function waitForCdp() {
  const endpoint = `http://127.0.0.1:${port}/json/version`;
  for (let index = 0; index < 80; index++) {
    try {
      return await fetchJson(endpoint);
    } catch {
      await delay(250);
    }
  }
  throw new Error("Edge DevTools endpoint was not ready");
}

async function waitForPageReady(cdp, sessionId) {
  for (let index = 0; index < 80; index++) {
    const result = await cdp.send(
      "Runtime.evaluate",
      {
        expression:
          "document.readyState === 'complete' && !location.pathname.includes('/login')",
        returnByValue: true
      },
      sessionId
    );
    if (result.result.value === true) return;
    await delay(250);
  }
}

async function waitForDocumentComplete(cdp, sessionId) {
  for (let index = 0; index < 80; index++) {
    const result = await cdp.send(
      "Runtime.evaluate",
      {
        expression: `location.href.startsWith('${baseUrl}') && document.readyState === 'complete'`,
        returnByValue: true
      },
      sessionId
    );
    if (result.result.value === true) return;
    await delay(250);
  }
  const current = await cdp.send(
    "Runtime.evaluate",
    {
      expression: "location.href",
      returnByValue: true
    },
    sessionId
  );
  throw new Error(`Page did not reach ${baseUrl}; current=${current.result.value}`);
}

async function evaluate(cdp, sessionId, expression) {
  const result = await cdp.send(
    "Runtime.evaluate",
    {
      expression,
      awaitPromise: true,
      returnByValue: true
    },
    sessionId
  );
  if (result.exceptionDetails) {
    throw new Error(
      JSON.stringify(
        {
          text: result.exceptionDetails.text,
          exception: result.exceptionDetails.exception?.description
        },
        null,
        2
      )
    );
  }
  return result.result.value;
}

(async () => {
  fs.mkdirSync(outputDir, { recursive: true });
  fs.rmSync(userDataDir, { recursive: true, force: true });
  fs.mkdirSync(userDataDir, { recursive: true });

  const edge = spawn(edgePath, [
    "--headless=new",
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${userDataDir}`,
    "--disable-gpu",
    "--no-first-run",
    "--no-default-browser-check",
    "--window-size=1920,1080",
    "about:blank"
  ]);

  try {
    const version = await waitForCdp();
    const cdp = new Cdp(version.webSocketDebuggerUrl);
    await cdp.open();
    const target = await cdp.send("Target.createTarget", {
      url: `${baseUrl}/login`
    });
    const attached = await cdp.send("Target.attachToTarget", {
      targetId: target.targetId,
      flatten: true
    });
    const sessionId = attached.sessionId;
    await cdp.send("Page.enable", {}, sessionId);
    await cdp.send("Runtime.enable", {}, sessionId);
    await cdp.send(
      "Emulation.setDeviceMetricsOverride",
      {
        width: 1920,
        height: 1080,
        deviceScaleFactor: 1,
        mobile: false
      },
      sessionId
    );
    await cdp.send("Page.navigate", { url: `${baseUrl}/login` }, sessionId);
    await waitForDocumentComplete(cdp, sessionId);
    await delay(500);

    const loginData = await loginFromNode();
    await evaluate(
      cdp,
      sessionId,
      `(() => {
        const data = ${JSON.stringify(loginData)};
        const expires = new Date(data.expires).getTime();
        const userInfo = {
          accessToken: data.accessToken,
          refreshToken: data.refreshToken,
          expires,
          avatar: data.avatar,
          username: data.username,
          nickname: data.nickname,
          roleCode: data.roleCode,
          permissions: data.permissions
        };
        localStorage.setItem('user-info', JSON.stringify(userInfo));
        document.cookie = 'multiple-tabs=true; path=/';
        return true;
      })()`
    );

    for (const theme of ["light", "dark"]) {
      await evaluate(
        cdp,
        sessionId,
        `(() => {
          const nextLayout = ${JSON.stringify(layout(theme))};
          localStorage.setItem('responsive-layout', JSON.stringify(nextLayout));
          document.documentElement.setAttribute('data-theme', 'light');
          document.documentElement.classList.toggle('dark', nextLayout.darkMode);
        })()`
      );

      for (const [name, route] of pages) {
        await cdp.send("Page.navigate", { url: `${baseUrl}${route}` }, sessionId);
        await waitForPageReady(cdp, sessionId);
        await delay(1400);
        const screenshot = await cdp.send(
          "Page.captureScreenshot",
          { format: "png", fromSurface: true },
          sessionId
        );
        fs.writeFileSync(
          path.join(outputDir, `${theme}-${name}.png`),
          Buffer.from(screenshot.data, "base64")
        );
      }
    }

    cdp.close();
    console.log(`captured ${pages.length * 2} screenshots in ${outputDir}`);
  } finally {
    edge.kill();
  }
})().catch(error => {
  console.error(error);
  process.exit(1);
});
