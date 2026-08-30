# Matching 책임 경계와 생산 기준선 계획

작성일: 2026-08-21

> **Active responsibility boundary with historical Track A evidence.** Track A의
> 완료 수치, package 버전과 artifact 경로는 2026-08-21 기록이며 현재 source
> 검증 결과가 아니다. Track B는 아래 입력 prerequisite가 확보될 때까지 미완료다.
> 현재 프로젝트 상태와 우선순위는
> [`OPENVISIONLAB_CURRENT_STATUS.md`](OPENVISIONLAB_CURRENT_STATUS.md)를 따른다.

## 1. 목표와 경계

이 문서는 두 작업을 분리한다.

1. Track A: 기존 observable behavior를 바꾸지 않고 Matching 계층의 책임을 내부 소유자 단위로 분리한다.
2. Track B: 실제 센서 데이터, 교정, ground truth와 생산 공차가 확보된 뒤 정확도·성능 기준선을 수립한다.

Track A는 알고리즘 개선이나 임계값 튜닝이 아니다. Track B가 완료되기 전에는 합성 Smoke 결과를
생산 정확도, metrology 적합성, Gauge R&R 또는 생산 승인으로 표현하지 않는다.

## 2. Track A 비협상 계약

- 공개 타입, property, method와 `MatchingResult` 계약을 삭제하거나 변경하지 않는다.
- 결과 개수·순서·index, ROI 전역 좌표, angle/scale/score, polarity, 오류 코드/메시지,
  diagnostics/metrics/overlay, `Mat` 소유권과 dispose 동작을 유지한다.
- 병렬 검색의 tie-break와 반복 실행 결정성을 유지한다.
- 기존 Smoke 154개는 이름과 실행 순서를 바꾸지 않고 그대로 선행 실행한다.
- 구조 검증을 위해 검사를 추가할 수 있지만 기존 검사를 삭제·완화·rename하지 않는다.
- 수치 변화, search-space 축소, 새 default/validation/coercion은 별도 승인 없이는 포함하지 않는다.

## 3. 실제 책임 소유자와 호출 경로

| 공개 façade | 내부 소유자 | 소유 책임 |
|---|---|---|
| `MatchingTool` | `MatchingSearchEngine` | 전처리, ROI 실행, angle/scale 열거, exhaustive/coarse-to-fine/pyramid 검색, refinement, suppression와 결과 dedup |
| `MatchingTool` | `RotatedTemplateCache` | 회전, content hash, LRU, byte/entry budget, lease와 eviction/dispose |
| `EdgeBasedTemplateMatchingTool` | `EdgeTemplateModelStore` | 원본 template, revision, prepared/model/scaled/rotated cache 생성과 수명 |
| `EdgeBasedTemplateMatchingTool` | `EdgeCandidateSearch` | angle/scale/pyramid 제안, 병렬 위치 검색, edge scoring, seed 병합, subpixel과 polarity 계산 |
| `EdgeBasedTemplateMatchingTool` | `EdgeHybridCandidateSearch` | image proposal, angle proposal, descriptor/image verification과 hybrid 후보 선택 |
| `EdgeBasedTemplateMatchingTool` | `EdgeMatchDecision` | unique-match 대안 거리/score margin 계산, success/ambiguous/no-match 판정 |
| `AutoMPointTool` | `AutoMPointCandidateAnalyzer` | 후보 생성, feature prefilter, Edge matcher 평가, synthetic/representative 검증, 최종 ranking과 결과 image 작성 |

의존 방향은 공개 façade에서 해당 내부 소유자로 향한다. `AutoMPointCandidateAnalyzer`는 기존과 같이
공개 `EdgeBasedTemplateMatchingTool` 계약을 사용하며 Edge 검색 내부 타입에 직접 의존하지 않는다.
새 interface, factory, DI container, partial 파일은 추가하지 않는다.

대표 호출 경로는 다음과 같다.

```text
MatchingTool.Run
  -> MatchingSearchEngine.Run
     -> RotatedTemplateCache

EdgeBasedTemplateMatchingTool.Run
  -> EdgeTemplateModelStore
  -> EdgeCandidateSearch
  -> EdgeHybridCandidateSearch
  -> EdgeMatchDecision
  -> public results / diagnostics / drawing

AutoMPointTool.Run
  -> AutoMPointCandidateAnalyzer.Run
     -> EdgeBasedTemplateMatchingTool public API
```

## 4. Characterization 기준

기존 154개 뒤에 다음 7개를 추가해 총 161개로 관리한다.

1. single ROI의 전역 좌표
2. multi ROI 순서와 result index
3. `CCoeffNormed`, `CCorrNormed`, `SqDiffNormed` 위치 parity
4. exhaustive와 coarse-to-fine angle 결과 parity
5. exhaustive와 pyramid position proposal 결과 parity
6. scale search의 위치와 scale
7. 같은 크기의 template 교체 시 cache invalidation과 반복 결정성

구조 완료 조건은 새 내부 타입이 실제 호출 경로에 있고, 이전 façade가 이동한 상태/수식을 직접
소유하지 않으며, 공개 API 차이 0, Release build 경고/오류 0, Smoke 161/161, package-only 소비자
실행 통과다.

## 5. Track B 시작 전에 필요한 사용자 결정

다음 값이 없으면 생산 기준선 작업은 `Blocked`다.

- 승인할 알고리즘과 recipe 버전
- 센서 모델/serial/firmware, lens, exposure/gain, lighting, trigger와 acquisition mode
- calibration 종류, 적용 좌표계/단위, calibration ID와 파일 hash, 유효 기간
- 부품 family, lot/fixture/operator/environment와 검사 defect/feature 정의
- 독립 ground-truth 장비·절차·단위·불확도·측정 반복 수
- 각 출력의 LSL/USL 또는 허용 오차
- false accept/false reject 목표와 ambiguous/no-match 처리 정책
- cold/warm takt, median/P95/P99, 메모리와 처리 장비 기준

## 6. 데이터 저장과 manifest 계약

운영 데이터와 파생 증거의 기본 물리 위치는 다음과 같다.

```text
D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\production-baseline\
  raw\
  ground-truth\
  manifests\
  derived\
  reports\
```

SDK 저장소에는 센서별 parser를 추가하지 않는다. 별도 benchmark harness가 입력을 `Mat`,
`HeightMap3D` 또는 XYZ로 정규화하되 원본 파일과 provenance를 보존한다.

최소 manifest 예시는 다음과 같다.

```json
{
  "sampleId": "lot07-part0042-frame03",
  "sourcePath": "raw/lot07/part0042/frame03.tiff",
  "sourceSha256": "<sha256>",
  "partFamily": "<family>",
  "lot": "lot07",
  "sensor": { "model": "<model>", "serial": "<serial>", "firmware": "<version>" },
  "acquisition": { "exposureUs": 0, "gain": 0, "lighting": "<id>", "fixture": "<id>" },
  "calibration": { "id": "<id>", "sha256": "<sha256>", "frameId": "<frame>", "unit": "mm" },
  "recipe": { "id": "<id>", "version": "<immutable version>", "sha256": "<sha256>" },
  "groundTruth": { "value": 0, "unit": "mm", "method": "<CMM/gauge/reference>", "uncertainty": 0 },
  "tolerance": { "lsl": 0, "usl": 0 },
  "conditionTags": ["nominal"]
}
```

모든 source, calibration, recipe, ground-truth 파일은 SHA-256으로 고정한다. manifest 변경은 새
dataset version으로 취급한다.

## 7. 데이터 분할과 누출 방지

- development: algorithm/threshold 선택과 오류 분석에만 사용한다.
- locked validation: threshold를 고정한 뒤 blind 실행한다.
- challenge: 공차 경계, 다른 lot, 조명/초점/blur/pose/scale/polarity/occlusion/noise,
  결측과 온도/fixture/operator 변동을 포함한다.
- 같은 part 또는 같은 연속 acquisition burst가 서로 다른 split에 들어가지 않게 part/lot 단위로 나눈다.
- 실패한 locked validation에 맞춰 threshold를 바꾸면 기존 validation은 development가 되고 새 blind set이 필요하다.

표본 수는 목표 오류율에서 역산한다. 예를 들어 false accept 0건일 때 95% 상한을 근사하는
rule of three는 `3/N`이다. 관련 negative 3,000개에서 0건이어야 약 0.1% 상한을 주장할 수 있다.
조건별 표본 수와 confidence interval 없이 전체 평균만 보고하지 않는다.

## 8. Ground truth와 지표

2D ground truth는 교정된 좌표/각도/scale 또는 독립 reference를 사용한다. 3D는 CMM, gauge block,
독립 측정기 또는 추적 가능한 reference를 사용하며 label audit와 반복 측정 불확도를 기록한다.
SDK 오차와 ground-truth 장비 불확도를 혼합해 숨기지 않는다.

2D 기준 지표:

- position px/mm, angle, scale error
- precision/recall, false accept/reject, ambiguous/no-match
- uniqueness margin 분포와 조건별 성능
- cold/warm median/P95/P99, memory/cache와 반복 결정성

3D 기준 지표:

- bias, MAE, RMSE, max absolute error
- repeatability standard deviation와 6-sigma
- tolerance 경계 confusion, `NotMeasured`/coverage
- sensor/lot/fixture 조건별 결과, runtime/memory
- 필요한 경우 별도 Gauge R&R

## 9. 실행 순서와 중단 게이트

1. 고유 package version, commit과 public API/Smoke 기준선을 고정한다.
2. 같은 머신·입력·recipe로 리팩터링 전후 package를 실행한다.
3. 결과 JSON, 좌표/점수/오류/진단/overlay와 입력·출력 image hash를 비교한다.
4. behavior-preserving 리팩터링에서는 설명되지 않은 차이를 0으로 요구한다.
5. Track B 입력 계약을 승인하고 development/validation/challenge manifest를 고정한다.
6. development에서만 threshold를 선택하고 recipe version을 고정한다.
7. locked validation과 challenge를 blind 실행한다.
8. package/commit/SHA, dataset/manifest hash, sensor/calibration, recipe, ground-truth uncertainty,
   지표, 실패 사례와 적용 범위를 최종 report에 기록한다.

다음 중 하나면 중단한다.

- 공개 API, 결과 순서/좌표/score, 오류/diagnostic, 결정성 또는 소유권 회귀
- 기존 154개 중 하나라도 실패하거나 새 characterization 실패
- 원인을 설명하지 못한 성능 저하
- calibration/ground-truth/tolerance/provenance 누락 또는 split 누출
- locked validation을 본 뒤 같은 validation에 맞춘 threshold 변경

전체 Smoke wall-clock은 시작 비용과 suite 증가 영향을 받으므로 알고리즘 성능 승인 지표가 아니다.
동일한 고정 workload의 반복 측정 노이즈가 5% 미만일 때만 median 10% 또는 P95 15% 증가를
리팩터링 경보로 사용한다. 생산 takt 합격선은 Track B의 장비와 recipe 계약에서 별도로 정한다.

## 10. 완료 기록

```text
Status: Complete
Scope: 기존 Matching/EdgeBased/AutoMPoint 공개 동작을 유지한 내부 책임 분리, 기존 154개 보존과 Matching characterization 7개 추가, Track B 데이터 계약 문서화
Acceptance criteria: 내부 소유자 7개와 façade 호출 경로 -> pass; 기존 154개 이름/순서 보존 및 전체 161/161 -> pass; 공개 API 삭제/서명 변경 0 -> pass; Release build 경고/오류 0 -> pass; 고유 commit package 5개 version/commit/README/XML/내부 의존성 -> pass; 격리 package-only 소비 -> pass
Verification: dotnet build OpenVisionLab.VisionSdk.sln -c Release -> 0 warnings/0 errors; Release smoke 161/161을 5회 실행 -> 모두 pass; dotnet pack 3.0.1-dev.20260821.matching.2 -> 5 packages; packed-only isolated restore/build/run -> pass; rg 구조/호출 경로와 git 공개 surface diff 점검 -> pass
Evidence: D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\20260821-matching-responsibility-refactor
Input / source: baseline 4fd838667270308547241c2ae4f03c1f208526de; implementation a535c51206a4a762afbec9291b7645da0d5014f3; committed-packages.json에 5개 SHA-256 기록
Boundary / next dependency: 전체 Smoke 중앙값 2,440.591ms는 154개 기준선과 161개 현재 suite 구성이 달라 성능 합격 비교값이 아니다. Track B는 실제 센서/설정, calibration ID와 hash, part/recipe, 독립 ground truth와 불확도, LSL/USL, false accept/reject 및 takt 기준 승인 전까지 Blocked다. NuGet 게시와 소비 저장소 변경은 수행하지 않았다.
```
