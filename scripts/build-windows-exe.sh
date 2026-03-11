#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${repo_root}/src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"

config="${CONFIGURATION:-Release}"
rid="${RID:-win-x64}"
app_name="${APP_NAME:-PulseAPK.Avalonia}"
app_exe="${app_name}.exe"

out_root="${repo_root}/artifacts/windows/${rid}"
publish_dir="${out_root}/publish"
zip_path="${out_root}/PulseAPK-${rid}.zip"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but was not found in PATH." >&2
  exit 1
fi

if [[ "${rid}" != win-* ]]; then
  echo "RID must target Windows (for example 'win-x64'). Received '${rid}'." >&2
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
  exe_candidates=("${publish_dir}"/*.exe)
  if [[ ${#exe_candidates[@]} -ne 1 || ! -f "${exe_candidates[0]}" ]]; then
    echo "Expected executable '${app_exe}' was not found in ${publish_dir}." >&2
    echo "Detected executables:"
    find "${publish_dir}" -maxdepth 1 -type f -name '*.exe' -print
    exit 1
  fi

  app_exe="$(basename "${exe_candidates[0]}")"
  echo "Expected '${APP_NAME:-PulseAPK.Avalonia}.exe' was not found; using discovered executable '${app_exe}'."
fi

if ! file "${publish_dir}/${app_exe}" | grep -Eq 'PE32\+?|MS Windows'; then
  echo "Published file '${app_exe}' is not a valid Windows executable (PE format)." >&2
  exit 1
fi

if command -v zip >/dev/null 2>&1; then
  rm -f "${zip_path}"
  (
    cd "${publish_dir}"
    zip -r "${zip_path}" .
  )
  echo "Windows package created: ${zip_path}"
else
  echo "zip not found. Skipping archive creation."
fi

echo "Windows executable created: ${publish_dir}/${app_exe}"
