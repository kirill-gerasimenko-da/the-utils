set positional-arguments
alias t := restore
alias b := build
alias p := pack
alias d := deps
alias td := test-db
alias tdp := test-db-pg
alias tda := test-db-all

default: build

deps *args='':
    @(cd ./src && dotnet outdated -u:prompt "$@")

restore:
    @(cd ./src && dotnet restore -tl:off)

build:
    @(cd ./src && dotnet build --no-restore -tl:off)

# Test the core database monad (uses PostgreSQL via Testcontainers)
test-db:
    @dotnet test ./tests/TheUtils.Db.Tests/TheUtils.Db.Tests.csproj -tl:off

# Test PostgreSQL-specific extensions (uses PostgreSQL via Testcontainers)
test-db-pg:
    @dotnet test ./tests/TheUtils.Db.Postgresql.Tests/TheUtils.Db.Postgresql.Tests.csproj -tl:off

# Run all database tests
test-db-all: test-db test-db-pg

pack: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils/TheUtils.csproj" -c Release -o ../publish /p:PackageVersion=2.2.0-beta-6)
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.SourceGenerator/TheUtils.SourceGenerator.csproj" -c Release -o ../publish /p:PackageVersion=2.2.0-beta-6)

# Pack the core database monad
pack-db: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Db/TheUtils.Db.csproj" -c Release -o ../publish /p:PackageVersion=1.0.0-beta)

# Pack PostgreSQL extensions
pack-db-pg: build
    @(cd ./src && dotnet build --no-restore -tl:off -c Release && dotnet pack "./TheUtils.Db.Postgresql/TheUtils.Db.Postgresql.csproj" -c Release -o ../publish /p:PackageVersion=1.0.0-beta)

# Pack all database packages
pack-db-all: pack-db pack-db-pg

pack-all: pack pack-db-all
