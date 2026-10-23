#!/usr/bin/env bash
# Plays every level of a pack with the autopilot and fails if any level
# can't be finished in its time limit.
#
#   tools/check-pack.sh [ID]      (default: main)
#
# Needs Godot 4.7 .NET as $GODOT (default: godot) and a built project.
set -euo pipefail

GODOT=${GODOT:-godot}
cd "$(dirname "$0")/.."

id=${1:-main}
count=$(jq '.levels | length' "packs/$id/pack.json")

failed=0
for i in $(seq 1 "$count"); do
	out=$(timeout 120 "$GODOT" --headless --fixed-fps 60 --path . -- --pack "$id" --level "$i" --autopilot 2>&1 || true)
	if line=$(grep -m1 'FINISHED level' <<<"$out"); then
		echo "$id $i: ${line#\[autopilot\] }"
	else
		echo "$id $i: FAILED $(grep -E 'TIME UP|fell off|ERROR' <<<"$out" | head -3 | tr '\n' ' ')"
		failed=1
	fi
done
exit $failed
