# Copyright 2026 Yoshifumi Tsuda
# Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
#
# cantoria_sinsy: Sinsy (HMM singing synthesis, Modified BSD) and hts_engine_API (Modified BSD)
# built into one shared library with a C entry point (cantoria_sinsy.cpp) for Cantoria.Core's
# SinsySinger. The sources are fetched at the commits below (r9y9's git forks of the
# SourceForge releases, Sinsy 0.92 and hts_engine_API 1.10) and checked by SHA-256.
# Sinsy's own CMakeLists asks for CMake 2.8 and builds its bundled hts_engine as a separate
# library, so the sources are listed here instead.
#
#   cmake -S native/sinsy -B <build> -G Ninja -DCMAKE_BUILD_TYPE=Release
#   cmake --build <build>
#
# tools/build-sinsy.ps1 does this and copies the library and Sinsy's Japanese dictionary to
# native/out/<rid>/, where Cantoria.Core's project takes them from.

cmake_minimum_required(VERSION 3.20)
project(cantoria_sinsy C CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
if(MSVC)
  # One DLL with the C runtime inside: nothing to install beside it.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/utf-8 /W1 /EHsc)
  add_compile_definitions(_CRT_SECURE_NO_WARNINGS _CRT_NONSTDC_NO_WARNINGS)
endif()

include(FetchContent)
FetchContent_Declare(sinsy_src
  URL https://github.com/r9y9/sinsy/archive/6a5be094f49285fdf704fdea5083caabdc6a62eb.tar.gz
  URL_HASH SHA256=2461525c49d8c5912446e6421b7705092cf996db9f19847ae96b0e5f409488e0)
FetchContent_Declare(hts_src
  URL https://github.com/r9y9/hts_engine_API/archive/214e26dfb7f728ff9db39c14a59db709abcc121d.tar.gz
  URL_HASH SHA256=2f3f316aa3f39a8b58a1ce9ccbf28820ee2eb91878b7037a3aa07a92a21b7842)
FetchContent_GetProperties(sinsy_src)
if(NOT sinsy_src_POPULATED)
  FetchContent_Populate(sinsy_src)
endif()
FetchContent_GetProperties(hts_src)
if(NOT hts_src_POPULATED)
  FetchContent_Populate(hts_src)
endif()

set(SINSY ${sinsy_src_SOURCE_DIR}/src)
set(HTS ${hts_src_SOURCE_DIR}/src)

file(GLOB HTS_SOURCES ${HTS}/lib/*.c)
set(SINSY_DIRS converter hts_engine_API japanese label score temporary util xml)
set(SINSY_SOURCES ${SINSY}/lib/Sinsy.cpp)
foreach(dir ${SINSY_DIRS})
  file(GLOB s ${SINSY}/lib/${dir}/*.cpp)
  list(APPEND SINSY_SOURCES ${s})
endforeach()

add_library(cantoria_sinsy SHARED cantoria_sinsy.cpp ${SINSY_SOURCES} ${HTS_SOURCES})
# No sound card: Cantoria takes the samples.
target_compile_definitions(cantoria_sinsy PRIVATE AUDIO_PLAY_NONE CANTORIA_SINSY_BUILD)
target_include_directories(cantoria_sinsy PRIVATE
  ${SINSY}/include ${SINSY}/include/sinsy ${SINSY}/lib ${HTS}/include ${HTS}/lib)
foreach(dir ${SINSY_DIRS})
  target_include_directories(cantoria_sinsy PRIVATE ${SINSY}/lib/${dir})
endforeach()
if(NOT MSVC AND NOT APPLE)
  target_link_options(cantoria_sinsy PRIVATE -static-libstdc++ -static-libgcc)
endif()

install(TARGETS cantoria_sinsy RUNTIME DESTINATION . LIBRARY DESTINATION .)
install(DIRECTORY ${SINSY}/dic/ DESTINATION sinsy-dic FILES_MATCHING PATTERN "japanese.*")
install(FILES ${SINSY}/COPYING DESTINATION sinsy-dic RENAME COPYING.sinsy)
install(FILES ${HTS}/COPYING DESTINATION sinsy-dic RENAME COPYING.hts_engine_API)
