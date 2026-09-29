# Changelog

## [1.2.0] - 2026-09-29

### Added
- 번역 엔진 **Claude Code (로그인 계정)**: API 키 없이, 설치·로그인된 Claude Code CLI 를 헤드리스(`claude -p`)로 실행해 번역
  - 모델 선택 (Haiku 기본 / Sonnet / Opus), CLI 경로 자동 탐지 (직접 지정도 가능)
  - 도구·MCP·설정·세션 저장·생각하기를 끄고 임시 폴더에서 실행해 응답만 빠르게 받음 (약 3~4초)

## [1.1.4] - 2026-09-29

### Changed
- 이름의 일부 단어만 용어집과 일치해도, 그 용어를 넣어 조합한 이름을 맨 위(BEST) 후보로 표시 (예: `상점` ↔ `Shop` 등록 시 `상점 - 뷰` → `Shop - View`)

## [1.1.3] - 2026-09-29

### Changed
- 팝업을 띄우는 동안 번역을 미리 시작 (단축키 입력 즉시). 여러 개 선택 시 다음 항목도 미리 번역
- 같은 이름의 번역 요청이 진행 중이면 새로 보내지 않고 결과를 이어받음
- 팝업을 처음부터 작은 크기로 열어 큰 빈 창이 떴다가 줄어드는 깜빡임 제거
- (6.3+) 새 Hierarchy 에서 위치 보정값을 기억해 두 번째부터 즉시 표시

## [1.1.2] - 2026-09-29

### Removed
- `Tools > Quick Translate > Translate Selected Names` 메뉴와 Project 창 우클릭 메뉴 `Translate Name (KO ↔ EN)` (단축키로만 실행)

## [1.1.1] - 2026-09-29

### Changed
- 최소 지원 버전을 Unity 6.0 (6000.0) 으로 낮춤

## [1.1.0] - 2026-09-29

### Added
- [실험] Inspector 에 포커스가 있을 때 GameObject 이름 칸 아래에 번역 팝업 표시
- MIT 라이선스

### Changed
- 번역 팝업 디자인: 머리(방향·종류 칩, 진행 수, 아이콘) / 후보 / 키캡 안내 영역으로 구분
- 팝업 크기를 실제 레이아웃 크기에 맞춤 (안내 문구 잘림 해결)
- 번역 중 표시에 사용 중인 엔진 이름 표시

## [1.0.0] - 2026-09-29

### Added
- Hierarchy / Project 창에서 선택한 이름을 한국어 ↔ 영어로 번역하는 팝업 (기본 단축키 Cmd/Ctrl+Shift+X)
- 번역 엔진: Google(무료·실험적), Google Cloud Translation, DeepL, Papago, Claude, ChatGPT, Gemini
- 팀 공용 용어집 (Project Settings)
- API 키를 프로젝트 루트 `.env` 에서 읽기
- UI Toolkit 기반 팝업과 설정 화면
