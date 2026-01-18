set positional-arguments

nuget_version := "3.0.0-beta-1"

alias t := restore
alias b := build
alias p := pack
alias d := deps
alias td := test-db
alias tdp := test-db-pg
alias tda := test-db-all
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

# Test Postgres-specific extensions (uses Postgres via Testcontainers)
test-db-pg:
    @dotnet test ./tests/TheUtils.Db.Postgres.Tests/TheUtils.Db.Postgres.Tests.csproj -tl:off

# Run all database tests
test-db-all: test-db test-db-pg

pack: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils/TheUtils.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

# Pack the core database monad
pack-db: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Db/TheUtils.Db.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

# Pack Postgres extensions
pack-db-pg: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Db.Postgres/TheUtils.Db.Postgres.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

# Pack all database packages
pack-db-all: pack-db pack-db-pg

# Pack CLI wrapper
pack-cli: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Cli/TheUtils.Cli.csproj" -c Release -o ../publish /p:PackageVersion={{nuget_version}})

pack-all: pack pack-cli pack-db-all
