@echo off
title BS Store Backend (Auto-Restart Watcher)
echo Starting BS Store Backend with auto-reload (dotnet watch)...
dotnet watch --project "%~dp0src\BSStore.API\BSStore.API.csproj" --no-hot-reload
