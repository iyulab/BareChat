"use strict";
// BareChat embedded UI — vanilla, no virtual DOM. XSS-safe (textContent only).
(function () {
  const BASE = window.BARECHAT_BASE || "";
  const $ = (id) => document.getElementById(id);

  const state = {
    channels: [],
    current: null,        // channelId
    connection: null,
    me: null,             // resolved senderId once we send/receive
    caps: { canEditMessages: true, canDeleteMessages: true, canRenderMarkdown: false }   // host policy; refined at boot
  };

  // ---------- REST ----------
  async function api(path, opts) {
    const res = await fetch(BASE + path, Object.assign({ credentials: "same-origin", headers: { "Content-Type": "application/json" } }, opts));
    if (res.status === 401) { location.href = BASE.replace(/\/chat$/, "") + "/"; throw new Error("unauthorized"); }
    return res;
  }
  const getJson = async (p) => (await api(p)).json();

  // ---------- dialogs (self-contained modal/toast — no native prompt/confirm/alert) ----------
  // Mobile-app feel: focus-trapped sheet, ESC / overlay-tap to cancel, restores prior focus.
  // XSS-safe (textContent only). Function declarations are hoisted so callers above can use them.
  function showToast(message, kind) {
    let root = $("toast-root");
    if (!root) { root = document.createElement("div"); root.id = "toast-root"; root.className = "toasts"; document.body.appendChild(root); }
    const t = document.createElement("div");
    t.className = "toast toast--" + (kind || "info");
    t.setAttribute("role", "status");
    t.textContent = message;
    root.appendChild(t);
    requestAnimationFrame(() => t.classList.add("is-in"));
    const remove = () => { t.classList.remove("is-in"); t.addEventListener("transitionend", () => t.remove(), { once: true }); };
    const timer = setTimeout(remove, 3200);
    t.addEventListener("click", () => { clearTimeout(timer); remove(); });
  }

  // Generic modal shell. `build(card, close)` fills the card and wires submit/cancel via close(value).
  function openModal(build) {
    return new Promise((resolve) => {
      const prevFocus = document.activeElement;
      const overlay = document.createElement("div");
      overlay.className = "modal-overlay";
      const card = document.createElement("div");
      card.className = "modal";
      card.setAttribute("role", "dialog");
      card.setAttribute("aria-modal", "true");
      card.setAttribute("aria-labelledby", "modal-title");
      overlay.appendChild(card);

      let settled = false;
      const close = (value) => {
        if (settled) return; settled = true;
        document.removeEventListener("keydown", onKey, true);
        overlay.classList.remove("is-in");
        overlay.addEventListener("transitionend", () => overlay.remove(), { once: true });
        if (prevFocus && prevFocus.focus) { try { prevFocus.focus(); } catch { } }
        resolve(value);
      };

      const focusable = () => Array.from(card.querySelectorAll('input,button,textarea,select,[tabindex]:not([tabindex="-1"])'))
        .filter((el) => !el.disabled && el.offsetParent !== null);
      function onKey(e) {
        if (e.key === "Escape") { e.preventDefault(); close(null); return; }
        if (e.key !== "Tab") return;
        const f = focusable(); if (!f.length) return;
        const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
      overlay.addEventListener("mousedown", (e) => { if (e.target === overlay) close(null); });
      document.addEventListener("keydown", onKey, true);

      build(card, close);
      document.body.appendChild(overlay);
      requestAnimationFrame(() => overlay.classList.add("is-in"));
      const auto = card.querySelector("[data-autofocus]") || focusable()[0];
      if (auto) auto.focus();
    });
  }

  // Footer with Cancel + primary action. The primary button submits the card's form.
  function modalActions(close, okLabel, danger) {
    const row = document.createElement("div");
    row.className = "modal__actions";
    const cancel = document.createElement("button");
    cancel.type = "button"; cancel.className = "modal__btn"; cancel.textContent = "Cancel";
    cancel.addEventListener("click", () => close(null));
    const ok = document.createElement("button");
    ok.type = "submit"; ok.className = "modal__btn modal__btn--primary" + (danger ? " modal__btn--danger" : "");
    ok.textContent = okLabel || "OK";
    row.append(cancel, ok);
    return row;
  }

  // Confirmation modal → resolves true on confirm, null (falsy) on cancel/ESC/overlay.
  function showConfirm({ title, message, okLabel, danger }) {
    return openModal((card, close) => {
      const form = document.createElement("form");
      const h = document.createElement("h2"); h.className = "modal__title"; h.id = "modal-title"; h.textContent = title;
      form.appendChild(h);
      if (message) { const p = document.createElement("p"); p.className = "modal__msg"; p.textContent = message; form.appendChild(p); }
      form.appendChild(modalActions(close, okLabel || "OK", danger));
      form.querySelector(".modal__btn--primary").setAttribute("data-autofocus", "");
      form.addEventListener("submit", (e) => { e.preventDefault(); close(true); });
      card.appendChild(form);
    });
  }

  // Form modal → resolves a {name: value} object on submit, null on cancel.
  // fields: [{ name, label?, type: "text"|"checkbox", value?, placeholder?, autofocus? }]
  function showForm({ title, fields, okLabel }) {
    return openModal((card, close) => {
      const form = document.createElement("form");
      const h = document.createElement("h2"); h.className = "modal__title"; h.id = "modal-title"; h.textContent = title;
      form.appendChild(h);
      const inputs = {};
      for (const f of fields) {
        const input = document.createElement("input");
        input.type = f.type === "checkbox" ? "checkbox" : "text";
        if (f.placeholder) input.placeholder = f.placeholder;
        if (f.autofocus) input.setAttribute("data-autofocus", "");
        if (f.type === "checkbox") input.checked = !!f.value; else input.value = f.value || "";
        inputs[f.name] = input;
        if (f.type === "checkbox") {
          const label = document.createElement("label"); label.className = "modal__check";
          const span = document.createElement("span"); span.textContent = f.label || f.name;
          label.append(input, span); form.appendChild(label);
        } else {
          if (f.label) { const label = document.createElement("label"); label.className = "modal__label"; label.textContent = f.label; form.appendChild(label); }
          input.className = "modal__input"; form.appendChild(input);
        }
      }
      form.appendChild(modalActions(close, okLabel || "OK", false));
      form.addEventListener("submit", (e) => {
        e.preventDefault();
        const out = {};
        for (const f of fields) out[f.name] = f.type === "checkbox" ? inputs[f.name].checked : inputs[f.name].value;
        close(out);
      });
      card.appendChild(form);
    });
  }

  // Single-text-field convenience → resolves the entered string, or null on cancel.
  async function showPrompt({ title, value, placeholder, okLabel }) {
    const r = await showForm({ title, okLabel, fields: [{ name: "v", type: "text", value, placeholder, autofocus: true }] });
    return r ? r.v : null;
  }

  // ---------- views ----------
  function showList() {
    $("chat-view").classList.remove("view--active");
    $("list-view").classList.remove("view--pushed");
    state.current = null;
  }
  function showChat() {
    $("list-view").classList.add("view--pushed");
    $("chat-view").classList.add("view--active");
  }

  // ---------- channel list ----------
  function initials(name) { return (name || "?").trim().charAt(0); }

  const badgeEls = new Map();   // channelId -> unread badge element (live-updated)

  function renderBadge(channelId) {
    const el = badgeEls.get(channelId);
    if (!el) return;
    const ch = state.channels.find((c) => c.id === channelId);
    const n = ch && ch.isMember ? (ch.unreadCount || 0) : 0;
    if (n > 0) {
      el.textContent = n > 99 ? "99+" : String(n);
      el.setAttribute("aria-label", `${n} unread message${n === 1 ? "" : "s"}`);   // screen readers read intent, not "3"
      el.hidden = false;
    } else {
      el.removeAttribute("aria-label");
      el.hidden = true;
    }
  }

  // Total cross-session unread across my channels → drives the WebView2 native badge.
  function totalUnread() {
    return state.channels.reduce((s, c) => s + (c.isMember ? (c.unreadCount || 0) : 0), 0);
  }
  function pushBridgeTotal() {
    bridge.unread = totalUnread();
    bridge.post({ type: "unread", count: bridge.unread });
  }

  // Local-only read reset: clear the badge and recompute the native total. No network.
  function resetUnreadLocal(channelId) {
    const ch = state.channels.find((c) => c.id === channelId);
    if (ch) { ch.unreadCount = 0; renderBadge(channelId); }
    pushBridgeTotal();
  }

  // Debounced server-side read persist (lastReadAt = server now). While actively watching a channel,
  // messages can stream in rapidly — without this we'd POST /read once per message (chatty). The exact
  // persist moment doesn't affect correctness (unread = others' messages after lastReadAt).
  let readPersistTimer = null;
  let pendingReadChannel = null;
  function persistReadDebounced(channelId, delay = 1000) {
    pendingReadChannel = channelId;
    clearTimeout(readPersistTimer);
    readPersistTimer = setTimeout(() => {
      const id = pendingReadChannel; pendingReadChannel = null; readPersistTimer = null;
      if (id) api(`/api/channels/${encodeURIComponent(id)}/read`, { method: "POST" }).catch(() => { });
    }, delay);
  }

  // Explicit read (channel opened / panel re-focused): reset locally and persist immediately so the
  // cross-session pointer is reliable even if the app closes right after.
  async function markRead(channelId) {
    clearTimeout(readPersistTimer); pendingReadChannel = null; readPersistTimer = null;
    resetUnreadLocal(channelId);
    try { await api(`/api/channels/${encodeURIComponent(channelId)}/read`, { method: "POST" }); } catch { }
  }

  // Streaming read (a message arrived in the channel I'm watching): reset locally now, persist debounced.
  function markReadStreaming(channelId) {
    resetUnreadLocal(channelId);
    persistReadDebounced(channelId);
  }

  async function loadChannels() {
    state.channels = await getJson("/api/channels");
    const list = $("channel-list");
    badgeEls.clear();
    list.replaceChildren();
    if (!state.channels.length) {
      const li = document.createElement("li");
      li.className = "empty"; li.textContent = "No channels yet.";
      list.appendChild(li);
      return;
    }
    for (const ch of state.channels) {
      const li = document.createElement("li");
      li.className = "channel";

      const avatar = document.createElement("div");
      avatar.className = "channel__avatar";
      avatar.textContent = initials(ch.name);

      const body = document.createElement("div");
      body.className = "channel__body";
      const name = document.createElement("div");
      name.className = "channel__name";
      name.textContent = (ch.isPrivate ? "🔒 " : "") + ch.name;
      const meta = document.createElement("div");
      meta.className = "channel__meta";
      meta.textContent = ch.isDefault ? "default" : ch.isPrivate ? "private" : ch.isMember ? "joined" : "tap to join";
      body.append(name, meta);

      li.append(avatar, body);

      const badge = document.createElement("span");
      badge.className = "channel__badge";
      badge.hidden = true;
      li.appendChild(badge);
      badgeEls.set(ch.id, badge);
      renderBadge(ch.id);

      if (!ch.isMember) {
        const join = document.createElement("button");
        join.className = "channel__join";
        join.textContent = "Join";
        join.addEventListener("click", async (e) => {
          e.stopPropagation();
          await api(`/api/channels/${encodeURIComponent(ch.id)}/join`, { method: "POST" });
          await loadChannels();
        });
        li.appendChild(join);
      }

      li.addEventListener("click", () => openChannel(ch));
      list.appendChild(li);
    }
    pushBridgeTotal();   // seed native badge from server-truth unread
  }

  // ---------- conversation ----------
  async function openChannel(ch) {
    if (!ch.isMember) {
      await api(`/api/channels/${encodeURIComponent(ch.id)}/join`, { method: "POST" });
    }
    state.current = ch.id;
    $("chat-title").textContent = (ch.isPrivate ? "🔒 " : "") + ch.name;
    // Creator of a private channel can invite members.
    $("add-member").hidden = !(ch.isPrivate && state.me != null && ch.createdBy === state.me);
    showChat();
    const msgs = await getJson(`/api/channels/${encodeURIComponent(ch.id)}/messages?limit=100`);
    const box = $("messages");
    box.replaceChildren();
    for (const m of msgs) appendMessage(m);
    scrollToBottom();
    await markRead(ch.id);   // opening a channel reads it (cross-session unread reset)
    await ensureConnected();
  }

  function appendMessage(m) {
    const el = document.createElement("div");
    if (m.messageId) el.dataset.messageId = m.messageId;
    renderMessage(el, m);
    $("messages").appendChild(el);
  }

  // (Re)builds a message bubble's content in place — shared by initial render and live MessageUpdated.
  function renderMessage(el, m) {
    el.replaceChildren();
    if (m.contentType === "System") {
      el.className = "msg msg--system";
      el.textContent = m.payload;
      return;
    }
    const mine = state.me != null && m.senderId === state.me;

    if (m.isDeleted) {
      el.className = "msg msg--deleted" + (mine ? " msg--mine" : "");
      el.textContent = "message deleted";
      return;
    }
    el.className = "msg" + (mine ? " msg--mine" : "");

    const sender = document.createElement("div");
    sender.className = "msg__sender";
    sender.textContent = m.senderName || m.senderId;

    let content;
    if (m.contentType === "Image" && isOwnBlobUrl(m.payload)) {
      content = document.createElement("img");
      content.className = "msg__image";
      content.loading = "lazy";
      content.src = m.payload;   // restricted to our own blob path
    } else {
      content = document.createElement("div");
      content.className = "msg__text";
      // Default: plain text (XSS-safe escape). Markdown is opt-in and still XSS-safe (DOM-built, no innerHTML).
      if (state.caps.canRenderMarkdown) appendInlineMarkdown(content, m.payload);
      else content.textContent = m.payload;
    }

    const time = document.createElement("div");
    time.className = "msg__time";
    time.textContent = formatTime(m.createdAtUtc) + (m.editedAtUtc ? " · edited" : "");

    if (!mine) el.appendChild(sender);
    el.append(content, time);

    // Author affordances: edit (text only) / delete — each gated by the host policy. Tap to reveal.
    const canEdit = mine && m.contentType === "Text" && state.caps.canEditMessages;
    const canDelete = mine && state.caps.canDeleteMessages;
    el.dataset.ownText = (canEdit || canDelete) ? "1" : "";
    if (canEdit || canDelete) {
      const actions = document.createElement("div");
      actions.className = "msg__actions";
      if (canEdit) {
        const edit = document.createElement("button");
        edit.type = "button"; edit.className = "msg__action"; edit.textContent = "Edit";
        edit.addEventListener("click", (e) => { e.stopPropagation(); editMessage(m); });
        actions.appendChild(edit);
      }
      if (canDelete) {
        const del = document.createElement("button");
        del.type = "button"; del.className = "msg__action"; del.textContent = "Delete";
        del.addEventListener("click", (e) => { e.stopPropagation(); deleteMessage(m); });
        actions.appendChild(del);
      }
      el.appendChild(actions);
    }
  }

  function findMessageEl(messageId) {
    return $("messages").querySelector(`[data-message-id="${CSS.escape(messageId)}"]`);
  }

  async function editMessage(m) {
    const next = await showPrompt({ title: "Edit message", value: m.payload, okLabel: "Save" });
    if (next == null || next.trim() === "" || next === m.payload) return;
    const res = await api(`/api/messages/${encodeURIComponent(m.messageId)}`, { method: "PUT", body: JSON.stringify({ payload: next }) });
    if (!res.ok) { showToast("Edit failed.", "error"); return; }
    const updated = await res.json();
    const el = findMessageEl(m.messageId);
    if (el) renderMessage(el, updated);
  }

  async function deleteMessage(m) {
    if (!(await showConfirm({ title: "Delete message", message: "This can't be undone.", okLabel: "Delete", danger: true }))) return;
    const res = await api(`/api/messages/${encodeURIComponent(m.messageId)}`, { method: "DELETE" });
    if (!res.ok) { showToast("Delete failed.", "error"); return; }
    const updated = await res.json();
    const el = findMessageEl(m.messageId);
    if (el) renderMessage(el, updated);
  }

  function isOwnBlobUrl(url) { return typeof url === "string" && url.startsWith(BASE + "/api/blobs/"); }

  // ---------- inline markdown (opt-in; XSS-safe by construction — builds DOM nodes, never innerHTML) ----------
  // Safe subset: **bold**, *italic*, `code`, [label](url), and bare http(s) autolinks. Only http(s)/mailto
  // links are emitted; any other scheme (javascript:, data:, …) falls back to literal text. Underscore
  // emphasis is intentionally omitted so identifiers like snake_case render literally.
  const MD_RULES = [
    { re: /`([^`]+)`/, tag: "code", literal: true },           // code first — its content is literal
    { re: /\[([^\]]+)\]\(([^)\s]+)\)/, link: true },           // [label](url)
    { re: /\*\*([^*]+)\*\*/, tag: "strong" },                  // **bold** (before *italic*)
    { re: /\*([^*]+)\*/, tag: "em" },                          // *italic*
    { re: /(https?:\/\/[^\s]+)/, autolink: true }              // bare url
  ];

  function makeLink(url, label) {
    if (!/^(https?:|mailto:)/i.test(url)) return document.createTextNode(label);   // reject unsafe schemes
    const a = document.createElement("a");
    a.href = url; a.textContent = label;
    a.target = "_blank"; a.rel = "noopener noreferrer"; a.className = "msg__link";
    return a;
  }

  // Append `text` to `parent` as a mix of text nodes and safe inline elements. Recurses for nested emphasis.
  function appendInlineMarkdown(parent, text) {
    while (text.length) {
      let best = null;
      for (const rule of MD_RULES) {
        rule.re.lastIndex = 0;
        const m = rule.re.exec(text);
        if (m && (best === null || m.index < best.m.index)) best = { rule, m };
      }
      if (best === null) { parent.appendChild(document.createTextNode(text)); return; }
      const { rule, m } = best;
      if (m.index > 0) parent.appendChild(document.createTextNode(text.slice(0, m.index)));
      if (rule.autolink) parent.appendChild(makeLink(m[1], m[1]));
      else if (rule.link) parent.appendChild(makeLink(m[2], m[1]));
      else {
        const el = document.createElement(rule.tag);
        if (rule.literal) el.textContent = m[1]; else appendInlineMarkdown(el, m[1]);
        parent.appendChild(el);
      }
      text = text.slice(m.index + m[0].length);
    }
  }

  function formatTime(iso) {
    try { return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); }
    catch { return ""; }
  }
  function scrollToBottom() { const b = $("messages"); b.scrollTop = b.scrollHeight; }

  // ---------- SignalR ----------
  async function ensureConnected() {
    if (state.connection) return;
    const httpOpts = bridge.token ? { accessTokenFactory: () => bridge.token } : {};
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(BASE + "/hub", httpOpts)
      .withAutomaticReconnect()
      .build();

    conn.on("ReceiveMessage", (m) => {
      if (m.channelId === state.current && bridge.panelVisible) {
        appendMessage(m);
        scrollToBottom();
        markReadStreaming(m.channelId);   // keep the read pointer current while watching (debounced persist)
      } else {
        // Live unread for a channel I'm not looking at — never count my own messages.
        const ch = state.channels.find((c) => c.id === m.channelId);
        if (ch && ch.isMember && m.senderId !== state.me) {
          ch.unreadCount = (ch.unreadCount || 0) + 1;
          renderBadge(m.channelId);
          pushBridgeTotal();
        }
        bridge.onIncoming(m);   // native newMessage toast (WebView2)
      }
    });

    // Live edit/delete: re-render the affected bubble in place if it's on screen.
    conn.on("MessageUpdated", (m) => {
      const el = findMessageEl(m.messageId);
      if (el) renderMessage(el, m);
    });

    // Toggle the visual dot and expose the state to assistive tech (the color alone isn't perceivable;
    // role=status + aria-live announces transitions). WCAG 1.4.1 / 4.1.2 / 4.1.3.
    const setConn = (online) => {
      const el = $("conn");
      el.classList.toggle("is-online", online);
      el.setAttribute("aria-label", online ? "Connected" : "Disconnected");
    };
    conn.onreconnecting(() => setConn(false));
    conn.onreconnected(() => setConn(true));
    conn.onclose(() => setConn(false));

    await conn.start();
    setConn(true);
    state.connection = conn;
  }

  async function sendMessage(text) {
    if (!text.trim() || !state.current || !state.connection) return;
    await state.connection.invoke("SendMessage", state.current, text);
  }

  // ---------- image upload (client-side Canvas resize, server load = 0) ----------
  async function uploadAndSendImage(file) {
    if (!file || !state.current) return;
    const blob = await resizeImage(file, 1200, 0.75);
    const form = new FormData();
    form.append("file", blob, (file.name || "image").replace(/\.[^.]+$/, "") + ".jpg");
    const res = await fetch(BASE + "/api/upload", { method: "POST", credentials: "same-origin", body: form });
    if (!res.ok) { showToast("Upload failed.", "error"); return; }
    const { url } = await res.json();
    await ensureConnected();
    await state.connection.invoke("SendImage", state.current, url);
  }

  function resizeImage(file, maxEdge, quality) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => {
        let { width, height } = img;
        const scale = Math.min(1, maxEdge / Math.max(width, height));
        width = Math.round(width * scale); height = Math.round(height * scale);
        const canvas = document.createElement("canvas");
        canvas.width = width; canvas.height = height;
        canvas.getContext("2d").drawImage(img, 0, 0, width, height);
        canvas.toBlob((b) => b ? resolve(b) : reject(new Error("toBlob failed")), "image/jpeg", quality);
      };
      img.onerror = reject;
      img.src = URL.createObjectURL(file);
    });
  }

  // ---------- WPF native bridge (bridge-protocol.md) ----------
  const bridge = {
    host: window.chrome && window.chrome.webview ? window.chrome.webview : null,
    token: null,
    panelVisible: true,   // assume visible outside WebView2
    unread: 0,
    post(obj) { if (this.host) this.host.postMessage(obj); },
    onIncoming(m) {
      // Native toast only; the unread total is owned by pushBridgeTotal() (server-truth).
      if (!this.host || this.panelVisible) return;
      this.post({ type: "newMessage", from: m.senderName || m.senderId, preview: m.contentType === "Image" ? "[image]" : m.payload, channelId: m.channelId });
    },
    init() {
      if (!this.host) return;
      this.panelVisible = false; // WebView2 host controls visibility explicitly
      this.host.addEventListener("message", async (e) => {
        const msg = e.data || {};
        if (msg.type === "auth") { this.token = msg.token; resetConnection(); }
        else if (msg.type === "panelVisible") {
          this.panelVisible = !!msg.visible;
          // Becoming visible means the user is now reading the open channel — mark it read and
          // recompute the native badge from remaining cross-session unread (not a blanket zero).
          if (this.panelVisible) {
            if (state.current) markRead(state.current);
            else pushBridgeTotal();
          }
        }
        else if (msg.type === "focusChannel" && msg.channelId) {
          const ch = state.channels.find((c) => c.id === msg.channelId);
          if (ch) await openChannel(ch);
        }
      });
      this.post({ type: "ready" });
    }
  };

  async function resetConnection() {
    if (state.connection) { try { await state.connection.stop(); } catch { } state.connection = null; }
    await ensureConnected();
  }

  // ---------- PWA shell (?shell=pwa only) ----------
  const shellMode = new URLSearchParams(location.search).get("shell");
  function registerServiceWorker() {
    if (shellMode !== "pwa" || !("serviceWorker" in navigator)) return;
    window.addEventListener("load", () => {
      navigator.serviceWorker.register(BASE + "/sw.js", { scope: BASE + "/" })
        .then(() => { if ("Notification" in window && Notification.permission === "granted") subscribePush(); })
        .catch(() => { });
    });
    // A click on a push notification asks the SW to focus the relevant channel.
    navigator.serviceWorker.addEventListener("message", (e) => {
      const msg = e.data || {};
      if (msg.type === "focusChannel" && msg.channelId) {
        const ch = state.channels.find((c) => c.id === msg.channelId);
        if (ch) openChannel(ch);
      }
    });
  }

  // Notification permission: requested only in the pwa shell, only when undecided, only once per
  // session, and only off a real user gesture (browsers penalize on-load prompts). On grant we subscribe.
  let notifRequested = false;
  function requestNotifications() {
    if (shellMode !== "pwa" || !("Notification" in window)) return;
    if (Notification.permission !== "default" || notifRequested) return;
    notifRequested = true;
    try {
      Notification.requestPermission().then((p) => { if (p === "granted") subscribePush(); }).catch(() => { });
    } catch { }
  }

  // Register this browser's push subscription with the server (no-op when push isn't configured → 404).
  async function subscribePush() {
    if (shellMode !== "pwa" || !("serviceWorker" in navigator) || !("PushManager" in window)) return;
    try {
      const res = await fetch(BASE + "/api/push/vapid-public-key", { credentials: "same-origin" });
      if (!res.ok) return;                       // push disabled on the server
      const { publicKey } = await res.json();
      const reg = await navigator.serviceWorker.ready;
      let sub = await reg.pushManager.getSubscription();
      if (!sub) {
        sub = await reg.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: urlBase64ToUint8Array(publicKey)
        });
      }
      await fetch(BASE + "/api/push/subscriptions", {
        method: "POST", credentials: "same-origin",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(sub)
      });
    } catch { /* push is best-effort */ }
  }

  function urlBase64ToUint8Array(base64) {
    const padding = "=".repeat((4 - (base64.length % 4)) % 4);
    const normalized = (base64 + padding).replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(normalized);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  // Install affordance: capture the deferred prompt, reveal the button, prompt on click.
  let deferredInstall = null;
  function setupInstallPrompt() {
    if (shellMode !== "pwa") return;
    const btn = $("install");
    window.addEventListener("beforeinstallprompt", (e) => {
      e.preventDefault();
      deferredInstall = e;
      if (btn) btn.hidden = false;
    });
    window.addEventListener("appinstalled", () => {
      deferredInstall = null;
      if (btn) btn.hidden = true;
      requestNotifications();
    });
    if (btn) btn.addEventListener("click", async () => {
      if (!deferredInstall) return;
      deferredInstall.prompt();
      try { await deferredInstall.userChoice; } catch { }
      deferredInstall = null;
      btn.hidden = true;
      requestNotifications();
    });
    // Already-installed (standalone) sessions never fire beforeinstallprompt — arm a one-shot
    // gesture listener so we can still ask for notifications without an on-load prompt.
    const armOnGesture = () => { document.removeEventListener("click", armOnGesture); requestNotifications(); };
    document.addEventListener("click", armOnGesture, { once: true });
  }

  // ---------- wiring ----------
  $("back").addEventListener("click", async () => { showList(); await loadChannels(); });
  $("new-channel").addEventListener("click", async () => {
    // Name + private toggle in one form (replaces the old prompt→confirm two-step).
    const result = await showForm({
      title: "New channel", okLabel: "Create",
      fields: [
        { name: "name", label: "Channel name", type: "text", placeholder: "e.g. general", autofocus: true },
        { name: "isPrivate", label: "Private — only people you add can see it", type: "checkbox" }
      ]
    });
    if (!result) return;
    const name = (result.name || "").trim();
    if (!name) return;
    const res = await api("/api/channels", { method: "POST", body: JSON.stringify({ name, isPrivate: result.isPrivate }) });
    if (res.ok) await loadChannels();
    else if (res.status === 409) showToast("A channel with that name already exists.", "error");
  });
  $("add-member").addEventListener("click", async () => {
    if (!state.current) return;
    const userId = await showPrompt({ title: "Add member", placeholder: "user id", okLabel: "Add" });
    if (!userId || !userId.trim()) return;
    const res = await api(`/api/channels/${encodeURIComponent(state.current)}/members`, { method: "POST", body: JSON.stringify({ userId: userId.trim() }) });
    if (res.ok) showToast(`Added ${userId.trim()}.`, "success");
    else if (res.status === 403) showToast("Only the channel creator can add members.", "error");
    else showToast("Could not add member.", "error");
  });
  $("composer").addEventListener("submit", async (e) => {
    e.preventDefault();
    const input = $("text");
    const text = input.value;
    input.value = "";
    await sendMessage(text);
  });
  // Tap your own text message to reveal/hide its edit/delete actions (touch-friendly, no hover).
  $("messages").addEventListener("click", (e) => {
    if (e.target.closest(".msg__action")) return;   // button clicks act, don't toggle
    const msg = e.target.closest('.msg[data-own-text="1"]');
    if (msg) msg.classList.toggle("is-actions-open");
  });
  $("attach").addEventListener("click", () => $("file").click());
  $("file").addEventListener("change", async (e) => {
    const file = e.target.files && e.target.files[0];
    e.target.value = "";
    if (file) await uploadAndSendImage(file);
  });

  // learn our own id: first message we send is echoed back; capture senderId of any message
  // whose senderName matches — instead, derive "me" from a lightweight whoami endpoint.
  (async function boot() {
    registerServiceWorker();
    setupInstallPrompt();
    bridge.init();
    try {
      const me = await getJson("/api/whoami");
      state.me = me.userId;
    } catch { /* whoami optional */ }
    try {
      state.caps = await getJson("/api/capabilities");
    } catch { /* keep optimistic defaults; server enforces regardless */ }
    await loadChannels();
  })();
})();
