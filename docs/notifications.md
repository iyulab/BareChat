# Notifications

BareChat의 알림은 **두 개의 다른 문제**입니다. 이 둘을 섞으면 사양이 모순됩니다.

1. **라이브 전달** — 앱/탭이 열려 있을 때 메시지를 즉시 화면에 표시
2. **깨우기 알림** — 앱이 백그라운드/닫힘 상태일 때 사용자를 다시 부르기

라이브 전달은 항상 SignalR 하나로 끝납니다. 프로파일별로 갈리는 것은 **깨우기 알림**뿐입니다.

---

## 1. 알림 매트릭스 (비겹침 확정)

|  | 앱/탭 떠 있음 | 백그라운드 / 닫힘 |
|---|---|---|
| **시나리오 1 — PWA** | SignalR 라이브 | **WebPush (VAPID)** |
| **시나리오 2 — WPF** | SignalR → 네이티브 브리지 → 버튼 색상 | 없음 (범위 밖, 확정) |

핵심 결론:

- **WebPush는 PWA 전용.** WPF 케이스는 닫히면 깨울 프로세스가 없으므로 VAPID·subscription-store가 **0개** 필요. 자사 WPF 내장은 푸시 인프라 없이 동작.
- **WPF는 앱 생존 기간 한정.** OS 차원 상주(트레이 + Windows toast)는 BareChat 범위 밖. "WPF가 떠 있을 때만 알림"으로 확정.

---

## 2. `INotificationChannel` 추상화

```csharp
namespace BareChat.Core;

public interface INotificationChannel
{
    /// <summary>대상 유저가 라이브 연결이 없을 때 깨우기를 시도한다.</summary>
    Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct);
}
```

| 구현체 | 프로파일 | 마일스톤 | 비고 |
|---|---|---|---|
| `InAppChannel` | 전부 | M1 (코어) | SignalR 라이브 전달. 항상 동작 |
| `NativeBridgeChannel` | WPF | M1 | `postMessage` → WebView2. 버튼 뱃지 |
| `WebPushChannel` | PWA | M2 | VAPID. subscription-store 필요 |

발송 로직(개념):

```
메시지 수신
  └─ IPresenceTracker: userId 에 라이브 연결 있나?
       ├─ 있음 → InAppChannel (SignalR push)  ... 끝
       └─ 없음 → 등록된 깨우기 채널 시도
              ├─ WebPushChannel  (PWA 구독이 있으면)
              └─ NativeBridgeChannel (WebView2 호스트면)
```

---

## 3. WebPush (PWA, M2)

- **VAPID 키 관리** + 디바이스/유저별 push subscription **영속 저장**(subscription-store).
- 브라우저 벤더 푸시 서비스(FCM/Mozilla autopush)로 **아웃바운드 호출** 필요.
- Service Worker가 `push` 이벤트 수신 → `Notification API` 로 OS 네이티브 알림 → 클릭 시 지정 라우트로 포커싱.
- 권한: 클라이언트 접속 시 `Notification.requestPermission()` 트리거.

> **폐쇄망 주의.** 공장 온프레미스는 FCM/Mozilla 아웃바운드가 차단되어 WebPush가 죽는 경우가 많습니다. 이 환경의 알림 경로는 사실상 WPF 네이티브 브리지뿐입니다. 그래서 `NativeBridgeChannel` 을 "이국적 옵션"이 아니라 **WebPush와 동급 1급 구현체**로 둡니다.

---

## 4. iframe 프로파일(C)에서의 제약

크로스오리진 iframe 안에서는:
- Service Worker 등록 / `requestPermission()` 이 대부분 차단됨(top-level user gesture / Permissions-Policy 위임 필요).
- 따라서 **iframe 모드에선 PWA/웹푸시가 사실상 죽는다.**

→ iframe(C)은 "라이브 채팅은 되지만 백그라운드 푸시는 안 됨"으로 명시. 푸시가 필요하면 PWA(B) 또는 WPF(2) 경로 사용.

---

## 5. 관련 문서
- 네이티브 브리지 메시지 스펙 → [bridge-protocol.md](bridge-protocol.md)
- 결정 근거(D1, D2) → [decisions.md](decisions.md#d1--라이브-전달과-깨우기-알림을-분리한다)