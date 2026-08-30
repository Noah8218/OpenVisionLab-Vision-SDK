# 3D 소비자 API 사용성 및 정식 배포 통합 계획

> **Historical record — 2026-08-04.** 이 문서는 Library-Noah 2.8/2.9 통합
> 당시의 관찰, 계획, 명령, 수치와 artifact 경로를 보존한다. 아래 `Complete`는
> 당시 작업 slice에만 적용되며 현재 OpenVisionLab Vision SDK 상태나 설치 버전을
> 뜻하지 않는다. 현재 권위는
> [`OPENVISIONLAB_CURRENT_STATUS.md`](OPENVISIONLAB_CURRENT_STATUS.md)이다. 현재
> solution과 smoke entry point는 `OpenVisionLab.VisionSdk.sln` 및
> `tests/OpenVisionLab.Inspection.Smoke/OpenVisionLab.Inspection.Smoke.csproj`다.

Document status: Historical completion record

## 목적

Library-Noah의 3D 계산 계약은 단위, 좌표 프레임, 결측값, 유효 샘플 수와
커버리지를 명시적으로 검증한다. 이 안전 계약을 유지하면서, 외부 C# 개발자가
2D Tool과 비슷한 수준으로 설치 경로와 실행 결과를 이해할 수 있도록 공개 소스,
NuGet 패키지와 문서를 하나의 정식 기준으로 맞춘다.

OpenVisionLab 3D Studio는 이 라이브러리를 검증하는 소비 애플리케이션이다. Studio의
어댑터, 레시피, ROI, Preview/Run, 오버레이와 화면 계약은 Library-Noah로 이동하지
않는다. Library-Noah는 UI와 센서에 독립적인 수치 계산 및 입력/결과 계약을 계속
소유한다.

## 2026-08-04 관찰 결과

### 사용성 평가

| 평가 항목 | 2D | 현재 3D | 판단 |
| --- | ---: | ---: | --- |
| 최초 실행 난이도 | 8/10 | 5/10 | 3D는 격자, 단위, 프레임과 결측 정책을 추가로 알아야 한다. |
| API 일관성 | 7/10 | 4/10 | Height-map 검사, source-neutral Tool과 치수 검사의 결과/오류 방식이 다르다. |
| 입력 오류 방지 | 7/10 | 9/10 | 명시적 계약과 fail-closed 검증은 유지해야 한다. |
| 결과 해석 직관성 | 7/10 | 5/10 | `Success`와 `HasMeasurement` 조합 및 문자열 metric key가 추가 설명을 요구한다. |
| 패키지 문서 발견성 | 7/10 | 3/10 | 현재 패키지에는 README와 XML IntelliSense 문서가 없다. |

점수는 코드, 공개 문서, 패키지 내용과 Studio 어댑터를 비교한 정성 평가이며 성능
또는 계측 정확도 점수가 아니다.

### 배포 기준 불일치

- GitHub `main`은 `4a21fb4`이며 공통 패키지 버전은 `2.8.0`이다.
- 저장소에 생성되어 있던 `Lib.ThreeD.2.8.0.nupkg`는 `f8f0eff` 기준이라 현재
  README의 분리 단위 생성자와 `HeightMapInputRequirements`를 포함하지 않는다.
- OpenVisionLab 3D Studio는 `21f2e3084843ef8a499e6fe02c4326a19813aa2c`에서
  생성한 `Lib.ThreeD 2.8.13`과 SHA-256
  `852B5A959A3DD76AF69A7C295CEAC77E13F72BBB969A79FC48D88A83B9D8229D`를
  고정 사용한다.
- `2.8.13` 계열은 다수의 검증된 3D Tool을 포함하지만 아직 `main`과 하나의
  공개 배포 기준으로 합쳐지지 않았다.

따라서 새 사용자는 GitHub 소스, README와 Studio에서 검증된 패키지 중 무엇이
정식 API인지 즉시 판단할 수 없다.

### 공개 API의 세 가지 사용 방식

1. 2D Tool: `Mat -> Property -> Execute -> VisionToolResult`
2. Height-map 검사: `HeightMap3D -> Options -> IThreeDInspectionTool -> ThreeDInspectionResult`
3. Full-XYZ/다중 입력 Tool: Tool별 typed input/options/result

`IThreeDInspectionTool`은 단일 `HeightMap3D` 입력을 받는 좁은 계약이다. Matching,
mesh, 다중 영역과 다중 surface Tool을 이 인터페이스에 강제로 맞추지 않는다.
대신 공개 문서에서 세 계층과 오류 계약을 분명히 구분한다.

## 작업 계약

### 사용자 목표

평가 내용을 개발문서에 남기고, 우선순위에 따라 3D 정식 배포 기준과 소비자
사용성을 개선한다.

### 비협상 요구사항

- `main`의 2D 정확도, native Mat 소유권과 edge polarity 변경을 보존한다.
- Studio가 검증한 `Lib.ThreeD 2.8.13`의 공개 API와 계산 동작을 보존한다.
- 단위 자동 변환, 별칭 추론, 좌표 프레임 추론 또는 자동 변환을 추가하지 않는다.
- `NaN`을 자동 보간하거나 Infinity를 유효 샘플로 허용하지 않는다.
- 기존 생성자, Tool, 옵션과 결과 멤버를 제거하거나 의미 변경하지 않는다.
- UI, 센서 파서, 레시피와 Studio 전용 어댑터를 Library-Noah에 추가하지 않는다.

### 책임 구조

현재:

```text
GitHub main 2.8.0 + 별도 3D 2.8.13 브랜치/패키지
                    -> Studio 전용 고정 패키지
```

목표:

```text
Library-Noah 단일 정식 소스/버전
    -> 동일 커밋에서 생성한 Lib.ThreeD NuGet
        -> 독립 소비자와 Studio 어댑터
```

수치 알고리즘과 source-neutral 입력/결과는 `Lib.ThreeD`가 소유한다. 실제 source
identity, 교정 증거, 레시피, UI lifecycle과 표시 결과는 소비 애플리케이션이
소유한다.

## 우선순위 및 완료 조건

### 1. 정식 배포 기준 통합

- `2.8.13` 계열과 `main`의 2D 변경을 한 브랜치에서 통합한다.
- 공통 패키지 버전을 새 정식 버전으로 올린다.
- 동일 소스에서 Release build, Smoke와 pack이 통과해야 한다.
- 생성된 패키지 repository commit, 버전과 공개 API가 소스와 일치해야 한다.

### 2. 최소 소비자 API 사용성 보강

- 기존 의미를 바꾸지 않는 파생 measurement outcome을 제공한다.
- 자주 사용하는 공통 metric은 문자열을 직접 작성하지 않아도 접근할 수 있게 한다.
- 2차원 배열을 명시적 단위/프레임 계약과 함께 안전하게 `HeightMap3D`로 만드는
  최소 팩토리를 제공한다.
- 저수준 다중 입력 Tool을 억지로 height-map 인터페이스에 통합하지 않는다.

### 3. 패키지와 문서 보강

- 패키지에 README와 XML IntelliSense 문서를 포함한다.
- README Quick Start가 생성된 패키지만 참조하는 독립 소비 프로젝트에서 컴파일된다.
- 도구 선택표에서 Height-map 검사, source-neutral primitive와 다중 입력 치수 검사를
  구분한다.
- 입력 불일치, 공차 불합격과 측정 불가의 처리 예를 제공한다.

## 검증 계획

- `git` 검색으로 양쪽 브랜치의 기존 소유권과 변경 보존을 확인한다.
- `dotnet build Lib.Common.sln -c Release`를 실행한다.
- `dotnet run --project Lib.Inspection.Smoke/Lib.Inspection.Smoke.csproj -c Release --no-build`를 실행한다.
- 동일 커밋에서 `dotnet pack Lib.Common.sln -c Release --no-build`를 실행한다.
- 패키지 내부의 DLL, XML 문서, README와 nuspec repository commit을 검사한다.
- D: 드라이브의 독립 임시 소비 프로젝트에서 README 2D/3D 예제를 컴파일하고 실행한다.
- 제거되어야 할 이전 버전 표기, 누락된 도구 문서와 직접 문자열 metric 접근을 검색한다.

## 범위 경계

- 합성 Smoke는 실제 센서, 교정, Gauge R&R 또는 생산 승인 증거가 아니다.
- 이번 작업은 OpenVisionLab 3D Studio UI의 사람 대상 사용성 시험을 대체하지 않는다.
- Studio의 고정 패키지 갱신은 패키지 경계 파일에만 적용했으며 UI와 Viewer 코드는
  변경하지 않았다.

## 완료 기록

`2.9.0`은 최신 2D mainline과 검증된 3D 입력 계약을 하나의 배포 기준으로 통합한다.
패키지 소스 커밋은 `9fdce9b2d4714d7cb7aa082a10b7afe217896e71`, SHA-256은
`2D8DCF71B9200289D67C27EFF2A7508CE7A5A3FD377C8E4891B467FC3CA1DF23`이다.

```text
Status: Complete
Scope: 2D/3D 배포 기준 통합, Lib.ThreeD 2.9.0 소비자 API·문서 개선, Studio 고정 패키지 승격
Acceptance criteria: 2.8.13 공개 API 호환 -> pass; 간편 배열 입력/명시적 결과/안전한 metric 접근 -> pass; README·3D 가이드·XML 문서 패키징 -> pass; Studio 고정 버전·소스·해시 일치 -> pass
Verification: Library Release 0/0, Smoke 135/135, package-only 2D/3D consumer pass, 2.8.13 대비 missing public types 0/members 0; Studio package verifier pass, Release 0/0, bridge 25/25, Thickness/Warpage/Datum 5/5 each, structure 29/29, NuGet 12/0/0
Evidence: D:\OpenVisionLab-TestData\Library-Noah\final-2.9.0; D:\OpenVisionLab-TestData\OpenVisionLab-3D-Studio\20260804-library-noah-2.9.0; OpenVisionLab-3D-Studio/docs/OPENVISIONLAB_3D_LIBRARY_NOAH_PACKAGE_BOUNDARY_20260717.md
Boundary / next dependency: 합성·패키지 검증은 물리 교정, 센서 획득 매핑, Gauge R&R, 생산 승인 또는 사람 대상 Studio UX 증거가 아니다. Studio의 다음 제품 우선순위는 B-12 획득/source provenance와 제한 문구다.
```
