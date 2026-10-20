@echo off
echo ============================================
echo   XYR_AlarmAnalysisTool Publish Script
echo ============================================
echo.

set PROJECT=XYR_AlarmAnalysisTool.csproj
set CONFIG=Release

echo Select publish mode:
echo.
echo   [1] Single-file (Recommended) - One exe with runtime, no .NET 8 needed
echo   [2] Self-contained (Folder)   - Folder with runtime, faster startup
echo   [3] Framework-dependent       - Needs .NET 8 Desktop Runtime installed
echo   [4] Exit
echo.
set /p choice="Enter option (1/2/3/4): "

if "%choice%"=="1" goto single_file
if "%choice%"=="2" goto self_contained
if "%choice%"=="3" goto framework_dep
if "%choice%"=="4" exit /b 0
echo Invalid option ^& pause ^& exit /b 1

:single_file
echo.
echo === Single-file publish ===
echo.
set PUBLISH_DIR=bin\publish\single-file
set RID=win-x64
dotnet publish %PROJECT% -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:RuntimeIdentifier=%RID% -o %PUBLISH_DIR%
if %errorlevel% neq 0 (
    echo.
    echo Publish FAILED!
    pause
    exit /b 1
)
goto done

:self_contained
echo.
echo === Self-contained publish (folder) ===
echo.
set PUBLISH_DIR=bin\publish\self-contained
set RID=win-x64
dotnet publish %PROJECT% -c %CONFIG% -r %RID% --self-contained true -p:RuntimeIdentifier=%RID% -o %PUBLISH_DIR%
if %errorlevel% neq 0 (
    echo.
    echo Publish FAILED!
    pause
    exit /b 1
)
goto done

:framework_dep
echo.
echo === Framework-dependent publish ===
echo Note: Target machine needs .NET 8 Desktop Runtime
echo.
set PUBLISH_DIR=bin\publish\framework-dependent
dotnet publish %PROJECT% -c %CONFIG% --self-contained false -o %PUBLISH_DIR%
if %errorlevel% neq 0 (
    echo.
    echo Publish FAILED!
    pause
    exit /b 1
)
goto done

:done
echo.
echo ============================================
echo  Publish DONE!
echo  Output: %PUBLISH_DIR%
echo ============================================
echo.
echo Executable: %PUBLISH_DIR%\AlarmAnalysisTool.exe
echo.
pause
