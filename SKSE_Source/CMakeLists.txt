cmake_minimum_required(VERSION 3.20)

project(BankPrismNative VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)

FetchContent_Declare(
  fmt
  URL https://github.com/fmtlib/fmt/archive/refs/tags/10.0.0.zip
)
FetchContent_MakeAvailable(fmt)

FetchContent_Declare(
  spdlog
  URL https://github.com/gabime/spdlog/archive/refs/tags/v1.12.0.zip
)
FetchContent_MakeAvailable(spdlog)

file(WRITE ${CMAKE_BINARY_DIR}/spdlogConfig.cmake "set(spdlog_FOUND TRUE)")
file(WRITE ${CMAKE_BINARY_DIR}/fmtConfig.cmake "set(fmt_FOUND TRUE)")
list(APPEND CMAKE_PREFIX_PATH ${CMAKE_BINARY_DIR})

# CommonLibSSE-NG requires this include dir even though we do not use rapidcsv ourselves.
set(RAPIDCSV_INCLUDE_DIRS "${CMAKE_CURRENT_SOURCE_DIR}/include" CACHE PATH "" FORCE)

set(BUILD_TESTS OFF CACHE BOOL "" FORCE)

# CommonLibSSE-NG's install() rules reference exports we do not build; neutralise them.
macro(install)
endmacro()

add_subdirectory(extern/CommonLibSSE-NG)

add_library(${PROJECT_NAME} SHARED src/main.cpp)

target_include_directories(${PROJECT_NAME} PRIVATE include)

target_link_libraries(${PROJECT_NAME} PRIVATE CommonLibSSE::CommonLibSSE)

target_compile_definitions(${PROJECT_NAME} PRIVATE SKSE_EXPORT)

# Deploy the built DLL straight into the mod's SKSE/Plugins folder, so the game never
# loads a stale DLL while build output sits unnoticed in build/Release.
set(BANKPRISM_DEPLOY_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../SKSE/Plugins")
add_custom_command(TARGET ${PROJECT_NAME} POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E make_directory "${BANKPRISM_DEPLOY_DIR}"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
          "$<TARGET_FILE:${PROJECT_NAME}>" "${BANKPRISM_DEPLOY_DIR}/"
  COMMENT "Deploying $<TARGET_FILE_NAME:${PROJECT_NAME}> to SKSE/Plugins"
)
