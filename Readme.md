cd F:\2026\code\repos\Bapala Media Manager\BapalaApp
dotnet build -f net9.0-android
cd C:\platform-tools
.\adb install -r "F:\2026\code\repos\Bapala Media Manager\BapalaApp\bin\Debug\net9.0-android\com.bapala.app-Signed.apk"