# Integration Guide

두 가지 주요 시나리오의 통합 절차와 코드. 공통 전제는 **호스트 ASP.NET Core 백엔드에 BareChat이 same-origin 마운트**되어 있다는 것입니다.

---

## 0. 서버 마운트 (공통)

```csharp
using BareChat.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBareChat(options =>
{
    // options.DataPath = "App_Data/barechat";   // (선택) 미지정 시 기본 경로. chat.db + blobs/ 가 여기 보관됨
    options.RoutePrefix = "/chat";
    options.MaxImageSizeInBytes = 3 * 1024 * 1024;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
// SQLite 파일 DB + 파일 blob 이 DataPath 에서 자동 동작 (zero-config)

var app = builder.Build();
app.UseRouting();
app.UseAuthentication();     // 호스트 인증
app.UseAuthorization();
app.UseBareChat();           // 반드시 인증/인가 뒤
app.Run();
```

이후 클라이언트는 `shell` 쿼리스트링으로 분기합니다.

| URL | 시나리오 |
|---|---|
| `/chat?shell=pwa` | 1 — 모바일 PWA |
| `/chat?shell=webview` | 2 — WPF 사이드패널 |
| `/chat?shell=iframe` | (best-effort) 레거시 임베드 |

---

## 시나리오 1 — PWA

**목표:** 웹앱으로 접속 → 모바일 홈 화면 설치 → 백그라운드에서도 웹푸시 수신.

### 인증
- same-origin이므로 **호스트 쿠키 세션을 그대로 흡수**. WS 포함 자동 동작.
- 별도 origin으로 띄울 경우만 토큰 모드(`?access_token=`) 사용.

### 설치 (PWA)
1. `/chat?shell=pwa` 접속 → 임베디드 매니페스트 + Service Worker 등록(M2).
2. 브라우저 설치 프롬프트 → 홈 화면 추가.
3. 접속 시 `Notification.requestPermission()` → 허용 시 push subscription 생성·서버 저장.

### 알림 (M2)
- 앱이 떠 있으면 SignalR 라이브.
- 백그라운드/닫힘 → 서버가 `WebPushChannel`(VAPID)로 발송 → SW가 OS 알림 표시 → 클릭 시 해당 채널로 포커싱.

> **폐쇄망에서는 WebPush가 막힐 수 있습니다**(FCM 아웃바운드 차단). 그 환경의 알림은 시나리오 2(WPF) 경로를 쓰세요. → [notifications.md](notifications.md#3-webpush-pwa-m2)

---

## 시나리오 2 — WPF (WebView2)

**목표:** WPF 앱 사이드패널에 채팅 UI를 띄우고, 새 메시지 발생 시 토글 버튼에 색상/뱃지. 백엔드 코드가 프로그래밍 방식으로 메시지 주입.

> **알림 범위:** OS 상주 없음. **앱이 떠 있는 동안에만** 동작. 닫히면 알림 종료(의도된 설계).

### 단계
1. WPF에 WebView2 컨트롤 배치, `EnsureCoreWebView2Async()`.
2. `/chat?shell=webview` 로 Navigate → UI가 `{type:"ready"}` 송신.
3. WPF가 `{type:"auth", token}` 회신 → JS가 SignalR을 `?access_token=` 으로 연결.
4. 메시지 수신 시 JS가 `{type:"unread", count}` 송신 → WPF 버튼 갱신.
5. 패널 열고 닫을 때 WPF가 `{type:"panelVisible", visible}` 송신 → unread 리셋.

전체 메시지 스펙과 샘플 코드는 [bridge-protocol.md](bridge-protocol.md).

### 프로그래밍 방식 발행
WPF/백엔드 어디서든, 열린 패널·라이브 소켓과 무관하게 REST로 게시:

```csharp
await httpClient.PostAsJsonAsync($"{baseUrl}/chat/messages", new
{
    channelId   = "line-a",
    contentType = "System",
    payload     = "작업지시 WO-2031 변경됨"
});
```

이를 통해 채팅 채널이 **시스템 이벤트 피드**(라인 알람·작업지시·빌드완료 등)로도 기능합니다.

### (옵션) 프로세스 차원 구독 — M2+
사람 UI(WebView2)와 별개로, WPF 프로세스가 직접 구독/발행하려면 얇은 `BareChat.Client`(.NET SignalR client 래퍼)를 사용. 패널이 닫혀 있어도 프로세스가 이벤트를 받을 수 있습니다.

---

## 부록 — 이미지 업로드 동작

- 드래그앤드롭 / 클립보드 붙여넣기 인터셉트.
- **클라이언트 Canvas**에서 max 1200px, WebP/JPEG q75로 리사이즈 후 업로드(서버 부하 0).
- 서버는 `IBlobStore`(기본 파일시스템)에 저장, DB엔 메타만. 서버측 ImageSharp 리사이즈는 pluggable 옵션.

## 부록 — API 표면 (M1 구현)

`RoutePrefix` 기본값 `/chat` 기준. 모든 엔드포인트는 인증된 호스트 유저를 요구합니다(미인증 → 401).

### REST
| 메서드 · 경로 | 설명 |
|---|---|
| `GET /chat/api/whoami` | 현재 유저(`userId`/`displayName`/`avatarUrl`) |
| `GET /chat/api/channels` | 전체 공개 채널 + `isMember` |
| `GET /chat/api/channels/mine` | 내가 구독한 채널 |
| `POST /chat/api/channels` `{name, channelId?}` | 채널 생성(slug 자동, 생성자 자동 가입) · 중복 409 |
| `POST /chat/api/channels/{id}/join` · `/leave` | 가입 · 이탈(기본 채널 이탈 불가) |
| `DELETE /chat/api/channels/{id}` | 삭제(생성자만, 기본 채널 불가) |
| `GET /chat/api/channels/{id}/messages?limit&before` | 메시지 히스토리(oldest-first) |
| `POST /chat/messages` `{channelId, payload, contentType?}` | 프로그래밍 발행(기본 `System`, 이벤트 피드) |
| `POST /chat/api/upload` (multipart `file`) | 이미지 업로드 → `{blobId, url}`. **매직바이트 스니핑 + 화이트리스트(PNG/JPEG/GIF/WebP만)** — 클라이언트 Content-Type 무시, SVG/HTML 거부 |
| `GET /chat/api/blobs/{id}` | blob 스트리밍. `X-Content-Type-Options: nosniff` + `Content-Security-Policy: default-src 'none'; sandbox` (stored XSS 방어) |

### SignalR Hub (`/chat/hub`)
- 클라이언트 호출: `SendMessage(channelId, text)` · `SendImage(channelId, url)` · `JoinChannel(channelId)` · `LeaveChannel(channelId)`
- 서버 이벤트: `ReceiveMessage(message)`
- 연결 시 presence 등록 + 기본 채널 자동 가입. 전송 폴백(WS→SSE→Long Polling)은 SignalR 기본.

### 임베디드 UI
`GET /chat` → 모바일 2-뷰 셸(채널 목록 ↔ 대화). `app.js`/`app.css`/`signalr.min.js` 동봉(오프라인). `RoutePrefix`는 셸에 주입됨.

## 부록 — 체크리스트

| 항목 | 시나리오 1 | 시나리오 2 |
|---|---|---|
| same-origin 마운트 | 필수 | 필수 |
| 쿠키 인증 | ✅ 자동 | — |
| 브리지 토큰 인증 | — | ✅ |
| Service Worker / 웹푸시 | ✅ (M2) | ❌ |
| 네이티브 브리지 뱃지 | ❌ | ✅ |
| REST publish | 사용 가능 | ✅ 핵심 |
| 백그라운드 알림 | 웹푸시 | 없음(앱 생존 한정) |
---

## 부록 — P2 (M2) API 표면

### PWA / Service Worker
| 엔드포인트 | 설명 |
|---|---|
| `GET /chat/sw.js` | Service Worker(템플릿 `BASE`·자산 해시 버전 주입). scope `/chat/` 로 애드온 격리 |
| `GET /chat/manifest.webmanifest` | `start_url=/chat/?shell=pwa`, `scope=/chat/`. 설치 이름/색상/아이콘 |

`?shell=pwa` 에서만 SW 등록 + 설치 프롬프트(`beforeinstallprompt`) + 제스처 기반 `Notification.requestPermission()` + 푸시 구독. 오프라인 시 앱셸은 캐시에서 서빙(라이브 데이터 `/api`·`/hub` 는 항상 네트워크).

### 웹푸시 (VAPID)
설정: `options.Push.PublicKey/PrivateKey/Subject` (미설정 시 푸시 자동 비활성).

| 엔드포인트 | 설명 |
|---|---|
| `GET /chat/api/push/vapid-public-key` | 구독용 VAPID 공개키(`{publicKey}`). 미설정 시 404 |
| `POST /chat/api/push/subscriptions` `{endpoint, keys:{p256dh, auth}}` | 현재 유저 구독 저장(인증 필수, 소유자=서버 컨텍스트) |
| `DELETE /chat/api/push/subscriptions` `{endpoint}` | 구독 제거 |

오프라인 멤버는 `MessagePublisher` → `WebPushChannel`(wake-up)로 라우팅. 푸시 서비스가 gone(404/410) 보고 시 구독 자동 prune. SW `push` → `showNotification`, `notificationclick` → 해당 채널 포커스(`postMessage{focusChannel}`).

### 크로스오리진 토큰 인증
`app.UseBareChatAccessToken();` 를 `UseAuthentication()` **앞**에 배치. hub 경로(`/chat/hub`) 요청의 `?access_token=` 을 `Authorization: Bearer` 로 승격(기존 헤더 미덮어쓰기, scheme-agnostic). 호스트의 bearer 인증 스킴이 WS 연결을 인증.

### .NET 클라이언트 SDK (`BareChat.Client`)
```csharp
await using var client = new BareChatClient(new BareChatClientOptions
{
    BaseAddress = new Uri("https://host/"),
    RoutePrefix = "/chat",
    AccessTokenProvider = () => token   // 크로스오리진 시(hub ?access_token= + REST Bearer)
});
client.MessageReceived += m => { /* ChatMessage */ };
await client.ConnectAsync();
await client.SubscribeAsync("general");          // JoinChannel
await client.SendTextAsync("general", "hi");     // SendMessage(라이브)
await client.PublishAsync("general", "event");   // REST publish(System, 소켓 불필요)
```

### 스케일아웃 백플레인
`AddBareChat(configure, configureSignalR: sr => sr.AddStackExchangeRedis(conn))` 로 SignalR 백플레인 결선(Redis 패키지는 호스트 종속성). 멀티노드 wake-up 정확성을 위해 분산 `IPresenceTracker` 구현체 필요(seam 준비됨, 구현은 수요 기반).

---

## 부록 — P3 (M3) API 표면

### 세션 간 unread (서버 `lastReadAt`)
`GET /chat/api/channels`·`/mine` 응답의 각 채널에 `unreadCount`(= 내 `lastReadAt` 이후, 본인·삭제 제외 메시지 수). 기준선은 `COALESCE(last_read_at, joined_at)` — 가입 이전 히스토리는 unread 아님.

| 엔드포인트 | 설명 |
|---|---|
| `POST /chat/api/channels/{id}/read` | 채널을 현재 시각으로 읽음 처리(비멤버는 no-op) → 204 |

### 메시지 편집/삭제 (작성자 전용, D6)
| 엔드포인트 | 설명 |
|---|---|
| `PUT /chat/api/messages/{id}` `{payload}` | 편집(작성자만·Text·미삭제). `editedAtUtc` 설정 → `MessageUpdated` 라이브 브로드캐스트. 403/400/404 |
| `DELETE /chat/api/messages/{id}` | 소프트 삭제(작성자만, `isDeleted=true` + payload 비움) → tombstone. `MessageUpdated` 브로드캐스트 |
| `GET /chat/api/capabilities` | `{canEditMessages, canDeleteMessages}` — UI가 affordance 게이팅 |

호스트 정책: `options.Messages.AllowEditing/AllowDeletion`(기본 true). false면 해당 엔드포인트 403 + UI에서 affordance 숨김. 편집은 push/wake-up 미발생(조용한 편집). 라이브 `MessageUpdated` 는 온라인 멤버에 한해 전송(오프라인은 다음 히스토리 로드 시 반영).

### Private 채널 (D9 확장)
`POST /chat/api/channels` 에 `{isPrivate: true}` 로 생성. private 채널은 `/channels` 목록에서 비멤버에게 숨겨지고, 읽기/쓰기/가입이 멤버십으로 게이팅(`ChannelMembershipAuthorizationProvider`).

| 엔드포인트 | 설명 |
|---|---|
| `POST /chat/api/channels` `{name, isPrivate?}` | `isPrivate=true` → private 채널 |
| `POST /chat/api/channels/{id}/members` `{userId}` | 멤버 초대(생성자만). private 채널 진입 유일 경로 → 204/403 |
| `POST /chat/api/channels/{id}/join` | public은 자유 가입, **private은 비멤버 403**(Hub `JoinChannel` 도 동일 차단) |

DTO에 `isPrivate` 추가. private 비멤버의 직접 history/publish 는 authz가 거부(403).
