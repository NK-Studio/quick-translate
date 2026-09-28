# Quick Translate

Unity 에디터에서 **GameObject / 에셋 이름을 한국어 ↔ 영어로 바로 번역**해 이름을 바꾸는 도구입니다.
Hierarchy 나 Project 창에서 선택하고 단축키를 누르면, 선택한 행 바로 아래에 번역 후보가 뜨고 Enter 로 적용합니다.

<img width="2184" height="1402" alt="image" src="https://github.com/user-attachments/assets/a3885745-22ff-4828-9696-793bcde1d273" />

### 특징
- cmd(alt) + shift + X 단축키를 눌러 한글이 있으면 한→영, 영문만 있으면 영→한으로 자동 판별
- 여러 개 선택 시 차례로 처리, `Shift+Enter` 로 나머지 일괄 적용
- GameObject 이름 변경은 Undo 지원 (에셋 이름 변경은 Unity 특성상 Undo 불가)
- 번역 엔진 선택: Google(무료·실험적), Google Cloud Translation, DeepL, Papago, Claude, ChatGPT, Gemini

## 유니티 지원 버전

- Unity 6.0 (6000.0) LTS 
- Unity 6.3 (6000.3) LTS
- Unity 6.5 (6000.5)
- Unity 6.7 Beta (6000.7) Or Higher

## 설치 (Git UPM)

**Window > Package Manager > + > Install package from git URL…** 에 입력:

```
https://github.com/NK-Studio/quick-translate.git
```

## 사용법

1. Hierarchy 또는 Project 창에서 이름을 바꿀 오브젝트/에셋을 선택합니다.
2. **Cmd+Shift+X** (Windows: Ctrl+Shift+X) 를 누릅니다.
3. 팝업에서 후보를 고릅니다.

| 키 | 동작 |
|---|---|
| ↑ / ↓ | 후보 이동 |
| Enter | 적용 |
| Tab | 선택한 후보를 직접 수정 |
| Shift+Enter | 현재 적용 + 남은 항목 모두 1순위로 적용 |
| Esc | 닫기 |

단축키는 `Edit > Shortcuts` 에서 `Quick Translate` 로 검색해 바꿀 수 있습니다.

## 설정

- **Preferences > Quick Translate**: 번역 엔진, 최대 후보 수, 첫 글자 대/소문자 우선, 문맥(DeepL·AI), AI 모델
<img width="1590" height="1070" alt="image" src="https://github.com/user-attachments/assets/bee301d1-2e72-4a61-b242-fcbdff40e0a4" />

- **Project Settings > Quick Translate**: 팀 공용 용어집 (예: `체력` ↔ `HP`)

### API 키 (.env)

API 키는 **프로젝트 루트(Assets 폴더 옆)의 `.env`** 에서 읽습니다. Assets 밖이라 빌드에 포함되지 않습니다.
**`.env` 는 VCS 에 올리지 마세요** (`.gitignore` 에 `.env` 추가).

<img width="1590" height="1070" alt="image" src="https://github.com/user-attachments/assets/fcff406c-0404-4050-a778-bb79d7f1d0df" />

```
DEEPL_API_KEY=...
GOOGLE_TRANSLATE_API_KEY=...
PAPAGO_CLIENT_ID=...
PAPAGO_CLIENT_SECRET=...
ANTHROPIC_API_KEY=...
OPENAI_API_KEY=...
GEMINI_API_KEY=...
```

사용하는 엔진의 키만 넣으면 됩니다. 설정 화면의 `.env 만들기` / `빠진 키 항목 추가` 버튼으로 틀을 만들 수 있습니다.

| 엔진 | 키 | 비고 |
|---|---|---|
| Google (무료·실험적) | 필요 없음 | 비공식 엔드포인트. 예고 없이 막히거나 IP 단위로 차단될 수 있음 |
| Google (공식 API) | `GOOGLE_TRANSLATE_API_KEY` | Cloud Translation API (Basic, v2) |
| DeepL | `DEEPL_API_KEY` | `:fx` 로 끝나면 Free API. 문맥(context) 지원 |
| Papago | `PAPAGO_CLIENT_ID`, `PAPAGO_CLIENT_SECRET` | 네이버 클라우드 플랫폼 Papago Translation |
| Claude / ChatGPT / Gemini | `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` / `GEMINI_API_KEY` | AI 가 후보를 직접 생성, 용어집·문맥을 지시문으로 전달 |

번역할 이름(과 AI 엔진의 경우 용어집·문맥)은 선택한 번역 서비스로 전송됩니다.

## 라이선스

[MIT](LICENSE.md)
