#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${repo_root}/src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"
project_relpath="src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"

app_name="PulseAPK"
entry_exe="PulseAPK"
config="${CONFIGURATION:-Release}"
rid="${RID:-linux-x64}"

out_root="${repo_root}/artifacts/linux/${rid}"
publish_dir="${out_root}/publish"
appdir="${out_root}/AppDir"
appimage_path="${out_root}/${app_name}-${rid}.AppImage"
tmp_appimage_path="${out_root}/${app_name}-${rid}.AppImage.tmp"

icon_src="${repo_root}/Resources/CyberUnpack.png"
icon_relpath="Resources/CyberUnpack.png"

verbose="${VERBOSE:-0}"

log() {
  echo "[build-appimage] $*"
}

fail() {
  echo "[build-appimage] ERROR: $*" >&2
  exit 1
}

debug_log() {
  [[ "${verbose}" == "1" ]] || return 0
  log "$*"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "'$1' is required but was not found in PATH."
}

is_png_file() {
  local icon_path="$1"
  local signature
  signature="$(od -An -tx1 -N8 "${icon_path}" | tr -d ' \n')"
  [[ "${signature}" == "89504e470d0a1a0a" ]]
}

require_cmd dotnet
require_cmd appimagetool

log "Using project path: ${project_relpath}"
debug_log "repo_root: ${repo_root}"
debug_log "project:   ${project_path}"
log "rid:       ${rid}"
log "config:    ${config}"
debug_log "output root: ${out_root}"

rm -rf "${publish_dir}" "${appdir}"
mkdir -p \
  "${publish_dir}" \
  "${appdir}/usr/bin" \
  "${appdir}/usr/share/icons/hicolor/256x256/apps"

log "Publishing .NET app..."
dotnet publish "${project_path}" \
  -c "${config}" \
  -r "${rid}" \
  --self-contained true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  /p:PublishSingleFile=false \
  -o "${publish_dir}"

log "Removing non-runtime artifacts from publish output..."
find "${publish_dir}" -type f \( -name '*.pdb' -o -name '*.xml' \) -delete

log "Copying publish output into AppDir..."
cp -a "${publish_dir}/." "${appdir}/usr/bin/"

log "Verifying AppDir does not include forbidden debug artifacts..."
if find "${appdir}/usr/bin" -type f -name '*.pdb' -print -quit | grep -q .; then
  fail "Forbidden symbol files (*.pdb) were found in ${appdir}/usr/bin."
fi

if [[ ! -f "${appdir}/usr/bin/${entry_exe}" ]]; then
  fail "Expected executable '${entry_exe}' was not found in artifacts/linux/${rid}/publish."
fi

chmod +x "${appdir}/usr/bin/${entry_exe}"

log "Creating AppRun..."
cat > "${appdir}/AppRun" <<'EOF'
#!/usr/bin/env bash
set -e
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "${here}/usr/bin/PulseAPK" "$@"
EOF
chmod +x "${appdir}/AppRun"

log "Preparing desktop entry..."
desktop_file="${appdir}/${app_name}.desktop"

cat > "${desktop_file}" <<EOF
[Desktop Entry]
Type=Application
Name=${app_name}
Exec=${entry_exe}
Icon=${app_name}
Categories=Development;
Terminal=false
EOF

if [[ -f "${icon_src}" ]] && is_png_file "${icon_src}"; then
  log "Using icon: ${icon_relpath}"
  debug_log "Icon source path: ${icon_src}"
  cp "${icon_src}" "${appdir}/${app_name}.png"
  cp "${icon_src}" "${appdir}/usr/share/icons/hicolor/256x256/apps/${app_name}.png"
  cp "${icon_src}" "${appdir}/.DirIcon"
else
  if [[ -f "${icon_src}" ]]; then
    log "Icon '${icon_relpath}' is not a valid PNG. Generating fallback SVG icon."
    debug_log "Invalid icon path: ${icon_src}"
  else
    log "Icon '${icon_relpath}' not found. Generating fallback SVG icon."
  fi

  cat > "${appdir}/${app_name}.svg" <<'EOF'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="48" fill="#2b2b2b"/>
  <rect x="36" y="36" width="184" height="184" rx="24" fill="#3a3a3a"/>
  <path d="M76 88h104v16H76zm0 32h72v16H76zm0 32h104v16H76z" fill="#7dd3fc"/>
  <path d="M156 124l28 28-28 28" fill="none" stroke="#f59e0b" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
EOF

  cp "${appdir}/${app_name}.svg" "${appdir}/.DirIcon"
fi

arch="${rid##*-}"
case "${arch}" in
  x64) appimage_arch="x86_64" ;;
  arm64) appimage_arch="aarch64" ;;
  *) appimage_arch="${arch}" ;;
esac

log "Building AppImage for architecture: ${appimage_arch}"
# Build to a temporary destination first to avoid "Text file busy" errors
# when the previous AppImage is currently running or mounted.
rm -f "${tmp_appimage_path}"
ARCH="${appimage_arch}" appimagetool "${appdir}" "${tmp_appimage_path}"
mv -f "${tmp_appimage_path}" "${appimage_path}"

log "AppImage created: artifacts/linux/${rid}/${app_name}-${rid}.AppImage"
debug_log "AppImage absolute path: ${appimage_path}"
