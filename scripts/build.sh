#!/usr/bin/env bash
# 无 Gradle 时的纯 javac 构建（需 JDK 17+），在项目根目录执行
set -e
cd "$(dirname "$0")/.."
mkdir -p build/classes build/test-classes
javac --release 17 -encoding UTF-8 -d build/classes $(find src/main/java -name '*.java')
javac --release 17 -encoding UTF-8 -cp build/classes -d build/test-classes $(find src/test/java -name '*.java')
echo "构建完成。运行: scripts/run.sh"
