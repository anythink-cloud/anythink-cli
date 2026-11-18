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
    hidden: false,
    disabled: false,
    setAttribute(name, value) { this.attributes[name] = value; },
    append(...nodes) { this.children.push(...nodes); },
    replaceChildren(...nodes) { this.children = nodes; },
    addEventListener(type, listener) { this.listeners[type] = listener; },
    click() { return this.listeners.click(); },
  });
  const root = make("div");
  return { root, createElement: make, getElementById: id => (id === "root" ? root : null) };
}

const all = node => (typeof node === "string" ? [] : [node, ...node.children.flatMap(all)]);
const find = (root, tag) => all(root).find(n => n.tag === tag);
const text = node => (typeof node === "string" ? node : node.children.map(text).join(" "));
const settle = () => new Promise(resolve => setImmediate(resolve));

function fakeHost({ frameDomains = [dashboard], displayModes = ["inline", "fullscreen"], toolName = "data_list", failOpen, failFrom = 1, hostError } = {}) {
  const sent = [];
  let deliver;
  let links = 0;
  const answer = msg => {
    if (msg.method === "ui/initialize")
      return {
        protocolVersion: "2026-01-26",
        hostCapabilities: { sandbox: { csp: { frameDomains } } },
        hostContext: { displayMode: "inline", availableDisplayModes: displayModes, toolInfo: { tool: { name: toolName } } },
      };
    if (msg.method === "tools/call") {
      links++;
      if (failOpen && links >= failFrom) return { isError: true, content: [{ type: "text", text: failOpen }] };
      const link = { url: `${dashboard}/org/1/auth/transfer?transfer_token=t${links}`, origin: dashboard, target: "dashboard", signedIn: true };
      return { content: [{ type: "text", text: JSON.stringify(link) }] };
    }
    if (msg.method === "ui/request-display-mode") return { mode: msg.params.mode };
    return {};
  };
  const bridge = createBridge(
    msg => {
      sent.push(msg);
      if (msg.id === undefined || !msg.method) return;
      const reply = hostError && msg.method === "tools/call" ? { error: { code: -32000, message: hostError } } : { result: answer(msg) };
      queueMicrotask(() => deliver({ jsonrpc: "2.0", id: msg.id, ...reply }));
    },
    listener => { deliver = listener; });
  const notify = (method, params) => deliver({ jsonrpc: "2.0", method, params });
  return { bridge, sent, notify, methods: () => sent.map(m => m.method) };
}

async function shownView(options) {
  const host = fakeHost(options);
  const doc = fakeDocument();
  const view = createView(host.bridge, doc);
  const started = view.start();
  host.notify("ui/notifications/tool-input", { arguments: { entity: "orders", project: "p1" } });
  host.notify("ui/notifications/tool-result", { content: [{ type: "text", text: "ok" }] });
  await started;
  return { host, doc, view };
}

test("frames when the host allows the link's origin", async () => {
  assert.strictEqual(chooseMode({ url: `${dashboard}/x`, origin: dashboard }, [dashboard]), "frame");

  const { doc } = await shownView();
  const frame = find(doc.root, "iframe");
  assert.ok(frame);
  assert.strictEqual(frame.attributes.src, `${dashboard}/org/1/auth/transfer?transfer_token=t1`);
  assert.strictEqual(frame.attributes.sandbox, "allow-scripts allow-same-origin allow-forms allow-popups");
  assert.strictEqual(frame.attributes.referrerpolicy, "no-referrer");
});

test("shows the button when the host allows no frames or another origin", async () => {
  const link = { url: `${dashboard}/x`, origin: dashboard };
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
  const { doc } = await shownView({ hostError: "Tool call denied" });
  assert.strictEqual(find(doc.root, "iframe"), undefined);
  assert.strictEqual(text(doc.root).trim(), "Tool call denied");
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
  const [init, initialized, call] = host.sent;
  assert.strictEqual(init.method, "ui/initialize");
  assert.strictEqual(init.params.protocolVersion, "2026-01-26");
  assert.deepStrictEqual(init.params.appCapabilities, { availableDisplayModes: ["inline", "fullscreen"] });
  assert.ok(init.params.appInfo.name);
  assert.strictEqual(initialized.method, "ui/notifications/initialized");
  assert.strictEqual(initialized.id, undefined);
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

test("script calls it the dashboard, never by its internal name, and inlines safely", () => {
  const source = require("node:fs").readFileSync(require.resolve("./view.js"), "utf8");
  assert.doesNotMatch(source, /breeze/i);
  assert.doesNotMatch(source, /<\/script/i);
});
