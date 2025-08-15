@echo off
echo 🚀 Material Calendar Widget 설치 프로그램 빌드 시작...
echo.

:: 빌드 폴더 정리
if exist "installer" rmdir /s /q "installer"
mkdir installer

:: .NET 런타임 포함된 단일 실행 파일 생성
echo 📦 단일 실행 파일 생성 중...
dotnet publish GoogleCalendarWidget/GoogleCalendarWidget.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:PublishTrimmed=false ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o installer/single-file

if %ERRORLEVEL% neq 0 (
    echo ❌ 빌드 실패!
    pause
    exit /b 1
)

:: 폴더 배포 버전 생성
echo 📁 폴더 배포 버전 생성 중...
dotnet publish GoogleCalendarWidget/GoogleCalendarWidget.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -o installer/folder-deployment

if %ERRORLEVEL% neq 0 (
    echo ❌ 폴더 배포 빌드 실패!
    pause
    exit /b 1
)

:: 설치 스크립트 생성
echo 📝 설치 스크립트 생성 중...

:: 단일 파일 설치 스크립트
echo @echo off > installer/single-file/install.bat
echo echo 🎯 Material Calendar Widget 설치 >> installer/single-file/install.bat
echo echo. >> installer/single-file/install.bat
echo set "INSTALL_DIR=%%LOCALAPPDATA%%\MaterialCalendarWidget" >> installer/single-file/install.bat
echo if not exist "%%INSTALL_DIR%%" mkdir "%%INSTALL_DIR%%" >> installer/single-file/install.bat
echo copy "MaterialCalendarWidget.exe" "%%INSTALL_DIR%%\" ^>nul >> installer/single-file/install.bat
echo echo ✅ 설치 완료! >> installer/single-file/install.bat
echo echo 설치 위치: %%INSTALL_DIR%% >> installer/single-file/install.bat
echo echo. >> installer/single-file/install.bat
echo echo 🚀 Material Calendar Widget를 실행하시겠습니까? >> installer/single-file/install.bat
echo pause >> installer/single-file/install.bat
echo start "" "%%INSTALL_DIR%%\MaterialCalendarWidget.exe" >> installer/single-file/install.bat

:: README 파일 생성
echo 📖 설치 가이드 생성 중...
echo Material Calendar Widget v1.0.0 > installer/README.txt
echo. >> installer/README.txt
echo 🎯 설치 방법: >> installer/README.txt
echo. >> installer/README.txt
echo 방법 1 - 단일 실행 파일: >> installer/README.txt
echo   1. single-file 폴더로 이동 >> installer/README.txt
echo   2. install.bat 실행 >> installer/README.txt
echo. >> installer/README.txt
echo 방법 2 - 폴더 배포: >> installer/README.txt
echo   1. folder-deployment 폴더를 원하는 위치에 복사 >> installer/README.txt
echo   2. MaterialCalendarWidget.exe 실행 >> installer/README.txt
echo. >> installer/README.txt
echo 📋 요구사항: >> installer/README.txt
echo   - Windows 10/11 >> installer/README.txt
echo   - 인터넷 연결 (Google API 사용) >> installer/README.txt
echo   - Google 계정 >> installer/README.txt
echo. >> installer/README.txt
echo 🌐 홈페이지: https://fbdjzk2002.github.io/GoogleCalendarWidget/ >> installer/README.txt
echo 📞 지원: https://github.com/fbdjzk2002/GoogleCalendarWidget/issues >> installer/README.txt

:: 빌드 정보 출력
echo.
echo ✅ 빌드 완료!
echo.
echo 📁 생성된 파일:
echo   📦 단일 실행 파일: installer\single-file\MaterialCalendarWidget.exe
echo   📁 폴더 배포: installer\folder-deployment\
echo   📝 설치 가이드: installer\README.txt
echo.
echo 🎯 설치 프로그램 사용법:
echo   1. installer 폴더를 배포
echo   2. 사용자가 원하는 방법 선택
echo   3. 설치 및 실행
echo.
pause
