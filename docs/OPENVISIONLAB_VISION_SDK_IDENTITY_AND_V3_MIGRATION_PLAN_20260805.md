# OpenVisionLab Vision SDK 제품 정체성 및 3.0 마이그레이션 계획

문서 상태: Complete  
승인일: 2026-08-05  
개발 상태: GitHub 저장소 이름 변경 및 3.0 main 반영 완료, NuGet 게시 및 소비 저장소 전환 전

## 1. 승인된 결정

`Library-Noah`와 `Lib.*`라는 기존 이름은 `2.9.1` 호환 기준까지만 사용한다.
다음 메이저 버전의 공식 제품명은 **OpenVisionLab Vision SDK**이며, 공개 패키지,
DLL, 프로젝트와 네임스페이스는 `OpenVisionLab.* 3.0.0`으로 전환한다.

이 결정의 목적은 라이브러리 소유자 이름보다 사용자가 찾는 기능을 먼저 드러내고,
[OpenVisionLab](https://github.com/Noah8218/OpenVisionLab)과
[OpenVisionLab 3D Studio](https://github.com/Noah8218/OpenVisionLab-3D-Studio)가
같은 2D/3D 알고리즘 SDK를 소비한다는 관계를 명확하게 만드는 것이다.

## 2. 제품 계약

OpenVisionLab Vision SDK는 OpenCvSharp 기반 2D Tool과 UI 독립적인 height-map,
full-XYZ, mesh 및 surface-matching 3D 계산을 제공하는 C# 알고리즘 SDK다.

SDK가 소유하는 범위:

- 2D/3D 수치 알고리즘
- source-neutral 입력, 옵션, 결과와 오류 계약
- 단위, 좌표 프레임, 결측값, 유효 샘플 및 커버리지 검증
- 2D/3D Tool 실행 계약과 합성 회귀 Smoke
- NuGet 패키지, XML IntelliSense 문서와 독립 소비자 예제

SDK가 소유하지 않는 범위:

- 카메라와 센서 획득, 장치 SDK 파서 또는 통신
- 교정 절차, source provenance와 생산 승인
- ROI 편집기, Viewer, 레이어, 오버레이와 레시피 UI
- Preview/Run lifecycle, PLC, I/O와 배포 플랫폼

OpenVisionLab과 OpenVisionLab 3D Studio는 위 기능을 소유하지 않고, 고정된 SDK
패키지를 명시적 어댑터로 소비하는 검증·사용 애플리케이션이다. SDK 변경이 두
애플리케이션에 자동으로 반영되어서는 안 된다.

## 3. 작업 계약

### 사용자 목표

외부 C# 사용자가 2D와 3D 알고리즘 DLL의 역할, 설치 패키지, 입력 계약과 결과
해석 방법을 이름만으로도 찾기 쉽게 만들고, 현재의 과도하게 큰 Smoke 파일과 3D
물리 폴더를 안정된 책임 단위로 정리한다.

### 비협상 요구사항

- 이름 변경과 물리 구조 정리는 기존 수치 알고리즘의 계산 결과를 변경하지 않는다.
- `2.9.1`의 단위, 좌표계, `NaN`, Infinity, 커버리지와 fail-closed 계약을 보존한다.
- 기존 Smoke 138건의 이름, 실행 순서, 입력, 기대값과 결과를 보존한다.
- 2D native `Mat` 소유권과 결과 자원 해제 계약을 보존한다.
- 공개 API 이름 변경은 `3.0.0`의 명시적인 breaking change로만 수행한다.
- UI, 센서 파서, 레시피 또는 Studio 전용 어댑터를 SDK로 이동하지 않는다.
- 새 테스트 프레임워크, 호환 래퍼 또는 추상화는 실제 필요가 확인되지 않으면
  추가하지 않는다.

### 범위

포함:

1. 제품명, 저장소, 솔루션, 프로젝트, 패키지, DLL과 네임스페이스 변경
2. Smoke 실행기의 도메인별 suite 분리
3. `Vision3D` 소스의 안정된 책임별 물리 폴더 정리
4. 패키지별 README, Quick Start, XML 문서와 package-only 소비자 검증
5. `2.9.1`에서 `3.0.0`으로 옮기는 마이그레이션 표와 체크리스트

제외:

- 알고리즘 수식, 공차 의미, 기본값 또는 오류 정책 변경
- OpenVisionLab 및 OpenVisionLab 3D Studio 저장소의 실제 코드 변경
- GitHub 저장소 이름 변경과 NuGet 공개 배포
- 물리 센서, 교정 데이터, Gauge R&R 또는 생산 승인 시험

제외 항목은 별도 사용자 승인과 해당 저장소의 독립 검증 없이 실행하지 않는다.

## 4. 이름과 공개 식별자

### 제품과 저장소

| 항목 | 2.9.1 | 3.0.0 목표 |
| --- | --- | --- |
| 제품명 | Library-Noah | OpenVisionLab Vision SDK |
| GitHub 저장소 | `Library-Noah` | `OpenVisionLab-Vision-SDK` |
| 솔루션 | `Lib.Common.sln` | `OpenVisionLab.VisionSdk.sln` |
| 패키지 접두사 | `Lib.*` | `OpenVisionLab.*` |
| 패키지 버전 | `2.9.1` | `3.0.0` |
| Assembly/File version | 프로젝트별 기존 값 | `3.0.0.0` |

GitHub 저장소 이름 변경은 외부 상태를 바꾸는 작업이므로 코드와 패키지 검증이
완료된 뒤 별도 체크포인트에서 수행한다.

### 프로젝트, 패키지와 DLL

| 2.9.1 프로젝트/패키지/DLL | 3.0.0 프로젝트/패키지/DLL | 책임 |
| --- | --- | --- |
| `Lib.Common` | `OpenVisionLab.Core` | 공통 변환, 2D 기하와 OpenCV runtime 자산 |
| `Lib.OpenCV` | `OpenVisionLab.Vision2D` | 주요 OpenCV 2D 검사 Tool과 pipeline |
| `Lib.OpenCV.Blob` | `OpenVisionLab.Vision2D.Blob` | Blob 라벨링과 면적 필터링 |
| `Lib.ThreeD` | `OpenVisionLab.Vision3D` | height-map, full-XYZ, mesh와 3D 검사 알고리즘 |
| `Lib.Inspection` | `OpenVisionLab.Inspection` | 2D/3D 결합 실행과 원래 결과 보존 |
| `Lib.Inspection.Smoke` | `OpenVisionLab.Inspection.Smoke` | 합성 계약·회귀 실행기; 비배포 프로젝트 |

패키지 ID, 어셈블리 이름과 프로젝트 이름은 같은 문자열을 사용한다. 사용자가
NuGet 패키지명, 생성된 DLL과 문서에서 서로 다른 이름을 해석하게 만들지 않는다.

### 네임스페이스

| 2.9.1 | 3.0.0 목표 |
| --- | --- |
| `Lib.Common` | `OpenVisionLab.Core` |
| `Lib.Line` | `OpenVisionLab.Core.Geometry2D` |
| 저장소 소유 `OpenCvSharp.Extensions` 이미지 변환 타입 | 소비자 UI 어댑터로 이동(SDK 대체 API 없음) |
| `Lib.OpenCV` | `OpenVisionLab.Vision2D` |
| `Lib.OpenCV.Pipeline` | `OpenVisionLab.Vision2D.Pipeline` |
| `Lib.OpenCV.Property` | `OpenVisionLab.Vision2D.Property` |
| `Lib.OpenCV.Result` | `OpenVisionLab.Vision2D.Result` |
| `Lib.OpenCV.Tool` | `OpenVisionLab.Vision2D.Tool` |
| `Lib.OpenCV.Blob` | `OpenVisionLab.Vision2D.Blob` |
| `Lib.ThreeD.Geometry` | `OpenVisionLab.Vision3D.Geometry` |
| `Lib.ThreeD.FeatureExtraction` | `OpenVisionLab.Vision3D.FeatureExtraction` |
| `Lib.ThreeD.Inspection` | `OpenVisionLab.Vision3D.Inspection` |
| `Lib.Inspection` | `OpenVisionLab.Inspection` |

이 표는 네임스페이스 소유권을 기본으로 설명한다. 공개 type과 member 이름은 별도 결함이
확인되지 않는 한 유지한다. 다만 UI 이미지 변환 타입은 승인된 Core 경계 정리에서 제거하고
소비자 UI 어댑터 책임으로 전환했다. 저장소가 소유한 타입을 제3자 네임스페이스에 계속 두지
않으며, 해당 예외의 호출부는 공개 API inventory에서 별도로 검증한다.

## 5. 버전 및 호환 정책

- `2.9.1`은 `Lib.*` 이름을 사용하는 마지막 호환 기준이다.
- `3.0.0`은 패키지, DLL과 네임스페이스가 바뀌므로 source/binary breaking release다.
- `2.9.1` 산출물과 태그를 지우거나 같은 버전으로 덮어쓰지 않는다.
- `3.0.0` 패키지가 `Lib.*` 패키지를 자동으로 교체한다고 가정하지 않는다.
- 첫 3.0 배포에는 type-forwarder, 이중 네임스페이스 또는 legacy facade를 만들지
  않는다. 실제 외부 소비자의 호환 요구가 확인될 때 별도 설계한다.
- 이름 변경과 별개인 수치 API 변경은 같은 마이그레이션에 섞지 않는다.

## 6. 목표 물리 구조

```text
OpenVisionLab-Vision-SDK
|- OpenVisionLab.VisionSdk.sln
|- Directory.Build.props
|- src
|  |- OpenVisionLab.Core
|  |- OpenVisionLab.Vision2D
|  |- OpenVisionLab.Vision2D.Blob
|  |- OpenVisionLab.Vision3D
|  |  |- Geometry
|  |  |- FeatureExtraction
|  |  |  |- Filtering
|  |  |  |- GeometryConstruction
|  |  |  |- GridAndStatistics
|  |  |  |- Metrology
|  |  |  |- Mesh
|  |  |  |- Registration
|  |  |  `- SurfaceMatching
|  |  `- Inspection
|  `- OpenVisionLab.Inspection
|- tests
|  `- OpenVisionLab.Inspection.Smoke
`- docs
   `- packages
```

`FeatureExtraction` 하위 폴더는 물리적 탐색 경계다. 3.0 네임스페이스 표에 없는
추가 하위 네임스페이스를 파일 이동만을 이유로 만들지 않는다.

Smoke 프로젝트는 새 테스트 프레임워크를 도입하지 않고 현재 실행형 검증 방식을
유지한다. `Program.cs`는 suite 등록과 최종 종료 코드만 소유하고 테스트 본문은
다음 책임으로 분리한다.

- height-map 및 3D inspection
- 3D geometry, transform 및 grid
- surface matching 및 mesh
- statistics 및 evidence
- 2D Tool, matching 및 resource ownership
- combined inspection runner

분리 완료의 기준은 파일 수가 아니라 `Program.cs`가 테스트 구현 세부사항을 더는
소유하지 않고, 각 suite가 독립된 도메인 테스트만 소유하는 것이다.

## 7. 패키지 사용자 경험

각 배포 패키지는 해당 패키지에 맞는 README와 생성된 XML IntelliSense 문서를
포함한다. 저장소 전체 README 하나를 모든 패키지의 설명으로 재사용하지 않는다.

필수 Quick Start:

1. `Vision2D`: 이미지 로드 → Tool 설정 → `Execute` → 결과/자원 해제
2. `Vision3D`: 단위·프레임이 선언된 `HeightMap3D` → 검사 → 측정 상태/metric 해석
3. `Vision3D`: surface match → pose/score component export
4. `Vision3D`: nominal/actual mesh comparison → 거리 결과 해석
5. `Inspection`: 2D/3D 결합 실행 → 원래 결과와 실패 후 증거 보존

문서의 예제는 소스 프로젝트 참조가 아니라 생성된 NuGet 패키지만 참조하는 독립
소비 프로젝트에서 컴파일·실행되어야 한다.

## 8. 구현 체크포인트

### 체크포인트 A — 설계 고정

- 이 문서와 GitHub README의 3.0 안내를 추가한다.
- 현재 `2.9.1` 동작과 앞으로의 `3.0.0` 목표를 섞어 서술하지 않는다.

### 체크포인트 B — 동작 비변경 구조 정리

- Smoke 138건을 도메인 suite로 이동하고 이름과 순서를 보존한다.
- `Lib.ThreeD/FeatureExtraction` 파일을 안정된 책임별 물리 폴더로 이동한다.
- 이 단계에서는 기존 `Lib.*` 공개 식별자를 바꾸지 않는다.

구현 결과(2026-08-05):

- `Program.cs`를 4,704줄에서 suite 실행과 종료 코드만 소유하는 31줄 entry point로
  축소했다.
- 138개 테스트 본문을 5개 독립 도메인 suite로 이동했다.
- assertion, 공통 fixture와 test double을 `Support` 소유자로 분리했다.
- `FeatureExtraction` 소스 41개를 `Filtering` 4개, `GeometryConstruction` 6개,
  `GridAndStatistics` 8개, `Metrology` 2개, `Mesh` 3개, `Registration` 5개,
  `SurfaceMatching` 13개로 이동했다.
- 모든 3D 파일의 `Lib.ThreeD.FeatureExtraction` 네임스페이스와 공개 API를 유지했다.

### 체크포인트 C — 3.0 공개 식별자 전환

- 프로젝트 디렉터리를 `src`와 `tests` 아래 목표 이름으로 이동한다.
- solution/project/package/DLL/namespace/using을 표의 이름으로 변경한다.
- `Directory.Build.props`, CI, package metadata와 repository URL을 함께 갱신한다.
- 이전 소유자가 남아 있지 않은지 `Lib.*`와 `Library-Noah`를 검색한다.

역사 기록, 마이그레이션 문서와 2.9.1 비교 명령은 검색 예외로 명시한다.

구현 결과(2026-08-05):

- 여섯 프로젝트를 `src`와 `tests` 아래 목표 경로로 이동하고 solution, csproj와
  buildTransitive targets 이름을 `OpenVisionLab.*`으로 맞췄다.
- 패키지, DLL, assembly/file version과 공개 namespace를 `3.0.0` 계약으로 전환했다.
- 저장소 소유 `BitmapConverter`를 `OpenCvSharp.Extensions`에서
  `OpenVisionLab.Core.Imaging`으로 이동하고 명시적인 OpenCvSharp 참조를 추가했다.
- 후속 Core 경계 정리에서 해당 임시 변환 API와 `System.Drawing.Common`, `WindowsBase`,
  `OpenCvSharp.Extensions`, COM 포트·시스템 시간·드라이브 관리 유틸리티를 제거했다.
  Vision2D 입력은 `Mat` 계약만 유지하고 화면 이미지 변환은 소비자 UI 어댑터 책임으로 확정했다.
- 현재 코드, solution, project와 CI의 `Lib.*`, `Library-Noah`, 이전 solution 참조를
  0건으로 정리했다.
- README, 현행 3D 가이드, affine 가이드와 독립 마이그레이션 가이드를 3.0 기준으로
  갱신했다. 완료된 2.x 개발·릴리스 기록의 이전 이름은 역사 증거로 유지했다.

### 체크포인트 D — 소비자 문서 및 패키지 검증

- 5개 패키지에 각각의 역할과 설치/첫 사용 흐름을 설명하는 전용 README를 추가했다.
- Vision3D 패키지 README에 bounded Surface Match pose와 Mesh Comparison Quick Start를
  실제 공개 API로 작성하고, 기존 XML documentation file과 3D 상세 문서를 함께
  패키징했다.
- package-only 독립 소비자가 생성된 nupkg만으로 2D native, height-map 검사,
  Surface Match와 Mesh Comparison을 실행하도록 CI에 추가했다.
- OpenVisionLab과 3D Studio용 마이그레이션 체크리스트를 작성했다. 두 소비
  저장소의 실제 코드는 변경하지 않았다.

### 체크포인트 E — 외부 이름 및 배포

- GitHub 저장소를 `Noah8218/Library-Noah`에서
  `Noah8218/OpenVisionLab-Vision-SDK`로 변경했다. 저장소 ID, 공개 상태와 기본
  브랜치는 유지했고 로컬 `origin`도 새 URL로 갱신했다.
- NuGet 게시, 소비 애플리케이션의 package pin 갱신 및 릴리스 태그는 각각 외부
  상태 변경으로 취급한다.

## 9. 검증 및 완료 기준

각 체크포인트는 관련 변경을 한 번에 적용한 뒤 다음 최소 검증을 수행한다.

1. `dotnet build <현재 솔루션> -c Release`가 warning/error 없이 성공한다.
2. Smoke가 기존 138개 이름과 순서로 `138/138 passed`를 출력한다.
3. `dotnet pack <현재 솔루션> -c Release --no-build`가 성공한다.
4. 패키지 안의 ID, 버전, DLL, XML, README, repository URL/commit을 검사한다.
5. D 드라이브의 package-only 소비 프로젝트에서 2D/3D 예제를 빌드·실행한다.
6. 이전 경로, 프로젝트 참조, 네임스페이스와 직접 호출이 허용된 역사 문서 외에
   남지 않았음을 검색한다.
7. 구조 정리 전후의 공개 API inventory와 Smoke 결과를 비교한다.

합성 Smoke와 package-only 검증은 실제 센서 정확도, 교정, Gauge R&R 또는 생산
승인을 증명하지 않는다.

## 10. 위험과 중단 조건

- 이름 전환은 모든 소비자에게 명시적인 소스 마이그레이션을 요구한다.
- 저장소가 소유했던 `OpenCvSharp.Extensions` 이미지 변환 타입의 제거는 소비자 UI
  어댑터로 명시적으로 마이그레이션해야 한다.
- NuGet 이름의 현재 미등록 상태는 예약이나 상표 사용 권리를 보장하지 않는다.
- 계산 결과, 기본값 또는 오류 의미가 바뀌면 이름 변경 작업을 중단하고 별도 수치
  변경으로 검토한다.
- OpenVisionLab 또는 3D Studio를 실제로 갱신해야 완료되는 단계에서는 해당
  저장소 변경 승인을 다시 받는다.

## 11. 설계 완료 기록 — 체크포인트 A 시점

```text
Status: Complete
Scope: OpenVisionLab Vision SDK 제품명, 3.0 공개 식별자, 책임 경계, 물리 구조, 구현 체크포인트와 검증 계약
Acceptance criteria: 공식 이름과 3.0 전환 명시 -> pass; 2.9.1 호환 기준과 breaking-change 경계 명시 -> pass; 2D/3D 패키지·namespace 대응표 -> pass; Smoke/FeatureExtraction 구조 목표와 검증 기준 -> pass; 소비 애플리케이션 경계 -> pass
Verification: 현재 Directory.Build.props 2.9.1, 6개 프로젝트, namespace 목록, Lib.ThreeD 59개 소스와 4,704줄 Smoke/138개 등록 기준에 대조
Evidence: docs/OPENVISIONLAB_VISION_SDK_IDENTITY_AND_V3_MIGRATION_PLAN_20260805.md
Boundary / next dependency: 이 기록은 설계 문서 완료만 의미한다. 프로젝트, package, DLL, namespace, GitHub 저장소와 소비 애플리케이션은 아직 변경하지 않았다.
```

## 12. 체크포인트 B 완료 기록 — 체크포인트 B 시점

```text
Status: Complete
Scope: Smoke 도메인 suite 분리와 Lib.ThreeD FeatureExtraction 책임별 물리 폴더 정리
Acceptance criteria: Program이 test 구현을 소유하지 않음 -> pass; 5개 suite/138개 case 이름·순서 보존 -> pass; FeatureExtraction 41개 파일/7개 책임 폴더/기존 namespace 보존 -> pass; 공개 API 불변 -> pass
Verification: Release build 0 warnings/0 errors; Smoke 138/138; baseline 대비 ordered case diff 0; Lib.ThreeD exported types 211, public API inventory 2,658 lines, diff 0; pack 5 packages 성공
Evidence: D:\OpenVisionLab-TestData\Library-Noah\20260805-openvisionlab-v3-structure\baseline; D:\OpenVisionLab-TestData\Library-Noah\20260805-openvisionlab-v3-structure\final
Boundary / next dependency: 현재 package, DLL, namespace와 solution 이름은 계속 Lib.* 2.9.1이다. 체크포인트 C에서만 OpenVisionLab.* 3.0.0으로 전환한다.
```

## 13. 체크포인트 C 완료 기록

```text
Status: Complete
Scope: solution/project/package/DLL/namespace와 src/tests 소유 경로를 OpenVisionLab Vision SDK 3.0으로 전환
Acceptance criteria: 여섯 프로젝트 새 경로와 참조 해결 -> pass; live code/config의 이전 식별자 0 -> pass; 패키지 5개 ID·DLL·version 일치 -> pass; type/member 계약 보존 -> pass; README·3D·affine·마이그레이션 문서 갱신 -> pass
Verification: Release build 0 warnings/0 errors; Smoke 138/138, baseline ordered case diff 0; 5개 배포 assembly 전체 공개 API 5,295줄을 이전 namespace로 정규화한 diff 0; pack 5개 성공; assembly/file version 3.0.0.0; package metadata errors 0; package-only 2D native/3D consumer 실행 pass
Evidence: D:\OpenVisionLab-TestData\Library-Noah\20260805-openvisionlab-v3-rename\final
Boundary / next dependency: GitHub 저장소 이름, NuGet 게시와 OpenVisionLab/3D Studio 소비 코드는 변경하지 않았다. 현재 검증용 nupkg의 repository commit은 미커밋 worktree 때문에 이전 HEAD 2abfbf02b9c94a14a2c7f6a945762df60679b2fc이며 릴리스 산출물이 아니다. 커밋 후 다시 pack하고 소스 commit·hash를 고정해야 한다. 체크포인트 D는 패키지별 README, Quick Start와 package-only 소비자 CI다.
```

## 14. 체크포인트 D 완료 기록

```text
Status: Complete
Scope: 5개 NuGet 패키지 전용 README, Vision3D Surface Match/Mesh Quick Start, package-only 2D/3D 소비자 CI, OpenVisionLab/3D Studio 전환 체크리스트
Acceptance criteria: 모든 패키지 README 포함 -> pass; Surface Match/Mesh 예제 실행 -> pass; ProjectReference 없는 package-only 소비자 -> pass; 2D native와 3D 검사 동시 실행 -> pass; 소비 저장소별 체크리스트 -> pass
Verification: Release build 0 warnings/0 errors; 기존 Smoke 138/138; pack 5개 성공; nupkg 5/5 README.md 포함; package-only 2D native/height-map/Surface Match/Mesh 실행 pass
Evidence: D:\OpenVisionLab-TestData\Library-Noah\20260805-openvisionlab-v3-package-docs-ci
Boundary / next dependency: 알고리즘과 공개 API 동작은 변경하지 않았다. 이 기록 시점에는 GitHub 저장소 rename, commit/push, NuGet 게시와 OpenVisionLab/3D Studio 실제 소비 코드 전환을 수행하지 않았다. 이후 GitHub rename 결과는 15절에 기록한다.
```

## 15. GitHub 저장소 이름 변경 완료 기록

```text
Status: Complete
Scope: GitHub 저장소 이름을 Noah8218/OpenVisionLab-Vision-SDK로 변경하고 로컬 origin을 새 clone URL로 갱신
Acceptance criteria: 목표 이름 사용 가능 및 admin 권한 -> pass; 동일 저장소 ID 유지 -> pass; public/main/archived=false 유지 -> pass; 새 origin fetch/push URL 일치 -> pass; 새 origin HEAD 조회 -> pass
Verification: GitHub repository ID 619374280 유지; repository_full_name Noah8218/OpenVisionLab-Vision-SDK; default_branch main; visibility public; git ls-remote origin HEAD 성공
Evidence: https://github.com/Noah8218/OpenVisionLab-Vision-SDK; local git remote -v
Boundary / next dependency: 로컬 디렉터리 C:\Git\Library-Noah는 이동하지 않았다. 이 기록은 저장소 rename만 증명하며 3.0 소스의 branch/PR/main 반영 상태는 Git 기록으로 확인한다. NuGet 게시 및 OpenVisionLab/3D Studio 소비 코드는 변경하지 않았다.
```

## 16. 3.0 main 반영 완료 기록

```text
Status: Complete
Scope: PR #1을 일반 merge commit 방식으로 main에 반영하고 로컬 기준 저장소를 병합 결과와 정렬
Acceptance criteria: PR #1 merged -> pass; merge commit 6eebe0da48a60205b2b99826b6919f029ad0d00a 확인 -> pass; 병합 후 Build 성공 -> pass; 로컬 main과 origin/main 일치 -> pass; 작업 브랜치 유지 -> pass; 4개 worktree clean -> pass
Verification: PR #1 metadata 및 merge parent 확인; GitHub Actions Build run 30982594294 success; git rev-parse main/origin/main 및 git worktree list/status 확인
Evidence: https://github.com/Noah8218/OpenVisionLab-Vision-SDK/pull/1; https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/30982594294; D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-pr1-fixes-a32f36a\COMPLETION_RECORD.md; D:\OpenVisionLab-TestData\Library-Noah\20260805-openvisionlab-sdk-git-migration\COMPLETION_RECORD.md
Boundary / next dependency: 이 기록은 3.0 소스의 main 반영과 로컬 정렬만 증명한다. NuGet 게시, OpenVisionLab 및 OpenVisionLab 3D Studio 소비 저장소 변경은 수행하지 않았으며 각각 별도 승인이 필요하다.
```

## 17. 승인된 후속 Core 구조 정리 결과

`OpenVisionLab.Core`가 UI 독립적인 SDK 기반 코드만 소유하도록 기존 Bitmap 처리·변환,
화면 좌표 변환, COM 포트·시스템 시간·드라이브 관리 코드와 관련 패키지·바이너리
의존성을 제거했다. `OpenVisionLab.Vision2D`는 `SetSourceImage(Mat)` 계약만 유지한다.

공개 assembly metadata 비교에서 Core는 승인 목록에 해당하는 공개 항목 110개와
`System.Drawing.Common`, `System.IO.Ports` 참조(총 112개)가 제거되고 추가 항목은 0개였다.
Vision2D는 Bitmap 입력 overload 2개와 `System.Drawing.Common` 참조(총 3개)가 제거되고
추가 항목은 0개였다. Release build는 warning/error 0건, 합성 Smoke는 138/138,
5개 패키지 pack과 package-only 소비자 실행은 모두 통과했다.

NuGet 게시는 수행하지 않았고 OpenVisionLab 및 OpenVisionLab 3D Studio 소비 저장소도
변경하지 않았다. 실제 소비자 전환은 계속 별도 승인 범위다.

## 18. 3.0 공개 API 및 패키지 사용성 검수 결과

이름 변경 직후 보존한 5개 어셈블리 공개 API 5,295줄과 현재 `main`을 다시 비교했다.
승인된 Core UI·운영체제 유틸리티 및 Vision2D Bitmap overload 제거에 해당하는 127줄
외의 누락은 0줄이었다. 추가된 공개 API는 pipeline step 성공 상태 2줄과 이번 검수에서
도입한 `BlobToolProperty` 62줄뿐이며, 승인되지 않은 namespace/type/member 추가는 0줄이다.

패키지 계약 검수에서는 기존 5개 nupkg 중 `OpenVisionLab.Vision3D`만 XML IntelliSense
파일을 포함하고 나머지 4개 패키지는 누락한 사실을 확인했다. 모든 배포 프로젝트에서
XML 문서를 생성하도록 고치고, CI가 5개 nupkg 각각의 `README.md`와 대응 XML 파일을
검사하도록 했다. 기존 Core XML 주석의 잘못된 매개 변수 이름 4곳도 실제 signature와
일치시켜 문서 빌드 경고를 제거했다.

`OpenVisionLab.Vision2D.Blob`에는 20개 필수 설정을 바로 제공하는 `BlobToolProperty`를
추가했다. 기존 `IOpenCVPropertyBlob` 확장 경로는 유지한다. 패키지 Quick Start의
자리표시자를 실제 타입으로 교체하고 package-only 소비자가 합성 Blob을 실제 실행해
결과 1개를 검증하도록 보강했다.

```text
Status: Complete
Scope: 3.0 공개 API 호환 재대조, 5개 패키지 XML IntelliSense 보장, Blob 첫 사용 설정과 package-only 실행 검증
Acceptance criteria: 2.9.1 기준 외 승인되지 않은 공개 API 누락 0 -> pass; namespace leak 0 -> pass; 신규 API가 BlobToolProperty에만 한정 -> pass; nupkg 5/5 README·XML 포함 -> pass; Blob package-only 실행 -> pass
Verification: .NET SDK 8.0.423 Release build 0 warnings/0 errors; Smoke 142/142; 공개 API 5,232줄, 승인 제거 127줄/승인 추가 64줄/불일치 0; pack 5개; isolated package-only Threshold/Blob/3D/Surface Match/Mesh 실행 pass
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-public-api-review
Boundary / next dependency: 기존 C*/CV*/Guage 오탈자 API는 2.9.1 호환 표면이므로 제거하지 않았다. 해당 제거는 실제 소비자 조사와 별도 major-version 승인 없이는 수행하지 않는다. NuGet 게시와 소비 저장소 변경도 수행하지 않았다.
```

## 19. 2D Tool 구체 Property 모델 완료 기록

현재 비레거시 2D Tool 중 소비자가 직접 설정 인터페이스를 구현해야 했던 Contour/Corner,
Matching, EdgeBasedTemplateMatching, SIFT, Mean, LineGauge에 구체 Property 모델을 추가했다.
공통 전처리 계약은 `OpenCvToolPropertyBase` 한 곳에 두고 각 Tool 모델은 고유 설정과
운영 기본값만 소유한다. 애플리케이션 전용 저장 모델을 위한 기존 인터페이스는 유지한다.

AutoMPoint 내부 Edge matcher와 Smoke의 Mean/Contour/Edge matcher가 사용하던 임시
Property 구현은 새 공개 모델로 교체했다. 이로써 중복 구현 3종을 제거하면서 실제 제품
실행 경로와 테스트가 공개 모델을 직접 사용한다. `CV*` 및 `LineGuage` 오탈자 계약은
2.9.1 레거시 호환 범위이므로 이번 변경에서 제외했다.

```text
Status: Complete
Scope: 비레거시 2D Tool의 concrete Property 모델, 공통 기본 설정, 문서와 package-only 실행 검증
Acceptance criteria: 현재 비레거시 Tool의 interface-only 설정 0 -> pass; 기존 공개 API 삭제 0 -> pass; 신규 공개 타입 7개만 추가 -> pass; 임시 Property 구현 3종 제거 -> pass; package-only Contour/Mean/Matching 실행과 Edge/SIFT/LineGauge 기본 계약 -> pass
Verification: .NET SDK 8.0.423 Release build 0 warnings/0 errors; Smoke 142/142; 공개 API 5,232 -> 5,586줄, 제거 0/승인 추가 354/예상 밖 추가 0; pack 5개 및 README·XML 5/5; isolated package-only restore/build 0 warnings/0 errors와 실행 pass
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-2d-property-models
Boundary / next dependency: C*/CV*/LineGuage 레거시 API 삭제, NuGet 게시, OpenVisionLab 및 OpenVisionLab 3D Studio 소비 저장소 변경, UI·카메라·PLC 통합은 수행하지 않았다.
```

## 20. C*·CV*·LineGuage 4.0 폐기 사전 조사

현재 공개 API에서 대소문자를 구분해 `C*`, `CV*`, `LineGuage` 후보의 합집합을
확인한 결과 top-level 공개 타입은 24개다. 이 중 15개에는 기존
`[Obsolete(..., false)]`가 있고 9개에는 없다. 초기 13/11 집계는
`[System.Obsolete(...)]` 형태의 `CResultCorner`와 `CResultMean`을 놓친 검색 오류였으며
소스 재검사로 정정했다. 현대식 대체 타입은 모두 존재하지만,
일부는 생성자, property 형식, 상속 계약 또는 오류 처리에 차이가 있어 이름만 바꾸는
일괄 삭제 대상으로 볼 수 없다.

읽기 전용 로컬 소비자 조사에서 `OpenVisionLab`, Dev, 3D Studio 현재 소스에는 정확한
후보 이름이 없었다. 반면 `Labelling_Application`의 측정 계산은 체크인된 레거시 DLL을
통해 `CLine`, `CFormula`, `CLineVertical`을 실제 사용하며 해당 동작 테스트도 존재한다.
따라서 이 세 타입을 포함한 4.0 제거는 소비 저장소의 별도 승인·전환·검증 전에는
진행할 수 없다.

전체 inventory, 타입별 대체 관계, 직렬화 위험, 제거 순서와 중단 게이트는
[레거시 C/CV/LineGuage 실제 사용처와 4.0 폐기 설계](LEGACY_C_CV_LINEGUAGE_V4_REMOVAL_PLAN_20260805.md)를 따른다. 이번 단계에서는 소스 삭제, 새 `[Obsolete]` 표시, NuGet 게시와 소비 저장소 변경을 수행하지 않았다.

## 21. 레거시 API 대표 parity 체크포인트

Smoke에 `LegacyApiCompatibilitySmokeSuite` 12개 case를 추가해 후보 24개를 모두 직접
참조했다. Core 계산, DTO 공유 필드, Blob/Contour/Mean/Matching/Line Gauge 대표 실행은
현대식 API와 일치했다. 반면 helper의 null 처리, `CVCorner.results`, `CVSIFT` native 실패와
현대식 fallback은 실제 비대칭으로 고정했다.

Release build는 warning/error 0건, 전체 Smoke는 142개에서 154개로 늘어 154/154를
통과했다. 이 결과는 대표 회귀 증거이며 4.0 제거, 소비 저장소 전환, 외부 recipe 호환,
NuGet 게시 승인을 대신하지 않는다. 상세 범위와 남은 게이트는 20절의 연결 문서 10절을
따른다.

## 22. NuGet 패키지 버전 불변성과 격리 소비자 검증

동일한 `OpenVisionLab.* 3.0.0` ID·버전으로 내용이 다른 로컬 패키지가 생성되어,
NuGet 전역 캐시의 이전 DLL이 현재 package-only 소비자 검사를 가리는 문제를
재현했다. 현재 nupkg에는 존재하는 2D Property 타입 10개가 캐시 DLL에는 없어 일반
복원이 컴파일에 실패했고, 두 패키지의 SHA-512도 달랐다. 빈 전용 캐시에서는 같은
현재 nupkg로 빌드와 실행이 통과했으므로 알고리즘 또는 현재 패키지 누락이 아니라
패키지 ID·버전 재사용과 캐시 오염이 원인이었다.

저장소의 로컬 개발 기본 `PackageVersion`을 `3.0.1-dev.1`로 이동하고, CI는 매 실행마다
`3.0.1-ci.<run>.<attempt>`를 사용한다. package-only 소비자는 같은 전역
`PackageVersion`을 참조하며, 방금 생성한 패키지 디렉터리만 source로 사용하는 실행별
전용 `RestorePackagesPath`에서 복원한다. 정식 버전은 승인된 배포 뒤 내용을 바꾸지 않고,
다음 변경은 새 patch 또는 prerelease 버전을 사용한다. 운영 명령, 버전 예시와 릴리스
증거 템플릿은 저장소 `README.md`의 Packaging Notes에 기록했다.

```text
Status: Complete
Scope: 5개 SDK 패키지의 고유 개발·CI 버전, package-only 참조 버전 정렬, 격리 NuGet 캐시 복원과 운영 문서
Acceptance criteria: 이전 mutable 3.0.0 기본값 제거 -> pass; CI 실행별 고유 버전 -> pass; 5개 nupkg 동일 버전 및 README/XML -> pass; 전역 캐시 비의존 package-only restore/build/run -> pass; 릴리스 버전·commit·SHA-256 기록 규칙 -> pass
Verification: .NET SDK 8.0.423 Release build 0 warnings/0 errors; Smoke 154/154; 3.0.1-ci.999999.1 nupkg 5개 생성 및 내부 version/README/XML 5/5; 전용 RestorePackagesPath와 packed-only RestoreSources에서 package consumer build 0 warnings/0 errors 및 실행 pass
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260821-immutable-packages
Boundary / next dependency: NuGet 게시는 수행하지 않았고 기존 전역 캐시를 삭제하지 않았다. 정식 3.0.1 배포, 소비 저장소 package pin·hash 갱신과 생산 계측 검증은 별도 승인 범위다.
```

## 23. Matching 책임 경계와 생산 기준선 선행 조건

`MatchingTool`, `EdgeBasedTemplateMatchingTool`, `AutoMPointTool`의 공개 API와 결과 계약은
유지하면서 검색, 회전 템플릿 캐시, Edge 모델 저장소, 후보 검색, hybrid 검증, 고유성 판정,
Auto MPoint 후보 분석을 명시적인 내부 소유자로 이전했다. 기존 154개 Smoke는 이름과 순서를
바꾸지 않고 선행 집합으로 유지하며 Matching 전용 characterization 7개를 맨 뒤에 추가했다.

실제 생산 정확도와 성능 승인은 이 합성 회귀와 분리한다. 센서 모델/설정, 교정 ID와 hash,
부품군/recipe, 독립 ground truth, ground-truth 불확도, 생산 LSL/USL 및 false accept/reject와
takt 기준이 승인된 뒤에만 고정 validation/challenge 데이터로 판정한다. 상세 소유자 지도,
manifest 예시, 데이터 분할, 지표와 중단 게이트는
[Matching 책임 경계와 생산 기준선 계획](MATCHING_RESPONSIBILITY_AND_PRODUCTION_BASELINE_PLAN_20260821.md)에 기록했다.

```text
Status: Complete
Scope: Matching/EdgeBased/AutoMPoint 내부 책임 분리, 기존 154개 보존과 characterization 7개 추가, 생산 기준선 선행 조건 문서화
Acceptance criteria: 내부 소유자와 실제 호출 경로 -> pass; Release build 0/0 -> pass; Smoke 161/161 5회 -> pass; commit 고정 nupkg 5개와 격리 package-only 소비 -> pass
Verification: implementation a535c51206a4a762afbec9291b7645da0d5014f3; package 3.0.1-dev.20260821.matching.2; 상세 명령과 hash는 연결 문서와 evidence 참조
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260821-matching-responsibility-refactor
Boundary / next dependency: 실제 센서/교정/ground truth/불확도/생산 공차/오류율/takt 승인 전까지 생산 정확도·성능 기준선 Track B는 Blocked다. NuGet 게시와 소비 저장소 변경은 수행하지 않았다.
```

## 24. Height-map crop 공개 패키지 계약 완료 기록

`HeightMapCropTool`은 유효한 `HeightMapRoi`를 더 작은 불변 `HeightMap3D`로 복사한다.
행 우선 값과 `NaN`, pitch, 단위, frame 및 source ID를 보존하며, 출력 원점은 선택한
source row/column 위치로 이동한다. 잘못된 ROI는 출력 없는 controlled result로 반환하고
취소는 `OperationCanceledException`으로 전파한다.

생성된 NuGet만 참조하는 독립 소비자는 Crop 공개 API를 직접 실행해 `SourceRoi`, 출력
크기, 원점, pitch, 단위, frame, source ID, 유효/결측 개수와 `NaN` 위치를 검증한다.
이 검사는 소스 프로젝트 참조나 전역 NuGet 캐시에 의존하지 않는다.

```text
Status: Complete
Scope: HeightMapCropTool/HeightMapCropResult 공개 계약, 합성 Smoke 2개, Vision3D Quick Start와 상세 문서, 격리 package-only Crop 실행
Acceptance criteria: 유효 ROI의 값/NaN/원점/메타데이터 보존 -> pass; invalid ROI controlled failure와 cancellation 전파 -> pass; Release build 경고/오류 0 -> pass; 전체 Smoke 163/163 -> pass; 고유 버전 5개 package의 ID/version/commit/README/XML/내부 의존성/hash -> pass; 격리 package-only restore/build/run -> pass; 원격 main CI -> pass
Verification: local .NET SDK 10.0.303; implementation 7da6631e714a9257af36c3da575474df9331ff36; package consumer b75a8d89581a9cc0f1cbf3556a2481f23a1e3a1e; package 3.0.1-dev.20260823.crop.b75a8d89581a; GitHub Actions Build 32636496085 success
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260823-height-map-crop-b75a8d89581a; committed-packages.json; https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/32636496085
Boundary / next dependency: 합성·패키지 검증은 실제 센서 정확도, 교정, Gauge R&R 또는 생산 승인이 아니다. NuGet 게시와 소비 저장소 변경은 수행하지 않았다. 다음 실행 가능한 알고리즘 우선순위는 TriangleMeshDistanceTool의 분석적 characterization을 먼저 보강한 뒤 BVH, closest-point 및 robust-sign 책임을 분리하는 것이다(Recommended model: gpt-5.6-sol; Reasoning effort: high).
```

## 25. Triangle-mesh distance 분석 계약과 책임 경계 완료 기록

`TriangleMeshDistanceTool`의 공개 API와 관찰 가능한 수치 동작을 유지하면서 기존 단일
구현의 세 책임을 실제 내부 소유자로 이전했다. `TriangleMeshBvhIndex`는 source 검증,
BVH 생성, centroid 정렬, nearest 및 bounds 후보 순회를 소유한다.
`TriangleClosestPointKernel`은 vertex/edge/face 영역별 삼각형 최근접점 수식만 소유하고,
`TriangleMeshSignResolver`는 direct sign, robust 후보 필터, face 우선순위, boundary
직교도 및 source-index tie break를 소유한다. 공개 façade는 query 계약, 호출 조율과
`PointMeshDistance` 조립만 남겼다.

실제 호출 경로는 다음과 같다.

```text
NominalActualMeshComparisonTool
  -> TriangleMeshDistanceTool
      -> TriangleMeshBvhIndex
          -> TriangleClosestPointKernel
      -> TriangleMeshSignResolver
          -> TriangleMeshBvhIndex.VisitBoundsCandidates
          -> TriangleClosestPointKernel
```

구조 변경 전에 분석적 characterization 4개를 기존 163개 뒤에 추가했다. 이 검사는
face/edge/vertex의 closest point·normal·sign evidence, 서로 다른 BVH child의 exact-distance
tie와 입력 역순, robust epsilon의 포함/제외 경계, face 우선순위, boundary 직교도와
source-index 순위, invalid 입력의 예외 타입과 `ParamName`을 고정한다. 새 검사는 이전
구현에서 먼저 167/167을 통과했으며, 구조 변경 뒤에도 동일하게 통과했다.

기존 robust sign에서 nonzero distance와 `side == 0`이 만나면 양의 signed distance로
resolved되는 동작은 이번 behavior-preserving 범위에서 유지했다. 설명 문서와 정책 의미가
모호하므로 이를 원하는 미래 정책으로 새로 승인하는 characterization은 추가하지 않았다.

```text
Status: Complete
Scope: TriangleMeshDistance 분석적 characterization 4개, BVH/closest-point/direct·robust-sign 실제 책임 분리, 공개 API 및 package 소비 경로 보존
Acceptance criteria: 기존 163개 이름·순서 exact prefix -> pass; 이전 구현에서 신규 포함 167/167 -> pass; 내부 소유자 3개와 실제 호출 경로 및 이전 owner 잔존 0 -> pass; NominalActualMeshComparisonTool 변경 0 -> pass; Release build warning/error 0 -> pass; 전체 Smoke 167/167 5회 동일 순서와 committed-source 1회 -> pass; Vision3D 공개 API 1,562줄 exact diff 0 -> pass; 고유 버전 5개 package ID/version/commit/README/XML/내부 의존성/DLL/hash -> pass; 격리 package-only restore/build/run -> pass; 구현 SHA 원격 main CI -> pass
Verification: local .NET SDK 10.0.303; characterization c74b3bb5bf2f237eef800e50ef6951109bf07cc5; implementation 4d8ef77e1498cf56427d4cca4534693a5dadc991; package 3.0.1-dev.20260823.mesh.4d8ef77e1498; GitHub Actions Build 32638002502 success
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260823-triangle-mesh-distance-refactor; REFACTOR_PROOF_PLAN.md; REFACTOR_PROOF_REPORT.md; final\STRUCTURE_PROOF.txt; committed-packages.json; https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/32638002502
Boundary / next dependency: 합성·package 검증은 실제 센서 정확도, 교정 유효성, Gauge R&R, 생산 오류율 또는 takt 성능을 증명하지 않는다. robust bounds 순회의 delegate callback 생산 비용도 측정하지 않았다. 실제 센서 데이터, 교정 ID/hash, 독립 ground truth와 불확도, 생산 LSL/USL·false accept/reject 한도 및 takt 한도가 제공·승인되기 전까지 생산 정확도·성능 기준선은 Blocked다. NuGet 게시와 소비 저장소 변경은 수행하지 않았다.
```

## 26. Nominal/actual mesh comparison 분석적 characterization 완료 기록

`NominalActualMeshComparisonTool`의 제품 코드와 공개 API를 바꾸지 않고, 기존 167개
Smoke 뒤에 책임별 분석적 characterization 4개를 추가했다. 수평 삼각형과 부호가
명확한 합성 점을 사용해 direct signed deviation `[-2, -1, +1, +2]`, edge에서의
robust sign recovery 1건, lower/upper inclusive tolerance 분류 `1/2/1`, unsigned와
signed 모집단 통계의 minimum/maximum/mean/population SD/RMS를 수식으로 검증했다.
nonzero distance와 `side == 0`인 모호한 정책 입력은 승인 범위 밖이므로 사용하지 않았다.

표시 증거는 7개 입력과 최대 3개에서 현재 구현의 ceil stride `3`, query index
`0/3/6`, cap, source identity와 순서를 확인했고, 최대 표시 개수 0이 표본만 비활성화하는
동작도 확인했다. stream의 too-few/too-many, non-finite point, null 입력, invalid expected
count/tolerance/display cap은 예외를 외부로 누출하지 않는 canonical failed result를
검증했다. 취소만 `OperationCanceledException`으로 전파되며, 동기 progress recorder로
65,537개 입력의 현재 중간 cadence `65,536`과 final `65,537`을 확인했다. 정확한 stride
산식과 65,536 cadence는 공개 문서의 새 장기 정책이 아니라 현재 구현을 의도적으로
고정한 characterization이며, 향후 정책 변경은 명시적인 테스트·문서 갱신이 필요하다.

baseline `c3061b534095d17da201e89c024205288e0da7a4`의 167개 PASS 이름과 순서를
위치별로 비교해 불일치 0건을 확인했다. test commit
`435160e540a38b10dcfcc9530c6983aa0790cda1`은 runner와 신규 suite만 변경했고
`src` 변경은 0건이다. 로컬 Release build는 warning/error 0건, 전체 Smoke는
171/171을 통과했다. 동일 test commit의 원격 CI도 Release build, 171 Smoke,
고유 버전 pack, package 문서 검사, 격리 package-only restore와 2D/3D consumer 실행을
모두 통과했다.

```text
Status: Complete
Scope: NominalActualMeshComparisonTool 분석적 characterization 4개; 기존 167개와 제품 코드·공개 API 보존
Acceptance criteria: baseline 167/167 -> pass; 기존 167개 이름·순서 exact prefix -> pass; 신규 포함 171/171 -> pass; robust/inclusive tolerance/전체 통계 -> pass; display stride/cap/order/0 -> pass; stream·invalid controlled failure -> pass; 65,536/final progress와 cancellation 전파 -> pass; production src diff 0 -> pass; 원격 CI pack/package consumer -> pass
Verification: local .NET SDK 10.0.303 Release build warning/error 0; Smoke 171/171; exact-prefix mismatch 0; test 435160e540a38b10dcfcc9530c6983aa0790cda1; GitHub Actions Build 32639216016 success
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260823-nominal-actual-characterization; baseline/pass-list.txt; characterized/pass-list.txt; characterized/prefix-comparison.txt; https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/32639216016
Boundary / next dependency: 이 합성 characterization은 실제 센서 정확도, 교정 유효성, Gauge R&R, 생산 오류율 또는 takt 성능을 증명하지 않는다. side == 0 정책, 제품 코드/API, NuGet 게시, 소비 저장소와 UI·카메라·PLC는 변경하지 않았다. 다음 실행 가능한 우선순위는 고정 합성 워크로드의 정확도 parity와 성능 기준선 수립이다(Recommended model: gpt-5.6-sol; Reasoning effort: high). 실제 생산 기준선 Track B는 센서 데이터, 교정 ID/hash, 독립 ground truth와 불확도, 생산 LSL/USL·false accept/reject 및 takt 한도 승인 전까지 Blocked다.
```

## 27. 고정 합성 mesh 정확도 parity와 성능 기준선 시도 기록

제품 `src`를 바꾸지 않고 commit `3f6e35beb951b8412e6fcd116c959f0a5c4d9a99`에
표준 라이브러리만 사용하는 opt-in benchmark harness를 추가했다. 비교 기준은 구조 변경 전
characterization commit `c74b3bb5bf2f237eef800e50ef6951109bf07cc5`이며, 같은 commit의
harness로 두 target DLL을 실행했다. 기존 171개 Smoke는 그대로 유지되고 harness commit의
원격 Build run `32641954389`에서 Restore, Release Build, 171 Smoke, Pack, 패키지 문서와
격리 package-only 소비자가 모두 통과했다.

고정 입력은 다음 두 종류다.

- `planar-direct-v1`: 64×64 cell, 8,192 triangle, 40,960 query,
  input SHA-256 `1f5d56e45ca7b174ece2e573e07f0442e405430859839873b925810bcc1a2730`
- `planar-boundary-v1`: 같은 mesh의 shared-diagonal 경계 12,288 query,
  input SHA-256 `e56ce73c3e17565b7cfa5368ffb398d308759187a4c2883f94affb2f0284e58e`

분석적 oracle에 대한 두 target의 point/result exact 및 `1e-12` quantized fingerprint는
두 workload 모두 일치했다. 결합 최대 절대 oracle 오차는 direct
`5.329070518200751e-15`, boundary `3.1086244689504383e-15`로 고정 허용치 안이다.
따라서 이 두 합성 입력에 대한 정확도 parity는 통과다. 이 결론은 historical 구현을
정답으로 간주한 것이 아니라 평면의 폐형식 정답과 각 target을 독립 비교한 결과다.

성능은 Windows 11 build 26100, AMD Ryzen 5 2600 6-core/12-thread, 약 32GB RAM,
.NET runtime 8.0.30, x64, 고성능 전원 계획에서 실행했다. 각 process는 debugger 없이
`High` priority와 inherited affinity `0xfff`를 사용했다. cold 1회, warm-up 3회,
10-operation batch의 measured 30회를 target별 두 독립 session에서 workload별
`A1-B1-B2-A2`로 실행했다. 단일 session wall/index/calculation RMAD와 같은 target의
session median 차이가 각각 모두 5% 미만이어야 하고, outlier 제거는 허용하지 않았다.

attempt 1–5는 각각 사전 CPU gate 또는 개별 session RMAD를 통과하지 못해 전부 제외했다.
attempt 6은 8개 개별 session을 통과했지만 direct current calculation session 차이
`7.516%`, boundary baseline calculation 차이 `6.379%`로 최종 비교가 무효였다.
attempt 7은 성능 session 직전 6개 CPU package-load 표본을 모두 20% 이하로 제한한
단 한 번의 최종 확인 실행이다. 정확도와 8개 개별 session은 다시 통과했지만 boundary
baseline A1/A2의 wall `5.875%`, index `8.873%` 차이로 최종 상태가 다시
`IncompletePerformance`가 됐다. 사전 규칙에 따라 attempt 간 결과를 섞거나 5% 기준을
완화하거나 통과할 때까지 반복하지 않는다.

다음 표는 attempt 7의 60개 pooled raw sample 관찰값이다. 성능 기준선 승인값이 아니며,
실패 원인과 다음 프로토콜을 설계하기 위한 진단값으로만 사용한다.

| workload/metric | baseline median / P95 (ms) | current median / P95 (ms) | current delta |
|---|---:|---:|---:|
| direct wall | 41.607 / 44.871 | 38.022 / 39.573 | -8.616% / -11.807% |
| direct index | 10.924 / 12.539 | 10.592 / 11.555 | -3.039% / -7.849% |
| direct calculation | 30.651 / 33.059 | 27.441 / 28.345 | -10.471% / -14.259% |
| boundary wall | 28.214 / 29.492 | 26.244 / 27.517 | -6.981% / -6.697% |
| boundary index | 11.118 / 11.918 | 11.383 / 12.198 | +2.376% / +2.351% |
| boundary calculation | 17.117 / 17.677 | 14.842 / 15.590 | -13.288% / -11.807% |

allocation은 direct에서 약 8.111MB/operation으로 target 간 동일한 수준이었다. boundary는
baseline 약 7.980MB/operation, current 약 9.159MB/operation이며, 30개 batch의 Gen0 합계도
baseline session `428/421`, current `508/512`로 증가했다. 원시 allocation/GC 값은
보존했지만, 무효인 timing 결과를 성능 개선 또는 회귀 승인 근거로 사용하지 않는다.

재현 절차와 원시 보고서, environment/provenance, rejected readiness window, runner transcript,
comparison JSON은
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260823-fixed-synthetic-baseline`에
보존했다. attempt-7 runner SHA-256은
`5081B088D409870B725219994F640B0BE7EA010C9698013B0381A43E88AA2B34`, 독립 비교기
SHA-256은 `3D45A30A0104A7E4F52E42972EFD8094E368E85F8B482DC64D8F859B0B2604AE`다.

```text
Status: Incomplete
Scope: 두 고정 합성 mesh workload의 historical/current 분석적 정확도 parity와 상대 성능 기준선 시도
Acceptance criteria: 제품 src 변경 0 -> pass; 고정 입력·oracle·exact/quantized parity -> pass; Release build/171 Smoke/원격 CI -> pass; 8개 개별 performance session RMAD < 5% -> pass(attempt 7); 모든 target session median 차이 < 5% -> fail(boundary baseline wall 5.875%, index 8.873%); 승인 가능한 상대 성능 기준선 -> fail
Verification: attempt-7 OFFICIAL_RUN_PROCEDURE.ps1 exit 1 / IncompletePerformance; Compare-OfficialResults.ps1 exit 1; direct·boundary accuracy fingerprints equal; raw 12 report와 8 readiness report의 provenance/hash 검증
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260823-fixed-synthetic-baseline\attempt-7; summary\official-comparison-3f6e35b.json; summary\official-provenance-3f6e35b.json; summary\official-raw-manifest-3f6e35b.json
Boundary / next dependency: v1을 다시 실행해 통과 결과를 고르지 않는다. 다음 성능 작업은 전용·격리 performance host를 제공하거나, 별도 승인을 받아 target을 더 근접하게 교차 계측하는 versioned protocol v2를 설계한 뒤 시작한다. 생산 Track B는 실제 센서 데이터, 교정 ID/hash, 독립 ground truth와 불확도, 생산 공차·오류율·takt 한도 승인 전까지 별도로 Blocked다.
```
