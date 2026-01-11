set positional-arguments
alias t := restore
alias b := build
alias p := pack
alias d := deps

default: build

deps *args='':
    @(cd ./src && dotnet outdated -u:prompt "$@")

restore:
    @(cd ./src && dotnet restore -tl:off)

build:
    @(cd ./src && dotnet build --no-restore -tl:off)

test-pg:
    @dotnet test ./tests/TheUtils.Pg.Tests/TheUtils.Pg.Tests.csproj -tl:off

pack: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils/TheUtils.csproj" -c Release -o ../publish /p:PackageVersion=2.2.0-beta-6)
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj" -c Release -o ../publish /p:PackageVersion=2.2.0-beta-6)

pack-pg: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Pg/TheUtils.Pg.csproj" -c Release -o ../publish /p:PackageVersion=1.0.0-beta)

pack-all: pack pack-pg
