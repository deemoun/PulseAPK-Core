#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${repo_root}/src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"

config="${CONFIGURATION:-Release}"
rid="${RID:-osx-x64}"
app_name="${APP_NAME:-PulseAPK.Avalonia}"
app_exe="${app_name}"

out_root="${repo_root}/artifacts/macos/${rid}"
publish_dir="${out_root}/publish"
archive_path="${out_root}/PulseAPK-${rid}.tar.gz"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but was not found in PATH." >&2
  exit 1
fi

if [[ "${rid}" != osx-* ]]; then
  echo "RID must target macOS (for example 'osx-x64' or 'osx-arm64'). Received '${rid}'." >&2
  exit 1
fi

rm -rf "${publish_dir}"
mkdir -p "${publish_dir}"

dotnet publish "${project_path}" \
  -c "${config}" \
  -r "${rid}" \
  --self-contained true \
  /p:UseAppHost=true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:EnableCompressionInSingleFile=true \
  -o "${publish_dir}"

if [[ ! -f "${publish_dir}/${app_exe}" ]]; then
  file_candidates=("${publish_dir}"/*)
  executable_candidates=()

  for candidate in "${file_candidates[@]}"; do
    [[ -f "${candidate}" && -x "${candidate}" ]] || continue
    executable_candidates+=("${candidate}")
  done

  if [[ ${#executable_candidates[@]} -ne 1 ]]; then
    echo "Expected executable '${app_exe}' was not found in ${publish_dir}." >&2
    echo "Detected executable files:"
    find "${publish_dir}" -maxdepth 1 -type f -perm -111 -print
    exit 1
  fi

  app_exe="$(basename "${executable_candidates[0]}")"
  echo "Expected '${APP_NAME:-PulseAPK.Avalonia}' was not found; using discovered executable '${app_exe}'."
fi

if ! file "${publish_dir}/${app_exe}" | grep -Eq 'Mach-O'; then
  echo "Published file '${app_exe}' is not a valid macOS executable (Mach-O format)." >&2
  exit 1
fi

if command -v tar >/dev/null 2>&1; then
  rm -f "${archive_path}"
  (
    cd "${publish_dir}"
    tar -czf "${archive_path}" .
  )
  echo "macOS archive created: ${archive_path}"
elif command -v zip >/dev/null 2>&1; then
  zip_path="${archive_path%.tar.gz}.zip"
  rm -f "${zip_path}"
  (
    cd "${publish_dir}"
    zip -r "${zip_path}" .
  )
  echo "macOS archive created: ${zip_path}"
else
  echo "tar/zip not found. Skipping archive creation."
fi

echo "macOS binary created: ${publish_dir}/${app_exe}"
