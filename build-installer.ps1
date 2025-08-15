# Material Calendar Widget 설치 프로그램 빌드 스크립트
Write-Host "🚀 Material Calendar Widget 설치 프로그램 빌드 시작..." -ForegroundColor Green
Write-Host ""

# 빌드 폴더 정리
if (Test-Path "installer") {
    Remove-Item -Recurse -Force "installer"
}
New-Item -ItemType Directory -Path "installer" | Out-Null

# .NET 런타임 포함된 단일 실행 파일 생성
Write-Host "📦 단일 실행 파일 생성 중..." -ForegroundColor Yellow
$result1 = dotnet publish GoogleCalendarWidget/GoogleCalendarWidget.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o installer/single-file

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 빌드 실패!" -ForegroundColor Red
    Read-Host "Press any key to exit"
    exit 1
}

# 폴더 배포 버전 생성
Write-Host "📁 폴더 배포 버전 생성 중..." -ForegroundColor Yellow
$result2 = dotnet publish GoogleCalendarWidget/GoogleCalendarWidget.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o installer/folder-deployment

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 폴더 배포 빌드 실패!" -ForegroundColor Red
    Read-Host "Press any key to exit"
    exit 1
}

# 설치 스크립트 생성
Write-Host "📝 설치 스크립트 생성 중..." -ForegroundColor Yellow

# PowerShell 설치 스크립트
$installScript = @'
# Material Calendar Widget 설치 스크립트
Write-Host "🎯 Material Calendar Widget 설치" -ForegroundColor Green
Write-Host ""

$InstallDir = "$env:LOCALAPPDATA\MaterialCalendarWidget"

if (!(Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

Copy-Item "MaterialCalendarWidget.exe" $InstallDir -Force

Write-Host "✅ 설치 완료!" -ForegroundColor Green
Write-Host "설치 위치: $InstallDir" -ForegroundColor Cyan
Write-Host ""

$response = Read-Host "🚀 Material Calendar Widget를 실행하시겠습니까? (Y/N)"
if ($response -eq 'Y' -or $response -eq 'y') {
    Start-Process "$InstallDir\MaterialCalendarWidget.exe"
}
'@

$installScript | Out-File -FilePath "installer/single-file/install.ps1" -Encoding UTF8

# 제거 스크립트
$uninstallScript = @'
# Material Calendar Widget 제거 스크립트
Write-Host "🗑️ Material Calendar Widget 제거" -ForegroundColor Yellow
Write-Host ""

$InstallDir = "$env:LOCALAPPDATA\MaterialCalendarWidget"

if (Test-Path $InstallDir) {
    # 실행 중인 프로세스 종료
    Get-Process -Name "MaterialCalendarWidget" -ErrorAction SilentlyContinue | Stop-Process -Force
    
    # 폴더 삭제
    Remove-Item -Recurse -Force $InstallDir
    
    Write-Host "✅ 제거 완료!" -ForegroundColor Green
} else {
    Write-Host "⚠️ Material Calendar Widget가 설치되어 있지 않습니다." -ForegroundColor Yellow
}

Read-Host "Press any key to exit"
'@

$uninstallScript | Out-File -FilePath "installer/single-file/uninstall.ps1" -Encoding UTF8

# README 파일 생성
Write-Host "📖 설치 가이드 생성 중..." -ForegroundColor Yellow

$readme = @"
# Material Calendar Widget v1.0.0

## 🎯 설치 방법

### 방법 1 - 단일 실행 파일 (권장)
1. `single-file` 폴더로 이동
2. PowerShell에서 `install.ps1` 실행 또는 `install.bat` 실행
3. 설치 완료 후 자동 실행

### 방법 2 - 폴더 배포
1. `folder-deployment` 폴더를 원하는 위치에 복사
2. `MaterialCalendarWidget.exe` 실행

## 🗑️ 제거 방법
- `single-file` 폴더의 `uninstall.ps1` 실행

## 📋 요구사항
- Windows 10/11
- 인터넷 연결 (Google API 사용)
- Google 계정

## 🔧 파일 설명
- `MaterialCalendarWidget.exe`: 메인 실행 파일
- `install.ps1`: PowerShell 설치 스크립트
- `install.bat`: 배치 설치 스크립트
- `uninstall.ps1`: 제거 스크립트

## 🌐 링크
- 홈페이지: https://fbdjzk2002.github.io/GoogleCalendarWidget/
- GitHub: https://github.com/fbdjzk2002/GoogleCalendarWidget
- 지원: https://github.com/fbdjzk2002/GoogleCalendarWidget/issues

## 📄 라이선스
MIT License - 무료 사용 가능
"@

$readme | Out-File -FilePath "installer/README.md" -Encoding UTF8

# 빌드 정보 출력
Write-Host ""
Write-Host "✅ 빌드 완료!" -ForegroundColor Green
Write-Host ""
Write-Host "📁 생성된 파일:" -ForegroundColor Cyan
Write-Host "  📦 단일 실행 파일: installer\single-file\MaterialCalendarWidget.exe" -ForegroundColor White
Write-Host "  📁 폴더 배포: installer\folder-deployment\" -ForegroundColor White
Write-Host "  📝 설치 가이드: installer\README.md" -ForegroundColor White
Write-Host ""
Write-Host "🎯 설치 프로그램 사용법:" -ForegroundColor Cyan
Write-Host "  1. installer 폴더를 배포" -ForegroundColor White
Write-Host "  2. 사용자가 원하는 방법 선택" -ForegroundColor White
Write-Host "  3. 설치 및 실행" -ForegroundColor White
Write-Host ""

# 파일 크기 정보
$singleFileSize = (Get-Item "installer/single-file/MaterialCalendarWidget.exe").Length / 1MB
Write-Host "📊 단일 파일 크기: $([math]::Round($singleFileSize, 1)) MB" -ForegroundColor Cyan

Read-Host "Press any key to exit"
