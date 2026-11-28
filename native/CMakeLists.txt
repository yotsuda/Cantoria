# Copyright 2026 Yoshifumi Tsuda
# Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
#
# Cantoria's native libraries, each with a C entry point for Cantoria.Core:
#   sinsy/  cantoria_sinsy  Sinsy and hts_engine_API, for SinsySinger
#   world/  cantoria_world  WORLD, for UtauSinger
#
#   cmake -S native -B <build> -G Ninja -DCMAKE_BUILD_TYPE=Release
#   cmake --build <build>
#
# tools/build-native.ps1 does this and installs the libraries (and Sinsy's Japanese dictionary)
# into native/out/<rid>/, where Cantoria.Core's project takes them from.

cmake_minimum_required(VERSION 3.20)
project(cantoria_native C CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
if(MSVC)
  # Each DLL with the C runtime inside: nothing to install beside it.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/utf-8 /W1 /EHsc)
  add_compile_definitions(_CRT_SECURE_NO_WARNINGS _CRT_NONSTDC_NO_WARNINGS)
endif()

add_subdirectory(sinsy)
add_subdirectory(world)
