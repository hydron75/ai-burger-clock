# Microsoft.Data.Sqlite 10.0.14 갱신 (Draft)

다음 Windows 2.9.1·Mac 0.9.1에 함께 넣을 패키지 갱신입니다. 이 PR은 앱 버전·DB schema·배포 EXE·운영 등록을 바꾸지 않으며 별도 배포하지 않습니다.

## 범위와 실제 의존성

- `AiBurgerClock.csproj`, `Mac/AiBurgerClock.Mac.csproj`, `Shared.Tests/AiBurgerClock.Shared.Tests.csproj`의 직접 참조만 **10.0.12 → 10.0.14**로 같이 맞춥니다.
- Windows·Shared.Tests의 실제 복원 결과는 `Microsoft.Data.Sqlite`/`Microsoft.Data.Sqlite.Core` **10.0.14**, `SQLitePCLRaw.bundle_e_sqlite3`/`core`/`lib.e_sqlite3`/`provider.e_sqlite3` **2.1.13**입니다. SQLitePCLRaw 직접 PackageReference나 버전 override는 없습니다.
- 이번 Windows x64 네이티브 라이브러리가 보고한 **SQLite 엔진은 3.53.3**입니다(`SELECT sqlite_version()`). 단일 EXE가 실제로 로드한 DLL의 SHA-256도 이 검사 라이브러리와 같습니다.
- 2.1.14/3.x 전환은 하지 않습니다. 2.x에 SQLite 3.54.1 이상이 들어오면 재검토한다는 별도 보류 결정은 유지합니다.
- 제품 C# 원본은 바꾸지 않습니다. 공통 검사 `StorageTests.cs`에 합성 WAL·온라인 백업·재열기 9건만 추가합니다. 현재 의존성 설명은 README·AGENTS, 진행 상태는 BACKLOG에 반영합니다. 과거 배포 기록은 유지합니다.

## DB 회귀

공통 합성 검사는 고유 임시 폴더에 만든 DB만 씁니다. WAL 활성화, reader snapshot 유지, 쓰기 후 WAL/SHM 존재, checkpoint 전 온라인 백업에 새 Unicode 행 포함, backup integrity, reader 종료 후 checkpoint/TRUNCATE, 마지막 연결 종료 후 sidecar 정리, 원본/백업 재열기를 고정합니다. 이 9건은 Windows·Shared.Tests와 Mac 공통 검사에서 함께 실행됩니다.

추가로 사용자가 외부 배포 때 만든 기존 DB 백업의 실제 열린 경로·해시를 확인하고, **새 복사본에서만** 앱 저장소 초기화·기존 이력 읽기·설정 쓰기/재열기·무결성/schema·WAL reader/commit·온라인 백업·checkpoint·sidecar 종료·재열기 **14건**을 통과했습니다. 원본 백업의 전후 해시는 같습니다. 운영 기본/LocalCache DB와 인증·CLI·원문 로그는 읽거나 변경하지 않았습니다. 개인 데이터가 든 복사본·생성 도우미는 저장소 밖 임시 diagnostics에만 두며 커밋하지 않습니다. 결과에는 계정 값·메모·실제 상태·행 수를 출력하지 않습니다.

## Windows 검증 (2026-11-09 KST)

기준 main은 `c9be3165f2b32924f941ee54793d53815eee5c24`, 검증한 패키지·검사 코드 커밋은 `fa749922f3d9ccbbf51c5be41a36b96fe21fd216`입니다. 이후 커밋은 문서뿐입니다. #130과 독립된 main 기준이므로 #130의 추가 검사 건수는 이 결과에 포함하지 않습니다.

| 검사 | 결과 |
|---|---|
| SDK 10.0.403 `build.ps1` | 경고 0·오류 0, 종료 코드 0 |
| 일반 빌드 `--self-test` | **292,885건 통과** |
| Shared.Tests | **291,764건 통과**, 기존 권한 제한 symlink fixture 1건 제외 |
| 추가 합성 WAL/backup 검사 | **9건 통과**, 기존 storage 검사 173 → 182건 |
| 기존 DB의 새 복사본 | **14건 통과**, 원본 백업 해시 불변 |
| Portable 단일 EXE publish | 경고 0·오류 0, 후보 폴더에 EXE 1개 |
| 단일 EXE `--self-test` | **292,885건·종료 코드 0** |
| 단일 EXE native DLL | 자식 프로세스에 `e_sqlite3.dll` 로드, 테스트용 bundle 추출 폴더·3.53.3 검사 DLL 해시 일치 |
| 고정 after 슬롯 단일 EXE smoke | **1,012 PASS·49 PNG·종료 코드 0**, 1회 |

후보는 `artifacts/sqlite-package/candidate/`에 따로 publish했습니다. Portable profile의 dist 경로는 명령행 PublishDir/PublishUrl로 이 후보 폴더에만 지정했으며 운영 dist를 쓰지 않았습니다. 네이티브 추출 경로 비교의 `/`·`\` 표기 차이 오판정은 양쪽 절대 경로를 정규화해 재검사했으며 앱 코드 수정은 없었습니다. 실제 OS 알림 제출·운영 등록·사이트 송신은 0회입니다. 반복 트레이 기록 증가는 단일 smoke라 미평가입니다. 검사 후 기존 2.9.0의 외부 재실행 완료는 사용자 확인입니다.

로그는 `artifacts/sqlite-10-0-14/`(build·고정 슬롯 smoke), `artifacts/sqlite-package/`(복원·Shared.Tests·publish·단일 EXE 자체 검사·native-bundle-final.json)에 있습니다. 실제 DB 복사본의 비공개 검사 결과는 저장소 밖 `diagnostics/sqlite-upgrade-20261109/`에 보관합니다.

## Mac 확인 항목과 미확인

- [ ] 격리 사본에서 `Mac/build.sh`, 경고·오류 0. 앱 버전은 별도 버전 PR 전까지 유지.
- [ ] Mac에서도 직접 참조 10.0.14·기본 SQLitePCLRaw 2.1.13, 실제 `SELECT sqlite_version()` 확인.
- [ ] `.app` 안 `libe_sqlite3.dylib` 포함·arm64 로딩과 최종 번들/내부 dylib 서명 검증. Windows에서 NuGet의 osx-arm64 native asset 존재만 확인했으며 실제 Mac 번들 포함·서명 확인을 대신하지 않음.
- [ ] Shared.Tests(현재 브랜치 기준 291,764건), 새 WAL/backup 9건, native smoke.
- [ ] 기존 Mac DB의 새 복사본에서 읽기·쓰기·백업·WAL/checkpoint·재열기 회귀. 원본·운영 DB는 변경하지 않음.
- [ ] Mac/README의 현행 직접 의존성 10.0.12 표기는 Mac 담당이 10.0.14로 맞춤. 과거 기록은 유지.

사이트 한도 송신 계약·표시·판정은 바뀌지 않아 사이트 반영은 필요하지 않습니다. Mac 실제 검증과 다음 버전의 최종 병합 조합 검사는 아직 수행하지 않았습니다. 병합·버전 올림·배포는 사용자가 결정합니다.
