# BareChat

> **Ultra-lightweight, embeddable chat module for .NET** — 단일 DLL, in-process, zero-infra.

BareChat은 ASP.NET Core 호스트에 미들웨어 한 줄로 얹는 **올인원 채팅 애드온**입니다. 외부 메시지 브로커나 별도 채팅 서버 없이, 호스트 프로세스 안에서 즉시 동작합니다. **슬랙형 채널 채팅**(유저간 DM 없음, 공개 채널 자유 참여)을 모바일 앱 느낌의 UI로 제공하며, UI 정적 자산은 어셈블리에 내장되어 같은 UI 한 벌이 **웹/PWA**와 **WPF(WebView2)** 양쪽에서 그대로 재사용됩니다.

```csharp
builder.Services.AddBareChat();   // SQLite 파일 DB + 파일 저장이 기본값으로 작동 (zero-config)
app.UseBareChat();                // /chat 하위에 채팅 전체가 마운트됨
```

---

## 왜 BareChat인가

기존 채팅 솔루션은 대부분 별도 인프라(메시지 브로커, 전용 서버, SaaS)를 요구합니다. BareChat은 **호스트 .NET 앱에 내장(in-process)되는 것**을 1급 목표로 설계되어, 다음 환경에서 강점을 가집니다.

- **온프레미스 / 폐쇄망 공장 환경** — 외부 푸시 서비스(FCM 등) 아웃바운드 없이도 핵심 기능 동작
- **기존 .NET 백엔드에 채팅을 "끼워 넣고" 싶은 경우** — 인증/세션을 호스트에서 그대로 상속
- **앱 내장 + 모바일 PWA 설치를 동시에** 원하는 경우 — UI 한 벌로 두 쉘 커버

---

## 핵심 특징

- **단일 미들웨어 마운트** — `UseBareChat()` 하나로 Hub·API·임베디드 UI 전부 공급
- **zero-config 인프라** — `DataPath` 단일 설정에 `chat.db`(SQLite)와 `blobs/`가 함께 보관. 무설정으로 기본 작동, 필요 시만 지정
- **채널 채팅** — 공개 채널 자유 생성·참여(슬랙형). 기본 채널은 삭제 불가, 멤버십은 영속 구독(내 채널 목록 + 알림 대상)
- **추상 인프라** — 스토리지/채널/블롭/인증/인가/알림/프레즌스가 전부 인터페이스. 기본 구현(SQLite + 파일시스템 + 인메모리)을 끼고 시작, 필요 시 교체
- **초경량 모바일 Vanilla UI** — 가상 DOM 프레임워크 없이 선언적 렌더, 채널 목록 ↔ 대화 2-뷰 네비게이션, UI 코드 100KB 미만(SignalR 클라이언트 제외)
- **실시간 + 폴백** — SignalR 기반 WebSockets → SSE → Long Polling 자동 폴백
- **프로그래밍 방식 발행** — `POST /chat/messages` REST 엔드포인트로 시스템 이벤트를 채널에 주입(채팅을 **이벤트 피드**로도 사용)
- **클라이언트 측 이미지 최적화** — 업로드 전 Canvas 리사이즈/압축으로 서버 부하 0

---

## 패키지 구조

| 패키지 | 내용 | 종속성 |
|---|---|---|
| **`BareChat.Core`** | 도메인 엔티티(`ChatMessage`, `Channel`, `ChannelMembership`), 인터페이스(`IChannelStore`, `IChatStorageProvider`, `IChatAuthProvider`, `INotificationChannel` 등), 추상 팩토리 | 외부 인프라 종속성 제로 |
| **`BareChat`** | SignalR Hub, API Controllers, 임베디드 UI 미들웨어, SQLite 기본 공급자 포함 일체형 | ASP.NET Core (`net10.0`) |

---

## 빠른 시작

```csharp
using BareChat.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBareChat(options =>
{
    // options.DataPath = "App_Data/barechat";        // (선택) 미지정 시 기본 경로. chat.db + blobs/ 가 여기 보관됨
    options.RoutePrefix = "/chat";
    options.MaxImageSizeInBytes = 3 * 1024 * 1024;     // 3MB
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
// SQLite 파일 DB + 파일 blob 이 DataPath 에서 자동 동작. 커스텀 provider 로 교체 가능

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();   // 호스트 User 컨텍스트를 BareChat이 상속
app.UseAuthorization();
app.UseBareChat();         // 인증/인가 뒤에 배치

app.Run();
```

마운트 후 클라이언트는 `shell` 쿼리스트링으로 알림 전략을 분기합니다.

| URL | 용도 |
|---|---|
| `/chat?shell=pwa` | 모바일 PWA 설치 + 웹푸시 |
| `/chat?shell=webview` | WPF(WebView2) 사이드패널 + 네이티브 브리지 |
| `/chat?shell=iframe` | 레거시 웹앱 임베드 (best-effort) |

---

## 두 가지 주요 시나리오

### 시나리오 1 — 웹앱 → PWA 모바일 설치
브라우저에서 접속 → 홈 화면 설치 → 백그라운드에서도 **웹푸시(VAPID)** 로 알림 수신. 자세한 내용은 [통합 가이드](docs/integration-guide.md#시나리오-1--pwa).

### 시나리오 2 — WPF 앱 사이드패널 (WebView2)
WPF 앱의 사이드패널에 WebView2로 BareChat UI를 로드. 새 메시지 발생 시 **네이티브 브리지를 통해 토글 버튼에 색상/뱃지 표시**. 백엔드 코드가 `POST /chat/messages` 로 시스템 메시지를 **프로그래밍 방식 주입** 가능. 자세한 내용은 [통합 가이드](docs/integration-guide.md#시나리오-2--wpf-webview2)와 [브리지 프로토콜](docs/bridge-protocol.md).

> **WPF 알림 범위:** OS 차원 상주 없음. **앱이 떠 있는 동안에만** 동작하며, 닫히면 알림도 종료됩니다. (의도된 설계)

---

## 문서

| 문서 | 내용 |
|---|---|
| [docs/architecture.md](docs/architecture.md) | 토폴로지, 패키지, 코어 인터페이스, 데이터/스토리지 |
| [docs/decisions.md](docs/decisions.md) | 설계 결정 레지스터 (ADR-lite) |
| [docs/notifications.md](docs/notifications.md) | 라이브 전달 vs 깨우기 알림, `INotificationChannel` |
| [docs/bridge-protocol.md](docs/bridge-protocol.md) | WebView2 ↔ WPF 네이티브 브리지 계약 |
| [docs/integration-guide.md](docs/integration-guide.md) | 두 시나리오 통합 절차 + 코드 |

---

## 마일스톤

- **M1 — 코어 + 채널 + WPF 완성:** Hub + 추상 스토리지 + **채널 CRUD·멤버십** + zero-config(`DataPath`) + 모바일 임베디드 Vanilla UI + `shell` 스위치 + REST publish + 네이티브 브리지(JS측). 푸시·SW 없음 → **시나리오 2 거의 완성.**
- **M2 — PWA 푸시:** `shell=pwa` 경로 = Service Worker + VAPID + subscription-store + `WebPushChannel`. → **시나리오 1 완성.**
- **M3 — 하드닝:** 세션 간 unread(`lastReadAt`), private 채널·역할/권한, 크로스오리진 iframe best-effort, soft-delete UI.

---

## 라이선스

[MIT License](LICENSE)