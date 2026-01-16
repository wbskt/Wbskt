@REM working dir = ./wbskt/Core

@REM run a docker container with azuresqledge
@REM sudo docker run -e "ACCEPT_EULA=1" \                                  
@REM            -e "MSSQL_SA_PASSWORD=Welcome1234" \       
@REM            -p 1433:1433 \
@REM            --name azuresqledge \
@REM            -d mcr.microsoft.com/azure-sql-edge

dotnet build .\Wbskt.Database\Wbskt.Database.sqlproj -c Release       
@REM dotnet build Wbskt.Database/Wbskt.Database.sqlproj -c Release       

@REM ONLY NEED TO RUN ONCE IN YOUR LIFE
@REM dotnet tool update -g microsoft.sqlpackage

sqlpackage /Action:publish /SourceFile:".\Wbskt.Database\bin\Release\net8.0\Wbskt.Database.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Wbskt.Database;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Multiple Active Result Sets=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False;Command Timeout=0"
@REM sqlpackage /Action:Publish /SourceFile:Wbskt.Database/bin/Release/net8.0/Wbskt.Database.dacpac /TargetConnectionString:"Server=localhost,1433;Database=Wbskt.Database;User Id=sa;Password=Welcome1234;Encrypt=False;TrustServerCertificate=True"

dotnet ef database update --project Services/Wbskt.Auth.Api/Wbskt.Auth.Api.csproj --startup-project Services/Wbskt.Auth.Api/Wbskt.Auth.Api.csproj