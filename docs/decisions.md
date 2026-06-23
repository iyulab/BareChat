# Decision Register (ADR-lite)

BareChat v1.2 기준 확정 설계 결정. 각 항목은 **결정 / 맥락 / 대안 / 결과** 구조입니다. `[권장]` 은 기본값으로 채택했으나 호스트 사정에 따라 뒤집을 수 있는 항목입니다.

---

## D0 — 배포 프로파일을 3개로 못 박는다 (마스터 결정)

**결정.** 하나의 config로 모든 상황을 커버하지 않는다. `shell` 쿼리스트링으로 배포 프로파일을 명시적으로 분기한다.

| 프로파일 | 지위 | 인증 | PWA/푸시 |
|---|---|---|---|
| **A. Same-origin Embedded** | 1급 (자사 기본) | 쿠키 흡수 | 정상 |
| **B. Standalone PWA** | 1급 (모바일 설치) | 토큰 | 풀 PWA + 웹푸시 |
| **C. Cross-origin iframe** | best-effort (문서화만) | postMessage 토큰 | 사실상 불가 |

**맥락.** 자사 내장이 동기지만 OSS 일반성을 지켜야 한다. 모든 시나리오를 단일 경로로 묶으면 크로스오리진 쿠키·iframe PWA 차단 문제가 코어로 새어 들어온다.

**대안.** (1) 단일 만능 config — 모순 발생. (2) 시나리오마다 별도 빌드 — 유지보수 비용 폭증.

**결과.** A·B만 CI 검증, C는 README 한 단락. 코어에서 크로스오리진 리스크 증발.

---

## D1 — "라이브 전달"과 "깨우기 알림"을 분리한다

**결정.** 라이브 전달 = 항상 SignalR(`InAppChannel`, 코어). 깨우기 알림 = `INotificationChannel` 추상화 + 프로파일별 구현체.

**맥락.** "어떤 방식이든 푸시"의 답. 앱/탭이 열려 있으면 SignalR로 끝나며, **푸시 인프라가 필요 없는 경우가 많다.** 푸시는 백그라운드/닫힘에서만 필요한 보조 기능.

**구현체.** `InAppChannel`(SignalR, 항상) · `WebPushChannel`(VAPID, B) · `NativeBridgeChannel`(postMessage→WebView2, WPF).

**결과.** 자사 WPF 케이스는 웹푸시 인프라 0개. 폐쇄망에서 FCM 아웃바운드가 막혀도 NativeBridge가 1급 경로로 동작.

---

## D2 — Presence를 1급 개념으로

**결정.** `IPresenceTracker` 도입. 단일 노드 인메모리, 스케일아웃 시 backplane 동기화.

**맥락.** "이 유저 지금 연결 없음?" 판정은 푸시 발송 여부의 전제. 읽음/온라인 표시도 여기서 파생.

**결과.** M1은 인메모리로 충분, 분산은 D-transport 백플레인과 함께 확장.

---

## D3 — 인증 매트릭스 확정

**결정.** `IChatAuthProvider.ResolveUserAsync` 단일 seam 유지. 채널별 비대칭 명문화.

| 모드 | 메커니즘 |
|---|---|
| PWA (same-origin) | 쿠키 자동 흡수, WS 포함 |
| WPF (WebView2) | 브리지 토큰 주입 → WS `?access_token=` |
| Cross-origin (C) | 부모창 postMessage 토큰 → WS 쿼리스트링 |

**맥락.** WS 핸드셰이크에 `Authorization` 헤더가 안 실린다. Bearer는 standalone 전용이 아니라 **Integrated에서도** 쿼리스트링 + `OnMessageReceived` 가 필요.

**결과.** WebView2 쿠키 공유를 만지지 않고 origin 무관하게 동작.

---

## D4 — 스토리지 / 블롭 분리

**결정.** 메시지 → SQLite. 이미지 → `IBlobStore`(기본 파일시스템). **BLOB-in-DB 금지.** `[권장]`

**맥락.** 이미지를 SQLite BLOB로 넣으면 볼륨 증가 시 DB가 비대해져 "lightweight" 서사가 깨진다.

**결과.** DB엔 메타만. 이미지 리사이즈는 **클라이언트 Canvas 기본**(서버 부하 0), 서버 ImageSharp는 옵션. `[권장]`

---

## D5 — 프로그래밍 방식 발행 (REST publish)

**결정.** `POST /chat/messages` 엔드포인트를 1급으로 추가. `[권장]`

**맥락.** 시나리오 2의 "프로그래밍 방식 push". 백그라운드 스레드가 채널에 메시지를 주입하려면 라이브 소켓·열린 패널에 의존하면 안 된다(WebView2 JS를 거치면 패널이 닫혔을 때 실패).

**결과.** 인증된 `MessageType.System` 메시지를 한 방에 게시 → Hub 브로드캐스트. 장비 알람·작업지시·빌드완료를 채널에 흘리는 통로 = 채팅이 **이벤트 피드** 겸용. 얇은 `BareChat.Client`(.NET SignalR client 래퍼)는 M2+ 선택. `[권장]`

---

## D6 — 메시지 모델에 edit/delete 여지 확보

**결정.** `ChatMessage`에 `IsDeleted` / `EditedAtUtc` 필드를 지금 추가. 편집 UI는 M3로 이연. `[권장]`

**맥락.** `init`-only record라 수정/삭제 스토리가 없었다.

**결과.** 스키마 호환성을 미리 확보, 기능은 나중에.

---

## D7 — 권한 / XSS

**결정.** `IChatAuthorizationProvider` seam만 코어에 정의(기본: 인증 유저 전부 r/w). 채널 CRUD·멤버십은 D9로 **M1**, 채널별 접근권한(private·역할)은 M3 이연. `Payload`는 기본 `textContent` 이스케이프 렌더, 마크다운/링크는 opt-in + sanitizer 필수. `[권장]`

**맥락.** 멀티테넌트/공장 납품의 첫 요구사항이 채널 권한과 XSS. 코어에 seam을 박아두면 납품 시 구현만 끼우면 된다.

---

## D8 — 전송 / 번들 표현 정정

**결정.** Transport 폴백(WS→SSE→Long Polling)은 SignalR 기본 의존으로 명시(자체 구현 아님). UI <100KB 예산은 **SignalR JS 클라이언트 제외 기준**. `[권장]`

**맥락.** 원 사양의 "완벽한 폴백 레이어 지원"이 마치 자체 구현처럼 읽혔다. SignalR JS 클라이언트만 minified ~30–40KB.

---

## D9 — 채널 모델: public-only, 멤버십 = 구독

**결정.** 대화 단위는 **채널 하나뿐**(유저간 DM 없음). 모든 채널은 **공개(public-only)**, 누구나 발견·join/leave. **멤버십은 영속 "구독"** — *내 채널 목록*과 *알림 대상*만 결정하며 **read/write 권한과 무관**(인증 유저는 전 채널 r/w). `[권장]`

**맥락.** 슬랙 채널형 협업이 목표. 1:1 DM·private/초대·역할 모델은 첫 수요가 아니며, 넣는 순간 가시성 플래그·접근제어가 코어로 새어 든다. 채널을 1급 엔티티(`Channel`)로 올리고 멤버십(`ChannelMembership`)을 구독으로 한정하면 권한 seam(D7)은 "전원 r/w + 채널 존재 확인"으로 단순해진다.

**구현.** **기본 채널**(`IsDefault`, seed)은 삭제·이탈 불가, 신규 유저 자동 가입. 사용자 생성 채널은 누구나 생성(slug 중복 불가)·발견·join/leave, **삭제는 생성자만**(+ 호스트 관리자 seam). 저장은 `IChannelStore`(채널/멤버십)로 메시지 저장소와 분리.

**대안.** (1) DM·멀티워크스페이스 포함 — 범위 폭증, YAGNI 위반. (2) 멤버십=접근권한 — public 모델과 모순, private는 P3로 이연.

**결과.** 채널 CRUD가 제품 중심이 되어 **M1로 승격**(아래 매핑). private 채널/역할은 D9 확장으로 M3.

---

## D10 — zero-config 인프라: `DataPath` 단일 설정

**결정.** 인프라 설정을 **`DataPath` 하나**로 묶는다. 그 아래 `chat.db`(SQLite 파일 DB)와 `blobs/`(이미지)가 함께 보관된다. `AddBareChat()` 만으로 기본값 작동(무설정). `[권장]`

**맥락.** 온프레미스/폐쇄망 납품의 1차 마찰은 설정이다. 연결 문자열을 손으로 만들게 하면 "lightweight, 끼우면 동작" 서사가 깨진다. `DataPath`는 `appsettings.json`의 `BareChat:DataPath`로 자동 바인딩되며, 미지정 시 ContentRoot 하위 기본 경로. 디렉터리/`chat.db`는 시작 시 자동 생성·마이그레이션.

**구현.** TFM `net10.0`(최신 LTS) 단일 타깃. 드라이버 `Microsoft.Data.Sqlite` 직접 사용(EF Core 미사용). SQLite 연결 문자열은 `DataPath`에서 파생 — 기존 `AddSQLiteStorage(connString)` API를 대체.

**결과.** `builder.Services.AddBareChat(); app.UseBareChat();` 두 줄로 채널 채팅이 동작. 커스텀은 `options.DataPath` 또는 provider 교체 시에만.

---

## D11 — 커스터마이징 주권 (브랜딩)

**결정.** 호스트가 **리빌드 없이** 앱 이름·색상·아이콘을 소유한다. 이름/색상은 `BareChatOptions.Branding`(config 바인딩), 아이콘은 `{DataPath}/branding/` 파일 드롭(없으면 내장 기본). PWA `manifest.webmanifest` 를 동적 생성해 설치 이름/아이콘을 즉시 제공한다. `[권장]`

**맥락.** 자사/납품처마다 "A Company MES Chat" 처럼 브랜드가 다르다. 호스트 자산을 라이브러리 어셈블리에 넣으면 브랜드 변경마다 리빌드가 필요해 주권이 깨진다. 이름/색상은 generic 1급 수요(도메인 개념 아님)이라 옵션에 두는 것이 정당(데맨드-드리븐 위반 아님).

**대안.** (1) 아이콘 어셈블리 임베드 — 리빌드 필요, 주권 상실. (2) 별도 브랜딩 서버 — 과설계.

**결과.** `Branding.AppName` 등은 무설정 기본("BareChat")으로 동작하고, 아이콘은 DataPath 파일로 교체. manifest 는 Service Worker(P2)와 독립이라 지금 제공. SW·설치 프롬프트·웹푸시는 P2. 가이드 → [customization.md](customization.md).

---

## 결정 → 마일스톤 매핑

| 마일스톤 | 포함 결정 |
|---|---|
| **M1** | D0(A), D1(InApp+NativeBridge), D3(쿠키/브리지), D4, D5, D6(필드), D7(seam), D8, **D9(채널 CRUD·멤버십)**, **D10(zero-config DataPath)**, **D11(브랜딩: 이름·색상·아이콘·manifest)** |
| **M2** | D0(B), D1(WebPush), D2(presence 본격), D5(Client SDK) |
| **M3** | 세션 간 unread, D0(C), D6(편집 UI), **D9 확장(private 채널·역할)**, D7(권한 본격) |