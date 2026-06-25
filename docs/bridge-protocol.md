# Native Bridge Protocol (WPF ↔ WebView2)

시나리오 2(WPF 사이드패널)의 핵심. WebView2 안의 BareChat UI(JS)와 WPF 네이티브 레이어 사이의 양방향 계약을 정의합니다. 이 계약이 **버튼 색상 표시**와 **토큰 인증**, 그리고 **unread 동기화**를 담당합니다.

전송: WebView2 표준 메시지 채널.
- **JS → WPF:** `window.chrome.webview.postMessage(json)`
- **WPF → JS:** `coreWebView2.PostWebMessageAsJson(json)`

모든 메시지는 `{ "type": "...", ... }` JSON 객체입니다.

---

## 1. JS → WPF 메시지

| type | 페이로드 | WPF 동작 |
|---|---|---|
| `ready` | — | 핸드셰이크 완료. WPF는 이때 `auth` 를 회신 |
| `unread` | `{ count: number }` | 사이드패널 토글 버튼 색상/뱃지 갱신 |
| `newMessage` | `{ from: string, preview: string, channelId: string }` | (옵션) Windows toast 또는 버튼 강조 애니메이션 |

```jsonc
// JS → WPF
{ "type": "ready" }
{ "type": "unread", "count": 3 }
{ "type": "newMessage", "from": "라인A 관리자", "preview": "온도 임계 초과", "channelId": "line-a" }
```

---

## 2. WPF → JS 메시지

| type | 페이로드 | JS 동작 |
|---|---|---|
| `auth` | `{ token: string }` | SignalR 연결 전 토큰 보관 → `?access_token=` 으로 연결 |
| `focusChannel` | `{ channelId: string }` | 해당 채널로 전환 |
| `panelVisible` | `{ visible: boolean }` | 패널 표시 상태 갱신. `true` 면 현재 채널 unread 리셋 |
| `theme` | `{ theme: "light" \| "dark" }` | `<html data-theme>` 갱신 → 호스트 테마에 맞춰 light/dark 전환. 초기 로드는 `?theme=` 로 무플래시 적용 가능 |

```jsonc
// WPF → JS
{ "type": "auth", "token": "eyJhbGciOi..." }
{ "type": "panelVisible", "visible": true }
{ "type": "focusChannel", "channelId": "line-a" }
{ "type": "theme", "theme": "light" }
```

---

## 3. 핸드셰이크 & 인증 흐름

```
WPF                                   WebView2 (BareChat UI / JS)
 │                                            │
 │  Navigate("/chat?shell=webview")           │
 │ ─────────────────────────────────────────►│
 │                                            │  앱 로드, 브리지 활성화
 │              { type:"ready" }              │
 │ ◄──────────────────────────────────────── │
 │                                            │
 │       { type:"auth", token }               │
 │ ─────────────────────────────────────────►│
 │                                            │  SignalR 연결 ?access_token=token
 │                                            │  (WS 핸드셰이크 헤더 불가 → 쿼리스트링)
 │              { type:"unread", count:0 }     │
 │ ◄──────────────────────────────────────── │
```

WPF는 이미 호스트(예: MES)에 로그인된 상태이므로, 자기 토큰을 `auth` 로 주입합니다. WebView2 쿠키 공유 설정을 만질 필요가 없고 origin에 무관합니다.

---

## 4. Unread 동기화 로직 (M1)

M1에서는 **클라이언트 측 카운트로 충분**합니다.

- 메시지 수신 시 `panelVisible === false` 이면 `unread++` → `{type:'unread', count}` 송신
- WPF가 `{type:'panelVisible', visible:true}` 를 보내면 현재 채널 unread 리셋 → `{type:'unread', count:0}` 송신

```js
let unread = 0;
let panelVisible = true;

connection.on("ReceiveMessage", (msg) => {
  renderMessage(msg);
  if (!panelVisible) {
    unread++;
    postToHost({ type: "unread", count: unread });
  }
});

// WPF → JS
window.chrome.webview.addEventListener("message", (e) => {
  const m = e.data;
  if (m.type === "panelVisible") {
    panelVisible = m.visible;
    if (m.visible) { unread = 0; postToHost({ type: "unread", count: 0 }); }
  }
  if (m.type === "auth")  { authToken = m.token; startConnection(); }
});

function postToHost(obj) { window.chrome.webview?.postMessage(obj); }
```

> **세션 간 누적 unread**("앱 껐다 켰더니 N개")는 서버 `lastReadAt` 추적이 필요합니다 → **M3로 이연**. M1 카운트는 세션 내에서만 유효.

---

## 5. WPF 측 샘플

```csharp
// 초기화
await webView.EnsureCoreWebView2Async();
webView.CoreWebView2.WebMessageReceived += OnWebMessage;
webView.CoreWebView2.Navigate($"{baseUrl}/chat?shell=webview");

private void OnWebMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
{
    var json = e.WebMessageAsJson;          // { "type": "...", ... }
    var msg = JsonSerializer.Deserialize<BridgeMessage>(json);

    switch (msg?.Type)
    {
        case "ready":
            Send(new { type = "auth", token = _session.AccessToken });
            break;
        case "unread":
            ChatButton.BadgeCount = msg.Count;          // 버튼 색상/뱃지
            break;
        case "newMessage":
            ChatButton.Flash();                          // (옵션) 강조
            break;
    }
}

private void Send(object payload) =>
    webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));

// 패널 열고/닫을 때
private void ToggleChatPanel(bool open) =>
    Send(new { type = "panelVisible", visible = open });
```

---

## 6. 프로그래밍 방식 발행과의 관계

버튼 뱃지(받기)는 이 브리지로 해결합니다. 그러나 **코드가 메시지를 보내는** 경우(예: "장비 #3 온도 임계 초과"를 백그라운드 스레드가 게시)는 패널/소켓에 의존하면 안 되므로 브리지가 아니라 REST를 씁니다.

```csharp
// 라이브 소켓·열린 패널과 무관하게 한 방에 게시
await httpClient.PostAsJsonAsync($"{baseUrl}/chat/messages", new
{
    channelId = "line-a",
    contentType = "System",
    payload = "장비 #3 온도 임계 초과 (94°C)"
});
// → Hub 가 채널 구독자 전원에게 브로드캐스트
```

자세한 통합 절차는 [integration-guide.md](integration-guide.md) 참고.