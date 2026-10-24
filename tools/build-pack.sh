#!/usr/bin/env bash
# Generates a level pack and proves every level can be finished.
#
#   tools/build-pack.sh ID COUNT [SEED] [FROM] [TO] [NAME] [FEATURE] [THEME]
#
# Writes packs/ID/ with COUNT generated levels whose difficulty rises from
# FROM to TO (0 to 1). With FEATURE (a section name such as Ice) every level
# has that section in it. The autopilot then plays each level; a level it can't
# finish without falling is generated again from another seed, and a level
# it finishes gets its time limit from the autopilot's time. THEME is a
# built-in world theme from themes/ (default classic), e.g. lava; pass ""
# as FEATURE to give a theme without one.
#
# Needs Godot 4.7 .NET as $GODOT (default: godot) and a built project
# (dotnet build).
set -euo pipefail

GODOT=${GODOT:-godot}
cd "$(dirname "$0")/.."

id=$1
count=$2
seed=${3:-1}
from=${4:-0}
to=${5:-1}
name=${6:-$id}
feature=${7:-}
theme=${8:-classic}
tries=8

tools() { "$GODOT" --headless --path . res://Scenes/Tools.tscn -- "$@" 2>&1 | grep '^\[tools\]' || true; }

# Plays one level with the autopilot as fast as the machine allows (the
# physics still steps 60 times per game second, so results don't change).
play() {
	timeout 120 "$GODOT" --headless --fixed-fps 60 --path . -- --pack "$id" --level "$1" --autopilot 2>&1 || true
}

tools --generate-pack "$id" --count "$count" --seed "$seed" --from "$from" --to "$to" --name "$name" ${feature:+--feature "$feature"} --theme "$theme"

failed=0
for i in $(seq 1 "$count"); do
	ok=
	for try in $(seq 1 "$tries"); do
		out=$(play "$i")
		line=$(grep -oE 'FINISHED level [0-9]+ in [0-9.]+s with [0-9.]+s left, 0 fall' <<<"$out" || true)
		if [[ -n $line ]]; then
			time=$(sed -E 's/.* in ([0-9.]+)s .*/\1/' <<<"$line")
			tools --set-time "$id" "$i" "$time"
			ok=1
			break
		fi
		echo "level $i, try $try: $(grep -E 'TIME UP|fell off|FINISHED|ERROR' <<<"$out" | head -3 | tr '\n' ' ')"
		tools --regenerate "$id" "$i" $((seed * 1000 + i + try * 100003))
	done
	if [[ -z $ok ]]; then
		echo "level $i: no finishable layout after $tries tries"
		failed=1
	fi
done
exit $failed
