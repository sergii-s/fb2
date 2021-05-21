#!/bin/bash

set -e -o pipefail

dotnet restore dotnet-fake.csproj
dotnet nuget list source
dotnet fake $@
