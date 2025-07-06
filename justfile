alias t := restore
alias b := build
alias p := pack

default: build

restore:
    @(cd ./src && dotnet restore -tl:off)

build:
    @(cd ./src && dotnet build --no-restore -tl:off)

pack:
    @(cd ./src && dotnet pack "./TheUtils/TheUtils.csproj" -c Release -o ../publish /p:PackageVersion=2.1.0-beta-14)
