# OpenVisionLab Vision SDK

C# 애플리케이션에 2D 검사 도구와 높이 맵 / 전체 XYZ 3D 알고리즘을 연결하세요.

[![Source v3.2.0-rc.5](https://img.shields.io/badge/source-v3.2.0--rc.5-blue)](Directory.Build.props) [![Libraries .NET Standard 2.0](https://img.shields.io/badge/libraries-.NET_Standard_2.0-512BD4)](src/OpenVisionLab.Vision3D/OpenVisionLab.Vision3D.csproj) [![Native runtime Windows x64](https://img.shields.io/badge/native_runtime-Windows_x64-0078D6)](src/OpenVisionLab.Core/OpenVisionLab.Core.csproj)

[English](README.md) · **한국어**

SDK는 이미지·측정값·결과 근거를 반환합니다. 영상 수집, 화면 표시, ROI 편집,
레시피 저장과 최종 설비 판정은 사용하는 애플리케이션이 담당합니다.

## 짧은 검사 예제

```csharp
using System;
using OpenCvSharp;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using Mat source = new Mat(16, 16, MatType.CV_8UC1, new Scalar(100));
using ThresholdTool tool = new ThresholdTool();
tool.SetProperty(new ThresholdToolProperty { Threshold = 50 });
using VisionToolResult result = tool.Execute(source);
if (!result.Success) throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
Console.WriteLine($"{result.ResultImage.Width}x{result.ResultImage.Height}");
```

관리형·네이티브 의존성을 설정한 뒤의 예상 출력은 `16x16`입니다.
기본 이진화 설정은 이 상수 입력을 흰색 이미지로 변환합니다.
입력 `source`는 호출자, 출력 이미지는 결과 객체 소유이며 결과와 도구를 함께 해제합니다.

## 세 단계로 시작하기

1. **Windows x64 환경을 준비합니다.** 소스 빌드는 [global.json](global.json)의
   .NET SDK `8.0.423`을 선택하며 검증 스크립트에는 PowerShell 7이 필요합니다.
   라이브러리 대상은 `netstandard2.0`입니다. 빌드에 사용하는 SDK 버전과
   소비 애플리케이션의 대상 프레임워크는 구분합니다.
2. **필요한 모듈을 참조합니다.** 선택한 피드의 정확한 불변 패키지 버전을 사용하고
   SDK 패키지를 호환되는 같은 버전으로 맞춥니다.
   [Vision2D 패키지 안내](src/OpenVisionLab.Vision2D/README.md)부터 확인하세요.
   소스를 참조한다면 [관리형·네이티브 참조 예제](docs/sdk-reference.md#installation-and-references)를 따릅니다.
   프로젝트 참조만으로는 OpenCvSharp 런타임 전체가 제공되지 않습니다.
3. **실행하고 결과를 확인합니다.** 위 메모리 입력 예제 다음으로
   [첫 검사 흐름](#first-inspection-example) 또는
   [3D 시작 예제](docs/sdk-reference.md#3d-quick-start)를 따라갑니다.
   오류·지표·단위·소유권을 확인한 뒤 애플리케이션의 판정과 연결합니다.

이 체크아웃의 소스 버전은 **`3.2.0-rc.5`**이며 [Directory.Build.props](Directory.Build.props)에
정의된 사전 버전입니다. CLR 어셈블리 식별 버전은 `3.0.0.0`을 유지합니다.
소스 버전이 해당 NuGet 패키지나 바이너리 릴리스의 게시를 뜻하지는 않습니다.
기존 애플리케이션을 업데이트하기 전에 [버전 이력](docs/sdk-reference.md#version)과
[2.9.1 → 3.0 마이그레이션 안내](docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md)를 읽으세요.

## 핵심 기능

<a id="blobtool"></a>

| 필요한 기능 | 모듈과 계약 |
| --- | --- |
| 이진화·필터·엣지·윤곽선·매칭·LineGauge·QR 판독 | [Vision2D](src/OpenVisionLab.Vision2D/README.md) |
| 연결 객체와 면적 필터링 | [Vision2D.Blob](src/OpenVisionLab.Vision2D.Blob/README.md) |
| 높이 맵 측정·전체 XYZ 기하·필터·정합·표면/메시 도구 | [Vision3D](src/OpenVisionLab.Vision3D/README.md) · [도구 목록](docs/three-d-inspection.md#public-tool-catalog) |
| 원래 결과를 보존하는 2D·단일 높이 맵 순차 검사 | [Inspection](src/OpenVisionLab.Inspection/README.md) |
| 좌표·직선 계산과 OpenCV 런타임 파일 | [Core](src/OpenVisionLab.Core/README.md) |
| 선택형 CUDA 세션과 실행별 CPU 전환 근거 | [Compute](src/OpenVisionLab.Compute/README.md) |

기본 실행은 CPU입니다. 선택형 CUDA는 일부 엣지 기반 매칭 점수와
결정적 3D Median/LocalMedian 연산에 적용됩니다. Normal 준비, Scharr와
3D 표면 자세 검색은 CPU를 유지합니다. 호환되는 CUDA 네이티브 모듈은 별도이며
장치 탐색 성공만으로 가속을 입증하지 않습니다. 실행 결과의 `AccelerationApplied`와
`FallbackReason`, [Compute 계약](src/OpenVisionLab.Compute/README.md#local-operations)을 확인하세요.

## 검사 흐름

```text
Host 입력 수집 → 설정/학습 → 명시적 실행
    → 오류·지표·근거 확인 → Host 판정과 표시 → 해제
```

2D는 Tool을 설정하고 `Execute(Mat)`을 호출합니다. 호출자 입력을 보존하고
`VisionToolResult`를 해제하기 전에 사용합니다. 같은 인스턴스의 설정·실행·해제는
직렬로 처리합니다. [Pipeline XML](docs/sdk-reference.md#pipeline-usage)은 설정을 담으며
파일·데이터베이스·저장물 해석은 호스트가 담당합니다.

3D는 [3D 계약](docs/three-d-inspection.md)에 따라 높이 맵, 전체 XYZ 형식 입력,
다중 입력 측정 중 알맞은 경로를 선택하고 단위·좌표계·유효 비율을 선언합니다.
높이 맵의 `NotMeasured`와 형식별 도구의 `Success`/`Passed`는 의미가 다릅니다.
다중 입력·메시 도구는 `IThreeDInspectionTool`을 거치지 않고 직접 실행합니다.

<a id="first-inspection-example"></a>

### 첫 검사 예제

[패키지만 참조하는 콘솔 예제](samples/OpenVisionLab.FirstInspection/Program.cs)는
학습·기준점 위치·이동된 ROI·존재 검사·픽셀 엣지 측정을 연결합니다.
순서는 **prepare → teach → inspect → load → inspect 재실행**입니다.
정확한 패키지 버전·피드와 [전체 명령·결과·종료 코드 계약](docs/sdk-reference.md#first-inspection-example)을 사용하세요.
`load`는 설정을 검증·복원하며 검사나 CUDA 탐색을 하지 않습니다.
예제의 `Accepted`는 한정된 근거이며 최종 설비 판정은 애플리케이션 책임입니다.

<a id="qr-configure-restore-and-explicit-inspection"></a>

같은 예제의 [QR 흐름](docs/sdk-reference.md#qr-configure-restore-and-explicit-inspection)은
**configure → load → inspect**에 위치 보정 ROI·기대 문자열·실패 후 복구를 더합니다.
QR 판독 성공과 호스트의 문자열 비교 결과는 구분합니다.

## 소스 빌드

<a id="clone-and-develop-on-another-pc"></a>

Windows x64의 짧은 체크아웃 경로와 위에서 지정한 도구를 사용합니다.
저장소 루트에서 소스 트리 밖의 출력 경로를 선택하세요.

```powershell
dotnet --version
dotnet tool restore
$outputRoot = Read-Host "Absolute directory for build output"
dotnet build OpenVisionLab.VisionSdk.sln -c Release --artifacts-path "$outputRoot/build"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$smoke = "$outputRoot/build/bin/OpenVisionLab.Inspection.Smoke/release/OpenVisionLab.Inspection.Smoke.dll"
dotnet $smoke
```

스모크 실행 파일은 합성 입력의 계약·회귀를 검사합니다. Windows Server에서는
번들 OpenCV 런타임이 사용하는 Media Foundation도 필요합니다.
집중·API·패키지 검사는 [전체 소스 준비 안내](docs/sdk-reference.md#clone-and-develop-on-another-pc),
[개발자 Start Here](docs/README.md#start-here), [CI 구성](.github/workflows/build.yml)을 따릅니다.
CI는 검사를 정의하며 패키지를 게시하거나 릴리스를 만들지 않습니다.

## 상세 문서

<a id="2d-quick-start"></a>
<a id="packaging-notes"></a>

| 주제 | 안내 |
| --- | --- |
| 2D Canny·3D 두께 예제 | [2D 시작](docs/sdk-reference.md#2d-quick-start) · [3D 시작](docs/sdk-reference.md#3d-quick-start) |
| 도구·속성·Pipeline·오버레이·네이티브 이미지 수명 | [SDK 참조](docs/sdk-reference.md#2d-tool-execution-model) |
| 입력·채널·ROI·오류·취소·복구 계약 | [2D 계약](src/OpenVisionLab.Vision2D/README.md#input-and-option-contracts) · [3D 계약](docs/three-d-inspection.md#execution-failure-and-lifetime-boundaries) |
| 매칭과 객체 근거 | [Auto MPoint](docs/AUTO_MPOINT_V1.md) · [고유 엣지 매칭](docs/EDGE_BASED_UNIQUE_MATCH_V1.md) · [Blob/Contour 후보](docs/OBJECT_CANDIDATE_CONTRACT.md) |
| 버전 이력·프로젝트 구조·레거시 API·패키징 | [전체 참조](docs/sdk-reference.md) · [패키징 안내](docs/sdk-reference.md#packaging-notes) |
| 빌드·네이티브 의존성·이전 | [소스 준비](docs/sdk-reference.md#clone-and-develop-on-another-pc) · [참조 설정](docs/sdk-reference.md#installation-and-references) · [마이그레이션](docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md) |

모듈별 README는 각 프로젝트에 그대로 두며 해당 패키지에 포함됩니다.
[문서 색인](docs/README.md)의 기존 개발자 읽기 순서도 유지합니다.
상세 참조에는 기존 루트 README의 API·빌드·마이그레이션·버전 정보를 보존합니다.

## 한계와 검증 범위

- 네이티브 런타임의 주 환경은 Windows x64입니다. `netstandard2.0`이 모든 OS·호스트
  지원을 입증하지는 않습니다. 기존 패키지 소비자 검사는 .NET 8과 .NET Framework 4.8/x64를 다룹니다.
- SDK는 연산과 근거를 제공합니다. 수집·표시·레시피 영속화·교정 승인·생산 수용은 호스트 책임입니다.
- 합성 스모크 검사는 센서 정확도·Gauge R&R·고객 공차·생산 Takt를 입증하지 않습니다.
  [기존 결과 이미지](docs/sdk-reference.md#example-detection-images)는 추적 가능한 재현 명세가 없는 합성 설명 이미지입니다.
- 취소는 협력 방식이며 timeout으로 네이티브 작업을 강제 중단할 수 없습니다.
  Compute 세션 공유 시 문서의 직렬 실행·수명 계약을 지켜야 합니다.
- 높이 맵은 단위·좌표계·유효 비율 계약이 필요합니다. `HeightMapInputRequirements`를
  생략하는 호환 모드는 생산 레시피의 선언을 대신하지 않습니다.
- 일부 `CV*`/`C*` 레거시 API는 3.x에서 유지됩니다. 번들 네이티브 런타임에서
  `SiftTool`은 문서에 명시된 ORB 전환을 사용하므로 실제 검출기 근거를 확인하세요.

## 생태계

| 애플리케이션 | 통합 경계 |
| --- | --- |
| [OpenVisionLab](https://github.com/Noah8218/OpenVisionLab) | 도구·레이어·Pipeline·결과 표시 흐름을 다루는 2D 워크벤치. |
| [OpenVisionLab 3D Studio](https://github.com/Noah8218/OpenVisionLab-3D-Studio) | 고정된 SDK 패키지와 명시적 어댑터를 사용하는 3D 워크벤치. |

각 애플리케이션은 패키지 버전과 통합 코드를 별도로 선택합니다.
SDK 체크아웃의 변경이 연계 애플리케이션을 자동 업데이트하지 않습니다.

## 라이선스와 재배포

OpenVisionLab이 작성한 부분은 [MIT License](LICENSE)를 따르며
[NOTICE](NOTICE)에 따라 필요한 저작권·기여 고지를 보존해야 합니다.

**Core에 번들된 OpenCvSharp/OpenCV 바이너리의 상업적 재배포는 최종 고지 묶음,
Intel 고지 조건과 배포 절차에 대한 배포·법무 책임자의 승인 전까지 차단 상태입니다.**
[Core 고지](src/OpenVisionLab.Core/ThirdParty/NOTICE.md),
[바이너리 출처 명세](src/OpenVisionLab.Core/ThirdParty/provenance.json),
[승인 점검표](docs/THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md)가 정확한 범위를 정합니다.
MIT 표기나 기술적 출처 검사는 해당 승인을 대신하지 않습니다.
