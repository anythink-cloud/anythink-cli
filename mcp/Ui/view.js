const FRAME_MESSAGES = ["open-in-new-tab", "exit-management", "navigation"];
const CANT_FRAME = "Claude can't show the dashboard here, so it opens in your browser.";
const SESSION_ENDED = "Your dashboard session ended.";
const NO_VIEW = "There's no view for this tool.";
const CANCELLED = "The request was cancelled, so there's nothing to show.";
const INLINE_HEIGHT = 640;
const NOTICE_MS = 5000;

function chooseMode(link, allowedFrameOrigins) {
  if (link.error) return "message";
  return link.target === "dashboard" && link.signedIn && (allowedFrameOrigins || []).includes(link.origin) ? "frame" : "button";
}

function acceptFrameMessage(event, framedOrigin) {
  const data = event.data;
  if (framedOrigin === "null" || event.origin !== framedOrigin || !data || !FRAME_MESSAGES.includes(data.type)) return null;
  return data.type === "open-in-new-tab" ? { type: data.type, path: data.path } : { type: data.type };
}

function createBridge(post, onMessage) {
  let nextId = 1;
  const pending = new Map();
  const handlers = {};
  onMessage(msg => {
    if (!msg || msg.jsonrpc !== "2.0") return;
    if (msg.method) {
      if (handlers[msg.method]) handlers[msg.method](msg.params || {});
      if (msg.id !== undefined) post({ jsonrpc: "2.0", id: msg.id, result: {} });
      return;
    }
    const call = pending.get(msg.id);
    if (!call) return;
    pending.delete(msg.id);
    if (msg.error) call.reject(new Error(msg.error.message));
    else call.resolve(msg.result);
  });
  return {
    request(method, params) {
      const id = nextId++;
      const reply = new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
      post({ jsonrpc: "2.0", id, method, params });
      return reply;
    },
    notify(method, params) {
      post({ jsonrpc: "2.0", method, params });
    },
    on(method, handler) {
      handlers[method] = handler;
    },
  };
}

function createView(bridge, doc) {
  const root = doc.getElementById("root");
  let context = {};
  let call = {};
  let framedOrigin = null;
  let expand = null;
  let notice = null;
  let reported = "";

  const el = (tag, attrs, ...children) => {
    const node = doc.createElement(tag);
    for (const name in attrs) node.setAttribute(name, attrs[name]);
    node.append(...children);
    return node;
  };

  const showExpand = () => {
    if (expand) expand.hidden = !(context.availableDisplayModes || []).includes("fullscreen") || context.displayMode === "fullscreen";
  };

  const show = (...nodes) => {
    root.replaceChildren(...nodes);
    fitHeight();
  };

  function fitHeight() {
    const dims = context.containerDimensions || {};
    if ("height" in dims || context.displayMode === "fullscreen") {
      root.style.height = "";
      reported = "";
      return;
    }
    const size = { width: doc.documentElement.clientWidth, height: Math.min(dims.maxHeight ?? INLINE_HEIGHT, INLINE_HEIGHT) };
    root.style.height = size.height + "px";
    if (reported === JSON.stringify(size)) return;
    reported = JSON.stringify(size);
    bridge.notify("ui/notifications/size-changed", size);
  }

  async function link(extra) {
    try {
      const result = await bridge.request("tools/call", { name: "ui_open", arguments: { ...call, ...extra } });
      const text = result.content[0].text;
      return result.isError ? { error: text } : JSON.parse(text);
    } catch (e) {
      return { error: e.message };
    }
  }

  async function openOutside(extra) {
    const found = await link(extra);
    if (!found.error) await bridge.request("ui/open-link", { url: found.url }).catch(() => {});
    return found;
  }

  function showFrame(found) {
    framedOrigin = found.origin;
    expand = el("button", { type: "button", class: "expand" }, "Expand");
    expand.addEventListener("click", async () => {
      const result = await bridge.request("ui/request-display-mode", { mode: "fullscreen" }).catch(() => ({ mode: context.displayMode }));
      context.displayMode = result.mode;
      showExpand();
      fitHeight();
    });
    showExpand();
    show(
      el("iframe", {
        src: found.url,
        title: "Anythink dashboard",
        sandbox: "allow-scripts allow-same-origin allow-forms allow-popups",
        referrerpolicy: "no-referrer",
      }),
      expand);
  }

  function showButton(note) {
    framedOrigin = null;
    const button = el("button", { type: "button" }, "Open in Anythink");
    button.addEventListener("click", async () => {
      button.disabled = true;
      const found = await openOutside();
      button.disabled = false;
      if (found.error) showMessage(found.error);
    });
    show(el("div", { class: "panel" }, button, el("p", {}, note)));
  }

  function showNotice(text) {
    if (notice) notice.hidden = true;
    notice = el("p", { class: "notice", role: "status" }, text);
    root.append(notice);
    const shown = notice;
    setTimeout(() => { shown.hidden = true; }, NOTICE_MS).unref?.();
  }

  function showMessage(text) {
    framedOrigin = null;
    show(el("div", { class: "panel" }, el("p", {}, text)));
  }

  return {
    async start() {
      const input = new Promise(resolve => bridge.on("ui/notifications/tool-input", p => resolve(p.arguments)));
      const result = new Promise((resolve, reject) => {
        bridge.on("ui/notifications/tool-result", resolve);
        bridge.on("ui/notifications/tool-cancelled", () => reject(new Error(CANCELLED)));
      });
      bridge.on("ui/notifications/host-context-changed", changed => {
        context = { ...context, ...changed };
        showExpand();
        fitHeight();
      });
      try {
        const init = await bridge.request("ui/initialize", {
          appInfo: { name: "anythink-view", version: "1.0.0" },
          appCapabilities: { availableDisplayModes: ["inline", "fullscreen"] },
          protocolVersion: "2026-01-26",
        });
        context = init.hostContext || {};
        bridge.notify("ui/notifications/initialized");
        fitHeight();
        const [args, toolResult] = await Promise.all([input, result]);
        const tool = context.toolInfo?.tool?.name ?? toolResult._meta?.["anythink/tool"];
        if (!tool) return showMessage(NO_VIEW);
        call = { tool, arguments: args };
        const found = await link();
        const mode = chooseMode(found, init.hostCapabilities?.sandbox?.csp?.frameDomains);
        if (mode === "frame") showFrame(found);
        else if (mode === "button") showButton(CANT_FRAME);
        else showMessage(found.error);
      } catch (e) {
        showMessage(e.message);
      }
    },
    async frameMessage(event) {
      const message = acceptFrameMessage(event, framedOrigin);
      if (!message) return;
      if (message.type === "open-in-new-tab") {
        const found = await openOutside({ path: message.path });
        if (found.error) showNotice(found.error);
      }
      if (message.type === "exit-management") showButton(SESSION_ENDED);
    },
  };
}

if (typeof module !== "undefined") module.exports = { chooseMode, acceptFrameMessage, createBridge, createView };

if (typeof window !== "undefined" && window.parent !== window) {
  let receive;
  const bridge = createBridge(msg => window.parent.postMessage(msg, "*"), listener => { receive = listener; });
  const view = createView(bridge, document);
  window.addEventListener("message", event => (event.source === window.parent ? receive(event.data) : view.frameMessage(event)));
  view.start();
}
