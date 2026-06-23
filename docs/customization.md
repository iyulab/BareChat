# Customization (Branding)

BareChat lets the **host own its branding** — app name, colors, and icons — without rebuilding the library. Name/colors are configuration; icons are files dropped into `DataPath`. Everything has a working default (zero-config).

자세한 설계 결정은 [decisions.md](decisions.md#d11--커스터마이징-주권-브랜딩)를 참고하세요.

---

## 1. 이름 · 색상 (코드 또는 config)

`BareChatOptions.Branding`:

```csharp
builder.Services.AddBareChat(options =>
{
    options.Branding.AppName = "A Company MES Chat";  // 인앱 헤더 + 문서 title + PWA name
    options.Branding.ShortName = "MES Chat";          // PWA short_name (미지정 시 AppName)
    options.Branding.ThemeColor = "#0f766e";          // 브라우저 UI / manifest theme_color
    options.Branding.BackgroundColor = "#0b0f19";     // PWA splash 배경
});
```

또는 코드 없이 `appsettings.json`:

```jsonc
{
  "BareChat": {
    "Branding": {
      "AppName": "A Company MES Chat",
      "ShortName": "MES Chat",
      "ThemeColor": "#0f766e"
    }
  }
}
```

적용 위치:
- **인앱 이름** — 채널 목록 헤더 + 브라우저 탭 `<title>`
- **PWA 설치 이름** — `manifest.webmanifest` 의 `name` / `short_name`
- **테마 색** — `<meta name="theme-color">` + manifest `theme_color`

---

## 2. 아이콘 (DataPath 파일 드롭)

라이브러리에 호스트 자산을 넣지 않습니다(주권). `{DataPath}/branding/` 에 파일을 두면 즉시 반영됩니다 — **리빌드·재배포 불필요**.

```
{DataPath}/branding/
├── icon-192.png    # PWA 아이콘 192x192 (홈 화면)
├── icon-512.png    # PWA 아이콘 512x512 (splash)
├── icon.svg        # (선택) 파비콘/벡터. 미제공 시 내장 기본 아이콘 사용
└── favicon.ico     # (선택)
```

- 파일이 있으면 `GET {prefix}/branding/<file>` 로 서빙되고 manifest 아이콘 목록에 포함됩니다.
- `icon-192/512.png` 가 없어도 내장 기본 `icon.svg` 로 PWA 설치가 가능합니다.
- 권장: 512x512 마스커블 PNG 1장 + 192x192 1장. 색은 ThemeColor 와 어울리게.

> **보안:** 브랜딩 파일은 **호스트 운영자가 두는 신뢰 자산**입니다(채팅 사용자 업로드와 다름). 그래도 `X-Content-Type-Options: nosniff` 로 서빙되며, 서빙 대상은 고정 파일명 화이트리스트(`icon-192.png`/`icon-512.png`/`icon.svg`/`favicon.ico`)로 제한됩니다.

---

## 3. 동작 확인

```bash
curl http://localhost:5099/chat/manifest.webmanifest   # name/short_name/theme_color/icons
curl http://localhost:5099/chat/branding/icon.svg      # 기본 또는 호스트 아이콘
```

브라우저로 `/chat` 접속 → 탭 제목과 헤더에 AppName, 설치 시 PWA 이름/아이콘 적용.

---

## 4. 범위 메모

- **지금 제공:** 인앱 이름·색상, 아이콘 오버라이드, PWA `manifest`(설치 이름/아이콘).
- **P2 예정:** Service Worker 등록 + 설치 프롬프트 + 오프라인/웹푸시. manifest 는 그 선행 조건으로 이미 제공됩니다.
