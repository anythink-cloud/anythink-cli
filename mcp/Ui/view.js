const FRAME_MESSAGES = ["open-in-new-tab", "exit-management", "navigation"];
const CANT_FRAME = "Claude can't show the dashboard here, so it opens in your browser.";
const SESSION_ENDED = "Your dashboard session ended.";

function chooseMode(link, allowedFrameOrigins) {
  if (link.error) return "message";
  return (allowedFrameOrigins || []).includes(link.origin) ? "frame" : "button";
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

  const el = (tag, attrs, ...children) => {
    const node = doc.createElement(tag);
    for (const name in attrs) node.setAttribute(name, attrs[name]);
    node.append(...children);
    return node;
  };

  const showExpand = () => {
    if (expand) expand.hidden = !(context.availableDisplayModes || []).includes("fullscreen") || context.displayMode === "fullscreen";
  };

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
      context.displayMode = (await bridge.request("ui/request-display-mode", { mode: "fullscreen" })).mode;
      showExpand();
    });
    showExpand();
    root.replaceChildren(
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
    root.replaceChildren(el("div", { class: "panel" }, button, el("p", {}, note)));
  }

  function showMessage(text) {
    framedOrigin = null;
    root.replaceChildren(el("div", { class: "panel" }, el("p", {}, text)));
  }

  return {
    async start() {
      const input = new Promise(resolve => bridge.on("ui/notifications/tool-input", p => resolve(p.arguments)));
      const result = new Promise(resolve => bridge.on("ui/notifications/tool-result", resolve));
      bridge.on("ui/notifications/host-context-changed", changed => {
        context = { ...context, ...changed };
        showExpand();
      });
      const init = await bridge.request("ui/initialize", {
        appInfo: { name: "anythink-view", version: "1.0.0" },
        appCapabilities: { availableDisplayModes: ["inline", "fullscreen"] },
        protocolVersion: "2026-01-26",
      });
      context = init.hostContext || {};
      bridge.notify("ui/notifications/initialized");
      const [args] = await Promise.all([input, result]);
      call = { tool: context.toolInfo?.tool.name, arguments: args };
      const found = await link();
      const mode = chooseMode(found, init.hostCapabilities?.sandbox?.csp?.frameDomains);
      if (mode === "frame") showFrame(found);
      else if (mode === "button") showButton(CANT_FRAME);
      else showMessage(found.error);
    },
    async frameMessage(event) {
      const message = acceptFrameMessage(event, framedOrigin);
      if (!message) return;
      if (message.type === "open-in-new-tab") await openOutside({ path: message.path });
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
