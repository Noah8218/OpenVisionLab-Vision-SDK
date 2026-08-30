# Lib.* 2.9.1에서 OpenVisionLab.* 3.0.0으로 마이그레이션

> 이 문서의 `3.0.0`은 패키지·DLL·네임스페이스의 마이그레이션 및 API
> baseline이다. 설치할 패키지는 선택한 feed에서 검증한 정확한 immutable
> `PackageVersion`을 사용한다. 현재 저장소의 로컬 기본값은 `3.0.1-dev.1`이며,
> 공유할 package를 만들 때는 고유 prerelease 버전으로 override한다.

`3.0.0`은 제품명, 패키지 ID, DLL과 네임스페이스를 함께 바꾸는 breaking
release다. 알고리즘 수식, 공차 의미, 3D 단위·좌표 프레임·결측값·커버리지 계약은
`2.9.1`과 동일하게 유지한다.

## 1. 패키지 교체

| 2.9.1 | 3.0.0 |
| --- | --- |
| `Lib.Common` | `OpenVisionLab.Core` |
| `Lib.OpenCV` | `OpenVisionLab.Vision2D` |
| `Lib.OpenCV.Blob` | `OpenVisionLab.Vision2D.Blob` |
| `Lib.ThreeD` | `OpenVisionLab.Vision3D` |
| `Lib.Inspection` | `OpenVisionLab.Inspection` |

필요한 기존 패키지를 제거하고 대응하는 3.0 패키지를 추가한다.

```powershell
dotnet remove package Lib.ThreeD
$packageVersion = "3.0.1-dev.1" # 선택한 feed의 정확한 버전으로 교체한다.
dotnet add package OpenVisionLab.Vision3D --version $packageVersion
```

로컬 빌드 패키지를 사용할 때는 저장소 루트에서 먼저 pack한다.

```powershell
$packageVersion = "3.0.1-dev.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
dotnet pack OpenVisionLab.VisionSdk.sln -c Release "-p:PackageVersion=$packageVersion"
dotnet add package OpenVisionLab.Vision3D --version $packageVersion --source .\artifacts\packages
```

## 2. 네임스페이스 교체

| 2.9.1 | 3.0.0 |
| --- | --- |
| `Lib.Common` | `OpenVisionLab.Core` |
| `Lib.Line` | `OpenVisionLab.Core.Geometry2D` |
| 저장소 소유 `OpenCvSharp.Extensions.BitmapConverter` | 소비자 UI 어댑터로 이동(SDK 대체 API 없음) |
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

예를 들어 3D 코드는 다음처럼 바꾼다.

```csharp
// 2.9.1
using Lib.ThreeD.Geometry;
using Lib.ThreeD.Inspection;

// 3.0.0
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
```

`BitmapConverter`는 UI 프레임워크 의존성을 Core에서 분리하기 위해 3.0 SDK에서
제거되었다. 해당 형식을 직접 사용했다면 소비자 UI 프로젝트의 WinForms/WPF 등
프레임워크별 어댑터로 변환 코드를 옮기고 SDK에는 `Mat`을 전달한다.

```csharp
// SDK 경계
tool.SetSourceImage(sourceMat);

// 화면 표시 변환은 소비자 UI 어댑터가 담당한다.
```

## 3. 소스 프로젝트 참조 교체

```xml
<ItemGroup>
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Core\OpenVisionLab.Core.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Vision2D\OpenVisionLab.Vision2D.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Vision3D\OpenVisionLab.Vision3D.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Inspection\OpenVisionLab.Inspection.csproj" />
</ItemGroup>
```

한 프로젝트에서 같은 기능의 `Lib.*`와 `OpenVisionLab.*` 패키지를 동시에 참조하지
않는다. 3.0에는 type-forwarder나 legacy facade가 없다.

## 4. 동작 계약 확인

이름 변경 후에도 다음 3D 규칙은 그대로다.

- 단위를 자동 변환하거나 별칭으로 추론하지 않는다.
- 좌표 프레임을 추론하거나 자동 변환하지 않는다.
- `double.NaN`은 결측 샘플이며 자동 보간하지 않는다.
- Infinity는 입력 오류다.
- `MinimumValidSamples`와 `MinimumValidCoverageRatio`를 모두 만족해야 측정한다.
- `Passed`, `OutOfTolerance`, `NotMeasured` 의미를 유지한다.

## 5. 소비자 검증 체크리스트

1. 모든 `PackageReference`와 `ProjectReference`가 한 버전 계열만 가리키는지 확인한다.
2. `Lib.` using이 남지 않았는지, 기존 `BitmapConverter` 사용이 소비자 UI 어댑터로 이동했는지 확인한다.
3. Release 빌드와 기존 소비자 테스트를 실행한다.
4. 3D 입력의 단위, frame ID, 결측값과 커버리지 기대값을 다시 확인한다.
5. OpenVisionLab 또는 3D Studio에서는 패키지 버전·SHA-256·어댑터 계약을 함께 갱신한다.

SDK 저장소의 이름 변경만으로 OpenVisionLab과 OpenVisionLab 3D Studio의 고정
패키지가 자동 갱신되지는 않는다. 두 소비 애플리케이션의 실제 전환은 각각 별도
변경과 검증으로 수행한다.

## 6. OpenVisionLab 소비 저장소별 체크리스트

### OpenVisionLab

1. `OpenVisionLab.Core`, `OpenVisionLab.Vision2D`,
   `OpenVisionLab.Vision2D.Blob`을 같은 3.0.x 버전으로 고정한다.
2. 기존 `Lib.Common`, `Lib.OpenCV`, `Lib.OpenCV.Blob` using과 참조를 제거한다.
3. 2D Tool의 `VisionToolResult`와 input/output layer 소유권 계약을 유지한다.
4. 패키지 출력의 `OpenCvSharpExtern.dll` 존재와 실제 Threshold/Edge 실행을 확인한다.
5. Preview/Run, 레이어 생성·삭제와 입력 레이어 선택 동작이 패키지 교체만으로
   바뀌지 않았는지 회귀 검증한다.

### OpenVisionLab 3D Studio

1. `OpenVisionLab.Vision3D` 3.0.x 버전과 승인된 nupkg SHA-256을 함께 고정한다.
2. `Lib.ThreeD.*` using을 `OpenVisionLab.Vision3D.*`으로 교체한다.
3. 어댑터가 planar/height unit, frame ID, source ID와 `NaN` 결측값을 그대로
   전달하는지 확인한다.
4. Surface Match에서 pose, coverage, RMSE, correspondence evidence를 각각
   표시하고 제품 pass/fail 공차는 Studio recipe가 소유하도록 유지한다.
5. Mesh Comparison에서 signed/unsigned 통계, tolerance count, sign-recovery
   evidence와 display sample을 보존한다.
6. 실제 C3D/mesh/height-map sample로 Preview/Run과 replay를 검증한다.

두 저장소 모두 ProjectReference와 PackageReference를 섞지 않는다. SDK 저장소를
rename하거나 패키지를 pack하는 것만으로 소비 저장소의 고정 버전은 변경되지 않는다.

## 7. SDK 자체 검증

```powershell
dotnet build OpenVisionLab.VisionSdk.sln -c Release
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Release --no-build
dotnet pack OpenVisionLab.VisionSdk.sln -c Release --no-build
```

CI는 이어서 `tests\OpenVisionLab.PackageConsumer.Smoke`를 생성된 패키지 소스만으로
복원하고 실행한다.
