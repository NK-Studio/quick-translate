# Changelog

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
