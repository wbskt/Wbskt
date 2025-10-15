@REM working dir = ./wbskt/Core

dotnet build .\Wbskt.Database\Wbskt.Database.sqlproj -c Release       

@REM ONLY NEED TO RUN ONCE IN YOUR LIFE
@REM dotnet tool update -g microsoft.sqlpackage

sqlpackage /Action:publish /SourceFile:".\Wbskt.Database\bin\Release\net8.0\Wbskt.Database.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Wbskt.Database;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Multiple Active Result Sets=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False;Command Timeout=0"
