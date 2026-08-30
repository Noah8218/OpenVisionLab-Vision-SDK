# `C*`·`CV*`·`LineGuage` 실제 사용처와 4.0 폐기 설계

작성일: 2026-08-05
대상 제품: OpenVisionLab Vision SDK
제품 경계: UI 독립적인 2D/3D 비전 알고리즘 SDK

> **Active 3.x compatibility policy with historical inventory evidence.** 이
> 문서의 type 수, consumer 검색, Smoke 수치와 artifact 경로는 작성일 당시의
> 조사 기록이다. 3.x 유지 및 별도 승인 없는 4.0 제거 금지 정책은 계속
> 적용되며, 실제 제거 전에는 현재 source와 consumer를 다시 검증해야 한다.
> 현재 프로젝트 상태는
> [`OPENVISIONLAB_CURRENT_STATUS.md`](OPENVISIONLAB_CURRENT_STATUS.md)를 따른다.

## 1. 결론

현재 `C*`, `CV*`, `LineGuage` 공개 API 24개는 3.x에서 유지한다. 이번 조사에서는
소스 삭제, 새 `[Obsolete]` 표시, 패키지 게시, 소비 저장소 변경을 하지 않는다.

24개 모두 현대식 이름의 대체 타입이 존재하지만 바로 삭제해도 된다는 뜻은 아니다.
특히 `Labelling_Application`이 `CLine`, `CFormula`, `CLineVertical`을 실제 제품 코드에서
사용하고 있으며, 세 타입은 체크인된 `Lib.Common.dll`/`Lib.OpenCV.dll` 계약에 묶여 있다.
따라서 소비 저장소의 별도 승인·전환·테스트 없이는 4.0 제거 조건이 충족되지 않는다.

현재 15개 타입에는 이미 `[Obsolete(..., false)]`가 있고 9개에는 없다. 이 문서는
기존 상태를 기록할 뿐 새 경고를 추가하지 않는다. 4.0 제거는 8절의 모든 게이트와
별도 구현 승인을 통과한 뒤 한 번의 major-version 변경으로 수행한다.

초기 문서의 13/11 집계는 `[System.Obsolete(...)]` 형태로 선언된 `CResultCorner`와
`CResultMean`을 검색식이 놓친 오류였다. 소스 재검사에서 두 타입을 포함해 15/9로
정정했으며 API 소스 자체는 변경하지 않았다.

## 2. 조사 기준과 범위

### 2.1 기준

- 저장소: `C:\Git\OpenVisionLab-Vision-SDK`
- 브랜치: `main`
- 기준 HEAD: `829d55f81ed2f8e26fb1f83da1802df44e06de5b`
- 공개 API 증거:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-2d-property-models\final-public-api.txt`
- 공개 API 증거 SHA-256:
  `46AC643EA5BD5FBFA6BF78A76C7CE7786DE88DD16447567ED9FAFF7761A57585`
- 전체 exported type: 332개
- 이번 후보: 서로 다른 top-level 공개 타입 24개

후보 판정은 대소문자를 구분했다. `C*`는 `^C[A-Z]`에서 `CV*`를 분리했고,
`LineGuage`는 오탈자가 포함된 이름을 별도로 합산했다. 따라서 분류 수는 서로 겹칠 수
있지만 후보의 합집합은 정확히 24개다.

### 2.2 읽기 전용 소비 저장소 조사

| 저장소 | 조사 HEAD | 정확한 후보 이름의 현재 소스 사용 |
|---|---|---|
| `OpenVisionLab-Vision-SDK` | `829d55f81ed2f8e26fb1f83da1802df44e06de5b` | 레거시 구현 파일 내부에만 있음 |
| `OpenVisionLab` | `e5ee244ec612868a505e4105fd04729fe6a6aa07` | 0; 과거 분석 문서에만 있음 |
| `OpenVisionLab_Dev` | `6698a83a212641beb6e3c12066ddf41c7d1593a1` | 0; 과거 분석 문서에만 있음 |
| `OpenVisionLab-3D-Studio` | `fb2850ca6c6f7156a56193f971fa4f6c637756d8` | 0 |
| `Labelling_Application` | `18c7474298acb371201d435481d91f415f2f070a` | `CLine`, `CFormula`, `CLineVertical` 사용 |

검색은 `.git`, `bin`, `obj`, `artifacts`, `packages`를 제외하고 C# 소스와 추적 문서·설정
파일을 대상으로 정확한 단어 경계로 수행했다. 이 결과는 위 로컬 clone과 HEAD만
증명한다. 다른 외부 사용자, 게시된 바이너리, 로컬에 없는 recipe 파일까지 사용자가
없다는 뜻은 아니다.

## 3. 24개 공개 타입 목록과 판정

상태의 `기존 경고`는 현재 소스에 이미 `[Obsolete(..., false)]`가 있다는 뜻이다.
`4.0 후보`는 삭제 승인이 아니라 8절 게이트를 통과했을 때 검토할 수 있다는 뜻이다.

| 레거시 공개 타입 | 현대식 대체 타입 | 현재 상태 | 조사 판정 |
|---|---|---|---|
| `CConverter` | `CommonConverter` | 경고 없음 | 공개 멤버 34/34 대응; 4.0 후보 |
| `CFormula` | `FormulaUtil` | 경고 없음; 소비 중 | 공개 멤버 22/22 대응; 소비자 전환 필수 |
| `CLine` | `LineSegment2D` | 경고 없음; 소비 중 | 공개 멤버 10/10 대응; 소비자 전환 필수 |
| `CLineCalculatorFitting` | `LineFittingCalculator` | 경고 없음 | 공개 멤버 12/12 대응; 4.0 후보 |
| `CLineFitting` | `LineFitting` | 경고 없음 | 공개 멤버 2/2 대응; 오류 출력 차이 검증 필요 |
| `CLineVertical` | `VerticalLineCalculator` | 경고 없음; 소비 중 | 공개 멤버 6/6 대응; 소비자 전환 필수 |
| `COpenCVHelper` | `OpenCvHelper` | 경고 없음 | 공개 멤버 8/8 대응; 예외·오류 처리 차이 검증 필요 |
| `COpenCVAlgorithmBase` | `OpenCvAlgorithmBase` | 경고 없음 | 기존 16개 멤버 포함, 현대 타입 4개 추가; 상속 소비자 조사 필요 |
| `CResultBlob` | `BlobResult` | 기존 경고 | 공개 멤버 20/20 대응; 4.0 후보 |
| `CResultContour` | `ContourResult` | 기존 경고 | 공개 멤버 19/19 대응; 4.0 후보 |
| `CResultCorner` | `CornerResult` | 기존 경고 | 공개 멤버 13/13 대응; 4.0 후보 |
| `CResultMatching` | `MatchingResult` | 기존 경고 | 현대 결과가 확장됨; 생성자 metadata 차이 검증 필요 |
| `CResultMean` | `MeanResult` | 기존 경고 | 공개 멤버 13/13 대응; 4.0 후보 |
| `CVBlob` | `BlobTool` | 기존 경고 | 공개 멤버 5/5 대응; 동작 parity 게이트 필요 |
| `CVContour` | `ContourTool` | 기존 경고 | 공개 멤버 8/8 대응; 동작 parity 게이트 필요 |
| `CVCorner` | `CornerTool` | 기존 경고 | 공개 멤버 5/5 대응; 동작 parity 게이트 필요 |
| `CVLineGuage` | `LineGaugeTool` | 기존 경고 | property interface 형식 변경; Line Gauge 묶음으로 전환 |
| `CVMatching` | `MatchingTool` | 기존 경고 | 공개 멤버 9/9 대응; 회전·점수 parity 게이트 필요 |
| `CVMean` | `MeanTool` | 기존 경고 | 공개 멤버 7/7 대응; 동작 parity 게이트 필요 |
| `CVSIFT` | `SiftTool` | 기존 경고 | 현대 타입에 공개 `ConvertRectToRect2f` 없음 |
| `CVLineGuage_Edge` | `LineGaugeEdge` | 기존 경고 | 공개 멤버 12/12 대응; 직렬화 확인 필요 |
| `CVLineGuage_Result` | `LineGaugeResult` | 기존 경고 | `CLine`→`LineSegment2D`, `Index`·count 계약 차이 |
| `CVLineGuage_VerticalLines` | `LineGaugeVerticalLines` | 기존 경고 | `List<CLine>`→`List<LineSegment2D>` 형식 변경 |
| `IOpenCVPropertyLineGuage` | `IOpenCvPropertyLineGauge` | 경고 없음 | 공개 멤버 45/45 대응; 구현체·recipe 확인 필요 |

정규화한 공개 멤버가 같아도 수치 동작, 예외, 로그, 메모리 소유권, 직렬화까지 같다는
증거는 아니다. 특히 현재 SDK 테스트 소스에는 24개 레거시 이름을 직접 사용하는 테스트가
0개다. 현대식 타입은 현재 제품 경로와 Smoke에서 사용되지만 레거시-현대식 parity는
별도 증명해야 한다.

## 4. 실제 사용처

### 4.1 SDK 내부

후보 이름은 선언 파일과 레거시 구현끼리의 의존에서만 검색됐다. 예를 들어 `CVBlob`은
`COpenCVAlgorithmBase`, `COpenCVHelper`, `CResultBlob`을 사용하고, Line Gauge 레거시
구현은 `CLine`과 `CVLineGuage_*` 결과를 사용한다. 현대식 Tool 경로가 이 레거시 타입을
호출하는 역방향 의존은 발견되지 않았다.

따라서 제거 순서는 공유 기반부터 지우는 방식이 될 수 없다. leaf Tool·결과를 먼저
전환하고, 마지막 레거시 Tool까지 없어진 뒤 `COpenCVAlgorithmBase`와 `COpenCVHelper`를
제거해야 한다. Core의 `CLine*` 묶음도 소비자와 레거시 Line Gauge 전환 뒤에 제거한다.

### 4.2 `Labelling_Application`

실제 제품 소스
`Library\Viewer\MeasurementGeometry.cs`는 다음 호출을 포함한다.

- `CLineVertical.GetLineCoef`: 1회
- `CFormula.CrossCheck`와 `CFormula.FindIntersection`: 2회
- `new CLine(...)`: 3회

`OpenVisionLab.LabelingStudio.csproj`는 `dll\Lib.Common.dll`과
`dll\Lib.OpenCV.dll`을 직접 참조한다. 즉, 현재 소비자는 OpenVisionLab 3.0 패키지를
사용하는 상태가 아니며 SDK 저장소에서 타입을 지우는 것만으로 안전한 전환이 되지 않는다.

소비자에는 `TestMeasurementGeometry`가 있고 수평선과 기준점으로부터 수직 교점
`(50, 10)`과 거리 `40`을 확인한다. 나중에 별도 승인을 받아 현대식 타입으로 전환할 때
이 테스트가 최소 회귀 게이트다.

권장 소스 대응은 다음과 같다. 이것은 예시이며 이번 작업에서 소비자 코드를 바꾸지 않는다.

```csharp
using OpenVisionLab.Core;
using OpenVisionLab.Core.Geometry2D;

VerticalLineCalculator.GetLineCoef(...);
FormulaUtil.CrossCheck(...);
FormulaUtil.FindIntersection(...);
double distance = new LineSegment2D(start, end).Distance();
```

### 4.3 다른 로컬 소비자

`OpenVisionLab`, `OpenVisionLab_Dev`, `OpenVisionLab-3D-Studio` 현재 소스에서는 정확한
후보 이름이 0개였다. `OpenVisionLab`과 Dev의 `docs/contracts/library-noah` 아래에는
과거 조사와 계획이 남아 있으나 현재 컴파일 사용처가 아니다.

## 5. 직렬화·reflection·바이너리 위험

조사한 저장소의 추적된 JSON, XML, config, XAML에는 24개 후보 이름이 없었다. 그러나
다음 이유로 직렬화 호환 게이트를 생략할 수 없다.

- 공개 결과·property 타입에는 parameterless 생성자와 settable property가 있어 외부
  `XmlSerializer`/JSON 사용 가능성이 있다.
- `CVLineGuage_Result`와 `CVLineGuage_VerticalLines`는 property 형식 자체가 바뀐다.
- `CResultMatching`은 현대 결과에 점수·scale 속성이 추가되고 생성자 metadata가 다르다.
- `COpenCVAlgorithmBase`를 상속한 외부 타입은 base type 제거 시 컴파일·로딩이 깨진다.
- assembly-qualified type 이름, reflection 문자열, 로컬 recipe는 소스 검색만으로 찾을 수
  없다.
- 3.x DLL로 저장한 XML/JSON이 4.0 타입 이름을 자동으로 알아내지는 않는다.

4.0 후보 빌드 전에는 실제 사용자 recipe/data 루트를 별도로 승인받아 읽기 전용으로
검색하고, 대표 파일의 3.x 저장→4.0 읽기 round trip을 수행한다. 형식 변환이 필요하면
애플리케이션 로더가 명시적인 migration을 소유하며 SDK에 불필요한 이중 namespace나
장기 wrapper를 새로 만들지 않는다.

## 6. 버전별 정책

### 6.1 현재 3.0.x

- 24개 공개 타입을 유지한다.
- 기존 15개 `[Obsolete(..., false)]`를 유지한다.
- 나머지 9개에 새 경고를 추가하지 않는다.
- 문서와 새 예제는 현대식 타입만 사용한다.
- 기존 동작을 정리 목적으로 바꾸지 않는다.

### 6.2 향후 승인된 3.x 준비 릴리스

별도 승인이 있으면 남은 9개의 폐기 경고를 검토할 수 있다. 그 전에 모든 first-party
소비자의 현대식 타입 전환, 직렬화 조사, migration 문서, parity 테스트가 먼저 완료돼야
한다. 경고 메시지는 정확한 대체 타입과 제거 예정 major인 4.0을 명시해야 한다.

### 6.3 4.0

8절을 모두 통과한 후보만 제거한다. 패키지 ID, DLL, namespace를 다시 바꾸는 작업이
아니라 3.x에 남겨 둔 레거시 공개 표면을 정리하는 major-version 변경으로 한정한다.
NuGet 게시는 구현·검증과 별도 승인 단계다.

## 7. 제거 순서 설계

1. **Core 소비자 전환**: `Labelling_Application`의 세 타입을 현대식 API로 바꾸고 기존
   측정 테스트와 실제 빌드를 통과시킨다.
2. **레거시-현대식 parity 고정**: Core geometry, Blob, Contour, Corner, Matching, Mean,
   SIFT, Line Gauge에 대표 입력·실패 입력을 두 구현에 실행해 결과와 오류 계약을 비교한다.
3. **leaf 결과·Tool 제거 후보 확정**: `CResult*`, `CV*`, `CVLineGuage_*`의 외부 사용 0과
   recipe 호환을 증명한다.
4. **공유 기반 제거 후보 확정**: 마지막 레거시 Tool 제거 뒤에만
   `COpenCVAlgorithmBase`와 `COpenCVHelper`를 제거한다.
5. **Core 레거시 묶음 제거 후보 확정**: Line Gauge와 소비자 의존이 모두 끝난 뒤
   `CConverter`, `CFormula`, `CLine*`를 정리한다.
6. **4.0 package-only 검증**: 깨끗한 캐시에서 5개 패키지와 2D/3D 소비자 샘플을
   restore/build/run하고 공개 API 제거 목록이 승인 목록과 정확히 같은지 비교한다.

`LineGuage` 묶음은 오탈자 하나만 고치는 단순 rename이 아니다. 결과의 선 타입과
인덱스/count 계약이 달라 전체 묶음을 마지막 단계에서 함께 전환한다.

## 8. 4.0 제거 승인 게이트

다음 항목 중 하나라도 실패하면 삭제하지 않는다.

- [ ] 제거 대상과 대체 타입의 3.x 공개 API snapshot을 고정했다.
- [ ] SDK 현대식 경로의 build와 Smoke가 통과한다.
- [ ] 제거 대상별 대표 성공·실패·경계 입력 parity가 통과한다.
- [ ] 모든 first-party 소비 저장소에서 정확한 후보 이름이 0개다.
- [ ] `Labelling_Application`이 `OpenVisionLab.Core` 현대식 API로 전환됐고
      `TestMeasurementGeometry` 및 전체 빌드가 통과한다.
- [ ] 외부 상속 타입과 reflection 문자열 조사 범위를 사용자가 승인했고 결과가 0이다.
- [ ] 승인된 실제 recipe/data에서 후보 이름 검색과 3.x→4.0 round trip이 통과한다.
- [ ] `CResultMatching`, `CVSIFT`, Line Gauge의 비대칭 계약에 migration 예제와 검증이 있다.
- [ ] 4.0 공개 API diff의 제거 항목이 승인 목록과 정확히 일치하고 예상 밖 추가·삭제가 0이다.
- [ ] 5개 package-only 소비자가 깨끗한 캐시에서 restore/build/run에 성공한다.
- [ ] 사용자에게 4.0 소스 삭제와 소비 저장소 변경을 각각 별도로 승인받았다.
- [ ] NuGet 게시가 필요하면 게시 승인을 별도로 받았다.

## 9. 이번 작업의 완료 경계

```text
Status: Complete
Scope: 현재 C*·CV*·LineGuage 공개 API 24개 inventory, 로컬 first-party 사용처, API 비대칭, 직렬화 위험과 4.0 제거 게이트 설계
Acceptance criteria: 24개 합집합과 대체 타입 식별 -> pass; SDK/4개 로컬 소비 저장소 조사 -> pass; 실제 소비 코드와 테스트 식별 -> pass; 현재 Obsolete 상태와 API 차이 기록 -> pass; 삭제 중단 조건과 별도 승인 경계 명시 -> pass
Verification: 기준 HEAD 공개 API 332 exported type/후보 24개 재분류; 정확한 이름 검색; 공개 member 정규화 비교; tracked JSON/XML/config/XAML 검색; 저장소별 HEAD 기록; .NET SDK 8.0.423 Release build 0 warnings/0 errors; 격리 산출물 Smoke 142/142
Evidence: docs/LEGACY_C_CV_LINEGUAGE_V4_REMOVAL_PLAN_20260805.md; D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-2d-property-models\final-public-api.txt
Boundary / next dependency: 이번 완료는 조사와 설계만 뜻한다. 소스 삭제, 새 Obsolete 표시, NuGet 게시, 소비 저장소 변경은 하지 않았다. 4.0 구현은 8절 게이트와 별도 사용자 승인이 필요하다.
```

## 10. 대표 parity/characterization 테스트 체크포인트

`OpenVisionLab.Inspection.Smoke`에 `LegacyApiCompatibilitySmokeSuite` 12개 case를
추가했다. 후보 24개 이름을 테스트 코드에서 모두 직접 참조하고 동일 합성 입력으로
공유 계약을 비교한다.

확인된 parity는 다음과 같다.

- Core 변환·수식·선 모델·피팅·수직선 계산
- `COpenCVAlgorithmBase`와 `OpenCvAlgorithmBase`의 지원되는 source/result 상태
- Blob, Contour, Corner, Matching, Mean, Line Gauge 결과 DTO의 공유 필드
- Blob, Contour, Mean, Matching 회전·합성 template 검색, Line Gauge 합성 edge 실행
- SIFT의 `Point2f`→`Point2d` 변환

다음 세 항목은 parity가 아니라 제거 전에 결정해야 할 현재 비대칭으로 테스트에
명시했다.

- `COpenCVHelper.IsMatEmpty(null)`은 오류를 기록하고 `false`를 반환하지만
  `OpenCvHelper.IsMatEmpty(null)`은 `NullReferenceException`을 던진다.
- `CVCorner`는 합성 사각형을 결과 이미지에 그리지만 `results`를 채우지 않고,
  `CornerTool`은 corner DTO를 게시한다.
- 현재 포함된 native DLL에서 `CVSIFT`는 `features2d_SIFT_create` 진입점 실패를
  내부 로그로만 남긴다. `SiftTool`은 ORB로 fallback한 뒤 빈 영상에
  `FeatureNoKeypoints`를 반환한다.

이 체크포인트는 대표 입력에 대한 회귀 증거다. 8절의 모든 성공·실패·경계 입력,
외부 recipe, 상속/reflection, 소비 저장소와 4.0 package-only 게이트를 완료했다는 뜻은
아니므로 제거 체크박스는 그대로 유지한다.

```text
Status: Complete
Scope: 레거시 후보 24개를 직접 참조하는 대표 parity/characterization Smoke 12개와 Obsolete 집계 15/9 정정
Acceptance criteria: 후보 24/24 직접 참조 -> pass; Core/DTO/7개 2D Tool family 대표 계약 실행 -> pass; 발견한 비대칭을 거짓 parity로 처리하지 않음 -> pass; 제품 src/API 변경 0 -> pass
Verification: .NET SDK 8.0.423 full solution Release build 0 warnings/0 errors; OpenVisionLab.Inspection.Smoke 154/154; production src diff 0
Evidence: tests/OpenVisionLab.Inspection.Smoke/Suites/LegacyApiCompatibilitySmokeSuite.cs; D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260805-legacy-api-parity
Boundary / next dependency: 대표 회귀만 증명한다. API 삭제·새 Obsolete·NuGet 게시·소비 저장소 변경은 없으며 8절의 4.0 제거 게이트는 아직 완료되지 않았다.
```
