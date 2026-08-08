#!/usr/bin/env bash
cd "$(dirname "$0")/.."
java -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp build/classes com.dominionwars.app.Main
