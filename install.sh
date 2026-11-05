#!/usr/bin/env bash
# transferit installer (Linux). No root needed, except for the optional firewall step.
#
#   ./install.sh              install / update
#   ./install.sh --uninstall  remove the program (keeps your configs)
set -euo pipefail

BIN_DIR="$HOME/.local/bin"
BIN="$BIN_DIR/transferit"
CFG_DIR="$HOME/.config/transferit"
HERE="$(cd "$(dirname "$0")" && pwd)"

say()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mwarning:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

# ask "question" "default" -> prints answer (works even when stdin is piped)
ask() {
  local reply
  if [ -t 0 ]; then read -r -p "$1 [$2]: " reply || true; else read -r reply || true; fi
  echo "${reply:-$2}"
}
yes_no() { [[ "$(ask "$1 (y/n)" "$2")" =~ ^[Yy] ]]; }

# ---------- uninstall ----------
if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$BIN"
  say "Removed $BIN"
  echo "Your configs are still in $CFG_DIR (delete that folder to remove them too)."
  exit 0
fi

[ "$(uname -s)" = "Linux" ] || die "this installer is for Linux only."

# ---------- 1. get the program ----------
if [ -x "$HERE/transferit" ] && [ ! -f "$HERE/transferIt.csproj" ]; then
  # Running from an extracted release package: check it matches this CPU.
  case "$(uname -m)" in
    x86_64)        want=x64 ;;
    aarch64|arm64) want=arm64 ;;
    *)             die "unsupported CPU: $(uname -m)" ;;
  esac
  case "$HERE" in
    *linux-x64*)   have=x64 ;;
    *linux-arm64*) have=arm64 ;;
    *)             have=$want ;;  # renamed folder: trust it
  esac
  [ "$have" = "$want" ] || die "this package is for $have but this computer is $want. Use transferit-linux-$want.tar.gz."
  SRC="$HERE/transferit"
elif [ -f "$HERE/transferIt.csproj" ]; then
  # Running from the source folder: build it (needs the .NET SDK).
  command -v dotnet >/dev/null || die "no prebuilt program here and the .NET SDK isn't installed. Use a release package from build.sh."
  say "Building from source..."
  case "$(uname -m)" in
    x86_64) rid=linux-x64 ;; aarch64|arm64) rid=linux-arm64 ;; *) die "unsupported CPU: $(uname -m)" ;;
  esac
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  dotnet publish "$HERE/transferIt.csproj" -c Release -r "$rid" -o "$tmp" --nologo -v quiet
  SRC="$tmp/transferit"
else
  die "can't find the transferit program next to install.sh."
fi

# ---------- 2. install ----------
mkdir -p "$BIN_DIR"
install -m 755 "$SRC" "$BIN"
say "Installed $BIN"

# ---------- 3. PATH ----------
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    case "$(basename "${SHELL:-bash}")" in
      zsh)  rc="$HOME/.zshrc" ;;
      fish) rc="" ;;
      *)    rc="$HOME/.bashrc" ;;
    esac
    if [ -z "$rc" ]; then
      warn "$BIN_DIR is not in your PATH. Run: fish_add_path $BIN_DIR"
    elif grep -qs '.local/bin' "$rc"; then
      : # already configured, just not loaded in this shell yet
    else
      echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$rc"
      say "Added $BIN_DIR to PATH in $rc"
    fi
    export PATH="$BIN_DIR:$PATH"
    NEED_NEW_SHELL=1
    ;;
esac

# ---------- 4. configs ----------
"$BIN" list >/dev/null  # creates ~/.config/transferit/ and both config files if missing

current_dir="$(sed -n '/[^[:space:]]/{p;q}' "$CFG_DIR/receiver.cfg" 2>/dev/null || true)"
save_dir="$(ask "Folder to save received files to" "${current_dir:-$HOME/Downloads/transferit}")"
"$BIN" savedir "$save_dir"

# ---------- 5. firewall (optional) ----------
fw=""
if command -v ufw >/dev/null && systemctl is-active --quiet ufw 2>/dev/null; then fw=ufw
elif command -v firewall-cmd >/dev/null && systemctl is-active --quiet firewalld 2>/dev/null; then fw=firewalld
fi

if [ -n "$fw" ]; then
  echo
  echo "A firewall ($fw) is running. To RECEIVE files, the port you'll use must be open."
  echo "(Not needed if this computer will only send.)"
  if yes_no "Open a port now? Needs sudo" "y"; then
    port="$(ask "Port" "5555")"
    [[ "$port" =~ ^[0-9]+$ ]] && [ "$port" -ge 1 ] && [ "$port" -le 65535 ] || die "invalid port: $port"
    if [ "$fw" = ufw ]; then
      sudo ufw allow "$port" comment transferit
    else
      sudo firewall-cmd --permanent --add-port="$port/tcp" --add-port="$port/udp"
      sudo firewall-cmd --reload
    fi
    say "Port $port opened (TCP + UDP). Use $port when you run 'transferit receive'."
  fi
fi

# ---------- done ----------
echo
say "Done! Try it:"
echo "  transferit receive          # wait for files (both of you on the same Wi-Fi)"
echo "  transferit add <file>       # queue files to send"
echo "  transferit send             # send them"
echo "  transferit                  # show all commands"
[ -n "${NEED_NEW_SHELL:-}" ] && echo && warn "open a new terminal first so the 'transferit' command is found."
exit 0
