#!/bin/bash
# Drive a development build of YARG through its DevRemote (see Assets/Script/Dev/DevRemote.cs).
#
#   yarg-remote.sh build                 Build Builds/DevLinux with Unity (editor must be closed)
#   yarg-remote.sh start [--shared-data] [width height]
#                                        Launch the build windowed (default 1600x900). By default the game
#                                        keeps its data in a separate test folder ($DIR/data), seeded on first
#                                        use with the profiles, settings and song cache of the dev data folder,
#                                        so test results never touch real progress. --shared-data uses the
#                                        dev data folder itself (~/.config/unity3d/YARC/YARG/dev).
#   yarg-remote.sh wipe                  Delete the test data folder; the next start seeds it again
#   yarg-remote.sh do "<cmd>" ...        Run remote commands in order, print the results
#   yarg-remote.sh shot [file.png]       Save a screenshot, print its path
#   yarg-remote.sh stop                  Close the game
#
# Remote commands: nav <Up|Down|Left|Right|Green|Red|Yellow|Blue|Orange|Start|Select>,
#   menu <MainMenu|GameModes|CareerList|Career|...>, back, click <x> <y>, wait <s>,
#   shot <path>, window <w> <h>, state,
#   simulate / career / scorescreen (career testing, see Assets/Script/Dev/DevRemote.Career.cs), e.g.
#   simulate career="YARG Setlist Rock" tier=0 song=2 stars=4
set -u
REPO="$(cd "$(dirname "$(readlink -f "$0")")/../.." && pwd)"
BUILD="$REPO/Builds/DevLinux"
DIR="${YARG_REMOTE_DIR:-${XDG_RUNTIME_DIR:-/tmp}/yarg-dev-remote}"
DATA="$DIR/data"
SEED="${YARG_SEED_DATA:-$HOME/.config/unity3d/YARC/YARG/dev}"
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.3.5f2/Editor/Unity}"

running() { pgrep -f "^$BUILD/YARG " >/dev/null; }

run() {
    rm -f "$DIR/result.txt"
    printf '%s\n' "$@" > "$DIR/cmd.tmp" && mv "$DIR/cmd.tmp" "$DIR/cmd.txt"
    for _ in $(seq 600); do
        [ -f "$DIR/result.txt" ] && { cat "$DIR/result.txt"; return 0; }
        running || { echo "error: game is not running" >&2; return 1; }
        sleep 0.1
    done
    echo "error: timed out waiting for the game" >&2; return 1
}

# First use of the test data folder: copy what the game needs to start like the dev data folder
# (profiles and bindings, settings with the song folders, the song cache), but no scores or careers.
seed() {
    [ -d "$DATA/profiles" ] && return 0
    mkdir -p "$DATA"
    if [ -f "$SEED/profiles/profiles.json" ]; then
        cp -r "$SEED/profiles" "$DATA/"
    else
        echo "warning: no profiles in $SEED; the game will start without a profile" >&2
    fi
    for file in settings.json songcache.bin badsongs.txt; do
        [ -f "$SEED/$file" ] && cp "$SEED/$file" "$DATA/"
    done
    echo "seeded test data in $DATA from $SEED" >&2
}

case "${1:-}" in
    build)
        mkdir -p "$DIR"
        "$UNITY" -batchmode -quit -nographics -projectPath "$REPO" -buildTarget Linux64 \
            -executeMethod Editor.DevBuild.BuildLinux -logFile "$DIR/build.log"
        code=$?
        grep -E "DevBuild result|error CS" "$DIR/build.log" | sort -u | head -30
        exit $code;;
    start)
        shift
        data_args=()
        if [ "${1:-}" = "--shared-data" ]; then
            shift
        else
            seed
            data_args=(-persistent-data-path "$DATA")
        fi
        mkdir -p "$DIR"; rm -f "$DIR/cmd.txt" "$DIR/result.txt"
        # -download-location is what the YARC Launcher passes: it is where Setlists/ and Venues/ live.
        (setsid "$BUILD/YARG" -dev-remote "$DIR" -download-location "${YARG_DOWNLOADS:-$HOME/.local/share/YARC}" \
            "${data_args[@]}" -logFile "$DIR/player.log" >/dev/null 2>&1 &)
        sleep 3
        run "window ${1:-1600} ${2:-900}" "wait 1" "state";;
    wipe)
        if running; then echo "error: stop the game first" >&2; exit 1; fi
        rm -rf "$DATA" && echo "removed $DATA";;
    do)   shift; run "$@";;
    shot) out="${2:-$DIR/shot.png}"; run "shot $out" >/dev/null && echo "$out";;
    stop) pkill -f "^$BUILD/YARG " ;;
    *)    sed -n '2,21p' "$0";;
esac
