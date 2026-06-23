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