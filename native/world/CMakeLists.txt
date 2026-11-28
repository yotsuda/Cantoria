# Copyright 2026 Yoshifumi Tsuda
# Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
#
# cantoria_world: WORLD (speech analysis and synthesis by M. Morise, Modified BSD) built into one
# shared library with a C entry point (cantoria_world.cpp) for Cantoria.Core's UtauSinger: a
# voicebank's sample is analysed into its pitch, spectral envelope and aperiodicity, and
# synthesised again at the note's pitch and length (as OpenUtau's worldline does). The sources
# are fetched at the commit below and checked by SHA-256.

include(FetchContent)
FetchContent_Declare(world_src
  URL https://github.com/mmorise/World/archive/c794856b537758a3d300757db2e5e31685a822c2.tar.gz
  URL_HASH SHA256=c8a0a44572a9e893a46c7d5dcecf498409d6dc966052d85633936e94285b2c5a)
FetchContent_GetProperties(world_src)
if(NOT world_src_POPULATED)
  FetchContent_Populate(world_src)
endif()

set(WORLD ${world_src_SOURCE_DIR}/src)
add_library(cantoria_world SHARED cantoria_world.cpp
  ${WORLD}/cheaptrick.cpp ${WORLD}/common.cpp ${WORLD}/d4c.cpp ${WORLD}/dio.cpp ${WORLD}/fft.cpp
  ${WORLD}/harvest.cpp ${WORLD}/matlabfunctions.cpp ${WORLD}/stonemask.cpp ${WORLD}/synthesis.cpp)
target_include_directories(cantoria_world PRIVATE ${WORLD})
if(NOT MSVC AND NOT APPLE)
  target_link_options(cantoria_world PRIVATE -static-libstdc++ -static-libgcc)
endif()

install(TARGETS cantoria_world RUNTIME DESTINATION . LIBRARY DESTINATION .)
install(FILES ${world_src_SOURCE_DIR}/LICENSE.txt DESTINATION . RENAME COPYING.world)
