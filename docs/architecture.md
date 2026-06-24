# Architecture

BareChat의 시스템 구조, 패키지 분리, 코어 인터페이스, 데이터/전송 계층을 정의합니다.

---

## 1. 토폴로지 — 서버 1대, 쉘 2개

BareChat의 가장 중요한 구조적 사실: **클라이언트 쉘이 늘어나도 서버는 한 대다.**

```
              ┌──────────────────────────────────────────────┐
              │        Host ASP.NET Core Application          │
              │                                               │
              │   app.UseBareChat()  →  /chat                 │
              │   ┌─────────────┐ ┌──────────┐ ┌───────────┐  │
              │   │ SignalR Hub │ │   REST   │ │ Static UI │  │
              │   │ (realtime)  │ │ Publish  │ │ (embedded)│  │
              │   └──────┬──────┘ └────┬─────┘ └─────┬─────┘  │
              └──────────┼─────────────┼─────────────┼────────┘
                         │             │             │
              ┌──────────┴─────┐       │      ┌──────┴───────┐
              │  same-origin   │       │      │ embedded UI  │
              │  WS / cookie   │       │      │  served from │
              └──────┬─────────┘       │      │   assembly   │
                     │                 │      └──────┬───────┘
        ┌────────────┴───────┐  ┌──────┴──────┐      │
        │  Shell: PWA        │  │ Backend code│   (동일 UI 한 벌)
        │  (mobile install)  │  │ / threads   │      │
        │  SW + WebPush      │  │ POST /chat/ │      │
        └────────────────────┘  │  messages   │      │
        ┌────────────────────┐  └─────────────┘      │
        │  Shell: WPF WebView2│◄──────────────────────┘
        │  native bridge      │
        │  (button badge)     │
        └────────────────────┘
```

- **서버:** 호스트 ASP.NET Core 백엔드에 `UseBareChat()` 미들웨어로 마운트. **same-origin** 이 기본 전제(크로스오리진 쿠키·SW scope 문제를 원천 차단).
- **쉘:** PWA / WebView2 / iframe 은 모두 같은 임베디드 UI를 로드하고, `shell` 쿼리스트링으로 알림 전략만 분기.
- **WPF는 서버가 아니라 클라이언트다.** WPF 프로세스 안에서 서버가 도는 게 아니라, WebView2가 호스트 서버의 `/chat` 을 로드한다.

> **별도 origin / 별도 프로세스로 띄울 경우:** 인증(브리지 토큰 주입은 그대로 동작) 과 PWA Service Worker scope 만 재검토가 필요합니다. 기본 설계는 same-origin 마운트를 권장합니다.

---

## 2. 패키지 모듈화

### `BareChat.Core`
- 도메인 엔티티(`ChatMessage`, `Channel`, `ChannelMembership`, `MessageType`, `ChatUserContext` …)
- 인터페이스 및 추상 팩토리
- **외부 인프라 종속성 제로** — 어떤 호스트에서도 참조 가능

### `BareChat`
- TFM `net10.0`(최신 LTS), ASP.NET Core 종속: SignalR Hub, API Controllers, 임베디드 UI 미들웨어
- SQLite 기본 스토리지 공급자(`Microsoft.Data.Sqlite`), 인메모리 공급자
- `AddBareChat()` / `UseBareChat()` DI·미들웨어 확장 (zero-config: `DataPath` 단일 설정으로 동작)

---

## 3. 코어 인터페이스 (M1에서 *정의*, 구현은 마일스톤별)

| 인터페이스 | 책임 | M1 기본 구현 |
|---|---|---|
| `IChannelStore` | 채널 CRUD + 멤버십(구독) | SQLite, InMemory |
| `IChatStorageProvider` | 메시지 영속화/조회 | SQLite, InMemory |
| `IBlobStore` | 이미지 등 바이너리 저장 | 파일시스템 (BLOB-in-DB 금지) |
| `IChatAuthProvider` *(BareChat 패키지)* | "누구인가" 해석 | 호스트 `context.User` 프록시 |
| `IChatAuthorizationProvider` | "어느 채널 r/w 가능한가" | 인증 유저 전부 r/w (기본) |
| `INotificationChannel` | 깨우기 알림 전송 | `InAppChannel`(SignalR) |
| `IPresenceTracker` | 연결 상태 추적 | 인메모리 `ConcurrentDictionary` |

```csharp
// BareChat.Core — 인프라 종속성 제로 (HttpContext 등 ASP.NET 타입 참조 금지)
namespace BareChat.Core;

public interface IChatAuthorizationProvider
{
    Task<bool> CanReadAsync(ChatUserContext user, string channelId, CancellationToken ct = default);
    Task<bool> CanWriteAsync(ChatUserContext user, string channelId, CancellationToken ct = default);
}

public interface INotificationChannel
{
    Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default);
}
```

```csharp
// BareChat (ASP.NET 패키지) — HttpContext 에 결합되므로 Core 가 아니라 여기 둔다
namespace BareChat;

public interface IChatAuthProvider
{
    Task<ChatUserContext> ResolveUserAsync(HttpContext context);
}
```

> 코어는 인프라-무관 인터페이스만 들고, 무거운 구현(WebPush·backplane 등)은 상위 마일스톤에서 채웁니다. `HttpContext` 에 결합되는 `IChatAuthProvider` 는 **BareChat(ASP.NET) 패키지**에 두어 코어의 "종속성 제로 — 어떤 호스트에서도 참조 가능" 불변을 지킵니다. 이 분리가 "자사 내장 최단 경로 + OSS 일반성"을 동시에 만족시키는 축입니다.

---

## 4. 미들웨어 구동 메커니즘

- **리소스 내장:** HTML/CSS/JS 및 PWA 매니페스트는 어셈블리 내 `EmbeddedResource`. `UseBareChat()` 가 `RoutePrefix`(기본 `/chat`) 하위에서 `StaticFileMiddleware` 인터셉터로 공급.
- **배치 순서:** `UseAuthentication()` / `UseAuthorization()` **뒤**에 `UseBareChat()` — 호스트 User 컨텍스트를 안전하게 상속.
- **초경량 UI:** Vanilla JS 선언적 렌더. 가상 DOM 오버헤드 배제. UI 코드 예산 **100KB 미만(SignalR JS 클라이언트 제외)**.

---

## 5. 전송 계층

- SignalR 기반 **WebSockets → Server-Sent Events → Long Polling** 자동 폴백. *자체 구현이 아니라 SignalR이 제공하는 기능에 의존*합니다(공장/구형 브라우저 환경 강건성 확보용).
- **백플레인:** 단일 노드는 인메모리. 스케일아웃 시 최소 브리지 코드로 Redis 백플레인 확장. presence 동기화도 이때 backplane으로.

### 인증 × WebSocket 비대칭 (중요)
WS 핸드셰이크에는 브라우저가 `Authorization` 헤더를 싣지 않습니다. 따라서:
- **쿠키:** same-origin WS에서 자동 동작
- **Bearer:** `access_token` 쿼리스트링 + `OnMessageReceived` 패턴 (Integrated/Standalone 공통)

자세한 인증 매트릭스는 [bridge-protocol.md](bridge-protocol.md) 및 [decisions.md](decisions.md#b-인증) 참고.

---

## 6. 데이터 / 스토리지

### 메시지 모델
```csharp
namespace BareChat.Core.Domain;

public record ChatMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public string ChannelId { get; init; } = "default";
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string SenderAvatarUrl { get; init; } = string.Empty;
    public MessageType ContentType { get; init; } = MessageType.Text;
    public string Payload { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    // soft-delete / edit 여지 (M1에서 필드만 확보, UI 는 M3)
    public bool IsDeleted { get; init; }
    public DateTime? EditedAtUtc { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum MessageType { Text = 0, Image = 1, System = 2 }
```

### 채널 모델 (D9)

대화 단위는 **채널 하나뿐**(유저간 DM 없음). 채널은 기본 **공개**이며, 공개 채널의 멤버십은 영속 "구독"(내 채널 목록 + 알림 대상)으로 read/write 권한과 무관하다. **(M3)** `IsPrivate` 채널은 예외 — 비멤버에게 숨겨지고 self-join 불가, 멤버십이 곧 접근권한이며 `ChannelMembershipAuthorizationProvider`(D7 활성화)로 모든 표면에서 게이팅된다.

```csharp
namespace BareChat.Core.Domain;

public record Channel
{
    public string ChannelId { get; init; } = string.Empty;   // slug ("general", "line-a")
    public string Name { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;   // seed=System
    public bool IsDefault { get; init; }                     // 기본 채널 → 삭제·이탈 불가
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public record ChannelMembership
{
    public string ChannelId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime JoinedAtUtc { get; init; } = DateTime.UtcNow;
}
```

- **기본 채널**(seed)은 삭제·이탈 불가, 신규 유저 자동 가입. 사용자 생성 채널은 누구나 생성(slug 중복 불가)·발견·join/leave, 삭제는 생성자만(+ 호스트 관리자 seam).
- 채널/멤버십 저장은 `IChannelStore`로 메시지 저장소와 분리하되, SQLite 구현은 같은 `chat.db`를 공유한다.

### 저장 규칙 (D10)
- **`DataPath` 단일 설정**이 DB와 파일을 함께 품는다. `appsettings.json`의 `BareChat:DataPath`로 자동 바인딩, 미지정 시 ContentRoot 하위 기본 경로. 디렉터리/`chat.db`는 시작 시 자동 생성·마이그레이션.
  ```
  {DataPath}/
  ├── chat.db      # SQLite 파일 DB (메시지 + 채널 + 멤버십)
  └── blobs/       # IBlobStore (BLOB-in-DB 금지)
  ```
- 메시지·채널·멤버십 → SQLite (`chat.db`)
- **이미지 → `IBlobStore`(기본 파일시스템, `{DataPath}/blobs/`).** DB에는 경로/크기/mime 메타만. **BLOB-in-DB 금지** — lightweight 서사 유지.
- 이미지 리사이즈/압축 → **클라이언트 Canvas 기본**(max 1200px, WebP/JPEG q75). 서버측 ImageSharp 리사이즈는 pluggable 옵션.

### 보안
- `Payload`는 **신뢰 불가 입력**. 기본 렌더는 `textContent` 이스케이프. 마크다운/링크는 opt-in 이며 켜면 sanitizer(DOMPurify 류) 경유 필수.

---

## 7. 관련 문서
- 설계 결정 근거 → [decisions.md](decisions.md)
- 알림 매트릭스 → [notifications.md](notifications.md)
- WebView2 브리지 → [bridge-protocol.md](bridge-protocol.md)