const test = require("node:test");
const assert = require("node:assert");
const { chooseMode, acceptFrameMessage, createBridge, createView } = require("./view.js");

const dashboard = "https://dash.example.com";

function fakeDocument() {
  const make = tag => ({
    tag,
    attributes: {},
    children: [],
    listeners: {},
    style: {},
    hidden: false,
    disabled: false,
    setAttribute(name, value) { this.attributes[name] = value; },
    append(...nodes) { this.children.push(...nodes); },
    replaceChildren(...nodes) { this.children = nodes; },
    addEventListener(type, listener) { this.listeners[type] = listener; },
    click() { return this.listeners.click(); },
  });
  const root = make("div");
  return { root, documentElement: { clientWidth: 400 }, createElement: make, getElementById: id => (id === "root" ? root : null) };
}

const all = node => (typeof node === "string" ? [] : [node, ...node.children.flatMap(all)]);
const find = (root, tag) => all(root).find(n => n.tag === tag);
const text = node => (typeof node === "string" ? node : node.children.map(text).join(" "));

function fakeHost({ frameDomains = [dashboard], displayModes = ["inline", "fullscreen"], toolName = "data_list", context = {}, failOpen, failFrom = 1, errors = {}, signedIn = true } = {}) {
  const sent = [];
  let deliver;
  let links = 0;
  const answer = msg => {
    if (msg.method === "ui/initialize")
      return {
        protocolVersion: "2026-01-26",
        hostCapabilities: { sandbox: { csp: { frameDomains } } },
        hostContext: { displayMode: "inline", availableDisplayModes: displayModes, ...(toolName && { toolInfo: { tool: { name: toolName } } }), ...context },
      };
    if (msg.method === "tools/call") {
      links++;
      if (failOpen && links >= failFrom) return { isError: true, content: [{ type: "text", text: failOpen }] };
      const link = { url: `${dashboard}/org/1/auth/transfer?transfer_token=t${links}`, origin: dashboard, target: "dashboard", signedIn };
      return { content: [{ type: "text", text: JSON.stringify(link) }] };
    }
    if (msg.method === "ui/request-display-mode") return { mode: msg.params.mode };
    return {};
  };
  const bridge = createBridge(
    msg => {
      sent.push(msg);
      if (msg.id === undefined || !msg.method) return;
      const reply = errors[msg.method] ? { error: { code: -32000, message: errors[msg.method] } } : { result: answer(msg) };
      queueMicrotask(() => deliver({ jsonrpc: "2.0", id: msg.id, ...reply }));
    },
    listener => { deliver = listener; });
  const notify = (method, params) => deliver({ jsonrpc: "2.0", method, params });
  return { bridge, sent, notify, methods: () => sent.map(m => m.method) };
}

async function shownView(options = {}) {
  const host = fakeHost(options);
  const doc = fakeDocument();
  const view = createView(host.bridge, doc);
  const started = view.start();
  host.notify("ui/notifications/tool-input", { arguments: { entity: "orders", project: "p1" } });
  host.notify("ui/notifications/tool-result", options.toolResult || { content: [{ type: "text", text: "ok" }] });
  await started;
  return { host, doc, view };
}

test("frames when the host allows the link's origin", async () => {
  assert.strictEqual(chooseMode({ url: `${dashboard}/x`, origin: dashboard, target: "dashboard", signedIn: true }, [dashboard]), "frame");

  const { doc } = await shownView();
  const frame = find(doc.root, "iframe");
  assert.ok(frame);
  assert.strictEqual(frame.attributes.src, `${dashboard}/org/1/auth/transfer?transfer_token=t1`);
  assert.strictEqual(frame.attributes.sandbox, "allow-scripts allow-same-origin allow-forms allow-popups");
  assert.strictEqual(frame.attributes.referrerpolicy, "no-referrer");
});

test("shows the button when the host allows no frames or another origin", async () => {
  const link = { url: `${dashboard}/x`, origin: dashboard, target: "dashboard" };
  assert.strictEqual(chooseMode(link, undefined), "button");
  assert.strictEqual(chooseMode(link, []), "button");
  assert.strictEqual(chooseMode(link, ["https://other.example.com"]), "button");

  const { doc } = await shownView({ frameDomains: null });
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(find(doc.root, "button")), "Open in Anythink");
  assert.match(text(doc.root), /Claude can't show the dashboard here, so it opens in your browser\./);
});

test("shows the message when ui_open failed", async () => {
  assert.strictEqual(chooseMode({ error: "There's no view for this tool." }, [dashboard]), "message");

  const { doc } = await shownView({ failOpen: "That page isn't available." });
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(find(doc.root, "button"), undefined);
  assert.strictEqual(text(doc.root).trim(), "That page isn't available.");
});

test("shows the message when the host refuses the ui_open call", async () => {
  const { doc } = await shownView({ errors: { "tools/call": "Tool call denied" } });
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(doc.root).trim(), "Tool call denied");
});

test("never frames account pages, even when their origin is allowed", () => {
  const account = "https://my.example.com";
  assert.strictEqual(chooseMode({ url: `${account}/accounts/1`, origin: account, target: "account" }, [account]), "button");
  assert.strictEqual(chooseMode({ url: `${account}/accounts/1`, origin: account }, [account]), "button");
});

test("ignores messages from other origins, from \"null\", and of unknown types", () => {
  const event = (origin, data) => ({ origin, data });
  assert.deepStrictEqual(acceptFrameMessage(event(dashboard, { type: "open-in-new-tab", path: "/org/1/x" }), dashboard), { type: "open-in-new-tab", path: "/org/1/x" });
  assert.deepStrictEqual(acceptFrameMessage(event(dashboard, { type: "exit-management" }), dashboard), { type: "exit-management" });
  assert.deepStrictEqual(acceptFrameMessage(event(dashboard, { type: "navigation", url: "x" }), dashboard), { type: "navigation" });
  assert.strictEqual(acceptFrameMessage(event("https://evil.example.com", { type: "exit-management" }), dashboard), null);
  assert.strictEqual(acceptFrameMessage(event("null", { type: "exit-management" }), "null"), null);
  assert.strictEqual(acceptFrameMessage(event(dashboard, { type: "steal" }), dashboard), null);
  assert.strictEqual(acceptFrameMessage(event(dashboard, "exit-management"), dashboard), null);
  assert.strictEqual(acceptFrameMessage(event(dashboard, null), dashboard), null);
  assert.strictEqual(acceptFrameMessage(event(dashboard, { type: "exit-management" }), null), null);
});

test("bridge pairs responses by id and ignores unknown ids", async () => {
  const sent = [];
  let deliver;
  const bridge = createBridge(msg => sent.push(msg), listener => { deliver = listener; });
  const first = bridge.request("a", { n: 1 });
  const second = bridge.request("b", { n: 2 });
  assert.deepStrictEqual(sent.map(m => [m.jsonrpc, m.method, m.params.n]), [["2.0", "a", 1], ["2.0", "b", 2]]);
  assert.notStrictEqual(sent[0].id, sent[1].id);

  deliver({ jsonrpc: "2.0", id: 999, result: "stray" });
  deliver("not json-rpc");
  deliver(null);
  deliver({ jsonrpc: "2.0", id: sent[1].id, error: { code: -32000, message: "denied" } });
  deliver({ jsonrpc: "2.0", id: sent[0].id, result: "one" });

  assert.strictEqual(await first, "one");
  await assert.rejects(second, /denied/);

  bridge.notify("n", { x: 1 });
  assert.deepStrictEqual(sent[2], { jsonrpc: "2.0", method: "n", params: { x: 1 } });
  deliver({ jsonrpc: "2.0", id: 7, method: "ui/resource-teardown", params: { reason: "closed" } });
  assert.deepStrictEqual(sent[3], { jsonrpc: "2.0", id: 7, result: {} });
});

test("initializes with the spec handshake and asks ui_open for the tool that ran", async () => {
  const { host } = await shownView();
  const [init, initialized, size, call] = host.sent;
  assert.strictEqual(init.method, "ui/initialize");
  assert.strictEqual(init.params.protocolVersion, "2026-01-26");
  assert.deepStrictEqual(init.params.appCapabilities, { availableDisplayModes: ["inline", "fullscreen"] });
  assert.ok(init.params.appInfo.name);
  assert.strictEqual(initialized.method, "ui/notifications/initialized");
  assert.strictEqual(initialized.id, undefined);
  assert.strictEqual(size.method, "ui/notifications/size-changed");
  assert.deepStrictEqual(call.params, { name: "ui_open", arguments: { tool: "data_list", arguments: { entity: "orders", project: "p1" } } });
});

test("button click asks for a fresh link before opening", async () => {
  const { host, doc } = await shownView({ frameDomains: [] });
  const before = host.sent.length;

  await find(doc.root, "button").click();

  const after = host.sent.slice(before);
  assert.deepStrictEqual(after.map(m => m.method), ["tools/call", "ui/open-link"]);
  assert.deepStrictEqual(after[0].params, { name: "ui_open", arguments: { tool: "data_list", arguments: { entity: "orders", project: "p1" } } });
  assert.deepStrictEqual(after[1].params, { url: `${dashboard}/org/1/auth/transfer?transfer_token=t2` });
});

test("button shows the message when a fresh link can't be made", async () => {
  const { host, doc } = await shownView({ frameDomains: [], failOpen: "This connection covers one project only.", failFrom: 2 });

  await find(doc.root, "button").click();

  assert.notStrictEqual(host.methods().at(-1), "ui/open-link");
  assert.strictEqual(find(doc.root, "button"), undefined);
  assert.strictEqual(text(doc.root).trim(), "This connection covers one project only.");
});

test("open-in-new-tab asks ui_open for that path in the same project, then opens it", async () => {
  const { host, view } = await shownView();
  const before = host.sent.length;

  await view.frameMessage({ origin: dashboard, data: { type: "open-in-new-tab", path: "/org/1/workflows/9" } });

  const after = host.sent.slice(before);
  assert.deepStrictEqual(after.map(m => m.method), ["tools/call", "ui/open-link"]);
  assert.deepStrictEqual(after[0].params.arguments, { tool: "data_list", arguments: { entity: "orders", project: "p1" }, path: "/org/1/workflows/9" });
});

test("frame messages from another origin do nothing", async () => {
  const { host, doc, view } = await shownView();
  const before = host.sent.length;

  await view.frameMessage({ origin: "https://evil.example.com", data: { type: "open-in-new-tab", path: "/org/1/x" } });
  await view.frameMessage({ origin: "https://evil.example.com", data: { type: "exit-management" } });
  await view.frameMessage({ origin: dashboard, data: { type: "navigation", url: `${dashboard}/org/1/y` } });

  assert.strictEqual(host.sent.length, before);
  assert.ok(find(doc.root, "iframe"));
});

test("exit-management replaces the frame with the button", async () => {
  const { host, doc, view } = await shownView();

  await view.frameMessage({ origin: dashboard, data: { type: "exit-management" } });

  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(find(doc.root, "button")), "Open in Anythink");
  assert.match(text(doc.root), /Your dashboard session ended\./);
  const before = host.sent.length;
  await view.frameMessage({ origin: dashboard, data: { type: "open-in-new-tab", path: "/org/1/x" } });
  assert.strictEqual(host.sent.length, before);
});

test("expand asks for fullscreen and hides once there", async () => {
  const { host, doc } = await shownView();
  const expand = all(doc.root).find(n => n.tag === "button" && text(n) === "Expand");
  assert.strictEqual(expand.hidden, false);

  await expand.click();

  assert.deepStrictEqual(host.sent.at(-1).params, { mode: "fullscreen" });
  assert.strictEqual(host.sent.at(-1).method, "ui/request-display-mode");
  assert.strictEqual(expand.hidden, true);
  host.notify("ui/notifications/host-context-changed", { displayMode: "inline" });
  assert.strictEqual(expand.hidden, false);
});

test("expand is hidden when the host doesn't offer fullscreen", async () => {
  const { doc } = await shownView({ displayModes: ["inline"] });
  const expand = all(doc.root).find(n => n.tag === "button" && text(n) === "Expand");
  assert.strictEqual(expand.hidden, true);
});

test("script inlines safely", () => {
  const source = require("node:fs").readFileSync(require.resolve("./view.js"), "utf8");
  assert.doesNotMatch(source, /<\/script/i);
});

const sizeReports = host => host.sent.filter(m => m.method === "ui/notifications/size-changed").map(m => m.params);

test("inline view with a flexible height gets a usable height and reports it", async () => {
  for (const [dims, height] of [[{ width: 400, maxHeight: 600 }, 600], [{ width: 400 }, 640], [{ maxHeight: 900 }, 640], [undefined, 640]]) {
    const { host, doc } = await shownView({ context: { containerDimensions: dims } });
    assert.strictEqual(doc.root.style.height, `${height}px`);
    assert.deepStrictEqual(sizeReports(host).at(-1), { width: 400, height });
  }
});

test("button and message pages get the same usable height", async () => {
  const button = await shownView({ frameDomains: [], context: { containerDimensions: { maxHeight: 500 } } });
  assert.strictEqual(button.doc.root.style.height, "500px");
  assert.deepStrictEqual(sizeReports(button.host).at(-1), { width: 400, height: 500 });
  const message = await shownView({ failOpen: "That page isn't available.", context: { containerDimensions: { maxHeight: 500 } } });
  assert.strictEqual(message.doc.root.style.height, "500px");
  assert.deepStrictEqual(sizeReports(message.host).at(-1), { width: 400, height: 500 });
});

test("fixed height and fullscreen keep the host's height", async () => {
  const fixed = await shownView({ context: { containerDimensions: { height: 500, width: 400 } } });
  assert.strictEqual(fixed.doc.root.style.height, "");
  assert.deepStrictEqual(sizeReports(fixed.host), []);
  const full = await shownView({ context: { displayMode: "fullscreen", containerDimensions: { maxHeight: 600 } } });
  assert.strictEqual(full.doc.root.style.height, "");
  assert.deepStrictEqual(sizeReports(full.host), []);
});

test("height is re-applied when the host context changes", async () => {
  const { host, doc } = await shownView({ context: { containerDimensions: { maxHeight: 600 } } });
  host.notify("ui/notifications/host-context-changed", { displayMode: "fullscreen" });
  assert.strictEqual(doc.root.style.height, "");
  host.notify("ui/notifications/host-context-changed", { displayMode: "inline", containerDimensions: { maxHeight: 300 } });
  assert.strictEqual(doc.root.style.height, "300px");
  assert.deepStrictEqual(sizeReports(host).at(-1), { width: 400, height: 300 });
});

test("without toolInfo the tool name comes from the tool result's _meta", async () => {
  const { host } = await shownView({ toolName: null, toolResult: { content: [], _meta: { "anythink/tool": "workflows_get" } } });
  const call = host.sent.find(m => m.method === "tools/call");
  assert.strictEqual(call.params.arguments.tool, "workflows_get");
});

test("toolInfo wins over the tool result's _meta", async () => {
  const { host } = await shownView({ toolResult: { content: [], _meta: { "anythink/tool": "workflows_get" } } });
  assert.strictEqual(host.sent.find(m => m.method === "tools/call").params.arguments.tool, "data_list");
});

test("with no tool name at all the view shows a message and asks for no link", async () => {
  const { host, doc } = await shownView({ toolName: null });
  assert.strictEqual(host.methods().includes("tools/call"), false);
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(doc.root).trim(), "There's no view for this tool.");
});

test("a cancelled tool shows a message instead of the loading skeleton", { timeout: 2000 }, async () => {
  const host = fakeHost();
  const doc = fakeDocument();
  const started = createView(host.bridge, doc).start();
  host.notify("ui/notifications/tool-input", { arguments: {} });
  host.notify("ui/notifications/tool-cancelled", { reason: "user" });
  await started;
  assert.strictEqual(host.methods().includes("tools/call"), false);
  assert.strictEqual(text(doc.root).trim(), "The request was cancelled, so there's nothing to show.");
});

test("a refused ui/initialize shows a message", { timeout: 2000 }, async () => {
  const host = fakeHost({ errors: { "ui/initialize": "Not supported" } });
  const doc = fakeDocument();
  await createView(host.bridge, doc).start();
  assert.strictEqual(host.methods().includes("ui/notifications/initialized"), false);
  assert.strictEqual(text(doc.root).trim(), "Not supported");
});

test("a refused fullscreen request keeps Expand and rejects nothing", async () => {
  const { doc } = await shownView({ errors: { "ui/request-display-mode": "Denied" } });
  const expand = all(doc.root).find(n => n.tag === "button" && text(n) === "Expand");
  await expand.click();
  assert.strictEqual(expand.hidden, false);
});

test("the loading skeleton gets the usable height before the link arrives", async () => {
  const host = fakeHost({ context: { containerDimensions: { maxHeight: 600 } } });
  const doc = fakeDocument();
  createView(host.bridge, doc).start();
  await new Promise(resolve => setImmediate(resolve));
  assert.strictEqual(host.methods().includes("tools/call"), false);
  assert.strictEqual(doc.root.style.height, "600px");
  assert.deepStrictEqual(sizeReports(host), [{ width: 400, height: 600 }]);
});

test("expand on a flexible-height host drops the inline height", async () => {
  const { host, doc } = await shownView({ context: { containerDimensions: { maxHeight: 600 } } });
  const expand = all(doc.root).find(n => n.tag === "button" && text(n) === "Expand");
  assert.strictEqual(doc.root.style.height, "600px");

  await expand.click();

  assert.strictEqual(doc.root.style.height, "");
  host.notify("ui/notifications/host-context-changed", { displayMode: "inline" });
  assert.strictEqual(doc.root.style.height, "600px");
  assert.deepStrictEqual(sizeReports(host).at(-1), { width: 400, height: 600 });
  assert.strictEqual(sizeReports(host).length, 2);
});

test("unsigned link shows the button even when the origin is allowed", async () => {
  assert.strictEqual(chooseMode({ url: `${dashboard}/x`, origin: dashboard, target: "dashboard", signedIn: false }, [dashboard]), "button");

  const { doc } = await shownView({ signedIn: false });
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(find(doc.root, "button")), "Open in Anythink");
});

test("open-in-new-tab that ui_open refuses shows the refusal and keeps the frame", async () => {
  const { host, doc, view } = await shownView({ failOpen: "That page isn't available.", failFrom: 2 });

  await view.frameMessage({ origin: dashboard, data: { type: "open-in-new-tab", path: "https://console.firebase.google.com/" } });

  assert.ok(find(doc.root, "iframe"));
  const notice = all(doc.root).find(n => n.attributes && n.attributes.class === "notice");
  assert.strictEqual(text(notice), "That page isn't available.");
  assert.strictEqual(notice.hidden, false);
  assert.ok(!host.methods().includes("ui/open-link"));
});
