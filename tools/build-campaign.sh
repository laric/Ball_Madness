#!/usr/bin/env bash
# Builds the campaign's worlds (tools/campaign.txt) with tools/build-pack.sh:
# 10 generated levels per world, each one played by the autopilot and
# generated again until it can be finished, with time limits set from the
# autopilot's times. Then plays every world once more with check-pack.sh and
# prints the summary to paste into the pull request.
#
#   tools/build-campaign.sh             (every world)
#   tools/build-campaign.sh w03-forest  (just these worlds)
#
# Needs Godot 4.7 .NET as $GODOT (default: godot) and a built project
# (dotnet build). Takes a while: run it on a desktop, not in CI.
set -euo pipefail

cd "$(dirname "$0")/.."

want=" $* "
built=()
failed=0
while read -r id seed from to theme name; do
	[[ -z $id || $id == \#* ]] && continue
	[[ $# -gt 0 && $want != *" $id "* ]] && continue
	echo "=== $id ($name, difficulty $from to $to, $theme)"
	tools/build-pack.sh "$id" 10 "$seed" "$from" "$to" "$name" "" "$theme" || failed=1
	built+=("$id")
done < tools/campaign.txt

echo
echo "=== Summary"
for id in "${built[@]}"; do
	tools/check-pack.sh "$id" || failed=1
done
exit $failed
