@echo off
cls
"build\tools\nuget\NuGet.exe" "Install" "FAKE" "-OutputDirectory" "packages" "-ExcludeVersion" "-version" "3.14.7"
"packages\FAKE\tools\Fake.exe" build\build.fsx
