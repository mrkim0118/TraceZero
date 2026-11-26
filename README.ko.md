<p align="center">
  <img src="Assets/TraceZero.png" width="128" alt="TraceZero 아이콘">
</p>

<h1 align="center">TraceZero</h1>

<p align="center">파일을 복구할 수 없게 안전하게 지우는 Windows 프로그램입니다.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><a href="README.md">English</a> | <b>한국어</b></p>

TraceZero는 파일 내용을 덮어쓰고, 메타데이터와 이름을 정리한 뒤 삭제합니다. 선택 기능인 흔적 정리와 빈 공간 정리로 일반 삭제가 남기는 흔적까지 지웁니다.

## 주요 기능

- **파일 추가**: 여러 파일을 한 번에 선택하거나, 파일과 폴더를 창에 끌어다 놓아 추가합니다.
- **삭제 전 덮어쓰기**: 삭제 전에 파일 내용을 마지막 클러스터 끝까지 무작위 데이터로 1회 덮어씁니다 (NIST SP 800-88).
- **메타데이터 정리**: NTFS 대체 데이터 스트림(ADS), 타임스탬프, 파일 크기 정보를 정리합니다.
- **이름 무작위화**: 최종 삭제 전에 파일 이름을 여러 번 무작위로 바꿉니다.
- **안전한 링크 처리**: 심볼릭 링크는 링크만 지우고, 폴더를 훑을 때 정션은 따라가지 않습니다.
- **위험 표시**: 압축·암호화·스파스 파일은 원본이 빈 공간에 남을 수 있다고 표시합니다.
- **흔적 정리** (선택, UAC 1회): 섀도 복사본 삭제, USN 저널 삭제, SSD TRIM.
- **최근 항목·클라우드 폴더**: 탐색기 "최근 항목"의 바로가기도 지우고, 클라우드 동기화 폴더의 파일은 경고합니다.
- **빈 공간 정리**: 선택한 드라이브의 빈 공간과 NTFS MFT 빈 칸을 덮어씁니다.
- **진행률과 로그**: 컴팩트한 창에서 진행률, 파일별 상태, 로그(저장 가능)를 보여줍니다.
- **한국어 / English**: 화면 언어를 전환할 수 있습니다.
- **업데이트 확인**: 실행할 때 GitHub 최신 릴리스를 확인하고, 새 버전이 있으면 내려받아 설치할지 묻습니다.

### 복구 테스트 결과

고유한 표시 문자열로 채운 2MB 테스트 파일을 지운 뒤, 그 파일이 있던 디스크 위치를 캐시를 거치지 않고 직접 읽었습니다. 복구 프로그램이 데이터를 찾는 방식과 같습니다.

| 발견된 표시 문자열 수 | HDD | SSD (NVMe) |
|---|---|---|
| 삭제 전 | 48,770 | 48,770 |
| Windows 일반 삭제 후 | 48,770 (전부 복구 가능) | 48,770 (전부 복구 가능) |
| TraceZero 덮어쓰기 직후 (삭제 전) | 0 | 0 |
| TraceZero 삭제 후 | 0 | 0 |

이 테스트는 컴퓨터가 디스크에서 읽어 올 수 있는 데이터를 확인한 것입니다. SSD 칩을 직접 읽는 수준의 포렌식은 포함하지 않으며, 그 수준까지 막으려면 흔적 정리(TRIM)와 전체 디스크 암호화를 함께 쓰세요.

## 요구 사항

- Windows 10 또는 11 (x64)
- .NET 8 Desktop Runtime x64
- 관리자 권한은 선택 기능인 흔적 정리에만 필요합니다 (UAC 확인 창 1회)

## 사용법

1. 파일을 추가하거나 폴더를 끌어다 놓은 뒤 **영구삭제 시작**을 누릅니다.
2. 흔적 정리 질문에 **예**를 누르면 섀도 복사본과 USN 저널을 지우고 SSD TRIM을 실행합니다. 해당 드라이브의 시스템 복원 지점이 모두 삭제됩니다.
3. 같은 드라이브에서 **빈 공간 정리**를 실행해 예전에 지운 파일의 흔적까지 덮어씁니다. 작업 중 드라이브가 잠시 가득 차므로 다른 프로그램을 먼저 닫아 주세요.
4. SSD라면 BitLocker 같은 전체 디스크 암호화를 켜 두는 것이 가장 확실합니다.

## 데이터와 설정

| 데이터 | 위치 |
|---|---|
| 시작 로그 | `%LocalAppData%\TechArchive\TraceZero\startup.log` |

## 제한 사항

TraceZero는 일반 삭제보다 훨씬 강력하지만, 어떤 프로그램도 "영원히 복구 불가"를 포렌식 수준으로 100% 보장할 수는 없습니다.

- SSD는 쓰기 위치를 내부에서 바꿉니다. 삭제 후 흔적 정리(TRIM)를 실행하세요. BitLocker 같은 전체 디스크 암호화가 가장 확실한 방법입니다.
- 클라우드 동기화 폴더는 서비스의 휴지통과 버전 기록에 복사본이 남습니다.
- 정리하지 않는 곳: 탐색기 썸네일 캐시(`thumbcache_*.db`, 탐색기가 잠그고 있으므로 디스크 정리 > 미리 보기 사진 사용), Windows 검색 색인, `$LogFile`, pagefile, hiberfil.
- 드라이브가 가득 찬 상태에서 폴더 목록이 더 커질 수 없으면 MFT 빈 칸 덮어쓰기가 일찍 끝날 수 있습니다.

## 소스에서 빌드

.NET 8 SDK가 필요합니다.

### 빌드

```powershell
dotnet run --project TraceZero.csproj
```

### 테스트

```powershell
dotnet test tests\TraceZero.Tests
```

### 배포

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-Velopack.ps1 -Version 1.2.0
```

설치 파일은 .NET 런타임을 포함하지 않으므로, 설치할 PC에 .NET 8 Desktop Runtime x64가 있어야 합니다. 릴리스 패키지에는 PDB 디버그 기호가 들어가지 않습니다.

스크립트가 만드는 것:

- `artifacts\publish\<version>\win-x64`
- `artifacts\velopack\Releases`
- TraceZero 아이콘이 적용된 `Setup.exe` 설치 파일

## 프로젝트 구조

| 경로 | 내용 |
|---|---|
| `MainWindow.xaml` | 메인 창 (WPF) |
| `Services/SecureDeleteService.cs` | 덮어쓰기, 메타데이터 정리, 삭제 |
| `Services/TraceCleanupService.cs` | 섀도 복사본, USN 저널, TRIM |
| `Services/FreeSpaceWipeService.cs` | 빈 공간·MFT 덮어쓰기 |
| `Services/GitHubUpdater.cs` | GitHub 릴리스 업데이트 확인 |
| `Services/UiTextProvider.cs` | 한국어 / 영어 화면 문구 |
| `Models/` | 대기열 항목과 진행률 모델 |
| `tests/TraceZero.Tests/` | 테스트 |
| `scripts/Publish-Velopack.ps1` | 배포 패키지 생성 |

## 사용한 라이브러리

- [Velopack](https://velopack.io/): 설치 프로그램과 업데이트
