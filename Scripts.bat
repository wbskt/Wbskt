@REM working dir = ./wbskt/Core

@REM run a docker container with azuresqledge
@REM sudo docker run -e "ACCEPT_EULA=1" \                                  
@REM            -e "MSSQL_SA_PASSWORD=Welcome1234" \       
@REM            -p 1433:1433 \
@REM            --name azuresqledge \
@REM            -d mcr.microsoft.com/azure-sql-edge

dotnet build .\Wbskt.Database.Auth\Wbskt.Database.Auth.sqlproj -c Release       
@REM dotnet build ./Wbskt.Database.Auth/Wbskt.Database.Auth.sqlproj -c Release       

@REM ONLY NEED TO RUN ONCE IN YOUR LIFE
@REM dotnet tool update -g microsoft.sqlpackage

sqlpackage /Action:publish /SourceFile:".\Wbskt.Database.Auth\bin\Release\net10.0\Wbskt.Database.Auth.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Wbskt.Database.Auth;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False"

sqlpackage /Action:publish /SourceFile:"./Wbskt.Database.Auth/bin/Release/net10.0/Wbskt.Database.Auth.dacpac" /TargetConnectionString:"Data Source=localhost;Database=Wbskt.Database.Auth;Persist Security Info=True;User ID=sa;PWD=Welcome1234;Pooling=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=False"

docker run -d --name wbskt-rabbitmq -p 5672:5672 -p 15672:15672 -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest --restart unless-stopped rabbitmq:3-management

docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" -p 1433:1433 --name azuresqledge -d mcr.microsoft.com/azure-sql-edge