@REM working dir = ./wbskt/Core

@REM run a docker container with azuresqledge
@REM sudo docker run -e "ACCEPT_EULA=1" \                                  
@REM            -e "MSSQL_SA_PASSWORD=Welcome1234" \       
@REM            -p 1433:1433 \
@REM            --name azuresqledge \
@REM            -d mcr.microsoft.com/azure-sql-edge

dotnet build .\Webskt.Auth.Database\Webskt.Auth.Database.sqlproj -c Release       
@REM dotnet build ./Webskt.Auth.Database/Webskt.Auth.Database.sqlproj -c Release       

@REM ONLY NEED TO RUN ONCE IN YOUR LIFE
@REM dotnet tool update -g microsoft.sqlpackage

sqlpackage /Action:publish /SourceFile:".\Webskt.Auth.Database\bin\Release\net10.0\Webskt.Auth.Database.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Webskt.Auth.Database;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False"

sqlpackage /Action:publish /SourceFile:"./Webskt.Auth.Database/bin/Release/net10.0/Webskt.Auth.Database.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Webskt.Auth.Database;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False"
