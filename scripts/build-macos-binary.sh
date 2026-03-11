#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${repo_root}/src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"

config="${CONFIGURATION:-Release}"
rid="${RID:-osx-x64}"
app_name="${APP_NAME:-PulseAPK.Avalonia}"
bundle_name="${APP_BUNDLE_NAME:-PulseAPK}"
app_exe="${app_name}"

out_root="${repo_root}/artifacts/macos/${rid}"
publish_dir="${out_root}/publish"
bundle_dir="${out_root}/${bundle_name}.app"
archive_path="${out_root}/${bundle_name}-${rid}.tar.gz"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but was not found in PATH." >&2
  exit 1
fi

if [[ "${rid}" != osx-* ]]; then
  echo "RID must target macOS (for example 'osx-x64' or 'osx-arm64'). Received '${rid}'." >&2
  exit 1
fi

rm -rf "${publish_dir}" "${bundle_dir}"
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

bundle_contents="${bundle_dir}/Contents"
bundle_macos="${bundle_contents}/MacOS"
bundle_resources="${bundle_contents}/Resources"

mkdir -p "${bundle_macos}" "${bundle_resources}"
cp -a "${publish_dir}/." "${bundle_macos}/"
chmod +x "${bundle_macos}/${app_exe}"

cat > "${bundle_contents}/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>${app_exe}</string>
  <key>CFBundleIdentifier</key>
  <string>com.pulseapk.${bundle_name,,}</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>${bundle_name}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleVersion</key>
  <string>1.0.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

if command -v tar >/dev/null 2>&1; then
  rm -f "${archive_path}"
  (
    cd "${out_root}"
    tar -czf "${archive_path}" "${bundle_name}.app"
  )
  echo "macOS app bundle archive created: ${archive_path}"
elif command -v zip >/dev/null 2>&1; then
  zip_path="${archive_path%.tar.gz}.zip"
  rm -f "${zip_path}"
  (
    cd "${out_root}"
    zip -r "${zip_path}" "${bundle_name}.app"
  )
  echo "macOS app bundle archive created: ${zip_path}"
else
  echo "tar/zip not found. Skipping archive creation."
fi

echo "macOS app bundle created: ${bundle_dir}"
echo "Executable entry point: ${bundle_macos}/${app_exe}"
