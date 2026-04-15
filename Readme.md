*Building and deploying BapalaApp*

cd F:\2026\code\repos\Bapala Media Manager\BapalaApp
dotnet build -f net9.0-android
cd C:\platform-tools
.\adb install -r "F:\2026\code\repos\Bapala Media Manager\BapalaApp\bin\Debug\net9.0-android\com.bapala.app-Signed.apk"

*Building and deploying BapalaServer*

cd "F:\2026\code\repos\Bapala Media Manager\BapalaServer"

dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true -o "..\publish\win-x64"

Copy-Item "F:\2026\code\repos\Bapala Media Manager\installer\open-browser.bat" "F:\2026\code\repos\Bapala Media Manager\publish\win-x64\"

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "F:\2026\code\repos\Bapala Media Manager\installer\BapalaServer.iss"