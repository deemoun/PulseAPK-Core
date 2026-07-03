#!/usr/bin/env bash
set -euo pipefail

# Minimize external data sharing/noisy output during CI builds.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Ensure artifacts are not created world-readable by default.
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${repo_root}/src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"
macos_icon_path="${repo_root}/Resources/PulseAPK.icns"
macos_icon_name="PulseAPK"
macos_icon_file="${macos_icon_name}.icns"

config="${CONFIGURATION:-Release}"
rid="${RID:-osx-arm64}"
app_name="${APP_NAME:-PulseAPK}"
bundle_name="${APP_BUNDLE_NAME:-PulseAPK}"
app_exe="${app_name}"
app_version="${APP_VERSION:-}"

out_root="${repo_root}/artifacts/macos/${rid}"
publish_dir="${out_root}/publish"
bundle_dir="${out_root}/${bundle_name}.app"
zip_path="${out_root}/${bundle_name}-${rid}.zip"
notary_zip_path="${out_root}/${bundle_name}-${rid}-notary.zip"
bundle_identifier_name="$(printf '%s' "${bundle_name}" | tr '[:upper:]' '[:lower:]')"

version="${APP_VERSION:-${VERSION:-}}"
if [[ -z "${version}" ]]; then
  version="$(sed -nE 's:^[[:space:]]*<Version>([^<]+)</Version>[[:space:]]*$:\1:p' "${project_path}" | head -n 1)"
fi

if [[ -z "${version}" ]]; then
  version="1.0.0"
  echo "Unable to determine project version; falling back to ${version}." >&2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but was not found in PATH." >&2
  exit 1
fi

if [[ "${rid}" != "osx-arm64" ]]; then
  echo "PulseAPK macOS releases are arm64 only. Set RID=osx-arm64 (received '${rid}')." >&2
  exit 1
fi

if [[ ! "${app_name}" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "APP_NAME contains unsupported characters. Allowed: letters, digits, '.', '_' and '-'." >&2
  exit 1
fi

if [[ ! "${bundle_name}" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "APP_BUNDLE_NAME contains unsupported characters. Allowed: letters, digits, '.', '_' and '-'." >&2
  exit 1
fi

if [[ ! -f "${macos_icon_path}" ]]; then
  echo "macOS icon '${macos_icon_path}' was not found." >&2
  exit 1
fi

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required but was not found in PATH." >&2
  exit 1
fi

rm -rf "${publish_dir}" "${bundle_dir}"
mkdir -p "${publish_dir}"

dotnet publish "${project_path}" \
  -c "${config}" \
  -r "${rid}" \
  --self-contained true \
  ${app_version:+-p:Version="${app_version}"} \
  ${app_version:+-p:InformationalVersion="${app_version}"} \
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
  echo "Expected '${APP_NAME:-PulseAPK}' was not found; using discovered executable '${app_exe}'."
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
cp "${macos_icon_path}" "${bundle_resources}/${macos_icon_file}"
chmod +x "${bundle_macos}/${app_exe}"

# PDB files can include local source paths and machine/user details.
# Exclude them from distributable artifacts by default.
find "${bundle_macos}" -maxdepth 1 -type f \( -name '*.pdb' -o -name '*.dbg' \) -delete

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
  <string>com.pulseapk.${bundle_identifier_name}</string>
  <key>CFBundleIconFile</key>
  <string>${macos_icon_name}</string>
  <key>CFBundleDisplayName</key>
  <string>${bundle_name}</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>${bundle_name}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${version}</string>
  <key>CFBundleVersion</key>
  <string>${version}</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSDesktopFolderUsageDescription</key>
  <string>PulseAPK needs access to APK files and output folders you select on the Desktop so it can decompile and rebuild APK projects.</string>
  <key>NSDocumentsFolderUsageDescription</key>
  <string>PulseAPK needs access to APK files and output folders you select in Documents so it can decompile and rebuild APK projects.</string>
  <key>NSDownloadsFolderUsageDescription</key>
  <string>PulseAPK needs access to APK files and output folders you select in Downloads so it can decompile and rebuild APK projects.</string>
  <key>NSRemovableVolumesUsageDescription</key>
  <string>PulseAPK needs access to APK files and output folders you select on removable volumes so it can decompile and rebuild APK projects.</string>
  <key>NSNetworkVolumesUsageDescription</key>
  <string>PulseAPK needs access to APK files and output folders you select on network volumes so it can decompile and rebuild APK projects.</string>
</dict>
</plist>
PLIST

if command -v plutil >/dev/null 2>&1; then
  plutil -lint "${bundle_contents}/Info.plist" >/dev/null
fi

if [[ ! -s "${bundle_resources}/${macos_icon_file}" ]]; then
  echo "Expected macOS icon '${bundle_resources}/${macos_icon_file}' was not copied into the app bundle." >&2
  exit 1
fi

# Refresh bundle metadata after writing Info.plist and the icon so Finder/Dock
# pick up the icon when the freshly built app is launched.
touch "${bundle_dir}" "${bundle_contents}/Info.plist" "${bundle_resources}/${macos_icon_file}"

codesign_identity="${MACOS_CODESIGN_IDENTITY:-}"
notarize_requested=false
if [[ -n "${APPLE_ID:-}" || -n "${APPLE_TEAM_ID:-}" || -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" ]]; then
  notarize_requested=true
fi

create_zip() {
  local destination="$1"

  rm -f "${destination}"
  (
    cd "${out_root}"
    COPYFILE_DISABLE=1 zip -r "${destination}" "${bundle_name}.app"
  )
}

if [[ "${notarize_requested}" == "true" ]]; then
  missing_notarization_vars=()
  [[ -n "${APPLE_ID:-}" ]] || missing_notarization_vars+=("APPLE_ID")
  [[ -n "${APPLE_TEAM_ID:-}" ]] || missing_notarization_vars+=("APPLE_TEAM_ID")
  [[ -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" ]] || missing_notarization_vars+=("APPLE_APP_SPECIFIC_PASSWORD")

  if [[ ${#missing_notarization_vars[@]} -gt 0 ]]; then
    printf 'Notarization was requested, but these variables are missing: %s\n' "${missing_notarization_vars[*]}" >&2
    exit 1
  fi

  if [[ -z "${codesign_identity}" ]]; then
    echo "Notarization requires MACOS_CODESIGN_IDENTITY; ad-hoc signed apps cannot be notarized." >&2
    exit 1
  fi
fi

if command -v codesign >/dev/null 2>&1; then
  if [[ -n "${codesign_identity}" ]]; then
    echo "Signing macOS app bundle with identity '${codesign_identity}': ${bundle_dir}"
    codesign --force --deep --options runtime --timestamp --sign "${codesign_identity}" "${bundle_dir}"
  else
    echo "Ad-hoc signing macOS app bundle: ${bundle_dir}"
    codesign --force --deep --sign - "${bundle_dir}"
  fi
  codesign --verify --deep --strict "${bundle_dir}"
elif [[ "$(uname -s)" == "Darwin" ]]; then
  echo "codesign is required to sign macOS app bundles on Darwin but was not found in PATH." >&2
  exit 1
else
  echo "Warning: codesign was not found in PATH; skipping macOS app bundle signing." >&2
fi

if [[ "${notarize_requested}" == "true" ]]; then
  if ! command -v xcrun >/dev/null 2>&1; then
    echo "xcrun is required for macOS notarization but was not found in PATH." >&2
    exit 1
  fi

  echo "Creating notarization ZIP: ${notary_zip_path}"
  create_zip "${notary_zip_path}"

  echo "Submitting macOS app bundle ZIP for notarization."
  xcrun notarytool submit "${notary_zip_path}" \
    --apple-id "${APPLE_ID}" \
    --team-id "${APPLE_TEAM_ID}" \
    --password "${APPLE_APP_SPECIFIC_PASSWORD}" \
    --wait

  echo "Stapling notarization ticket to app bundle: ${bundle_dir}"
  xcrun stapler staple "${bundle_dir}"
  xcrun stapler validate "${bundle_dir}"
  rm -f "${notary_zip_path}"
fi

create_zip "${zip_path}"
echo "macOS ZIP package created: ${zip_path}"

echo "macOS app bundle created: ${bundle_dir}"
echo "Executable entry point: ${bundle_macos}/${app_exe}"
