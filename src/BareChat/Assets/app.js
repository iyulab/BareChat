"use strict";
// BareChat embedded UI — vanilla, no virtual DOM. XSS-safe (textContent only).
(function () {
  const BASE = window.BARECHAT_BASE || "";
  const $ = (id) => document.getElementById(id);

  const state = {
    channels: [],
    current: null,        // channelId
    connection: null,
    me: null              // resolved senderId once we send/receive
  };

  // ---------- REST ----------
  async function api(path, opts) {
    const res = await fetch(BASE + path, Object.assign({ credentials: "same-origin", headers: { "Content-Type": "application/json" } }, opts));
    if (res.status === 401) { location.href = BASE.replace(/\/chat$/, "") + "/"; throw new Error("unauthorized"); }
    return res;
  }
  const getJson = async (p) => (await api(p)).json();

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

  async function loadChannels() {
    state.channels = await getJson("/api/channels");
    const list = $("channel-list");
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
      name.textContent = ch.name;
      const meta = document.createElement("div");
      meta.className = "channel__meta";
      meta.textContent = (ch.isDefault ? "default" : ch.isMember ? "joined" : "tap to join");
      body.append(name, meta);

      li.append(avatar, body);

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
  }

  // ---------- conversation ----------
  async function openChannel(ch) {
    if (!ch.isMember) {
      await api(`/api/channels/${encodeURIComponent(ch.id)}/join`, { method: "POST" });
    }
    state.current = ch.id;
    $("chat-title").textContent = ch.name;
    showChat();
    const msgs = await getJson(`/api/channels/${encodeURIComponent(ch.id)}/messages?limit=100`);
    const box = $("messages");
    box.replaceChildren();
    for (const m of msgs) appendMessage(m);
    scrollToBottom();
    await ensureConnected();
  }

  function appendMessage(m) {
    const box = $("messages");
    const el = document.createElement("div");
    if (m.contentType === "System") {
      el.className = "msg msg--system";
      el.textContent = m.payload;
      box.appendChild(el);
      return;
    }
    const mine = state.me != null && m.senderId === state.me;
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
      content.textContent = m.payload;   // XSS-safe
    }

    const time = document.createElement("div");
    time.className = "msg__time";
    time.textContent = formatTime(m.createdAtUtc);

    if (!mine) el.appendChild(sender);
    el.append(content, time);
    box.appendChild(el);
  }

  function isOwnBlobUrl(url) { return typeof url === "string" && url.startsWith(BASE + "/api/blobs/"); }

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
      } else {
        bridge.onIncoming(m);   // unread / native badge
      }
    });

    const setConn = (online) => $("conn").classList.toggle("is-online", online);
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
    if (!res.ok) { alert("Upload failed."); return; }
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
      if (!this.host) return;
      if (!this.panelVisible) {
        this.unread++;
        this.post({ type: "unread", count: this.unread });
        this.post({ type: "newMessage", from: m.senderName || m.senderId, preview: m.contentType === "Image" ? "[image]" : m.payload, channelId: m.channelId });
      }
    },
    init() {
      if (!this.host) return;
      this.panelVisible = false; // WebView2 host controls visibility explicitly
      this.host.addEventListener("message", async (e) => {
        const msg = e.data || {};
        if (msg.type === "auth") { this.token = msg.token; resetConnection(); }
        else if (msg.type === "panelVisible") {
          this.panelVisible = !!msg.visible;
          if (this.panelVisible) { this.unread = 0; this.post({ type: "unread", count: 0 }); }
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
    const name = prompt("New channel name");
    if (!name) return;
    const res = await api("/api/channels", { method: "POST", body: JSON.stringify({ name }) });
    if (res.ok) await loadChannels();
    else if (res.status === 409) alert("A channel with that name already exists.");
  });
  $("composer").addEventListener("submit", async (e) => {
    e.preventDefault();
    const input = $("text");
    const text = input.value;
    input.value = "";
    await sendMessage(text);
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
    await loadChannels();
  })();
})();
