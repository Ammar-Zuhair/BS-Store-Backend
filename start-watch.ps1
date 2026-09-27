# تشغيل خادم BS Store API مع المراقبة وإعادة التشغيل التلقائي عند أي تعديل في الملفات
Write-Host "Starting BS Store Backend with auto-reload (dotnet watch)..." -ForegroundColor Cyan
dotnet watch --project "$PSScriptRoot\src\BSStore.API\BSStore.API.csproj" --no-hot-reload
