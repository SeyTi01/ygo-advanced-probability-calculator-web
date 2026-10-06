#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
publish_root=${1:-"$repo_root/YGOProbabilityCalculatorBlazor/bin/Release/net10.0/publish/wwwroot"}
YGO_PUBLISH_ROOT=$(CDPATH= cd -- "$publish_root" && pwd)
export YGO_PUBLISH_ROOT
# Optional local Pages server or public preview; HTTP checks never follow canonical-route redirects.
YGO_SEO_BASE_URL=${2:-${YGO_SEO_BASE_URL:-}}
export YGO_SEO_BASE_URL

dotnet test "$repo_root/YGOProbabilityCalculatorBlazorTest/YGOProbabilityCalculatorBlazorTest.csproj" \
    --no-build --no-restore --filter 'FullyQualifiedName~PublishedOutputSeoTest'
