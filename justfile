set positional-arguments

nuget_version := "3.0.0-beta-4"

alias t := restore
alias b := build
alias p := pack
alias d := deps
alias td := test-db
alias pc := pack-cli

default: build

deps *args='':
    @(cd ./src && dotnet outdated -u:prompt "$@")

restore:
    @(cd ./src && dotnet restore -tl:off)

build:
    @(cd ./src && dotnet build --no-restore -tl:off)

# Test the core database monad (uses Postgres via Testcontainers)
test-db:
    @dotnet test ./tests/TheUtils.Db.Tests/TheUtils.Db.Tests.csproj -tl:off

pack: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils/TheUtils.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

# Pack the core database monad
pack-db: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Db/TheUtils.Db.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})


# Pack CLI wrapper
pack-cli: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Cli/TheUtils.Cli.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

pack-all: pack pack-cli pack-db
